#nullable enable

using System;
using System.Runtime.InteropServices;

namespace Libooth.Editor.WebView
{
    /// <summary>
    /// Declares the native WebView C ABI.
    /// </summary>
    internal static class Bindings
    {
        private const string Library = "webview";

        /// <summary>
        /// Opens a native browser window.
        /// </summary>
        /// <param name="url">
        /// A NUL-terminated UTF-8 HTTPS URL.
        /// </param>
        /// <returns>
        /// The window ID, or zero on failure.
        /// </returns>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint webview_open(byte[] url);

        /// <summary>
        /// Closes a native browser window.
        /// </summary>
        /// <param name="id">The window ID. Unknown IDs are ignored.</param>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void webview_close(uint id);

        /// <summary>
        /// Closes all native browser windows.
        /// </summary>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void webview_close_all();

        /// <summary>
        /// Processes queued native window events.
        /// </summary>
        /// <returns>True on success; otherwise, false.</returns>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool webview_process_events();

        /// <summary>
        /// Retrieves recorded page-load history as JSON.
        /// </summary>
        /// <param name="id">
        /// The window ID.
        /// </param>
        /// <returns>
        /// An allocated UTF-8 JSON string, or zero on failure.
        /// </returns>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr webview_get_history(uint id);

        /// <summary>
        /// Retrieves matching native cookies as JSON.
        /// </summary>
        /// <param name="id">
        /// The window ID.
        /// </param>
        /// <param name="domain">
        /// A NUL-terminated UTF-8 DNS domain.
        /// </param>
        /// <param name="includeSubdomains">
        /// Whether to include subdomain cookies.
        /// </param>
        /// <returns>
        /// An allocated UTF-8 JSON string, or zero on failure.
        /// </returns>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr webview_get_cookies(
            uint id,
            byte[] domain,
            [MarshalAs(UnmanagedType.I1)] bool includeSubdomains 
        );

        /// <summary>
        /// Requests navigation to a URL.
        /// </summary>
        /// <param name="id">
        /// The window ID.
        /// </param>
        /// <param name="url">
        /// A NUL-terminated UTF-8 HTTPS URL.
        /// </param>
        /// <returns>
        /// True on success; otherwise, false.
        /// </returns>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool webview_navigate(uint id, byte[] url);

        /// <summary>
        /// Determines whether a window exists.
        /// </summary>
        /// <param name="id">
        /// The window ID.
        /// </param>
        /// <returns>
        /// True if the window exists; otherwise, false.
        /// </returns>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool webview_is_open(uint id);

        /// <summary>
        /// Releases a string allocated by this plugin.
        /// </summary>
        /// <param name="pointer">An unreleased string pointer from this plugin, or zero.</param>
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void webview_free_string(IntPtr pointer);
    }
}
