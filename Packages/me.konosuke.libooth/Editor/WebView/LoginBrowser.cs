#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Libooth.Editor.WebView
{
    /// <summary>
    /// Opens BOOTH login and returns cookies after confirming authenticated access.
    /// </summary>
    /// <remarks>
    /// Create and dispose instances on the Unity main thread.
    /// Closing the window, assembly reload, or Editor shutdown cancels the login.
    /// Cookies are returned in memory and are not saved by this class.
    /// </remarks>
    internal sealed class LoginBrowser : Browser
    {
        private static readonly Uri LibraryUrl = new("https://accounts.booth.pm/library");

        private readonly TaskCompletionSource<Cookie[]> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _cancellation = new();

        private bool _disposed;
        private bool _checking;
        private bool _polling;
        private int _historyCount;
        private double _nextCheck;

        /// <summary>
        /// Opens the protected library page and starts monitoring login.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The native browser cannot be opened.
        /// </exception>
        public LoginBrowser() : base(LibraryUrl)
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
        }

        /// <summary>
        /// Gets the login result without blocking the Editor.
        /// </summary>
        /// <value>
        /// A task returning BOOTH cookies, including HttpOnly cookies.
        /// The task is canceled when login is interrupted and faults on failure.
        /// </value>
        public Task<Cookie[]> Completion => _completion.Task;

        /// <summary>
        /// Stops monitoring, cancels pending login, and closes the window.
        /// </summary>
        public override void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            EditorApplication.update -= Update;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.quitting -= Dispose;

            _cancellation.Cancel();
            _completion.TrySetCanceled();

            try
            {
                base.Dispose();
            }
            finally
            {
                _cancellation.Dispose();
            }
        }

        private void Update()
        {
            if (_disposed || _polling || EditorApplication.timeSinceStartup < _nextCheck)
            {
                return;
            }

            _nextCheck = EditorApplication.timeSinceStartup + 0.25;
            _polling = true;

            try
            {
                if (!IsOpen)
                {
                    Dispose();
                    return;
                }

                if (_checking)
                {
                    return;
                }

                var history = GetHistory();
                if (history.Length == _historyCount)
                {
                    return;
                }

                _historyCount = history.Length;
                if (history.Length == 0 || !IsBoothPage(history[^1]))
                {
                    return;
                }

                var cookies = GetCookies("booth.pm", includeSubdomains: true);
                if (cookies.Length != 0)
                {
                    _ = CompleteAsync(cookies);
                }
            }
            catch (Exception)
            {
                Fail();
            }
            finally
            {
                _polling = false;
            }
        }

        private async Task CompleteAsync(Cookie[] cookies)
        {
            _checking = true;
            var token = _cancellation.Token;

            try
            {
                var authenticated = await VerifyAsync(cookies, token);
                if (_disposed || token.IsCancellationRequested)
                {
                    return;
                }

                if (!IsOpen)
                {
                    Dispose();
                    return;
                }

                if (authenticated)
                {
                    var authenticatedCookies = GetCookies("booth.pm", includeSubdomains: true);
                    base.Dispose();
                    _completion.TrySetResult(authenticatedCookies);
                    Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                Dispose();
            }
            catch (Exception)
            {
                Fail();
            }
            finally
            {
                _checking = false;
            }
        }

        private void Fail()
        {
            if (!_disposed)
            {
                _completion.TrySetException(
                    new InvalidOperationException("Failed to complete BOOTH login."));
                Dispose();
            }
        }

        private static bool IsBoothPage(Uri url)
        {
            return url.Scheme == Uri.UriSchemeHttps
                && (url.Host.Equals("booth.pm", StringComparison.OrdinalIgnoreCase)
                    || url.Host.EndsWith(".booth.pm", StringComparison.OrdinalIgnoreCase));
        }

        private static async Task<bool> VerifyAsync(Cookie[] cookies, CancellationToken token)
        {
            var container = new CookieContainer();
            foreach (var cookie in cookies)
            {
                var nativeCookie = new System.Net.Cookie(
                    cookie.name, cookie.value, cookie.path, cookie.domain)
                {
                    Secure = cookie.secure,
                    HttpOnly = cookie.httpOnly
                };

                if (!cookie.session)
                {
                    nativeCookie.Expires = DateTimeOffset.FromUnixTimeSeconds(cookie.expires).UtcDateTime;
                }

                container.Add(nativeCookie);
            }

            using var handler = new HttpClientHandler
            {
                CookieContainer = container,
                AllowAutoRedirect = false
            };
            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            using var response = await client.GetAsync(
                LibraryUrl, HttpCompletionOption.ResponseHeadersRead, token);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return string.Equals(
                    response.Content.Headers.ContentType?.MediaType,
                    "text/html",
                    StringComparison.OrdinalIgnoreCase);
            }

            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400
                || response.StatusCode == HttpStatusCode.Unauthorized
                || response.StatusCode == HttpStatusCode.Forbidden)
            {
                return false;
            }

            throw new HttpRequestException("BOOTH login verification failed.");
        }
    }
}
