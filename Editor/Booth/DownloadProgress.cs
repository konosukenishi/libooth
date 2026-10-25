#nullable enable

namespace Libooth.Editor.Booth
{
    /// <summary>
    /// Reports saved bytes and the response size, when known.
    /// </summary>
    public record DownloadProgress(long DownloadedBytes, long? TotalBytes)
    {
        /// <summary>
        /// The number of bytes written to the temporary file.
        /// </summary>
        public long DownloadedBytes { get; } = DownloadedBytes;

        /// <summary>
        /// The response size in bytes, or null when unknown.
        /// </summary>
        public long? TotalBytes { get; } = TotalBytes;

        /// <summary>
        /// The transfer fraction from zero to one, or null when the total is unknown or zero.
        /// </summary>
        public double? Fraction => TotalBytes is > 0 ? (double)DownloadedBytes / TotalBytes.Value : (double?)null;
    }
}
