using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Libooth.Editor.UI
{
    /// <summary>Displays selectable literal text with clickable HTTP and HTTPS URLs.</summary>
    public sealed class UrlAwareLabel : Label
    {
        /// <summary>Enables links without opening a URL when text is dragged to select it.</summary>
        public UrlAwareLabel()
        {
            AddToClassList("url-aware-label");
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/me.konosuke.libooth/Editor/UI/Elements/UrlAwareLabel.uss"));
            focusable = true;
            enableRichText = true;
            ((ITextSelection)this).isSelectable = true;
            var pointerDown = Vector2.zero;
            var selectingText = false;
            RegisterCallback<PointerDownEvent>(evt =>
            {
                pointerDown = evt.position;
                selectingText = false;
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(evt =>
            {
                if ((evt.pressedButtons & 1) != 0 && Vector2.Distance(pointerDown, evt.position) > 3)
                    selectingText = true;
            }, TrickleDown.TrickleDown);
            RegisterCallback<PointerOverLinkTagEvent>(_ => AddToClassList("link-hover"));
            RegisterCallback<PointerOutLinkTagEvent>(_ => RemoveFromClassList("link-hover"));
            RegisterCallback<PointerUpLinkTagEvent>(evt =>
            {
                if (evt.button != 0 || selectingText || Vector2.Distance(pointerDown, evt.position) > 3) return;
                if (Uri.TryCreate(evt.linkID, UriKind.Absolute, out var url)
                    && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp))
                    Application.OpenURL(url.AbsoluteUri);
            });
        }

        /// <summary>Registers this label for UXML and UI Builder.</summary>
        public new class UxmlFactory : UxmlFactory<UrlAwareLabel, UxmlTraits> { }

        /// <summary>Applies the standard label attributes and links URLs in the text attribute.</summary>
        public new class UxmlTraits : Label.UxmlTraits
        {
            public override void Init(VisualElement element, IUxmlAttributes bag, CreationContext context)
            {
                base.Init(element, bag, context);
                var label = (UrlAwareLabel)element;
                label.enableRichText = true;
                ((ITextSelection)label).isSelectable = true;
                label.SetText(label.text);
            }
        }

        /// <summary>Displays literal text, turning embedded HTTP and HTTPS URLs into links.</summary>
        public void SetText(string text)
        {
            focusable = true;
            enableRichText = true;
            ((ITextSelection)this).isSelectable = true;
            RemoveFromClassList("link-hover");
            base.text = Format(text ?? string.Empty);
        }

        /// <summary>
        /// Adds TextCore links while displaying the original text literally.
        /// </summary>
        private static string Format(string text)
        {
            var result = new StringBuilder();
            var offset = 0;
            string Literal(string value) => Regex.Replace(value, "[<>]", match => $"<noparse>{match.Value}</noparse>");
            foreach (Match match in Regex.Matches(text, @"https?://[^\s<>""「」『』]+"))
            {
                var url = match.Value.TrimEnd('.', ',', ';', '!', '?', ']', '}', '。', '、');
                // Keep balanced parentheses used in URLs, remove sentence-closing ones.
                while (url.EndsWith(")") && url.Count(c => c == ')') > url.Count(c => c == '('))
                    url = url.Substring(0, url.Length - 1);
                result.Append(Literal(text.Substring(offset, match.Index - offset)));
                if (Uri.TryCreate(url, UriKind.Absolute, out _))
                    result.Append($"<link=\"{url}\"><color=#50A0E6><u>{Literal(url)}</u></color></link>");
                else result.Append(Literal(url));
                offset = match.Index + url.Length;
            }
            return result.Append(Literal(text.Substring(offset))).ToString();
        }
    }
}
