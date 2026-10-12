#nullable enable

using System.Text;

namespace Libooth.Editor
{
    /// <summary>Reads UTF-8 ZIP names, with CP932 support for legacy Japanese archives.</summary>
    internal sealed class ZipFileNameEncoding : Encoding
    {
        private readonly Encoding _utf8 = new UTF8Encoding(false, true);
        private readonly Encoding _legacy = Encoding.GetEncoding(932,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public override string GetString(byte[] bytes, int index, int count)
        {
            try { return _utf8.GetString(bytes, index, count); }
            catch (DecoderFallbackException) { return _legacy.GetString(bytes, index, count); }
        }

        public override int GetCharCount(byte[] bytes, int index, int count)
            => GetString(bytes, index, count).Length;

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
        {
            var value = GetString(bytes, byteIndex, byteCount);
            value.CopyTo(0, chars, charIndex, value.Length);
            return value.Length;
        }

        public override int GetByteCount(char[] chars, int index, int count)
            => _utf8.GetByteCount(chars, index, count);

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
            => _utf8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

        public override int GetMaxByteCount(int charCount) => _utf8.GetMaxByteCount(charCount);
        public override int GetMaxCharCount(int byteCount) => _legacy.GetMaxCharCount(byteCount);
    }
}
