#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Libooth.Editor.Booth;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Libooth.Editor.UI
{
    /// <summary>Browses downloaded files and offers actions appropriate to each file.</summary>
    public sealed class DownloadableWindow : EditorWindow
    {
        private static readonly Dictionary<string, Func<FileInfo, VisualElement>> FileViews =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".unitypackage"] = file => new UnityPackageView(file)
            };

        private BoothClient? _client;
        private Downloadable? _file;
        private readonly CancellationTokenSource _cancellation = new();
        private string? _directory;
        private bool _loading;
        private FileInfo? _download;
        private ScrollView _files = null!;
        private VisualElement _actions = null!;
        private ProgressBar _progress = null!;
        private Button _retry = null!;
        private Label _loadingStatus = null!;
        private VisualElement _loadingView = null!;
        private VisualElement _content = null!;

        /// <summary>Opens an independent window for a BOOTH download.</summary>
        internal static void Open(BoothClient client, Downloadable file)
        {
            var window = CreateInstance<DownloadableWindow>();
            window._client = client;
            window._file = file;
            window.titleContent = new GUIContent(file.Name);
            window.minSize = new Vector2(640, 400);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/me.konosuke.libooth/Editor/UI/DownloadableWindow.uxml").CloneTree(rootVisualElement);
            rootVisualElement.focusable = true;
            rootVisualElement.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Escape) return;
                evt.StopPropagation();
                evt.PreventDefault();
                Close();
            }, TrickleDown.TrickleDown);
            rootVisualElement.Focus();
            _files = rootVisualElement.Q<ScrollView>("files");
            _actions = rootVisualElement.Q<VisualElement>("actions");
            _progress = rootVisualElement.Q<ProgressBar>("download-progress");
            _retry = rootVisualElement.Q<Button>("retry");
            _loadingStatus = rootVisualElement.Q<Label>("loading-status");
            _loadingView = rootVisualElement.Q<VisualElement>("loading-view");
            _content = rootVisualElement.Q<VisualElement>("content");
            rootVisualElement.Q<Button>("cancel-download").clicked += Close;
            rootVisualElement.Q<Button>("save-download").clicked += SaveDownload;
            rootVisualElement.Q<Button>("cancel").clicked += Close;
            rootVisualElement.Q<Button>("clear-cache").clicked += () => _ = ClearCacheAsync();
            _retry.clicked += () => _ = LoadAsync();
            if (_client == null || _file == null)
            {
                _loadingStatus.text = "Please reopen the download from the library.";
                return;
            }
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            if (_loading) return;
            _actions.Clear();
            CleanupDirectory();
            var token = _cancellation.Token;
            _loading = true;
            _retry.style.display = DisplayStyle.None;
            _loadingView.style.display = DisplayStyle.Flex;
            _content.style.display = DisplayStyle.None;
            _progress.value = 0;
            _loadingStatus.text = "Checking cache…";
            var directory = Path.Combine(Path.GetTempPath(), "Libooth", Guid.NewGuid().ToString("N"));
            _directory = directory;
            var downloading = true;
            try
            {
                Directory.CreateDirectory(directory);
                var progress = new Progress<DownloadProgress>(value =>
                {
                    if (this == null || token.IsCancellationRequested || !downloading) return;
                    _progress.value = (float)((value.Fraction ?? 0) * 100);
                    _loadingStatus.text = value.Fraction.HasValue
                        ? $"Downloading {value.Fraction.Value:P0}"
                        : $"Downloading {value.DownloadedBytes / 1048576d:F1} MB (size unknown)";
                });
                var download = await _client!.DownloadAsync(_file!, progress, token);
                _download = download;
                rootVisualElement.Q<Button>("save-download").SetEnabled(true);
                rootVisualElement.Q<Button>("clear-cache").SetEnabled(true);
                downloading = false;
                _progress.value = 100;
                _loadingStatus.text = "Reading files…";
                var entries = await Task.Run(() => ExtractFilesAsync(download, _file!.Name, directory, token), token);
                token.ThrowIfCancellationRequested();
                PopulateFiles(entries);
                _loadingView.style.display = DisplayStyle.None;
                _content.style.display = DisplayStyle.Flex;
            }
            catch (OperationCanceledException) { CleanupDirectory(); }
            catch (Exception exception)
            {
                CleanupDirectory();
                if (this != null && !token.IsCancellationRequested)
                {
                    _loadingStatus.text = "Could not download or read this file.";
                    _retry.style.display = DisplayStyle.Flex;
                    Debug.LogException(exception);
                }
            }
            finally { downloading = false; _loading = false; }
        }

        /// <summary>Extracts only actionable files to generated paths, keeping archive names for display only.</summary>
        private static async Task<List<(string Name, string Path)>> ExtractFilesAsync(
            FileInfo download, string fileName, string directory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = new List<(string Name, string Path)>();
            if (!Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (!HasFileActions(fileName)) return result;
                var path = Path.Combine(directory, "0" + Path.GetExtension(fileName));
                using var input = new FileStream(download.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 81920, true);
                using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, 81920, token);
                result.Add((fileName, path));
            }
            else
            {
                using var archive = ZipFile.Open(download.FullName, ZipArchiveMode.Read, new ZipFileNameEncoding());
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name) || !HasFileActions(entry.Name)) continue;
                    // Archive paths are display data only; extraction uses generated local paths.
                    var path = Path.Combine(directory, result.Count + Path.GetExtension(entry.Name));
                    using var input = entry.Open();
                    using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await input.CopyToAsync(output, 81920, token);
                    result.Add((entry.FullName, path));
                }
            }
            return result;
        }

        /// <summary>Lists supported files without their archive hierarchy.</summary>
        private void PopulateFiles(List<(string Name, string Path)> entries)
        {
            _actions.Clear();
            _files.Clear();
            var supported = entries.Where(entry => HasFileActions(entry.Path)).ToList();
            rootVisualElement.Q<VisualElement>("file-pane").style.display = supported.Count == 0
                ? DisplayStyle.None : DisplayStyle.Flex;
            rootVisualElement.Q<VisualElement>("file-panes").style.display = supported.Count == 0
                ? DisplayStyle.None : DisplayStyle.Flex;
            rootVisualElement.Q<VisualElement>("empty-view").style.display = supported.Count == 0
                ? DisplayStyle.Flex : DisplayStyle.None;
            rootVisualElement.Q<ScrollView>("action-scroll").style.display = supported.Count == 0
                ? DisplayStyle.None : DisplayStyle.Flex;
            rootVisualElement.Q<Image>("empty-icon").image = EditorGUIUtility.ObjectContent(null, typeof(DefaultAsset)).image;
            foreach (var entry in supported)
            {
                var name = Path.GetFileName(entry.Name.Replace('\\', '/'));
                var button = new Button(() =>
                {
                    foreach (var child in _files.contentContainer.Children()) child.RemoveFromClassList("selected");
                    ShowActions(entry.Path, name);
                }) { tooltip = entry.Name };
                button.AddToClassList("file-tab");
                var icon = new Image { image = EditorGUIUtility.ObjectContent(null, typeof(DefaultAsset)).image };
                icon.AddToClassList("file-icon");
                button.Add(icon);
                button.Add(new Label(name));
                button.clicked += () => button.AddToClassList("selected");
                _files.Add(button);
            }
            if (supported.Count > 0)
            {
                _files.contentContainer[0].AddToClassList("selected");
                ShowActions(supported[0].Path, Path.GetFileName(supported[0].Name.Replace('\\', '/')));
            }
        }

        private void ShowActions(string path, string name)
        {
            _actions.Clear();
            rootVisualElement.Q<Label>("file-header-name").text = name;
            rootVisualElement.Q<Image>("file-header-icon").image = EditorGUIUtility.ObjectContent(null, typeof(DefaultAsset)).image;
            try
            {
                var file = new FileInfo(path);
                _actions.Add(FileViews[file.Extension](file));
            }
            catch (Exception exception)
            {
                _actions.Add(new Label("Could not open this file."));
                Debug.LogException(exception);
            }
        }

        private async Task ClearCacheAsync()
        {
            if (_loading || _client == null || _file == null) return;
            if (!EditorUtility.DisplayDialog("Remove download?",
                "Remove the downloaded copy of \"" + _file.Name + "\" from this computer?\n\n" +
                "Files saved with Save as are kept. Opening this download again will download a new copy.",
                "Remove", "Cancel")) return;
            var button = rootVisualElement.Q<Button>("clear-cache");
            button.SetEnabled(false);
            rootVisualElement.Q<Button>("save-download").SetEnabled(false);
            try
            {
                await _client.ClearDownloadCacheAsync(_file, _cancellation.Token);
                _download = null;
                if (this != null) Close();
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                if (this != null)
                {
                    button.SetEnabled(true);
                    rootVisualElement.Q<Button>("save-download").SetEnabled(_download?.Exists == true);
                    EditorUtility.DisplayDialog("Could not remove downloaded file", exception.Message, "OK");
                }
            }
        }

        /// <summary>Saves the original download, including its complete archive contents.</summary>
        private void SaveDownload()
        {
            if (_download == null || _file == null) return;
            var destination = EditorUtility.SaveFilePanel("Save as", GetDownloadsDirectory(), _file.Name,
                Path.GetExtension(_file.Name).TrimStart('.'));
            if (string.IsNullOrEmpty(destination)) return;
            try
            {
                if (Path.GetFullPath(destination) == _download.FullName) return;
                File.Copy(_download.FullName, destination, true);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Could not save download", exception.Message, "OK");
                Debug.LogException(exception);
            }
        }

        /// <summary>Finds the user's Downloads folder, including redirected Windows and Linux locations.</summary>
        private static string GetDownloadsDirectory()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var directory = Path.Combine(home, "Downloads");
