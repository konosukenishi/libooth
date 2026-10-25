#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Libooth.Editor.Booth;
using Libooth.Editor.WebView;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Libooth.Editor.UI
{
    /// <summary>
    /// Displays the BOOTH library and selected item details using UI Toolkit.
    /// </summary>
    [EditorWindowTitle(title = "Libooth")]
    public class MainWindow : EditorWindow
    {
        private const string LayoutPath = "Packages/dev.konosuke.libooth/Editor/UI/MainWindow.uxml";
        private readonly List<ItemProfile> _items = new();
        private readonly List<ItemProfile> _filteredItems = new();
        private bool _libraryComplete;
        private bool _detailFailed;
        private bool _detailLoading;
        [SerializeField] private bool _showDescription;
        private readonly AccentColorExtractor _accentColors = new(0.55f, 0.45f);
        private UserProfile? _user;
        private ItemProfile? _selectedItem;
        private readonly List<(UserProfile Profile, HttpClient Http, BoothClient Client, Cookie[] Cookies)> _accounts = new();
        private readonly HttpClient _imageClient = new();
        private readonly SemaphoreSlim _imageRequests = new(4);
        private readonly Dictionary<Uri, Task<Texture2D?>> _thumbnails = new();
        private CancellationTokenSource _imageCancellation = new();
        private Texture2D? _imageGlow;
        private Texture2D? _downloadIcon;
        private IVisualElementScheduledItem? _spinnerAnimation;
        private IVisualElementScheduledItem? _librarySpinnerAnimation;
        private LoginBrowser? _browser;
        private HttpClient? _httpClient;
        private BoothClient? _client;
        private CancellationTokenSource? _loading;
        private CancellationTokenSource? _selection;
        private ListView _list = null!;
        private Label _status = null!;
        private Button _login = null!;

        /// <summary>
        /// Opens the library window.
        /// </summary>
        [MenuItem("Tools/Libooth")]
        public static void Open()
        {
            var window = GetWindow<MainWindow>();
            window.Show();
        }

        /// <summary>
        /// Creates the visual tree and connects the library selection events.
        /// </summary>
        public void CreateGUI()
        {
            minSize = new Vector2(480, 320);
            maxSize = new Vector2(4000, 4000);
            rootVisualElement.Clear();
            rootVisualElement.focusable = true;
            rootVisualElement.tabIndex = -1;
            rootVisualElement.AddToClassList("window-root");
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LayoutPath).CloneTree(rootVisualElement);
            var mainPane = rootVisualElement.Q<VisualElement>(className: "main-pane");
            mainPane.focusable = true;
            mainPane.tabIndex = -1;
            rootVisualElement.Q<ToolbarToggle>("downloads-tab").RegisterValueChangedCallback(_ => SelectDetailTab(false));
            rootVisualElement.Q<ToolbarToggle>("description-tab").RegisterValueChangedCallback(_ => SelectDetailTab(true));
            SelectDetailTab(_showDescription);
            rootVisualElement.Q<Image>("selection-icon").image = EditorGUIUtility.IconContent("console.infoicon").image;
            rootVisualElement.Q<Image>("login-icon").image = EditorGUIUtility.IconContent("console.infoicon").image;
            rootVisualElement.Q<Image>("detail-error-icon").image = EditorGUIUtility.IconContent("console.erroricon").image;
            _list = rootVisualElement.Q<ListView>("items");
            _status = rootVisualElement.Q<Label>("login-status");
            _login = rootVisualElement.Q<Button>("login-center");
            _list.makeItem = () =>
            {
                var row = new VisualElement();
                row.AddToClassList("item-row");
                var image = new Image { name = "thumbnail", scaleMode = ScaleMode.ScaleToFit };
                image.AddToClassList("item-thumbnail");
                row.Add(image);
                var label = new Label { name = "name" };
                label.AddToClassList("item-name");
                row.Add(label);
                return row;
            };
            _list.bindItem = (element, index) =>
            {
                var profile = _filteredItems[index];
                element.userData = profile;
                element.Q<Label>("name").text = profile.Name;
                element.tooltip = profile.Name;
                element.Q<Image>("thumbnail").image = null;
                _ = BindThumbnailAsync(element, profile);
            };
            _list.unbindItem = (element, _) =>
            {
                element.userData = null;
                element.Q<Image>("thumbnail").image = null;
            };
            _list.itemsSource = _filteredItems;
            rootVisualElement.Q<ToolbarSearchField>("search").RegisterValueChangedCallback(_ => FilterItems());
            FilterItems();
            var accountMenu = rootVisualElement.Q<ToolbarMenu>("account-menu");
            var avatar = new VisualElement { name = "account-avatar" };
            avatar.AddToClassList("account-avatar");
            var accountContent = new VisualElement();
            accountContent.AddToClassList("account-content");
            accountContent.Add(avatar);
            var accountName = new Label { name = "account-name", enableRichText = false };
            accountName.AddToClassList("account-name");
            accountContent.Add(accountName);
            accountMenu.Insert(0, accountContent);
            UpdateLoginView();
            _list.selectionChanged += selection =>
            {
                if (selection.FirstOrDefault() is ItemProfile profile) _ = ShowItemAsync(profile);
                else ClearDetails();
            };
            _login.clicked += () => _ = LoginAsync();
            if (_accounts.Count == 0) rootVisualElement.schedule.Execute(() => _ = RestoreAccountsAsync());
            var shopLink = rootVisualElement.Q<VisualElement>("shop-info");
            shopLink.AddManipulator(new Clickable(() =>
            {
                if (shopLink.userData is string name) rootVisualElement.Q<ToolbarSearchField>("search").value = name;
            }));
            var spinner = rootVisualElement.Q<Image>("loading-spinner");
            var spinnerFrames = Enumerable.Range(0, 12)
                .Select(i => EditorGUIUtility.IconContent($"WaitSpin{i:00}").image).ToArray();
            var frame = 0;
            _spinnerAnimation = spinner.schedule.Execute(() => spinner.image = spinnerFrames[frame++ % spinnerFrames.Length]).Every(80);
            _spinnerAnimation.Pause();
            var librarySpinner = rootVisualElement.Q<Image>("library-spinner");
            var libraryFrame = 0;
            _librarySpinnerAnimation = librarySpinner.schedule.Execute(() =>
                librarySpinner.image = spinnerFrames[libraryFrame++ % spinnerFrames.Length]).Every(80);
            _librarySpinnerAnimation.Pause();
            var titleLink = rootVisualElement.Q<VisualElement>("title-link");
            rootVisualElement.Q<Label>("item-title").enableRichText = false;
            rootVisualElement.Q<Image>("external-link-icon").image =
                EditorGUIUtility.Load("Builtin Skins/Shared/Images/ServicesWindow-ExternalLink.png") as Texture2D;
            titleLink.RegisterCallback<GeometryChangedEvent>(_ => LayoutTitle());
            titleLink.AddManipulator(new Clickable(() =>
            {
                if (_list.selectedItem is ItemProfile profile) Application.OpenURL(profile.Url.AbsoluteUri);
            }));
            if (rootVisualElement.panel?.focusController.focusedElement == null)
                rootVisualElement.Focus();
        }

        /// <summary>
        /// Limits the title to two lines and places the built-in icon after the final line.
        /// </summary>
        private void LayoutTitle()
        {
            var title = rootVisualElement.Q<Label>("item-title");
            var link = rootVisualElement.Q<VisualElement>("title-link");
            var width = link.contentRect.width;
            if (width <= 0 || float.IsNaN(width)) return;
            var text = title.userData as string ?? "Select an item";
            var lines = new List<string>();
            var line = new StringBuilder();
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                var element = elements.GetTextElement();
                if (element == "\r") continue;
                if (element == "\n" || (line.Length > 0 && title.MeasureTextSize(line.ToString() + element,
                    0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined).x > width))
                {
                    lines.Add(line.ToString());
                    line.Clear();
                }
                if (element != "\n") line.Append(element);
            }
            lines.Add(line.ToString());
            if (lines.Count > 2)
            {
                lines.RemoveRange(2, lines.Count - 2);
                var last = lines[1];
                while (last.Length > 0 && title.MeasureTextSize(last + "…", 0,
                    VisualElement.MeasureMode.Undefined, 0,
                    VisualElement.MeasureMode.Undefined).x > width)
                {
                    var positions = StringInfo.ParseCombiningCharacters(last);
                    last = last.Substring(0, positions[positions.Length - 1]);
                }
                lines[1] = last + "…";
            }
            title.tooltip = text;
            title.text = string.Join("\n", lines);
            var lastWidth = title.MeasureTextSize(lines[lines.Count - 1], 0, VisualElement.MeasureMode.Undefined,
                0, VisualElement.MeasureMode.Undefined).x;
            var height = title.MeasureTextSize(title.text, 0, VisualElement.MeasureMode.Undefined,
                0, VisualElement.MeasureMode.Undefined).y;
            var icon = rootVisualElement.Q<Image>("external-link-icon");
            icon.style.left = lastWidth + 4;
            icon.style.top = Mathf.Max(0, height - 18);
        }

        /// <summary>
        /// Filters loaded profiles while retaining the selected item when possible.
        /// </summary>
        private void FilterItems()
        {
            var selected = _list.selectedItem as ItemProfile;
            _list.SetSelectionWithoutNotify(Array.Empty<int>());
            var query = rootVisualElement.Q<ToolbarSearchField>("search").value?.Trim() ?? "";
            _filteredItems.Clear();
            _filteredItems.AddRange(_items.Where(item => item.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || item.ShopName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            _list.RefreshItems();
            if (selected != null)
            {
                var index = _filteredItems.FindIndex(item => item.Id == selected.Id);
                if (index >= 0)
                {
                    _list.SetSelectionWithoutNotify(new[] { index });
                    if (selected.LibraryPageUrl != _filteredItems[index].LibraryPageUrl)
                        _ = ShowItemAsync(_filteredItems[index]);
                }
            }
            if (_list.selectedItem == null) ClearDetails();
            rootVisualElement.Q<VisualElement>("library-loading").style.display =
                _user == null || _libraryComplete ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>Finds the library independently of the focused UI element.</summary>
        [Shortcut("Libooth/Focus search", typeof(MainWindow), KeyCode.F, ShortcutModifiers.Action)]
        private static void FocusSearchShortcut(ShortcutArguments arguments)
        {
            ((MainWindow)arguments.context).FocusSearch();
        }

        private void FocusSearch()
        {
            var field = rootVisualElement.Q<ToolbarSearchField>("search")?.Q<TextField>();
            if (field == null) return;
            field.Focus();
            field.SelectAll();
        }

        private void OnFocus()
        {
            if (rootVisualElement.panel?.focusController.focusedElement == null)
                rootVisualElement.Focus();
        }

        /// <summary>Creates an HTTP session from the standard cookies returned by the browser.</summary>
        private static HttpClient CreateAccountClient(Cookie[] cookies)
        {
            var container = new CookieContainer(1000, 1000, 16384);
            foreach (var cookie in cookies)
            {
                if (cookie.Domain.TrimStart('.').Equals("booth.pm", StringComparison.OrdinalIgnoreCase)
                    || cookie.Domain.EndsWith(".booth.pm", StringComparison.OrdinalIgnoreCase))
                    container.Add(cookie);
            }
            return new HttpClient(new HttpClientHandler { CookieContainer = container });
        }

        /// <summary>Restores saved accounts and selects the most recently logged-in account.</summary>
        private async Task RestoreAccountsAsync()
        {
            if (_loading != null) return;
            using var loading = new CancellationTokenSource();
            _loading = loading;
            _login.SetEnabled(false);
            try
            {
                var saved = await AccountStore.LoadAsync();
                if (this == null || loading.IsCancellationRequested) return;
                foreach (var account in saved.Items)
                {
                    var http = CreateAccountClient(account.Cookies);
                    _accounts.Add((account.Profile, http, new BoothClient(http), account.Cookies));
                }
                var selected = _accounts.FirstOrDefault(account => account.Profile.Id == saved.LastLoginId);
                if (selected.Client == null) return;
                _client = selected.Client;
                _httpClient = selected.Http;
                _user = await selected.Client.GetUserProfileAsync(loading.Token);
                loading.Token.ThrowIfCancellationRequested();
                if (this == null) return;
                UpdateLoginView();
                await LoadLibraryAsync(loading.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (this != null)
                {
                    _user = null;
                    _client = null;
                    _httpClient = null;
                    _status.text = "Could not restore the saved account. Please log in again.";
                }
                Debug.LogException(exception);
            }
            finally
            {
                _loading = null;
                if (this != null) { _login.SetEnabled(true); SetLibraryLoading(false); UpdateLoginView(); }
            }
        }

        private async Task LoginAsync()
        {
            if (_loading != null) return;
            using var loading = new CancellationTokenSource();
            _loading = loading;
            _selection?.Cancel();
            _login.SetEnabled(false);
            _status.text = "Waiting for BOOTH login...";
            try
            {
                using var browser = new LoginBrowser();
                _browser = browser;
                var cookies = await browser.Completion;
                _browser = null;
                loading.Token.ThrowIfCancellationRequested();
                _selection?.Cancel();
                var http = CreateAccountClient(cookies);
                var client = new BoothClient(http);
                UserProfile user;
                try
                {
                    user = await client.GetUserProfileAsync(loading.Token);
                    await AccountStore.SaveAsync(user, cookies);
                    loading.Token.ThrowIfCancellationRequested();
                }
                catch { http.Dispose(); throw; }
                _httpClient = http;
                _client = client;
                _user = user;
                foreach (var existing in _accounts.Where(account => account.Profile.Id == user.Id)) existing.Http.Dispose();
                _accounts.RemoveAll(account => account.Profile.Id == user.Id);
                _accounts.Add((user, http, client, cookies));
                ClearDetails();
                UpdateLoginView();
                await LoadLibraryAsync(loading.Token);
            }
            catch (OperationCanceledException) { if (this != null) _status.text = string.Empty; }
            catch (Exception exception)
            {
                if (this != null) _status.text = "Could not load the library. Try logging in again.";
                Debug.LogException(exception);
            }
            finally
            {
                _browser = null;
                _loading = null;
                if (this != null) SetLibraryLoading(false);
                if (this != null) { _login.SetEnabled(true); UpdateLoginView(); }
            }
        }

        /// <summary>
        /// Shows the account controls or the centered login prompt.
        /// </summary>
        private void UpdateLoginView()
        {
            var signedIn = _user != null;
            UpdateDetailsView();
            rootVisualElement.Q<VisualElement>("login-prompt").style.display = signedIn ? DisplayStyle.None : DisplayStyle.Flex;
            var menu = rootVisualElement.Q<ToolbarMenu>("account-menu");
            menu.style.display = signedIn || _accounts.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            menu.text = "";
            rootVisualElement.Q<Label>("account-name").text = _user?.Name ?? "Select account";
            menu.menu.MenuItems().Clear();
            foreach (var account in _accounts.OrderBy(account => account.Profile.Name, StringComparer.OrdinalIgnoreCase).ThenBy(account => account.Profile.Id, StringComparer.Ordinal))
            {
                var selected = account;
                menu.menu.AppendAction(account.Profile.Name, action => { _ = SwitchAccountAsync(selected); },
                    _ => _loading != null ? DropdownMenuAction.Status.Disabled :
                        ReferenceEquals(_client, selected.Client) ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
            menu.menu.AppendSeparator();
            menu.menu.AppendAction("Add account", action => { _ = LoginAsync(); },
                _ => _loading == null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.menu.AppendAction("Log out", action => { _ = LogoutAsync(); },
                _ => _user != null && _loading == null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            var avatar = rootVisualElement.Q<VisualElement>("account-avatar");
            avatar.style.backgroundImage = StyleKeyword.None;
            if (_user?.AvatarUrl != null) _ = ShowAccountAvatarAsync(_user);
            rootVisualElement.Q<VisualElement>("library-loading").style.display = signedIn && !_libraryComplete ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Shows either item details or a centered selection prompt for signed-in users.
        /// </summary>
        private void UpdateDetailsView()
        {
            var showDetails = _user != null && _selectedItem != null;
            rootVisualElement.Q<VisualElement>("item-header").style.display = showDetails ? DisplayStyle.Flex : DisplayStyle.None;
            rootVisualElement.Q<Toolbar>("detail-tabs").style.display = showDetails && !_detailFailed && !_detailLoading ? DisplayStyle.Flex : DisplayStyle.None;
            rootVisualElement.Q<ScrollView>("main-view").style.display = showDetails && !_detailFailed ? DisplayStyle.Flex : DisplayStyle.None;
            rootVisualElement.Q<VisualElement>("detail-error").style.display = showDetails && _detailFailed ? DisplayStyle.Flex : DisplayStyle.None;
            rootVisualElement.Q<VisualElement>("selection-prompt").style.display =
                _user != null && _selectedItem == null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Switches the detail tab, retaining the selection across items.</summary>
        private void SelectDetailTab(bool showDescription)
        {
            _showDescription = showDescription;
            rootVisualElement.Q<ToolbarToggle>("downloads-tab").SetValueWithoutNotify(!showDescription);
            rootVisualElement.Q<ToolbarToggle>("description-tab").SetValueWithoutNotify(showDescription);
            rootVisualElement.Q<VisualElement>("downloads").style.display = showDescription ? DisplayStyle.None : DisplayStyle.Flex;
            rootVisualElement.Q<UrlAwareLabel>("description").style.display = showDescription ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// Displays the active account's avatar without delaying the library.
        /// </summary>
        private async Task ShowAccountAvatarAsync(UserProfile user)
        {
            var texture = await GetThumbnailAsync(user.AvatarUrl!);
            if (this == null || !ReferenceEquals(_user, user) || texture == null) return;
            rootVisualElement.Q<VisualElement>("account-avatar").style.backgroundImage = new StyleBackground(texture);
        }

        /// <summary>
        /// Animates the sidebar indicator only while library pages are being fetched.
        /// </summary>
        private void SetLibraryLoading(bool loading)
        {
            rootVisualElement.Q<Image>("library-spinner").style.display = loading ? DisplayStyle.Flex : DisplayStyle.None;
            if (loading) _librarySpinnerAnimation?.Resume();
            else _librarySpinnerAnimation?.Pause();
        }

        /// <summary>
        /// Loads each library page with a delay between requests.
        /// </summary>
        private async Task LoadLibraryAsync(CancellationToken token)
        {
            SetLibraryLoading(true);
            _items.Clear();
            _libraryComplete = false;
            _list.ClearSelection();
            FilterItems();
            var seen = new HashSet<string>();
            var client = _client ?? throw new InvalidOperationException("Not logged in.");
            var collections = new Func<int, CancellationToken, Task<(ItemProfile[] Items, bool HasNextPage)>>[]
                { client.GetItemProfilesAsync, client.GetGiftItemProfilesAsync, client.GetFreeDownloadItemProfilesAsync };
            var firstRequest = true;
            foreach (var getPage in collections)
            {
                var collectionItems = new HashSet<string>();
                for (var page = 1; page <= 100; page++)
                {
                    if (!firstRequest) await Task.Delay(TimeSpan.FromSeconds(2), token);
                    firstRequest = false;
                    var result = await getPage(page, token);
                    token.ThrowIfCancellationRequested();
                    var previousCount = collectionItems.Count;
                    foreach (var profile in result.Items)
                    {
                        collectionItems.Add(profile.Id);
                        if (seen.Add(profile.Id)) _items.Add(profile);
                        else if (profile.LibraryPageUrl != null)
                        {
                            // Keep the gift download source when the product also appears in purchases.
                            var index = _items.FindIndex(item => item.Id == profile.Id);
                            if (index >= 0) _items[index] = profile;
                        }
                    }
                    FilterItems();
                    _status.text = $"{_items.Count} items loaded...";
                    if (!result.HasNextPage) break;
                    if (collectionItems.Count == previousCount || page == 100)
                        throw new InvalidOperationException("Invalid BOOTH pagination.");
                }
            }
            _libraryComplete = true;
            FilterItems();
            _status.text = $"{_items.Count} items loaded.";
        }

        /// <summary>
        /// Selects an account kept in memory for this window.
        /// </summary>
        private async Task SwitchAccountAsync((UserProfile Profile, HttpClient Http, BoothClient Client, Cookie[] Cookies) account)
        {
            if (_loading != null) return;
            _selection?.Cancel();
            _user = account.Profile;
            _httpClient = account.Http;
            _client = account.Client;
            ClearDetails();
            UpdateLoginView();
            using var loading = new CancellationTokenSource();
            _loading = loading;
            try
            {
                _user = await account.Client.GetUserProfileAsync(loading.Token);
                loading.Token.ThrowIfCancellationRequested();
                if (this == null) return;
                UpdateLoginView();
                await LoadLibraryAsync(loading.Token);
            }
            catch (OperationCanceledException) { if (this != null) _status.text = string.Empty; }
            catch (Exception exception)
            {
                if (this != null)
                {
                    _user = null;
                    _client = null;
                    _httpClient = null;
                    ClearDetails();
                    _status.text = "Could not open the saved account. Please log in again.";
                }
                Debug.LogException(exception);
            }
            finally
            {
                _loading = null;
                if (this != null) { SetLibraryLoading(false); UpdateLoginView(); }
            }
        }

        /// <summary>
        /// Clears the selected item before changing accounts.
        /// </summary>
        private void ClearDetails()
        {
            _selection?.Cancel();
            _selectedItem = null;
            rootVisualElement.Q<VisualElement>("downloads").Clear();
            UpdateDetailsView();
            rootVisualElement.Q<Label>("item-title").userData = null;
            rootVisualElement.Q<Label>("item-title").text = "Select an item";
            rootVisualElement.Q<VisualElement>("title-link").RemoveFromClassList("item-link");
            rootVisualElement.Q<VisualElement>("shop-info").style.display = DisplayStyle.None;
            rootVisualElement.Q<Image>("item-image").image = null;
            rootVisualElement.Q<Image>("item-image").style.display = DisplayStyle.None;
            rootVisualElement.Q<VisualElement>("image-glow").style.backgroundImage = StyleKeyword.None;
            rootVisualElement.Q<UrlAwareLabel>("description").SetText("");
            rootVisualElement.Q<VisualElement>("detail-loading").style.display = DisplayStyle.None;
            _spinnerAnimation?.Pause();
        }

        /// <summary>
        /// Signs out locally and forgets the current account's session.
        /// </summary>
        private async Task LogoutAsync()
        {
            if (_user == null || _loading != null) return;
            using var loading = new CancellationTokenSource();
            _loading = loading;
            UpdateLoginView();
            try
            {
                await AccountStore.RemoveAsync(_user.Id);
                if (this == null || loading.IsCancellationRequested) return;
                _selection?.Cancel();
                _accounts.RemoveAll(account => ReferenceEquals(account.Client, _client));
                _httpClient?.Dispose();
                _httpClient = null;
                _client = null;
                _user = null;
                _items.Clear();
                _libraryComplete = false;
                ClearDetails();
                FilterItems();
                _status.text = string.Empty;
            }
            catch (Exception exception) { Debug.LogException(exception); }
            finally
            {
                _loading = null;
                if (this != null) UpdateLoginView();
            }
        }

        private async Task ShowItemAsync(ItemProfile profile)
        {
            _selectedItem = profile;
            UpdateDetailsView();
            _selection?.Cancel();
            _selection?.Dispose();
            _selection = new CancellationTokenSource();
            var token = _selection.Token;
            var image = rootVisualElement.Q<Image>("item-image");
            image.image = null;
            rootVisualElement.Q<ScrollView>("main-view").scrollOffset = Vector2.zero;
            image.style.display = DisplayStyle.None;
            rootVisualElement.Q<VisualElement>("image-glow").style.backgroundImage = StyleKeyword.None;
            if (_imageGlow != null) DestroyImmediate(_imageGlow);
            _imageGlow = null;
            _ = ShowItemImageAsync(image, profile.ThumbnailUrl, token);
            var title = rootVisualElement.Q<Label>("item-title");
            var shop = rootVisualElement.Q<Label>("shop-name");
            var downloads = rootVisualElement.Q<VisualElement>("downloads");
            downloads.Clear();
            var description = rootVisualElement.Q<UrlAwareLabel>("description");
            _detailFailed = false;
            _detailLoading = true;
            UpdateDetailsView();
            title.userData = profile.Name;
            title.text = profile.Name;
            title.schedule.Execute(LayoutTitle);
            var titleLink = rootVisualElement.Q<VisualElement>("title-link");
            titleLink.AddToClassList("item-link");
            titleLink.tooltip = profile.Url.AbsoluteUri;
            shop.text = profile.ShopName;
            var shopInfo = rootVisualElement.Q<VisualElement>("shop-info");
            shopInfo.style.display = profile.ShopName.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            shopInfo.userData = profile.ShopName;
            shopInfo.tooltip = $"Filter by {profile.ShopName}";
            shopInfo.EnableInClassList("item-link", profile.ShopName.Length > 0);
            var shopIcon = rootVisualElement.Q<VisualElement>("shop-icon");
            shopIcon.style.backgroundImage = StyleKeyword.None;
            shopIcon.style.display = DisplayStyle.None;
            if (profile.ShopIconUrl != null) _ = ShowShopIconAsync(shopIcon, profile.ShopIconUrl, token);
            var loading = rootVisualElement.Q<VisualElement>("detail-loading");
            loading.style.display = DisplayStyle.Flex;
            _spinnerAnimation?.Resume();
            description.SetText("");

            try
            {
                var item = await _client!.GetItemAsync(profile, token);
                token.ThrowIfCancellationRequested();
                description.SetText(item.Description);
                foreach (var file in item.Downloadables)
                {
                    var row = new Box();
                    row.AddToClassList("download-row");
                    var icon = new Image
                    {
                        image = EditorGUIUtility.ObjectContent(null, typeof(DefaultAsset)).image,
                        scaleMode = ScaleMode.ScaleToFit,
                        pickingMode = PickingMode.Ignore
                    };
                    icon.AddToClassList("file-icon");
                    row.Add(icon);
                    var name = new Label(file.Name);
                    name.AddToClassList("download-name");
                    var fileName = new VisualElement();
                    fileName.AddToClassList("download-file-name");
                    fileName.Add(name);
                    var open = new Button();
                    open.AddToClassList("open-button");
                    open.AddToClassList("primary-button");
                    var openIcon = new Image
                    {
                        image = GetDownloadIcon(),
                        tintColor = Color.white,
                        scaleMode = ScaleMode.ScaleToFit,
                        pickingMode = PickingMode.Ignore
                    };
                    openIcon.AddToClassList("open-icon");
                    open.Add(openIcon);
                    var buttonText = new Label { pickingMode = PickingMode.Ignore };
                    open.Add(buttonText);
                    var client = _client!;
                    var cachedIcon = new Image
                    {
                        image = EditorGUIUtility.IconContent("d_CacheServerConnected").image,
                        scaleMode = ScaleMode.ScaleToFit,
                        tooltip = "Downloaded and cached locally"
                    };
                    cachedIcon.AddToClassList("cached-icon");
                    fileName.Add(cachedIcon);
                    void UpdateCachedIcon()
                    {
                        var cached = client.HasCachedDownload(file);
                        cachedIcon.style.display = cached ? DisplayStyle.Flex : DisplayStyle.None;
                        buttonText.text = cached ? "Open" : "Download";
                        openIcon.image = cached ? EditorGUIUtility.IconContent("Folder Icon").image : GetDownloadIcon();
                    }
                    UpdateCachedIcon();
                    row.schedule.Execute(UpdateCachedIcon).Every(1000);
                    open.clicked += () => DownloadableWindow.Open(client, file);
                    row.Add(fileName);
                    row.Add(open);
                    if (downloads.childCount == 0) row.style.marginTop = 0;
                    downloads.Add(row);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (!token.IsCancellationRequested && this != null)
                {
                    _detailFailed = true;
                    UpdateDetailsView();
                }
                Debug.LogException(exception);
            }
            finally
            {
                if (!token.IsCancellationRequested && this != null)
                {
                    _detailLoading = false;
                    UpdateDetailsView();
                    loading.style.display = DisplayStyle.None;
                    _spinnerAnimation?.Pause();
                }
            }
        }

        private async Task ShowShopIconAsync(VisualElement image, Uri url, CancellationToken token)
        {
            var texture = await GetThumbnailAsync(url);
            if (this == null || token.IsCancellationRequested || texture == null) return;
            image.style.backgroundImage = new StyleBackground(texture);
            image.style.display = DisplayStyle.Flex;
        }

        private async Task BindThumbnailAsync(VisualElement row, ItemProfile profile)
        {
            if (profile.ThumbnailUrl == null) return;
            var texture = await GetThumbnailAsync(profile.ThumbnailUrl);
            if (this != null && ReferenceEquals(row.userData, profile))
                row.Q<Image>("thumbnail").image = texture;
        }

        private Task<Texture2D?> GetThumbnailAsync(Uri url)
        {
            if (!_thumbnails.TryGetValue(url, out var request))
            {
                request = LoadThumbnailAsync(url, _imageCancellation.Token);
                _thumbnails.Add(url, request);
            }
            return request;
        }

        private async Task ShowItemImageAsync(Image image, Uri? url, CancellationToken token)
        {
            image.userData = url;
            if (url == null) return;
            var texture = await GetThumbnailAsync(url);
            if (this == null || token.IsCancellationRequested || !Equals(image.userData, url) || texture == null) return;
            image.image = texture;
            if (_imageGlow != null) DestroyImmediate(_imageGlow);
            _imageGlow = CreateImageGlow(texture);
            rootVisualElement.Q<VisualElement>("image-glow").style.backgroundImage = new StyleBackground(_imageGlow);
            image.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// Creates a header gradient from the image's single accent color.
        /// </summary>
        private Texture2D CreateImageGlow(Texture2D source)
        {
            const int sampleSize = 64;
            var target = RenderTexture.GetTemporary(sampleSize, sampleSize, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var sample = new Texture2D(sampleSize, sampleSize, TextureFormat.RGBA32, false);
            var accent = Color.clear;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                sample.ReadPixels(new Rect(0, 0, sampleSize, sampleSize), 0, 0, false);
                accent = _accentColors.Extract(sample.GetPixels32());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                DestroyImmediate(sample);
            }
            const int width = 96;
            const int height = 64;
            var gradient = new Color[width * height];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var u = x / (float)(width - 1);
                var v = y / (float)(height - 1);
                var color = accent;
                color.a *= 0.32f * (1 - u) * v;
                gradient[y * width + x] = color;
            }
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(gradient);
            texture.Apply(false, true);
            return texture;
        }

        private async Task<Texture2D?> LoadThumbnailAsync(Uri url, CancellationToken token)
        {
            try
            {
                await _imageRequests.WaitAsync(token);
                try
                {
                    using var response = await _imageClient.GetAsync(url, token);
                    response.EnsureSuccessStatusCode();
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    token.ThrowIfCancellationRequested();
                    var texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    if (texture.LoadImage(bytes, true)) return texture;
                    DestroyImmediate(texture);
                    return null;
                }
                finally { _imageRequests.Release(); }
            }
            catch (OperationCanceledException) { return null; }
            catch (HttpRequestException) { return null; }
            catch (Exception exception) { Debug.LogException(exception); return null; }
        }

        /// <summary>Matches the download icon to the open icon's color, preserving its alpha.</summary>
        private Texture2D GetDownloadIcon()
        {
            if (_downloadIcon != null) return _downloadIcon;
            var source = EditorGUIUtility.IconContent("package_update").image;
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = temporary;
                _downloadIcon = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                Graphics.Blit(EditorGUIUtility.IconContent("Folder Icon").image, temporary);
                _downloadIcon.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                var referencePixels = _downloadIcon.GetPixels32();
                var color = Vector3.zero;
                float weight = 0;
                foreach (var pixel in referencePixels)
                {
                    color += new Vector3(pixel.r, pixel.g, pixel.b) * pixel.a;
                    weight += pixel.a;
                }
                color = weight > 0 ? color / weight : new Vector3(255, 255, 255);
                Graphics.Blit(source, temporary);
                _downloadIcon.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                var pixels = _downloadIcon.GetPixels32();
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32((byte)color.x, (byte)color.y, (byte)color.z, pixels[i].a);
                _downloadIcon.SetPixels32(pixels);
                _downloadIcon.Apply();
                return _downloadIcon;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private void OnDestroy()
        {
            _imageClient.Dispose();
            _imageCancellation.Dispose();
        }

        private void OnDisable()
        {
            if (_downloadIcon != null) DestroyImmediate(_downloadIcon);
            _librarySpinnerAnimation?.Pause();
            _spinnerAnimation?.Pause();
            if (_imageGlow != null) DestroyImmediate(_imageGlow);
            _imageGlow = null;
            _imageCancellation.Cancel();
            foreach (var request in _thumbnails.Values)
                if (request.Status == TaskStatus.RanToCompletion && request.Result != null)
                    DestroyImmediate(request.Result);
            _thumbnails.Clear();
            _imageCancellation.Dispose();
            _imageCancellation = new CancellationTokenSource();
            _loading?.Cancel();
            _selection?.Cancel();
            _selection?.Dispose();
            _selection = null;
            _browser?.Dispose();
            foreach (var account in _accounts) account.Http.Dispose();
            _accounts.Clear();
            _httpClient = null;
            _user = null;
            _selectedItem = null;
            _client = null;
            _items.Clear();
            _filteredItems.Clear();
            _libraryComplete = false;
        }
    }
}
