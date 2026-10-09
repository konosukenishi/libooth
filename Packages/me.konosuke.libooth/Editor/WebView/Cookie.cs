#nullable enable

using System;

namespace Libooth.Editor.WebView
{
    /// <summary>
    /// Represents a snapshot of a cookie from the native cookie store.
    /// </summary>
    [Serializable]
    internal sealed class Cookie
    {
        /// <summary>
        /// The name of the cookie.
        /// </summary>
        public string name = string.Empty;

        /// <summary>
        /// The sensitive value of the cookie.
        /// </summary>
        public string value = string.Empty;

        /// <summary>
        /// The domain of the cookie.
        /// </summary>
        public string domain = string.Empty;

        /// <summary>
        /// The path of the cookie.
        /// </summary>
        public string path = string.Empty;

        /// <summary>
        /// Whether the cookie requires a secure connection.
        /// </summary>
        public bool secure;
        
        /// <summary>
        /// Whether the cookie is inaccessible to document.cookie.
        /// </summary>
        public bool httpOnly;

        /// <summary>
        /// Whether the cookie has no explicit expiration time.
        /// </summary>
        public bool session;

        /// <summary>
        /// The expiration time in Unix seconds, or zero for a session cookie.
        /// </summary>
        public long expires;
    }
}
