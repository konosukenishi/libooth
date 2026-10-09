#nullable enable

using System;
using Libooth.Editor.WebView;
using UnityEditor;
using UnityEngine;

namespace Libooth.Editor
{
    public class MainWindow : EditorWindow
    {
        private LoginBrowser? _browser;
        
        [MenuItem("Tools/Libooth")]
        public static void Open()
        {
            var window = GetWindow<MainWindow>();
            window.titleContent = new GUIContent("Libooth");
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Login"))
            {
                if (_browser == null)
                {
                    _ = LoginAsync();
                }
            }

            if (GUILayout.Button("Close Login"))
            {
                CloseBrowser();
            }
        }

        private async System.Threading.Tasks.Task LoginAsync()
        {
            LoginBrowser? browser = null;
            try
            {
                browser = new LoginBrowser();
                _browser = browser;
                var cookies = await browser.Completion;
                Debug.Log($"BOOTH login completed. Retrieved {cookies.Length} cookies.");
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                browser?.Dispose();
                if (ReferenceEquals(_browser, browser))
                {
                    _browser = null;
                }
            }
        }

        private void OnDisable()
        {
            CloseBrowser();
        }

        private void CloseBrowser()
        {
            try
            {
                _browser?.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _browser = null;
            }
        }
    }
}
