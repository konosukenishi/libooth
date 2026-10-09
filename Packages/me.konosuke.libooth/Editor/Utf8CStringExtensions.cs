#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Libooth.Editor
{
    /// <summary>
    /// Provides conversions between managed strings and UTF-8 C strings.
    /// </summary>
    internal static class Utf8CStringExtensions
    {
        /// <summary>
        /// Encodes a string as NUL-terminated UTF-8.
        /// </summary>
        /// <param name="value">
        /// The string to encode.
        /// </param>
        /// <returns>
        /// A UTF-8 byte array with a trailing NUL byte.
        /// An empty string produces an array containing only the NUL bytes.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="value"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> contains a NUL character.
        /// </exception>
        internal static byte[] ToUtf8CString(this string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("The string must not contain a NUL character.", nameof(value));
            }

            return Encoding.UTF8.GetBytes(value + "\0");
        }

        /// <summary>
        /// Copies a NUL-terminated UTF-8 string into managed memory.
        /// </summary>
        /// <param name="pointer">
        /// A pointer to readable, NUL-terminated UTF-8 memory,
        /// or <see cref="IntPtr.Zero"/>.
        /// The memory must remain valid and unchanged throughout this call.
        /// </param>
        /// <returns>
        /// A managed copy of the string, or null if
        /// <paramref name="pointer"/> is <see cref="IntPtr.Zero"/>.
        /// </returns>
        /// <remarks>
        /// This method does not release this native memory.
        /// The caller remains responsible for releasing it through its owner.
        /// Invalid pointers or missing termination can cause a process crash.
        /// Invalid UTF-8 sequences are decoded using replacement characters.
        /// </remarks>
        internal static string? ReadUtf8CString(this IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            var length = 0;

            while (Marshal.ReadByte(pointer, length) != 0)
            {
                length++;
            }

            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);

            return Encoding.UTF8.GetString(bytes);
        }
    }
}