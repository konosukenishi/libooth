using System;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Libooth.Editor.WebView
{
    [InitializeOnLoad]
    internal sealed class Browser : IDisposable
    {
        private static readonly int MainThreadId = Thread.CurrentThread.ManagedThreadId;

        private static bool _initialized;

        private static bool _calling;

        private static bool _cleanupRequested;

        private uint _id;

        static Browser()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }

        private Browser(uint id)
        {
            _id = id;
        }

        public static Browser Open(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("An absolute HTTPS URL is required.", nameof(url));
            }

            var bytes = Encoding.UTF8.GetBytes(uri.AbsoluteUri + "\0");

            return Invoke(() =>
            {
                var id = Bindings.webview_open(bytes);

                if (id == 0)
                {
                    throw new InvalidOperationException("Failed to open the WebView.");
                }

                _initialized = true;
                return new Browser(id);
            });
        }

        public bool IsOpen
        {
            get { return Invoke(() => _id != 0 && Bindings.webview_is_open(_id)); }
        }

        public void Dispose()
        {
            Invoke(() =>
            {
                if (_id == 0) return true;

                Bindings.webview_close(_id);
                _id = 0;
                return true;
            });
        }

        private static void Update()
        {
            if (!_initialized || _calling)
            {
                return;
            }

            Invoke(() =>
            {
                if (Bindings.webview_process_events()) return true;

                Debug.LogError("Failed to process WebView events.");
                Cleanup();

                return true;
            });
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

            Invoke(() =>
            {
                Bindings.webview_close_all();
                _initialized = false;
                return true;
            });
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

            _calling = true;

            EditorApplication.LockReloadAssemblies();

            try
            {
                return action();
            }
            finally
            {
                _calling = false;

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
                    EditorApplication.UnlockReloadAssemblies();
                }
            }
        }
    }
}