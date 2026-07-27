using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Components;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleyPortal))]
    public class AlleyPortalEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "teal", "PORTAL");

            var idField = new TextField("World ID");
            idField.BindProperty(serializedObject.FindProperty(nameof(AlleyPortal.worldId)));
            card.Add(idField);

            Label status = AlleyInspectorKit.MakeStatus(card);
            idField.RegisterValueChangedCallback(_ => { SyncToMarker(); UpdateStatus(status); });
            SyncToMarker();
            UpdateStatus(status);

            var test = new Button(OpenInBrowser) { text = "TEST LINK IN BROWSER" };
            test.AddToClassList("alley-insp-button-ghost");
            card.Add(test);

            return root;
        }

        private AlleyPortal Helper => (AlleyPortal)target;
        private string CurrentId => (Helper.worldId ?? "").Trim();

        // the helper is just editor sugar, the real settings live on the vrc
        // marker so the export works without this script
        private void SyncToMarker()
        {
            AlleyPortal helper = Helper;
            var marker = helper.GetComponent<VRCPortalMarker>();
            if (marker != null && marker.roomId != CurrentId)
            {
                Undo.RecordObject(marker, "Set World ID");
                marker.roomId = CurrentId;
                PrefabUtility.RecordPrefabInstancePropertyModifications(marker);
            }
        }

        private void UpdateStatus(Label status)
        {
            if (Helper.GetComponent<VRCPortalMarker>() == null)
            {
                AlleyInspectorKit.SetStatus(status,
                    "No VRC Portal Marker on this object. Use the bundled prefab (GameObject > Legends Alley > Portal) or add the component.",
                    "warn");
                return;
            }

            string id = CurrentId;
            if (string.IsNullOrEmpty(id))
            {
                AlleyInspectorKit.SetStatus(status,
                    "Paste the world ID. Open the world on the VRChat website and copy the wrld_... part from the address bar.",
                    "empty");
            }
            else if (!AlleyInspectorKit.IsVrcId(id, "wrld_"))
            {
                AlleyInspectorKit.SetStatus(status,
                    "That doesn't look like a world ID. It should be wrld_ followed by 36 characters. Make sure the world is public or the portal will not open.",
                    "warn");
            }
            else
            {
                AlleyInspectorKit.SetStatus(status,
                    "In game the portal stands right here facing the same way as this object, and walking into it sends people to your world. Keep it public. Heads up: portals are dead in local Build and Test instances, they only work in uploaded worlds.", null);
            }
        }

        private void OpenInBrowser()
        {
            string id = CurrentId;
            if (AlleyInspectorKit.IsVrcId(id, "wrld_")) Application.OpenURL("https://vrchat.com/home/world/" + id);
        }
    }
}
