#nullable enable

using System;

namespace Libooth.Editor.Booth
{
    /// <summary>
    /// Identifies a purchased file and its authenticated download URL.
    /// </summary>
    public record Downloadable(string Name, Uri DownloadUrl)
    {
        /// <summary>
        /// The filename displayed by BOOTH.
        /// </summary>
        public string Name { get; } = Name;

        /// <summary>
        /// The BOOTH download endpoint, which may redirect to file storage.
        /// </summary>
        public Uri DownloadUrl { get; } = DownloadUrl;
    }
}
