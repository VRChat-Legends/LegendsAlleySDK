using UdonSharpEditor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleyGroupButton))]
    public class AlleyGroupButtonEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "pink", "GROUP BUTTON");

            // keeps the usharp compile state and conversion handling working
            card.Add(new IMGUIContainer(() => UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)));

            var field = new TextField("Group ID");
            field.BindProperty(serializedObject.FindProperty(nameof(AlleyGroupButton.groupId)));
            card.Add(field);

            Label status = AlleyInspectorKit.MakeStatus(card);
            field.RegisterValueChangedCallback(_ => UpdateStatus(status));
            UpdateStatus(status);

            var test = new Button(OpenInBrowser) { text = "TEST LINK IN BROWSER" };
            test.AddToClassList("alley-insp-button-ghost");
            card.Add(test);

            AddCardArtFields(card);

            return root;
        }

        // the art lives on child objects, this saves creators from hunting
        // through the hierarchy just to put their own name and logo on it
        private void AddCardArtFields(VisualElement card)
        {
            var button = (AlleyGroupButton)target;

            if (button.nameLabel != null)
            {
                var nameField = new TextField("Community Name") { value = button.nameLabel.text };
                nameField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(button.nameLabel, "Set Community Name");
                    button.nameLabel.text = evt.newValue;
                    EditorUtility.SetDirty(button.nameLabel);
                });
                card.Add(nameField);
            }

            if (button.logoTarget != null)
            {
                var logoField = new ObjectField("Group Logo")
                {
                    objectType = typeof(Sprite),
                    allowSceneObjects = false,
                    value = button.logoTarget.sprite,
                };
                logoField.RegisterValueChangedCallback(evt =>
                {
                    Undo.RecordObject(button.logoTarget, "Set Group Logo");
                    var sprite = (Sprite)evt.newValue;
                    button.logoTarget.sprite = sprite;
                    // a null sprite would draw a white square over the badge
                    button.logoTarget.enabled = sprite != null;
                    FitLogo(button.logoTarget, sprite);
                    EditorUtility.SetDirty(button.logoTarget);
                });
                card.Add(logoField);
            }
        }

        // grows the logo until it covers the badge circle instead of sitting
        // inside it, the circle mask trims whatever hangs over the edge
        private static void FitLogo(UnityEngine.UI.Image target, Sprite sprite)
        {
            var rect = (RectTransform)target.transform;
            var circle = target.transform.parent as RectTransform;
            float size = circle != null ? Mathf.Min(circle.rect.width, circle.rect.height) : rect.rect.width;

            if (sprite == null || sprite.rect.height <= 0f)
            {
                rect.sizeDelta = new Vector2(size, size);
                return;
            }

            float aspect = sprite.rect.width / sprite.rect.height;
            rect.sizeDelta = new Vector2(size * Mathf.Max(aspect, 1f), size * Mathf.Max(1f / aspect, 1f));
        }

        private string CurrentId => (((AlleyGroupButton)target).groupId ?? "").Trim();

        private void UpdateStatus(Label status)
        {
            string id = CurrentId;
            if (string.IsNullOrEmpty(id))
            {
                AlleyInspectorKit.SetStatus(status,
                    "Paste your group ID. Open your group on the VRChat website and copy the grp_... part from the address bar.",
                    "empty");
            }
            else if (!AlleyInspectorKit.IsVrcId(id, "grp_"))
            {
                AlleyInspectorKit.SetStatus(status,
                    "That doesn't look like a group ID. It should be grp_ followed by 36 characters, like grp_12345678-1234-1234-1234-123456789abc.",
                    "warn");
            }
            else
            {
                AlleyInspectorKit.SetStatus(status,
                    "Pressing the button in game opens your group page so people can join on the spot.", null);
            }
        }

        private void OpenInBrowser()
        {
            string id = CurrentId;
            if (AlleyInspectorKit.IsVrcId(id, "grp_")) Application.OpenURL("https://vrchat.com/home/group/" + id);
        }
    }
}
