using System;
using System.Runtime.InteropServices;

namespace Libooth.Editor.Secrets
{
    internal static class Bindings
    {
        private const string Library = "secrets";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr secret_get(byte[] key);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool secret_set(byte[] key, byte[] value);
        
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool secret_delete(byte[] key);
        
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        internal static extern bool secret_free_string(IntPtr value);
    }
}