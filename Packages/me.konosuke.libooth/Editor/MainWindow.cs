using UnityEditor;
using UnityEngine;

namespace Libooth.Editor
{
    public class MainWindow : EditorWindow
    {
        [MenuItem("Tools/Libooth")]
        public static void Open()
        {
            var window = GetWindow<MainWindow>();

            window.titleContent = new GUIContent("Libooth");
        }
    }
}