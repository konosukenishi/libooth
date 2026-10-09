#nullable enable

using System;
using Libooth.Editor.WebView;
using UnityEditor;
using UnityEngine;

namespace Libooth.Editor
{
    public class MainWindow : EditorWindow
    {
        private Browser? _browser;
        
        [MenuItem("Tools/Libooth")]
        public static void Open()
        {
            var window = GetWindow<MainWindow>();
            window.titleContent = new GUIContent("Libooth");
        }

        private void OnGUI()
        {
            if (GUILayout.Button("Open WebView"))
            {
                try
                {
                    if (_browser?.IsOpen != true)
                    {
                        _browser = new Browser(new Uri("https://booth.pm"));
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            if (GUILayout.Button("Close WebView"))
            {
                CloseBrowser();
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
