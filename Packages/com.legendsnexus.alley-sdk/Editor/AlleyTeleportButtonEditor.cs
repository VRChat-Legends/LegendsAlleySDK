using UdonSharpEditor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleyTeleportButton))]
    public class AlleyTeleportButtonEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "purple", "TELEPORT BUTTON");

            // keeps the usharp compile state and conversion handling working
            card.Add(new IMGUIContainer(() => UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)));

            var destination = new ObjectField("Destination")
            {
                objectType = typeof(Transform),
                allowSceneObjects = true,
            };
            destination.BindProperty(serializedObject.FindProperty(nameof(AlleyTeleportButton.destination)));
            card.Add(destination);

            var back = new ObjectField("Return spot (optional)")
            {
                objectType = typeof(Transform),
                allowSceneObjects = true,
            };
            back.BindProperty(serializedObject.FindProperty(nameof(AlleyTeleportButton.returnPoint)));
            card.Add(back);

            var keep = new Toggle("Keep player facing");
            keep.BindProperty(serializedObject.FindProperty(nameof(AlleyTeleportButton.keepPlayerRotation)));
            card.Add(keep);

            Label status = AlleyInspectorKit.MakeStatus(card);
            UpdateStatus(status);
            root.TrackSerializedObjectValue(serializedObject, _ => UpdateStatus(status));

            return root;
        }

        private void UpdateStatus(Label status)
        {
            var button = (AlleyTeleportButton)target;

            if (button.GetComponent<Collider>() == null)
            {
                AlleyInspectorKit.SetStatus(status,
                    "This object has no collider, so nobody can press it. Add a Box Collider roughly the size of the button.",
                    "warn");
                return;
            }

            if (button.destination == null)
            {
                AlleyInspectorKit.SetStatus(status,
                    "Point Destination at an empty object where people should land. Keep it inside your booth, the checker flags markers outside.",
                    "empty");
                return;
            }

            AlleyInspectorKit.SetStatus(status, button.returnPoint != null
                ? "Pressing hops people between the two spots, back and forth. Teleports are local, only the person pressing moves."
                : "Pressing sends people to the destination marker. Teleports are local, only the person pressing moves.", null);
        }
    }
}
