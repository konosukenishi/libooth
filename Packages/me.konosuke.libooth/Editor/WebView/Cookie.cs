#nullable enable

namespace Libooth.Editor.WebView
{
    /// <summary>
    /// Represents a snapshot of a cookie from the native cookie store.
    /// </summary>
    internal sealed class Cookie
    {
        /// <summary>
        /// The name of the cookie.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The sensitive value of the cookie.
        /// </summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// The domain of the cookie.
        /// </summary>
        public string Domain { get; set; } = string.Empty;

        /// <summary>
        /// The path of the cookie.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Whether the cookie requires a secure connection.
        /// </summary>
        public bool Secure { get; set; }
        
        /// <summary>
        /// Whether the cookie is inaccessible to document.cookie.
        /// </summary>
        public bool HttpOnly { get; set; }

        /// <summary>
        /// Whether the cookie has no explicit expiration time.
        /// </summary>
        public bool Session { get; set; }

        /// <summary>
        /// The expiration time in Unix seconds, or zero for a session cookie.
        /// </summary>
        public long Expires { get; set; }
    }
}
