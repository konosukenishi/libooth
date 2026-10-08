using System.Runtime.InteropServices;

namespace Libooth.Editor.WebView
{
    internal static class Bindings
    {
        private const string Library = "webview";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern uint webview_open(byte[] url);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void webview_close(uint handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void webview_close_all();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool webview_process_events();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool webview_is_open(uint id);
    }
}