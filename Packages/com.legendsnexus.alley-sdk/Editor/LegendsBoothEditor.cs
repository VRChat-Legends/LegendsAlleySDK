using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(LegendsBooth))]
    public class LegendsBoothEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            AlleySession.LoadIfNeeded();
            var root = new VisualElement();
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(AlleyConfig.PackageRoot + "/Editor/Inspector/AlleyInspector.uss");
            if (styles != null) root.styleSheets.Add(styles);

            var card = new VisualElement();
            card.AddToClassList("alley-insp");
            card.AddToClassList("alley-insp-pink");
            root.Add(card);

            var header = new Label("LEGENDS BOOTH");
            header.AddToClassList("alley-insp-header");
            header.AddToClassList("alley-insp-header-pink");
            card.Add(header);

            card.Add(BuildCommunityPicker());

            var bounds = new Toggle("Show Bounds");
            bounds.BindProperty(serializedObject.FindProperty("showBounds"));
            card.Add(bounds);

            Vector3 limit = LegendsBooth.BoundsLimit;
            var hint = new Label(
                $"Keep the booth inside {limit.x} x {limit.y} x {limit.z} meters and build it facing the pink FRONT arrow. " +
                "Check everything and upload from the SDK window.");
            hint.AddToClassList("alley-insp-hint");
            card.Add(hint);

            var open = new Button(AlleySdkWindow.ShowWindow) { text = "OPEN LEGENDS ALLEY SDK" };
            open.AddToClassList("alley-insp-button");
            card.Add(open);

            return root;
        }

        // the booth belongs to a community, pick it from the signed in account
        private VisualElement BuildCommunityPicker()
        {
            SerializedProperty nameProp = serializedObject.FindProperty("displayName");
            CommunityInfo mine = AlleySession.IsSignedIn ? AlleySession.Community : null;

            if (mine == null || string.IsNullOrEmpty(mine.name))
            {
                var wrap = new VisualElement();
                var field = new TextField("Community")
                {
                    value = string.IsNullOrEmpty(nameProp.stringValue) ? "(not set)" : nameProp.stringValue,
                };
                field.SetEnabled(false);
                wrap.Add(field);
                var hint = new Label("Sign in on the Legends Alley SDK window to pick which community this booth belongs to.");
                hint.AddToClassList("alley-insp-hint");
                wrap.Add(hint);
                return wrap;
            }

            string stored = nameProp.stringValue;
            if (string.IsNullOrEmpty(stored))
            {
                nameProp.stringValue = mine.name;
                serializedObject.ApplyModifiedProperties();
                stored = mine.name;
            }

            var options = new List<string>();
            if (stored != mine.name) options.Add(stored);
            options.Add(mine.name);

            var dropdown = new DropdownField("Community", options, Mathf.Max(0, options.IndexOf(stored)));
            dropdown.RegisterValueChangedCallback(evt =>
            {
                serializedObject.Update();
                serializedObject.FindProperty("displayName").stringValue = evt.newValue;
                serializedObject.ApplyModifiedProperties();
            });
            return dropdown;
        }
    }
}
