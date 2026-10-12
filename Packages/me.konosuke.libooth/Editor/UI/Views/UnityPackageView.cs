using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Libooth.Editor.UI
{
    /// <summary>Provides the Unity package import action.</summary>
    internal sealed class UnityPackageView : VisualElement
    {
        /// <summary>Loads the package UI and binds its import button.</summary>
        internal UnityPackageView(FileInfo file)
        {
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Packages/me.konosuke.libooth/Editor/UI/Views/UnityPackageView.uxml").CloneTree(this);
            this.Q<Button>("import").clicked += () =>
            {
                try { AssetDatabase.ImportPackage(file.FullName, true); }
                catch (Exception exception)
                {
                    this.Q<Label>("message").text = "Could not import this package. See the Console for details.";
                    Debug.LogException(exception);
                }
            };
        }
    }
}
