using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    // shared bits for the small component inspectors so they all match
    internal static class AlleyInspectorKit
    {
        public static VisualElement BuildCard(VisualElement root, string accent, string headerText)
        {
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(AlleyConfig.PackageRoot + "/Editor/Inspector/AlleyInspector.uss");
            if (styles != null) root.styleSheets.Add(styles);

            var card = new VisualElement();
            card.AddToClassList("alley-insp");
            card.AddToClassList("alley-insp-" + accent);
            root.Add(card);

            var header = new Label(headerText);
            header.AddToClassList("alley-insp-header");
            header.AddToClassList("alley-insp-header-" + accent);
            card.Add(header);
            return card;
        }

        public static Label MakeStatus(VisualElement card)
        {
            var status = new Label();
            status.AddToClassList("alley-insp-status");
            card.Add(status);
            return status;
        }

        public static void SetStatus(Label status, string text, string tone)
        {
            status.text = text;
            status.RemoveFromClassList("alley-insp-status-empty");
            status.RemoveFromClassList("alley-insp-status-warn");
            if (!string.IsNullOrEmpty(tone)) status.AddToClassList("alley-insp-status-" + tone);
        }

        // vrchat ids are a prefix plus a guid, like grp_xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        public static bool IsVrcId(string value, string prefix)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return Regex.IsMatch(value.Trim(),
                "^" + prefix + "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$");
        }
    }
}
