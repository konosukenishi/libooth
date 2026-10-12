#nullable enable

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Libooth.Editor.Booth
{
    public sealed partial class BoothClient
    {
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

        /// <summary>
        /// Returns a project-independent cache directory for the current OS user.
        /// </summary>
        private static DirectoryInfo GetDownloadCacheDirectory()
        {
#if UNITY_EDITOR_OSX
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches");
#elif UNITY_EDITOR_LINUX
            var root = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
#else
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#endif
            if (string.IsNullOrEmpty(root)) throw new IOException("The user cache directory is unavailable.");
            return new DirectoryInfo(Path.Combine(root, "me.konosuke.libooth", "Cache"));
        }

        /// <summary>
        /// Retrieves a shared download, validating its ETag or downloading a fresh copy.
        /// </summary>
        /// <param name="file">The BOOTH file to retrieve.</param>
        /// <param name="progress">Receives byte counts during transfer.</param>
        /// <param name="cancellationToken">Cancels waiting and downloading.</param>
        /// <param name="useCache">Whether to reuse an ETag-validated cached file; defaults to true.
        /// False always downloads and publishes a fresh cached copy.</param>
        /// <returns>The immutable downloaded file shared between Unity projects.</returns>
        /// <remarks>
        /// The per-file lock is shared across processes. Cancellation affects only this caller.
        /// Previously returned files remain available to other projects until the cache is cleared.
        /// </remarks>
        public async Task<FileInfo> DownloadAsync(Downloadable file,
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default,
            bool useCache = true)
        {
            var directory = Directory.CreateDirectory(GetFileCacheDirectory(file));
            var metadataPath = Path.Combine(directory.FullName, "metadata.json");
            var previous = ReadMetadata(metadataPath);
            var gate = Gates.GetOrAdd(directory.FullName, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                FileStream? fileLock = null;
                while (fileLock == null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        fileLock = new FileStream(Path.Combine(directory.FullName, "download.lock"),
                            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        fileLock.Lock(0, 1);
                    }
                    catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
                    {
                        fileLock?.Dispose();
                        fileLock = null;
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    }
                    catch { fileLock?.Dispose(); throw; }
                }
                using (fileLock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Metadata? metadata = null;
                    var current = ReadMetadata(metadataPath);
                    if (current != null)
                    {
                        try { metadata = JsonSerializer.Deserialize<Metadata>(current, JsonSerialization.Options); }
                        catch (JsonException) { }
                    }
                    FileInfo? cached = null;
                    if (metadata != null && Guid.TryParseExact(metadata.File, "N", out _)
                        && (string.IsNullOrEmpty(metadata.ETag) || EntityTagHeaderValue.TryParse(metadata.ETag, out _)))
                    {
                        cached = new FileInfo(Path.Combine(directory.FullName, metadata.File));
                        if (!cached.Exists || cached.Length != metadata.Length) cached = null;
                    }
                    // A concurrent downloader has already published a fresh version while we waited.
                    if (useCache && cached != null && current != previous) return cached;

                    var version = Guid.NewGuid().ToString("N");
                    var partial = new FileInfo(Path.Combine(directory.FullName, version + ".part"));
                    var completed = new FileInfo(Path.Combine(directory.FullName, version));
                    var pendingMetadata = metadataPath + ".part";
                    var published = false;
                    try
                    {
                        var result = await DownloadFileAsync(file, partial, progress, cancellationToken,
                            useCache && cached != null && !string.IsNullOrEmpty(metadata?.ETag) ? metadata!.ETag : null)
                            .ConfigureAwait(false);
                        if (result.NotModified)
                        {
                            if (cached == null) throw new IOException("The server returned 304 without a valid cached file.");
                            return cached;
                        }
                        cancellationToken.ThrowIfCancellationRequested();
                        File.Move(partial.FullName, completed.FullName);
                        completed.Refresh();
                        var json = JsonSerializer.Serialize(new Metadata
                            { File = version, ETag = result.ETag ?? "", Length = completed.Length },
                            JsonSerialization.Options);
                        File.WriteAllText(pendingMetadata, json);
                        if (File.Exists(metadataPath)) File.Replace(pendingMetadata, metadataPath, null);
                        else File.Move(pendingMetadata, metadataPath);
                        published = true;
                        return completed;
                    }
                    finally
                    {
                        if (File.Exists(partial.FullName)) File.Delete(partial.FullName);
                        if (File.Exists(pendingMetadata)) File.Delete(pendingMetadata);
                        if (!published && File.Exists(completed.FullName)) File.Delete(completed.FullName);
                    }
                }
            }
            finally { gate.Release(); }
        }

        private static string GetFileCacheDirectory(Downloadable file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            var path = file.DownloadUrl.AbsolutePath.TrimEnd('/');
            var id = path.StartsWith("/downloadables/", StringComparison.Ordinal)
                ? path.Substring("/downloadables/".Length) : "";
            if (file.DownloadUrl.Scheme != "https" || file.DownloadUrl.Host != "booth.pm"
                || !file.DownloadUrl.AbsolutePath.StartsWith("/downloadables/", StringComparison.Ordinal)
                || !long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw new ArgumentException("A BOOTH downloadable URL is required.", nameof(file));

            return Path.Combine(GetDownloadCacheDirectory().FullName, "downloads", id);
        }

        /// <summary>Reports whether a complete local cached download is available.</summary>
        public bool HasCachedDownload(Downloadable file)
        {
            try
            {
                var directory = GetFileCacheDirectory(file);
                var json = ReadMetadata(Path.Combine(directory, "metadata.json"));
                if (json == null) return false;
                var metadata = JsonSerializer.Deserialize<Metadata>(json, JsonSerialization.Options);
                if (metadata == null || !Guid.TryParseExact(metadata.File, "N", out _)) return false;
                var cached = new FileInfo(Path.Combine(directory, metadata.File));
                return cached.Exists && cached.Length == metadata.Length;
            }
            catch (IOException) { return false; }
            catch (JsonException) { return false; }
        }

        /// <summary>Removes a download's cache while excluding concurrent transfers.</summary>
        public async Task ClearDownloadCacheAsync(Downloadable file, CancellationToken cancellationToken = default)
        {
            var directory = Directory.CreateDirectory(GetFileCacheDirectory(file));
            var gate = Gates.GetOrAdd(directory.FullName, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                FileStream? fileLock = null;
                while (fileLock == null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        fileLock = new FileStream(Path.Combine(directory.FullName, "download.lock"),
                            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        fileLock.Lock(0, 1);
                    }
                    catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
                    {
                        fileLock?.Dispose();
                        fileLock = null;
                        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                    }
                    catch { fileLock?.Dispose(); throw; }
                }
                using (fileLock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(Path.Combine(directory.FullName, "metadata.json"));
                    foreach (var path in Directory.EnumerateFiles(directory.FullName))
                    {
                        var name = Path.GetFileName(path);
                        if (Guid.TryParseExact(name, "N", out _) || name.EndsWith(".part", StringComparison.Ordinal))
                            File.Delete(path);
                    }
                }
            }
            finally { gate.Release(); }
        }

        private static string? ReadMetadata(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException) { return null; }
        }

        private sealed class Metadata
        {
            public string File { get; set; } = "";
            [JsonPropertyName("etag")]
            public string ETag { get; set; } = "";
            public long Length { get; set; }
        }

        private async Task<(bool NotModified, string? ETag)> DownloadFileAsync(Downloadable file, FileInfo destination,
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default,
            string? etag = null)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            using var request = new HttpRequestMessage(HttpMethod.Get, file.DownloadUrl);
            if (etag != null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));
            using var response = await _httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified && etag != null)
                return (true, response.Headers.ETag?.ToString() ?? etag);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
                throw new InvalidOperationException("BOOTH returned a web page instead of a file. Log in again.");

            await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            destination.Directory?.Create();
            await using var output = new FileStream(destination.FullName, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, true);
            try
            {
                var total = response.Content.Headers.ContentLength;
                var buffer = new byte[81920];
                long downloaded = 0;
                progress?.Report(new DownloadProgress(0, total));
                int count;
                while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                           .ConfigureAwait(false)) != 0)
                {
                    await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                    downloaded += count;
                    progress?.Report(new DownloadProgress(downloaded, total));
                }

                if (total.HasValue && downloaded != total.Value)
                    throw new IOException("The downloaded file size does not match the response size.");
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                return (false, response.Headers.ETag?.ToString());
            }
            catch
            {
                output.Dispose();
                destination.Delete();
                throw;
            }
        }

    }
}
