#nullable enable

using System;

namespace Libooth.Editor.Booth
{
    /// <summary>
    /// Represents an item listed in the authenticated BOOTH library.
    /// </summary>
    public record ItemProfile(string Id, string Name, Uri Url, Uri? ThumbnailUrl = null)
    {
        /// <summary>
        /// The BOOTH product identifier.
        /// </summary>
        public string Id { get; } = Id;

        /// <summary>
        /// The product display name.
        /// </summary>
        public string Name { get; } = Name;

        /// <summary>
        /// The product page URL.
        /// </summary>
        public Uri Url { get; } = Url;

        /// <summary>
        /// The thumbnail URL, or null when unavailable.
        /// </summary>
        public Uri? ThumbnailUrl { get; } = ThumbnailUrl;
    }
}
