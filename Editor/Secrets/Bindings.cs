#nullable enable

using System;
using System.Runtime.InteropServices;

namespace Libooth.Editor.Secrets
{
    /// <summary>
    /// Declares the native secret storage C ABI.
    /// </summary>
    internal static class Bindings
    {
        private const string Library = "secrets";

        /// <summary>
        /// Retrieves a secret as an allocated UTF-8 C string.
        /// </summary>
        /// <param name="key">
        /// A NUL-terminated UTF-8 key.
        /// </param>
        /// <returns>
        /// A pointer to the secret, or zero if absent or retrieval fails.
        /// </returns>
        /// <remarks>
        /// Release the returned string with secret_free_string.
        /// </remarks>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr secret_get(byte[] key);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr secret_get_legacy(byte[] key);

        /// <summary>
        /// Stores or replaces a secret.
        /// </summary>
        /// <param name="key">
        /// A NUL-terminated UTF-8 key.
        /// </param>
        /// <param name="value">
        /// A NUL-terminated UTF-8 secret.
        /// </param>
        /// <returns>
        /// True on success; otherwise, false.
        /// </returns>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool secret_set(byte[] key, byte[] value);

        /// <summary>
        /// Deletes a secret, treating an absent entry as success.
        /// </summary>
        /// <param name="key">
        /// A NUL-terminated UTF-8 key.
        /// </param>
        /// <returns>
        /// True on success; otherwise, false.
        /// </returns>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool secret_delete(byte[] key);

        /// <summary>
        /// Releases a string allocated by this plugin.
        /// </summary>
        /// <param name="value">An unreleased pointer from secret_get, or zero.</param>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void secret_free_string(IntPtr value);
    }
}
