#nullable enable

using System;
using System.Collections.Generic;

namespace Libooth.Editor.Booth
{
    /// <summary>
    /// Contains product details and purchased files for a library profile.
    /// </summary>
    public record Item(ItemProfile Profile, string Description, Uri? ImageUrl, IReadOnlyList<Downloadable> Downloadables)
    {
        /// <summary>
        /// The original purchased item profile.
        /// </summary>
        public ItemProfile Profile { get; } = Profile;

        /// <summary>
        /// The product description as plain text.
        /// </summary>
        public string Description { get; } = Description;

        /// <summary>
        /// The product image URL, or null when unavailable.
        /// </summary>
        public Uri? ImageUrl { get; } = ImageUrl;

        /// <summary>
        /// The files available for the purchased item.
        /// </summary>
        public IReadOnlyList<Downloadable> Downloadables { get; } = Downloadables;
    }
}
