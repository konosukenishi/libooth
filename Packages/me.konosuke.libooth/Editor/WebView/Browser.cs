#nullable enable

using System;
using System.Threading;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace Libooth.Editor.WebView
{
    /// <summary>
    /// Provides managed access to a native browser window.
    /// </summary>
    /// <remarks>
    /// All operations must run on the Unity main thread without reentrant calls. Native windows
    /// are closed before assembly reload and Editor shutdown.
    /// </remarks>
    [InitializeOnLoad]
    internal class Browser : IDisposable
    {
        private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;

        private static bool _initialized;

        private static bool _calling;

        private static bool _cleanupRequested;

        private uint _id;

        /// <summary>
        /// Registers Unity Editor lifecycle callbacks.
        /// </summary>
        static Browser()
        {
            EditorApplication.update += Update;
            EditorApplication.quitting += Cleanup;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        /// <summary>
        /// Opens the native window for this instance.
        /// </summary>
        /// <param name="url">An absolute HTTPS URI with a host.</param>
        /// <exception cref="ArgumentNullException">
        /// The URI is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The URI is not an absolute HTTPS URI with a host.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The native window cannot be opened.
        /// </exception>
        public Browser(Uri url)
        {
            RequireHttpsUrl(url);

            var bytes = url.AbsoluteUri.ToUtf8CString();

            _id = Invoke(() =>
            {
                var id = Bindings.webview_open(bytes);

                if (id == 0)
                {
                    throw new InvalidOperationException("Failed to open the WebView.");
                }

                _initialized = true;
                return id;
            });
        }

        /// <summary>
        /// Closes all windows owned by the native plugin.
        /// </summary>
        /// <remarks>
        /// This does not call Dispose on managed instances.
        /// </remarks>
        public static void CloseAll()
        {
            Invoke(() =>
            {
                if (_initialized)
                {
                    Bindings.webview_close_all();
                    _initialized = false;
                }

                return true;
            });
        }

        /// <summary>
        /// Processes queued native window events.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Native event processing fails.
        /// </exception>
        /// <remarks>
        /// Called automatically during Editor updates.
        /// </remarks>
        public static void ProcessEvents()
        {
            Invoke(() =>
            {
                if (_initialized && !Bindings.webview_process_events())
                {
                    throw new InvalidOperationException("Failed to process WebView events.");
                }

                return true;
            });
        }

        /// <summary>
        /// Gets whether the native window still exists.
        /// </summary>
        /// <value>True if the window exists; otherwise, false.</value>
        public bool IsOpen
        {
            get { return Invoke(() => _id != 0 && Bindings.webview_is_open(_id)); }
        }

        /// <summary>
        /// Requests navigation to the specified URI.
        /// </summary>
        /// <param name="url">An absolute HTTPS URI with a host.</param>
        /// <exception cref="ArgumentNullException">
        /// The URI is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The URI is not an absolute HTTPS URI with a host.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The browser window is closed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Navigation cannot be requested.
        /// </exception>
        /// <remarks>
        /// Returning successfully does not mean page loading has finished.
        /// </remarks>
        public void Navigate(Uri url)
        {
            RequireHttpsUrl(url);

            var bytes = url.AbsoluteUri.ToUtf8CString();

            Invoke(() =>
            {
                RequireOpen();

                if (!Bindings.webview_navigate(_id, bytes))
                {
                    throw new InvalidOperationException("Failed to navigate the WebView.");
                }

                return true;
            });
        }

        /// <summary>
        /// Retrieves a snapshot of the recorded page-load history.
        /// </summary>
        /// <returns>Absolute URIs in chronological order, or an empty array.</returns>
        /// <exception cref="ObjectDisposedException">
        /// The browser window is closed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Retrieval fails or the native response is invalid.
        /// </exception>
        /// <exception cref="JsonException">
        /// The JSON cannot be parsed.
        /// </exception>
        /// <remarks>
        /// Reading does not clear the history. This is not the back/forward list. Recorded URIs
        /// are not restricted to HTTPS.
        /// </remarks>
        public Uri[] GetHistory()
        {
            return Invoke(() =>
            {
                RequireOpen();
                var result = ReadJson<HistoryResponse>(Bindings.webview_get_history(_id));

                var urls = result?.History
                    ?? throw new InvalidOperationException("The WebView returned an invalid history response.");

                var history = new Uri[urls.Length];
                for (var i = 0; i < history.Length; i++)
                {

                    if (!Uri.TryCreate(urls[i], UriKind.Absolute, out var uri))
                    {
                        throw new InvalidOperationException("The WebView returned an invalid history URI.");
                    }
                    history[i] = uri;
                }
                return history;
            });
        }

        /// <summary>
        /// Retrieves cookies belonging to the specified domain.
        /// </summary>
        /// <param name="domain">
        /// A DNS domain without a scheme or path.
        /// </param>
        /// <param name="includeSubdomains">
        /// Whether to include cookies belonging to subdomains.
        /// </param>
        /// <returns>
        /// A cookie snapshot including HttpOnly cookies, or an empty array.
        /// </returns>
        /// <exception cref="ObjectDisposedException">
        /// The browser window is closed.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// The domain is empty or contains NUL.
        /// </exception>
        /// <exception cref="JsonException">
        /// The JSON cannot be parsed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Retrieval fails or the native response is invalid.
        /// </exception>
        /// <remarks>
        /// The native plugin validates and filters domains. Parent-domain cookies are not
        /// automatically included. Retrieval is synchronous; avoid calling it on every update.
        /// </remarks>
        public Cookie[] GetCookies(string domain, bool includeSubdomains = false)
        {
            if (string.IsNullOrEmpty(domain))
            {
                throw new ArgumentException("A nonempty cookie domain is required", nameof(domain));
            }

            var bytes = domain.ToUtf8CString();

            return Invoke(() =>
            {
                RequireOpen();
                var result = ReadJson<CookiesResponse>(
                    Bindings.webview_get_cookies(_id, bytes, includeSubdomains));

                return result?.Cookies
                    ?? throw new InvalidOperationException("The WebView returned an invalid cookies response.");
            });
        }

        /// <summary>
        /// Closes this browser window.
        /// </summary>
        /// <remarks>
        /// Repeated calls are allowed. Overrides must call the base implementation and remain
        /// idempotent.
        /// </remarks>
        public virtual void Dispose()
        {
            Invoke(() =>
            {
                if (_id != 0)
                {
                    Bindings.webview_close(_id);
                    _id = 0;
                }

                return true;
            });
        }

        private void RequireOpen()
        {
            if (_id == 0 || !Bindings.webview_is_open(_id))
            {
                throw new ObjectDisposedException(nameof(Browser), "The browser window is closed.");
            }
        }

        private static void Update()
        {
            if (!_initialized || _calling)
            {
                return;
            }

            try
            {
                ProcessEvents();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Cleanup();
            }
        }

        private static void Cleanup()
        {
            if (!_initialized)
            {
                return;
            }

            if (_calling)
            {
                _cleanupRequested = true;
                return;
            }

            CloseAll();
        }

        private static void RequireHttpsUrl(Uri url)
        {
            if (url is null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(url.Host))
            {
                throw new ArgumentException("An absolute HTTPS URI is required.", nameof(url));
            }
        }

        private static T? ReadJson<T>(IntPtr pointer) where T : class
        {
            if (pointer == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to retrieve data from the WebView.");
            }

            string json;
            try
            {
                json = pointer.ReadUtf8CString()
                    ?? throw new InvalidOperationException("The WebView returned a null JSON string.");
            }
            finally
            {

                Bindings.webview_free_string(pointer);
            }
            return JsonSerializer.Deserialize<T>(json, JsonSerialization.Options);
        }

        private static T Invoke<T>(Func<T> action)
        {
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
            {
                throw new InvalidOperationException("WebView operations must run on the Unity main thread.");
            }

            if (_calling)
            {
                throw new InvalidOperationException("A WebView operation is already in progress.");
            }

            EditorApplication.LockReloadAssemblies();
            _calling = true;

            try
            {
                return action();
            }
            finally
            {
                try
                {
                    if (_cleanupRequested)
                    {
                        _cleanupRequested = false;

                        if (_initialized)
                        {
                            Bindings.webview_close_all();
                            _initialized = false;
                        }
                    }
                }
                finally
                {
                    _calling = false;
                    EditorApplication.UnlockReloadAssemblies();
                }
            }
        }
        /// <summary>
        /// Represents the native history JSON envelope.
        /// </summary>
        private sealed class HistoryResponse
        {
            /// <summary>
            /// The recorded URLs in chronological order.
            /// </summary>
            public string?[]? History { get; set; }
        }

        /// <summary>
        /// Represents the native cookies JSON envelope.
        /// </summary>
        private sealed class CookiesResponse
        {
            /// <summary>
            /// The selected cookies.
            /// </summary>
            public Cookie[]? Cookies { get; set; }
        }
    }
}
