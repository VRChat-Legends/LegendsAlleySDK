using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;

namespace LegendsNexus.Alley.Editor
{
    // the booth builder's kit: an action button you configure in the inspector,
    // a promo code people can copy, a glass material, and the shared panel style
    // (deep blue glass tiles plus spring in motion) that the bundled video
    // player, group button and slideshow wear
    internal static class AlleyBoothKit
    {
        private static readonly Color Panel = new Color(0.17f, 0.22f, 0.58f, 0.92f);
        private static readonly Color Raised = new Color(0.45f, 0.53f, 1f, 0.22f);

        [MenuItem("GameObject/Legends Alley/Action Button", false, 15)]
        private static void SpawnActionButton(MenuCommand command)
        {
            GameObject root = Canvas(command, "Alley Action Button", new Vector2(420f, 140f));
            Image face = Tile(root.transform, "Face", Raised, new Vector2(420f, 140f));
            TMP_Text label = Label(root.transform, "Label", "Press me", new Vector2(380f, 110f), 52f);

            var button = (AlleyActionButton)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyActionButton));
            var motion = (AlleyUiMotion)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyUiMotion));
            button.motion = motion;
            UdonSharpEditorUtility.CopyProxyToUdon(button);

            var ui = face.gameObject.AddComponent<Button>();
            ui.targetGraphic = face;
            UnityEventTools.AddStringPersistentListener(ui.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(button).SendCustomEvent, "Press");
            label.raycastTarget = false;
            Finish(root, "Pick what it does on the Alley Action Button component.");
        }

        [MenuItem("GameObject/Legends Alley/Promo Code", false, 16)]
        private static void SpawnPromoCode(MenuCommand command)
        {
            GameObject root = Canvas(command, "Alley Promo Code", new Vector2(700f, 260f));
            Tile(root.transform, "Back", Panel, new Vector2(700f, 260f));
            TMP_Text title = Label(root.transform, "Title", "Use our code", new Vector2(640f, 70f), 44f);
            ((RectTransform)title.transform).anchoredPosition = new Vector2(0f, 80f);
            TMP_Text hint = Label(root.transform, "Hint", "Tap the code to open the keyboard and copy it", new Vector2(640f, 40f), 24f);
            hint.color = new Color(0.78f, 0.84f, 1f);
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, -95f);

            GameObject fieldGo = TMP_DefaultControls.CreateInputField(new TMP_DefaultControls.Resources());
            fieldGo.name = "Code";
            fieldGo.transform.SetParent(root.transform, false);
            var fieldRect = (RectTransform)fieldGo.transform;
            fieldRect.sizeDelta = new Vector2(560f, 90f);
            fieldRect.anchoredPosition = new Vector2(0f, -8f);
            fieldGo.GetComponent<Image>().color = Raised;
            var field = fieldGo.GetComponent<TMP_InputField>();
            field.text = "YOURCODE";
            if (field.textComponent != null)
            {
                field.textComponent.fontSize = 44f;
                field.textComponent.color = Color.white;
                field.textComponent.alignment = TextAlignmentOptions.Center;
            }

            var copy = (AlleyCopyField)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyCopyField));
            var motion = (AlleyUiMotion)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyUiMotion));
            copy.field = field;
            copy.text = "YOURCODE";
            copy.motion = motion;
            UdonSharpEditorUtility.CopyProxyToUdon(copy);
            UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(copy);
            UnityEventTools.AddStringPersistentListener(field.onSelect, backing.SendCustomEvent, "Tapped");
            UnityEventTools.AddStringPersistentListener(field.onValueChanged, backing.SendCustomEvent, "Restore");
            UnityEventTools.AddStringPersistentListener(field.onEndEdit, backing.SendCustomEvent, "Restore");
            UnityEventTools.AddStringPersistentListener(field.onDeselect, backing.SendCustomEvent, "Restore");
            Finish(root, "Set the text on the Alley Copy Field component, the field shows it in game.");
        }

        [MenuItem("Assets/Create/Legends Alley/Glass Material", false, 300)]
        private static void CreateGlass()
        {
            string folder = "Assets";
            if (Selection.activeObject != null)
            {
                string selected = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (AssetDatabase.IsValidFolder(selected)) folder = selected;
                else if (!string.IsNullOrEmpty(selected)) folder = System.IO.Path.GetDirectoryName(selected).Replace('\\', '/');
            }
            var material = new Material(Shader.Find("LegendsAlley/Glass"));
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/Alley Glass.mat");
            AssetDatabase.CreateAsset(material, path);
            Selection.activeObject = material;
        }

        [MenuItem("Tools/Legends Alley/Apply Booth UI Style To Selection", false, 40)]
        private static void StyleSelection()
        {
            foreach (GameObject go in Selection.gameObjects) Apply(go);
        }

        // deep blue panels, soft raised tiles for buttons, white text, and a
        // spring in on every canvas with a press bounce on every button
        public static void Apply(GameObject root)
        {
            if (root == null) return;
            foreach (Canvas canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.renderMode != RenderMode.WorldSpace) continue;
                var motion = canvas.GetComponent<AlleyUiMotion>();
                if (motion == null) motion = (AlleyUiMotion)UdonSharpComponentExtensions.AddUdonSharpComponent(canvas.gameObject, typeof(AlleyUiMotion));
                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(motion);

                foreach (Image image in canvas.GetComponentsInChildren<Image>(true))
                {
                    if (image.GetComponent<Button>() != null)
                    {
                        if (Dark(image.color)) image.color = Raised;
                        continue;
                    }
                    if (image.GetComponentInParent<Button>() != null) continue;
                    if (Dark(image.color)) image.color = new Color(Panel.r, Panel.g, Panel.b, image.color.a);
                }
                foreach (Button button in canvas.GetComponentsInChildren<Button>(true))
                {
                    if (backing == null) break;
                    bool wired = false;
                    for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    {
                        if (button.onClick.GetPersistentTarget(i) == backing) wired = true;
                    }
                    if (!wired) UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, "Pulse");
                }
                foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (Dark(text.color)) text.color = Color.white;
                }
            }
        }

        private static bool Dark(Color c)
        {
            return c.r + c.g + c.b < 0.45f;
        }

        private static GameObject Canvas(MenuCommand command, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = size;
            go.transform.localScale = Vector3.one * 0.0015f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<CanvasGroup>();
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(size.x, size.y, 1f);
            go.AddComponent<VRCUiShape>();
            return go;
        }

        private static Image Tile(Transform parent, string name, Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AlleyConfig.PackageRoot + "/Runtime/Textures/AlleyRounded.png");
            image.type = Image.Type.Sliced;
            image.color = color;
            ((RectTransform)go.transform).sizeDelta = size;
            return image;
        }

        private static TMP_Text Label(Transform parent, string name, string text, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.enableAutoSizing = true;
            label.fontSizeMin = fontSize * 0.5f;
            label.fontSizeMax = fontSize;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            ((RectTransform)go.transform).sizeDelta = size;
            return label;
        }

        private static void Finish(GameObject root, string hint)
        {
            Undo.RegisterCreatedObjectUndo(root, "Create " + root.name);
            Selection.activeGameObject = root;
            Debug.Log("[LegendsAlley] " + root.name + " added. " + hint);
        }
    }
}
