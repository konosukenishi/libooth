using System;

namespace Libooth.Editor.Secrets
{
    /// <summary>
    /// Provides access to the operating system's secret store.
    /// </summary>
    /// <remarks>
    /// Operations are synchronous and serialized within this class.
    /// Call them from a background thread to avoid blocking the Unity Editor.
    /// </remarks>
    public static class SecretStore
    {
        private static readonly object Sync = new();

        /// <summary>
        /// Retrieves the secret associated with the specified key.
        /// </summary>
        /// <param name="key">
        /// The nonempty identifier of the secret.
        /// </param>
        /// <returns>
        /// The stored secret, or null if it is absent or native retrieval fails.
        /// </returns>
        public static string Get(string key)
        {
            RequireNonEmptyKey(key);

            var keyBytes = key.ToUtf8CString();

            lock (Sync)
            {
                var pointer = Bindings.secret_get(keyBytes);

                if (pointer == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return pointer.ReadUtf8CString();
                }
                finally
                {
                    Bindings.secret_free_string(pointer);
                }
            }
        }


        /// <summary>
        /// Stores or replaces a secret associated with the specified key.
        /// </summary>
        /// <param name="key">
        /// The nonempty identifier of the secret.</param>
        /// <param name="value">
        /// The secret to store. An empty string is allowed.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// The native secret store fails to save the secret.
        /// </exception>
        public static void Set(string key, string value)
        {
            RequireNonEmptyKey(key);

            var keyBytes = key.ToUtf8CString();
            var valueBytes = value.ToUtf8CString();

            lock (Sync)
            {
                if (!Bindings.secret_set(keyBytes, valueBytes))
                {
                    throw new InvalidOperationException("Failed to store the secret.");
                }
            }
        }

        /// <summary>
        /// Deletes the secret associated with the specified key.
        /// </summary>
        /// <param name="key">
        /// The nonempty identifier of the secret.
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// The native secret store fails to delete the secret.
        /// </exception>
        public static void Delete(string key)
        {
            RequireNonEmptyKey(key);

            var keyBytes = key.ToUtf8CString();

            lock (Sync)
            {
                if (!Bindings.secret_delete(keyBytes))
                {
                    throw new InvalidOperationException("Failed to delete the secret.");
                }
            }
        }

        private static void RequireNonEmptyKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("A nonempty secret key is required.", nameof(key));
            }
        }
    }
}