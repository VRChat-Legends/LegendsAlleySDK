using UdonSharpEditor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleyAnimationButton))]
    public class AlleyAnimationButtonEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "pink", "ANIMATION BUTTON");

            // keeps the usharp compile state and conversion handling working
            card.Add(new IMGUIContainer(() => UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)));

            var animator = new ObjectField("Animator")
            {
                objectType = typeof(Animator),
                allowSceneObjects = true,
            };
            animator.BindProperty(serializedObject.FindProperty(nameof(AlleyAnimationButton.target)));
            card.Add(animator);

            var trigger = new TextField("Trigger name");
            trigger.BindProperty(serializedObject.FindProperty(nameof(AlleyAnimationButton.triggerName)));
            card.Add(trigger);

            var state = new TextField("State name (fallback)");
            state.BindProperty(serializedObject.FindProperty(nameof(AlleyAnimationButton.stateName)));
            card.Add(state);

            var cooldown = new Slider("Cooldown (s)", 0f, 30f) { showInputField = true };
            cooldown.BindProperty(serializedObject.FindProperty(nameof(AlleyAnimationButton.cooldownSeconds)));
            card.Add(cooldown);

            var busy = new ObjectField("Busy label (optional)")
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
            };
            busy.BindProperty(serializedObject.FindProperty(nameof(AlleyAnimationButton.busyIndicator)));
            card.Add(busy);

            Label status = AlleyInspectorKit.MakeStatus(card);
            UpdateStatus(status);
            root.TrackSerializedObjectValue(serializedObject, _ => UpdateStatus(status));

            return root;
        }

        private void UpdateStatus(Label status)
        {
            var button = (AlleyAnimationButton)target;

            if (button.GetComponent<Collider>() == null)
            {
                AlleyInspectorKit.SetStatus(status,
                    "This object has no collider, so nobody can press it. Add a Box Collider roughly the size of the button.",
                    "warn");
                return;
            }

            if (button.target == null)
            {
                AlleyInspectorKit.SetStatus(status,
                    "Point this at the Animator that holds your animation.",
                    "empty");
                return;
            }

            bool hasTrigger = !string.IsNullOrEmpty(button.triggerName);
            bool hasState = !string.IsNullOrEmpty(button.stateName);
            if (!hasTrigger && !hasState)
            {
                AlleyInspectorKit.SetStatus(status,
                    "Set a trigger name, or a state name like Base Layer.Open, or the button does nothing.",
                    "warn");
                return;
            }

            string action = hasTrigger
                ? $"fires the \"{button.triggerName}\" trigger"
                : $"plays the \"{button.stateName}\" state from the top";
            AlleyInspectorKit.SetStatus(status,
                $"Pressing {action} on {button.target.gameObject.name}. Runs locally for the person pressing, with a {button.cooldownSeconds:0.#}s cooldown.", null);
        }
    }
}
