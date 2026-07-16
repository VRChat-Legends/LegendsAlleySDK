using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Components;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleyAvatarPedestal))]
    public class AlleyAvatarPedestalEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "purple", "AVATAR PEDESTAL");

            var idField = new TextField("Avatar ID");
            idField.BindProperty(serializedObject.FindProperty(nameof(AlleyAvatarPedestal.avatarId)));
            card.Add(idField);

            Label status = AlleyInspectorKit.MakeStatus(card);
            idField.RegisterValueChangedCallback(_ => { SyncToPedestal(); UpdateStatus(status); });
            SyncToPedestal();
            UpdateStatus(status);

            var test = new Button(OpenInBrowser) { text = "TEST LINK IN BROWSER" };
            test.AddToClassList("alley-insp-button-ghost");
            card.Add(test);

            return root;
        }

        private AlleyAvatarPedestal Helper => (AlleyAvatarPedestal)target;
        private string CurrentId => (Helper.avatarId ?? "").Trim();

        // the helper is just editor sugar, the real settings live on the vrc
        // pedestal so the export works without this script
        private void SyncToPedestal()
        {
            AlleyAvatarPedestal helper = Helper;
            var pedestal = helper.GetComponent<VRCAvatarPedestal>();
            if (pedestal != null && pedestal.blueprintId != CurrentId)
            {
                Undo.RecordObject(pedestal, "Set Avatar ID");
                pedestal.blueprintId = CurrentId;
                PrefabUtility.RecordPrefabInstancePropertyModifications(pedestal);
            }
        }

        private void UpdateStatus(Label status)
        {
            if (Helper.GetComponent<VRCAvatarPedestal>() == null)
            {
                AlleyInspectorKit.SetStatus(status,
                    "No VRC Avatar Pedestal on this object. Use the bundled prefab (GameObject > Legends Alley > Avatar Pedestal) or add the component.",
                    "warn");
                return;
            }

            string id = CurrentId;
            if (string.IsNullOrEmpty(id))
            {
                AlleyInspectorKit.SetStatus(status,
                    "Paste the avatar ID. Open the avatar on the VRChat website and copy the avtr_... part from the address bar.",
                    "empty");
            }
            else if (!AlleyInspectorKit.IsVrcId(id, "avtr_"))
            {
                AlleyInspectorKit.SetStatus(status,
                    "That doesn't look like an avatar ID. It should be avtr_ followed by 36 characters. Make sure the avatar is public or nobody can wear it.",
                    "warn");
            }
            else
            {
                AlleyInspectorKit.SetStatus(status,
                    "In game the avatar's picture fills the frame and pressing it switches people into the avatar. Keep it public.", null);
            }
        }

        private void OpenInBrowser()
        {
            string id = CurrentId;
            if (AlleyInspectorKit.IsVrcId(id, "avtr_")) Application.OpenURL("https://vrchat.com/home/avatar/" + id);
        }
    }
}
