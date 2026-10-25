#nullable enable

using System;

namespace Libooth.Editor.Booth
{
    /// <summary>
    /// Contains the profile of the currently signed-in BOOTH user.
    /// </summary>
    public record UserProfile(string Id, string Name, Uri? AvatarUrl)
    {
        /// <summary>
        /// The BOOTH user identifier.
        /// </summary>
        public string Id { get; } = Id;

        /// <summary>
        /// The display name, or an empty string when unavailable.
        /// </summary>
        public string Name { get; } = Name;

        /// <summary>
        /// The avatar URL, or null when unavailable.
        /// </summary>
        public Uri? AvatarUrl { get; } = AvatarUrl;
    }
}
