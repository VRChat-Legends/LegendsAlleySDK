using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(BoothLocation))]
    [CanEditMultipleObjects]
    public class BoothLocationEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(AlleyConfig.PackageRoot + "/Editor/Inspector/AlleyInspector.uss");
            if (styles != null) root.styleSheets.Add(styles);

            var card = new VisualElement();
            card.AddToClassList("alley-insp");
            card.AddToClassList("alley-insp-gold");
            root.Add(card);

            var header = new Label("BOOTH LOCATION");
            header.AddToClassList("alley-insp-header");
            header.AddToClassList("alley-insp-header-gold");
            card.Add(header);

            var plotName = new TextField("Plot Name");
            plotName.BindProperty(serializedObject.FindProperty("plotName"));
            card.Add(plotName);

            SerializedProperty reservedProp = serializedObject.FindProperty("reservedFor");
            var reservedSlot = new VisualElement();
            card.Add(reservedSlot);

            void BuildReserved()
            {
                reservedSlot.Clear();
                StaffCommunity[] roster = StaffCommunityCache.Communities;
                if (serializedObject.isEditingMultipleObjects || roster.Length == 0)
                {
                    var text = new TextField("Reserved For");
                    text.BindProperty(reservedProp);
                    reservedSlot.Add(text);
                    if (roster.Length == 0)
                    {
                        var hint = new Label("Takes a community slug. Sign in as staff in the SDK window to pick from the roster instead.");
                        hint.AddToClassList("alley-insp-hint");
                        reservedSlot.Add(hint);
                    }
                    return;
                }

                var choices = new List<string> { "Anyone (no reservation)" };
                var slugs = new List<string> { "" };
                foreach (StaffCommunity community in roster)
                {
                    if (!community.active) continue;
                    choices.Add(community.name);
                    slugs.Add(community.slug);
                }

                string current = (reservedProp.stringValue ?? "").Trim();
                int index = slugs.FindIndex(s => string.Equals(s, current, System.StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    choices.Add(current + " (not in the roster)");
                    slugs.Add(current);
                    index = slugs.Count - 1;
                }

                var dropdown = new DropdownField("Reserved For", choices, index);
                dropdown.RegisterValueChangedCallback(_ =>
                {
                    serializedObject.Update();
                    reservedProp.stringValue = slugs[Mathf.Clamp(dropdown.index, 0, slugs.Count - 1)];
                    serializedObject.ApplyModifiedProperties();
                });
                // rebuild if undo or something external changes the slug behind our back
                dropdown.TrackPropertyValue(reservedProp, p =>
                {
                    string mapped = slugs[Mathf.Clamp(dropdown.index, 0, slugs.Count - 1)];
                    if (!string.Equals((p.stringValue ?? "").Trim(), mapped, System.StringComparison.OrdinalIgnoreCase)) BuildReserved();
                });
                reservedSlot.Add(dropdown);
            }

            BuildReserved();
            StaffCommunityCache.Changed += BuildReserved;
            root.RegisterCallback<DetachFromPanelEvent>(_ => StaffCommunityCache.Changed -= BuildReserved);
            StaffCommunityCache.EnsureLoaded();

            var locked = new Toggle("Locked");
            locked.BindProperty(serializedObject.FindProperty("locked"));
            card.Add(locked);

            if (serializedObject.isEditingMultipleObjects) return root;

            var status = new Label();
            status.AddToClassList("alley-insp-status");
            card.Add(status);

            var clear = new Button(ClearPlot) { text = "CLEAR PLOT" };
            clear.AddToClassList("alley-insp-button-ghost");
            card.Add(clear);

            void Refresh()
            {
                var location = (BoothLocation)target;
                if (location == null) return;
                bool has = location.HasBooth;
                status.text = has
                    ? $"Holding {location.placedCommunityName} v{location.placedVersion}."
                    : "Empty plot. The pink arrow is the booth front. Sync or place a booth from the SDK window.";
                status.EnableInClassList("alley-insp-status-empty", !has);
                clear.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            }

            Refresh();
            status.TrackSerializedObjectValue(serializedObject, _ => Refresh());

            return root;
        }

        private void ClearPlot()
        {
            var location = (BoothLocation)target;
            for (int i = location.transform.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(location.transform.GetChild(i).gameObject);
            }
            Undo.RecordObject(location, "Clear plot");
            location.ClearPlacement();
            EditorUtility.SetDirty(location);
        }
    }
}
