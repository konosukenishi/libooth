#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Libooth.Editor.Booth
{
    /// <summary>
    /// Retrieves purchased items, account information, and files through an existing HTTP session.
    /// </summary>
    public sealed class BoothClient
    {
        private static readonly Uri LibraryUrl = new("https://accounts.booth.pm/library");

        private static readonly Regex ItemPath = new(@"/(?:[a-z]{2}/)?items/(?<id>\d+)/?$",
            RegexOptions.CultureInvariant);

        private readonly HttpClient _httpClient;

        /// <summary>
        /// Creates a client using an existing HTTP session.
        /// </summary>
        /// <param name="httpClient">The HTTP client configured with BOOTH cookies.</param>
        /// <exception cref="ArgumentNullException">The HTTP client is null.</exception>
        public BoothClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        /// <summary>
        /// Retrieves one page of purchased items from the BOOTH library.
        /// </summary>
        /// <param name="page">The one-based library page number.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The profiles and whether another page is available.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The page is less than one.</exception>
        /// <exception cref="HttpRequestException">The HTTP request fails.</exception>
        /// <exception cref="InvalidOperationException">The session has expired or the response is not HTML.</exception>
        public Task<(ItemProfile[] Items, bool HasNextPage)> GetItemProfilesAsync(int page = 1, CancellationToken cancellationToken = default)
            => GetLibraryItemProfilesAsync("/library", page, cancellationToken);

        /// <summary>
        /// Retrieves one page of received gifts from the BOOTH library.
        /// </summary>
        /// <param name="page">The one-based library page number.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The profiles and whether another page is available.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The page is less than one.</exception>
        /// <exception cref="HttpRequestException">The HTTP request fails.</exception>
        /// <exception cref="InvalidOperationException">The session has expired or the response is not HTML.</exception>
        public Task<(ItemProfile[] Items, bool HasNextPage)> GetGiftItemProfilesAsync(int page = 1, CancellationToken cancellationToken = default)
            => GetLibraryItemProfilesAsync("/library/gifts", page, cancellationToken);

        /// <summary>
        /// Retrieves one page of free downloads from the BOOTH library.
        /// </summary>
        /// <param name="page">The one-based library page number.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The profiles and whether another page is available.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The page is less than one.</exception>
        /// <exception cref="HttpRequestException">The HTTP request fails.</exception>
        /// <exception cref="InvalidOperationException">The session has expired or the response is not HTML.</exception>
        public Task<(ItemProfile[] Items, bool HasNextPage)> GetFreeDownloadItemProfilesAsync(int page = 1, CancellationToken cancellationToken = default)
            => GetLibraryItemProfilesAsync("/library/free_downloads", page, cancellationToken);

        private async Task<(ItemProfile[] Items, bool HasNextPage)> GetLibraryItemProfilesAsync(string path, int page, CancellationToken cancellationToken)
        {
            if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));

            var pageUrl = new Uri(LibraryUrl, $"{path}?page={page}");
            var document = await GetHtmlAsync(pageUrl, cancellationToken);
            var items = new Dictionary<string, ItemProfile>();
            var root = document.DocumentNode.SelectSingleNode("//main") ?? document.DocumentNode;
            foreach (var anchor in root.Descendants("a"))
            {
                if (anchor.Ancestors().Any(n => n.Name is "header" or "footer" or "nav")) continue;
                if (!Uri.TryCreate(pageUrl, HtmlEntity.DeEntitize(anchor.GetAttributeValue("href", "")), out var url)
                    || url.Scheme != Uri.UriSchemeHttps || !IsBoothHost(url.Host)) continue;
                var match = ItemPath.Match(url.AbsolutePath);
                if (!match.Success) continue;
                var name = HtmlEntity.DeEntitize(anchor.InnerText).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = HtmlEntity.DeEntitize(anchor.GetAttributeValue("title", "")).Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = HtmlEntity
                        .DeEntitize(anchor.Descendants("img").FirstOrDefault()?.GetAttributeValue("alt", "") ?? "")
                        .Trim();
                var id = match.Groups["id"].Value;
                name = Regex.Replace(name, @"\s+", " ");
                var image = anchor.Descendants("img").FirstOrDefault();
                var thumbnail = ParseImageUrl(pageUrl, image?.GetAttributeValue("data-src", null)
                                                       ?? image?.GetAttributeValue("src", "") ?? "");
                items.TryGetValue(id, out var previous);
                if (previous != null && previous.Name.Length != 0) name = previous.Name;
                items[id] = new ItemProfile(id, name,
                    new Uri(url.GetLeftPart(UriPartial.Path)), thumbnail ?? previous?.ThumbnailUrl);
            }

            var hasNextPage = false;
            foreach (var node in document.DocumentNode.Descendants().Where(n => n.Name is "a" or "link"))
            {
                if (!node.GetAttributeValue("rel", "").Split(' ').Contains("next")
                    && !node.GetAttributeValue("class", "").Split(' ').Contains("next")
                    && !node.Ancestors().Any(n => n.GetAttributeValue("class", "").Split(' ').Contains("next")))
                {
                    continue;
                }

                if (Uri.TryCreate(pageUrl, HtmlEntity.DeEntitize(node.GetAttributeValue("href", "")), out var next)
                    && next.Scheme == Uri.UriSchemeHttps && next.Host == LibraryUrl.Host && next.IsDefaultPort
                    && next.AbsolutePath.TrimEnd('/') == path)
                {
                    hasNextPage = true;
                    break;
                }
            }

            return (items.Values.ToArray(), hasNextPage);
        }

        /// <summary>
        /// Retrieves product details and purchased files for an existing profile.
        /// </summary>
        /// <param name="profile">The profile returned by the library.</param>
        /// <param name="cancellationToken">Cancels product and order requests.</param>
        /// <returns>An item retaining the supplied profile.</returns>
        /// <exception cref="ArgumentNullException">The profile is null.</exception>
        /// <exception cref="HttpRequestException">A product or order request fails.</exception>
        /// <exception cref="InvalidOperationException">The session has expired or a response is not HTML.</exception>
        public async Task<Item> GetItemAsync(ItemProfile profile, CancellationToken cancellationToken = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            var document = await GetHtmlAsync(profile.Url, cancellationToken);
            var orders = document.DocumentNode.Descendants("a")
                .Select(node =>
                    Uri.TryCreate(profile.Url, HtmlEntity.DeEntitize(node.GetAttributeValue("href", "")), out var url)
                        ? url
                        : null)
                .Where(url =>
                    url != null && url.Scheme == Uri.UriSchemeHttps &&
                    url is { IsDefaultPort: true, Host: "accounts.booth.pm" } &&
                    Regex.IsMatch(url.AbsolutePath, @"^/orders/\d+/?$"))
                .Distinct();
            var files = new Dictionary<Uri, ItemDownload>();
            foreach (var order in orders)
            {
                if (order == null) continue;
                var orderDocument = await GetHtmlAsync(order, cancellationToken);
                foreach (var button in orderDocument.DocumentNode.Descendants().Where(n =>
                             n.GetAttributeValue("class", "").Split(' ').Contains("js-download-button")))
                {
                    if (!Uri.TryCreate(order, HtmlEntity.DeEntitize(button.GetAttributeValue("data-href", "")),
                            out var url)
                        || url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.Host != "booth.pm"
                        || !Regex.IsMatch(url.AbsolutePath, @"^/downloadables/\d+/?$")) continue;
                    var section = button.Ancestors()
                        .FirstOrDefault(n => n.GetAttributeValue("class", "").Split(' ').Contains("sheet"));
                    if (section == null || !section.Descendants("a").Any(n =>
                            Uri.TryCreate(order, n.GetAttributeValue("href", ""), out var product)
                            && IsBoothHost(product.Host) &&
                            ItemPath.Match(product.AbsolutePath).Groups["id"].Value == profile.Id)) continue;
                    var row = button.Ancestors().FirstOrDefault(n =>
                        n.GetAttributeValue("class", "").Split(' ').Contains("legacy-list-item"));
                    var name = HtmlEntity.DeEntitize(row?.Descendants("b").FirstOrDefault()?.InnerText ?? "").Trim();
                    if (name.Length != 0) files[url] = new ItemDownload(name, url);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var root = document.DocumentNode;
            var shop = HtmlEntity
                .DeEntitize(root
                    .SelectSingleNode("//*[contains(concat(' ', normalize-space(@class), ' '), ' shop-name ')]/a")
                    ?.InnerText ?? "").Trim();

            void AppendDescription(HtmlNode node, StringBuilder text)
            {
                if (node.Name is "script" or "style" or "iframe") return;
                if (node.NodeType == HtmlNodeType.Text)
                {
                    text.Append(((HtmlTextNode)node).Text);
                    return;
                }

                if (node.Name == "br")
                {
                    text.AppendLine();
                    return;
                }

                foreach (var child in node.ChildNodes) AppendDescription(child, text);
                if (node.Name is "p" or "div" or "li" or "h2" or "h3") text.AppendLine();
            }

            var description = new StringBuilder();
            var blocks =
                root.SelectNodes(
                    "//*[contains(concat(' ', normalize-space(@class), ' '), ' js-market-item-detail-description ') or contains(concat(' ', normalize-space(@class), ' '), ' shop__text ')]");
            if (blocks != null)
                foreach (var block in blocks)
                {
                    AppendDescription(block, description);
                    description.AppendLine().AppendLine();
                }

            var text = HtmlEntity.DeEntitize(description.ToString()).Trim();
            if (text.Length == 0)
                text = HtmlEntity.DeEntitize(root.SelectSingleNode("//meta[@property='og:description']")
                    ?.GetAttributeValue("content", "") ?? "").Trim();
            return new Item(profile, shop, text,
                ParseImageUrl(profile.Url,
                    root.SelectSingleNode("//meta[@property='og:image']")?.GetAttributeValue("content", "") ?? ""),
                files.Values.ToArray());
        }

        /// <summary>
        /// Retrieves the currently signed-in user profile from the BOOTH home page.
        /// </summary>
        /// <param name="cancellationToken">Cancels the request.</param>
        /// <returns>The current user profile.</returns>
        /// <exception cref="HttpRequestException">The HTTP request fails.</exception>
        /// <exception cref="InvalidOperationException">The session has expired, the response is not HTML, or the user cannot be identified.</exception>
        public async Task<UserProfile> GetUserProfileAsync(CancellationToken cancellationToken = default)
        {
            var home = new Uri("https://booth.pm/ja");
            var document = await GetHtmlAsync(home, cancellationToken);
            var header = document.DocumentNode.SelectSingleNode("//*[@id='js-header']")
                         ?? document.DocumentNode.SelectSingleNode("//header[@role='banner']")
                         ?? document.DocumentNode.SelectSingleNode("//header");
            Uri? avatarUrl = null;
            if (header != null)
            {
                var avatarNavigation = header.SelectSingleNode(".//*[@id='js-navigation']");
                avatarUrl = ParseImageUrl(home,
                    avatarNavigation?.GetAttributeValue("data-user-thumbnail-url", "") ?? "");

                var image = header.SelectSingleNode(".//*[@id='js-user-pulldown']//img[@id='avatar']");
                if (avatarUrl == null) avatarUrl = ParseImageUrl(home, image?.GetAttributeValue("src", "") ?? "");

                if (avatarUrl == null)
                {
                    foreach (var node in header.Descendants())
                    {
                        var source = node.GetAttributeValue("data-original", null)
                                     ?? node.GetAttributeValue("data-src", null)
                                     ?? node.GetAttributeValue("src", "");
                        if (source.Length == 0)
                        {
                            var style = node.GetAttributeValue("style", "");
                            var match = Regex.Match(style, @"background-image\s*:\s*url\(([^)]+)\)",
                                RegexOptions.IgnoreCase);
                            if (match.Success) source = match.Groups[1].Value.Trim().Trim('\"', '\'');
                        }

                        var url = ParseImageUrl(home, source);
                        if (url == null) continue;
                        var classes = node.GetAttributeValue("class", "").Split(' ');
                        if (url.AbsolutePath.Contains("/users/") || url.AbsolutePath.Contains("/user-profile/")
                                                                 || classes.Contains("user-avatar") ||
                                                                 classes.Contains("user-icon"))
                        {
                            avatarUrl = url;
                            break;
                        }
                    }
                }
            }

            var navigation = document.DocumentNode.SelectSingleNode("//*[@id='js-header']//*[@id='js-navigation']");
            var name = HtmlEntity.DeEntitize(navigation?.GetAttributeValue("data-user-name", "") ?? "");
            if (name.Length == 0)
            {
                var json = HtmlEntity.DeEntitize(document.DocumentNode.SelectSingleNode("//*[@id='js-user-pulldown']")
                    ?.GetAttributeValue("data-current-user", "") ?? "");
                var match = Regex.Match(json, "\"nickname\"\\s*:\\s*(\"(?:\\\\.|[^\"])*\")");
                if (match.Success) name = Regex.Unescape(match.Groups[1].Value.Trim('"'));
            }

            var id = avatarUrl == null ? "" : Regex.Match(avatarUrl.AbsolutePath, @"/users/(\d+)/").Groups[1].Value;
            if (id.Length == 0)
            {
                throw new InvalidOperationException("Could not identify the BOOTH user.");
            }

            return new UserProfile(id, name, avatarUrl);
        }

        /// <summary>
        /// Downloads a file directly to its destination with byte progress notifications.
        /// </summary>
        /// <param name="file">The file to retrieve.</param>
        /// <param name="destination">The destination file, replacing any existing contents.</param>
        /// <param name="progress">Receives byte counts as data is written.</param>
        /// <param name="cancellationToken">Cancels transfer and saving.</param>
        /// <exception cref="ArgumentNullException">The file or destination is null.</exception>
        /// <exception cref="HttpRequestException">The HTTP request fails.</exception>
        /// <exception cref="IOException">Saving fails or the response is incomplete.</exception>
        /// <exception cref="InvalidOperationException">BOOTH returns HTML instead of a file.</exception>
        /// <remarks>Canceled transfers delete the partial destination file. Other failures may leave it partially written.</remarks>
        public async Task DownloadAsync(ItemDownload file, FileInfo destination,
            IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            using var response = await _httpClient
                .GetAsync(file.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
                throw new InvalidOperationException("BOOTH returned a web page instead of a file. Log in again.");

            await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            destination.Directory?.Create();
            await using var output = new FileStream(destination.FullName, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, true);
            try
            {
                var total = response.Content.Headers.ContentLength;
                var buffer = new byte[81920];
                long downloaded = 0;
                progress?.Report(new DownloadProgress(0, total));
                int count;
                while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                           .ConfigureAwait(false)) != 0)
                {
                    await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                    downloaded += count;
                    progress?.Report(new DownloadProgress(downloaded, total));
                }

                if (total.HasValue && downloaded != total.Value)
                    throw new IOException("The downloaded file size does not match the response size.");
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                output.Dispose();
                destination.Delete();
                throw;
            }
        }

        private async Task<HtmlDocument> GetHtmlAsync(Uri url, CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentType?.MediaType != "text/html")
                throw new InvalidOperationException("BOOTH returned an unexpected response.");

            var html = await response.Content.ReadAsStringAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var document = new HtmlDocument();
            document.LoadHtml(html);
            if (document.DocumentNode.SelectSingleNode("//form[contains(@action, '/users/sign_in')]") != null
                || response.RequestMessage?.RequestUri?.AbsolutePath.Contains("/sign_in") == true)
                throw new InvalidOperationException("The BOOTH session has expired. Log in again.");

            return document;
        }

        private static bool IsBoothHost(string host) => host.Equals("booth.pm", StringComparison.OrdinalIgnoreCase)
                                                        || host.EndsWith(".booth.pm",
                                                            StringComparison.OrdinalIgnoreCase);

        private static Uri? ParseImageUrl(Uri page, string value)
        {
            return !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(page, HtmlEntity.DeEntitize(value), out var url)
                                                     && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort
                ? url
                : null;
        }
    }
}