#if UNITY_EDITOR_WIN
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            var result = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var path);
            try
            {
                if (result == 0 && path != IntPtr.Zero)
                    directory = System.Runtime.InteropServices.Marshal.PtrToStringUni(path) ?? directory;
            }
            finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(path); }
#elif UNITY_EDITOR_LINUX
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrEmpty(config) || !Path.IsPathRooted(config)) config = Path.Combine(home, ".config");
            var settings = Path.Combine(config, "user-dirs.dirs");
            if (File.Exists(settings))
            {
                foreach (var line in File.ReadLines(settings))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line,
                        "^\\s*XDG_DOWNLOAD_DIR=\"([^\"]*)\"");
                    if (!match.Success) continue;
                    var value = match.Groups[1].Value.Replace("${HOME}", home).Replace("$HOME", home);
                    if (Path.IsPathRooted(value)) directory = value;
                    break;
                }
            }
#endif
            return Directory.Exists(directory) ? directory : home;
        }

#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
#endif

        private static bool HasFileActions(string path) => FileViews.ContainsKey(Path.GetExtension(path));

        private void CleanupDirectory()
        {
            if (_directory == null || !Directory.Exists(_directory)) return;
            try { Directory.Delete(_directory, true); }
            catch (IOException) { }
        }

        private void OnDisable()
        {
            _cancellation.Cancel();
            if (!_loading) CleanupDirectory();
        }

        private void OnDestroy() => _cancellation.Dispose();
    }
}
