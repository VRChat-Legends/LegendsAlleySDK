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

            var reserved = new TextField("Reserved For");
            reserved.BindProperty(serializedObject.FindProperty("reservedFor"));
            card.Add(reserved);

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
