#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Libooth.Editor.Booth;
using Libooth.Editor.Secrets;

namespace Libooth.Editor
{
    /// <summary>Persists accounts across projects, with only encryption keys in the secret store.</summary>
    internal static class AccountStore
    {
        private const string KeyName = "booth.accounts.keys.v1";
        private static readonly object Sync = new();

        /// <summary>Contains a verified profile and its standard HTTP cookies.</summary>
        internal sealed class Account
        {
            public UserProfile Profile { get; set; } = null!;
            public Cookie[] Cookies { get; set; } = Array.Empty<Cookie>();
        }

        /// <summary>Contains saved accounts and the most recently logged-in account identifier.</summary>
        internal sealed class Accounts
        {
            public List<Account> Items { get; set; } = new();
            public string? LastLoginId { get; set; }
        }

        /// <summary>Loads accounts, copying authenticated legacy state on first use.</summary>
        internal static Task<Accounts> LoadAsync() => Task.Run(() => Access(null));

        /// <summary>Updates one account and records it as the most recently logged-in account.</summary>
        internal static Task SaveAsync(UserProfile profile, Cookie[] cookies) => Task.Run(() => Access(accounts =>
        {
            accounts.Items.RemoveAll(account => account.Profile.Id == profile.Id);
            accounts.Items.Add(new Account { Profile = profile, Cookies = cookies });
            accounts.LastLoginId = profile.Id;
        }));

        /// <summary>Removes the logged-out account without affecting other saved accounts.</summary>
        internal static Task RemoveAsync(string id) => Task.Run(() => Access(accounts =>
        {
            accounts.Items.RemoveAll(account => account.Profile.Id == id);
            if (accounts.LastLoginId == id) accounts.LastLoginId = accounts.Items.LastOrDefault()?.Profile.Id;
        }));

        private static Accounts Access(Action<Accounts>? update)
        {
            lock (Sync)
            {
                var directory = GetDirectory();
                Directory.CreateDirectory(directory);
                using var fileLock = new FileStream(Path.Combine(directory, "accounts.lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.ReadWrite);
                var deadline = DateTime.UtcNow.AddSeconds(15);
                while (true)
                {
                    try { fileLock.Lock(0, 1); break; }
                    catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(100); }
                }
                try
                {
                    var path = Path.Combine(directory, "accounts.enc");
                    var legacyPath = Path.Combine(GetDirectory("me.konosuke.libooth"), "accounts.enc");
                    var migrate = !File.Exists(path) && File.Exists(legacyPath);
                    // Keep the old writer excluded until validation and publication finish.
                    using var legacyLock = migrate ? AcquireLegacyLock(Path.GetDirectoryName(legacyPath)!) : null;
                    var sourcePath = migrate ? legacyPath : path;
                    var encodedKey = migrate ? SecretStore.GetLegacy(KeyName) : SecretStore.Get(KeyName);
                    if (encodedKey == null && File.Exists(sourcePath))
                        throw new CryptographicException("The saved accounts' encryption key is unavailable.");
                    if (encodedKey == null && update == null) return new Accounts();
                    if (encodedKey == null)
                    {
                        var generated = new byte[64];
                        using var random = RandomNumberGenerator.Create();
                        random.GetBytes(generated);
                        encodedKey = Convert.ToBase64String(generated);
                        SecretStore.Set(KeyName, encodedKey);
                    }
                    var key = Convert.FromBase64String(encodedKey);
                    if (key.Length != 64) throw new CryptographicException("Invalid account encryption key.");
                    using var aes = Aes.Create();
                    aes.Key = key.Take(32).ToArray();
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using var hmac = new HMACSHA256(key.Skip(32).ToArray());
                    var accounts = new Accounts();
                    if (File.Exists(sourcePath))
                    {
                        var bytes = File.ReadAllBytes(sourcePath);
                        // Format: version (1 byte), IV (16), ciphertext, HMAC-SHA256 (32).
                        if (bytes.Length < 65 || bytes[0] != 1)
                            throw new CryptographicException("Invalid saved account file.");
                        var signedLength = bytes.Length - 32;
                        var expected = hmac.ComputeHash(bytes, 0, signedLength);
                        var difference = 0;
                        for (var i = 0; i < expected.Length; i++) difference |= expected[i] ^ bytes[signedLength + i];
                        if (difference != 0) throw new CryptographicException("Saved account authentication failed.");
                        aes.IV = bytes.Skip(1).Take(16).ToArray();
                        using var decryptor = aes.CreateDecryptor();
                        var json = decryptor.TransformFinalBlock(bytes, 17, signedLength - 17);
                        accounts = JsonSerializer.Deserialize<Accounts>(json, JsonSerialization.Options)
                            ?? throw new InvalidDataException("Invalid saved account data.");
                    }
                    if (migrate)
                    {
                        // Validation above must succeed before either destination is written.
                        SecretStore.Set(KeyName, encodedKey);
                        var migrationFile = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            File.Copy(sourcePath, migrationFile);
                            File.Move(migrationFile, path);
                        }
                        finally { if (File.Exists(migrationFile)) File.Delete(migrationFile); }
                    }
                    if (update == null) return accounts;
                    update(accounts);
                    aes.GenerateIV();
                    using var encryptor = aes.CreateEncryptor();
                    var plain = JsonSerializer.SerializeToUtf8Bytes(accounts, JsonSerialization.Options);
                    var encrypted = encryptor.TransformFinalBlock(plain, 0, plain.Length);
                    var payload = new byte[17 + encrypted.Length + 32];
                    payload[0] = 1;
                    Buffer.BlockCopy(aes.IV, 0, payload, 1, 16);
                    Buffer.BlockCopy(encrypted, 0, payload, 17, encrypted.Length);
                    var tag = hmac.ComputeHash(payload, 0, payload.Length - 32);
                    Buffer.BlockCopy(tag, 0, payload, payload.Length - 32, 32);
                    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            output.Write(payload, 0, payload.Length);
                            output.Flush(true);
                        }
                        if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    return accounts;
                }
                finally { fileLock.Unlock(0, 1); }
            }
        }

        /// <summary>Excludes legacy writers while copying their authenticated state.</summary>
        private static FileStream AcquireLegacyLock(string directory)
        {
            var stream = new FileStream(Path.Combine(directory, "accounts.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.ReadWrite);
            var deadline = DateTime.UtcNow.AddSeconds(15);
            try
            {
                while (true)
                {
                    try { stream.Lock(0, 1); return stream; }
                    catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(100); }
                }
            }
            catch { stream.Dispose(); throw; }
        }

        /// <summary>Returns the user-specific persistent state directory, shared by Unity projects.</summary>
        private static string GetDirectory(string packageId = "dev.konosuke.libooth")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string root;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            else if (Directory.Exists("/System/Library/CoreServices"))
                root = Path.Combine(home, "Library", "Application Support");
            else
            {
                root = Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? "";
                if (!Path.IsPathRooted(root)) root = Path.Combine(home, ".local", "state");
            }
            return Path.Combine(root, packageId, "accounts");
        }
    }
}
