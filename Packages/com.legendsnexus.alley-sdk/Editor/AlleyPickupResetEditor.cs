using UdonSharpEditor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleyPickupReset))]
    public class AlleyPickupResetEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "gold", "PICKUP RESET");

            // keeps the usharp compile state and conversion handling working
            card.Add(new IMGUIContainer(() => UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)));

            var pickups = new PropertyField(serializedObject.FindProperty(nameof(AlleyPickupReset.pickups)), "Pickups");
            pickups.Bind(serializedObject);
            card.Add(pickups);

            var users = new PropertyField(serializedObject.FindProperty(nameof(AlleyPickupReset.allowedUsers)), "Allowed users");
            users.Bind(serializedObject);
            card.Add(users);

            var denied = new ObjectField("Denied label (optional)")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
            };
            denied.BindProperty(serializedObject.FindProperty(nameof(AlleyPickupReset.deniedIndicator)));
            card.Add(denied);

            var seconds = new Slider("Denied label time (s)", 1f, 10f) { showInputField = true };
            seconds.BindProperty(serializedObject.FindProperty(nameof(AlleyPickupReset.deniedSeconds)));
            card.Add(seconds);

            Label status = AlleyInspectorKit.MakeStatus(card);
            UpdateStatus(status);
            root.TrackSerializedObjectValue(serializedObject, _ => UpdateStatus(status));

            return root;
        }

        private void UpdateStatus(Label status)
        {
            var reset = (AlleyPickupReset)target;

            if (reset.GetComponent<Collider>() == null)
            {
                AlleyInspectorKit.SetStatus(status,
                    "This object has no collider, so nobody can press it. Add a Box Collider roughly the size of the button.",
                    "warn");
                return;
            }

            int wired = 0, empty = 0;
            if (reset.pickups != null)
            {
                foreach (var sync in reset.pickups)
                {
                    if (sync == null) empty++;
                    else wired++;
                }
            }

            if (wired == 0)
            {
                AlleyInspectorKit.SetStatus(status,
                    "Drop your pickups into the list. Each one needs a VRC Object Sync component, that is what sends it home.",
                    "empty");
                return;
            }

            if (empty > 0)
            {
                AlleyInspectorKit.SetStatus(status,
                    $"There {(empty == 1 ? "is an empty slot" : "are " + empty + " empty slots")} in the pickups list. Fill or remove them.",
                    "warn");
                return;
            }

            bool locked = false;
            if (reset.allowedUsers != null)
            {
                foreach (string name in reset.allowedUsers)
                {
                    if (!string.IsNullOrWhiteSpace(name)) { locked = true; break; }
                }
            }

            AlleyInspectorKit.SetStatus(status, locked
                ? $"Pressing sends {wired} pickup{(wired == 1 ? "" : "s")} home, but only for the usernames on the list. Names must match exactly, including capitals."
                : $"Pressing sends {wired} pickup{(wired == 1 ? "" : "s")} home for everyone. Add usernames to the list to lock it to your crew.", null);
        }
    }
}
