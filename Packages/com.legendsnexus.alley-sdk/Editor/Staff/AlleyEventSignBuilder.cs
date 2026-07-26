using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

namespace LegendsNexus.Alley.Editor
{
    // event info wall for spawn, same look as the directory kiosk
    internal static class AlleyEventSignBuilder
    {
        private static readonly Color32 Pink = new Color32(255, 0, 122, 255);
        private static readonly Color32 Purple = new Color32(107, 70, 193, 255);
        private static readonly Color32 Teal = new Color32(31, 209, 237, 255);
        private static readonly Color32 Gold = new Color32(255, 215, 0, 255);
        private static readonly Color32 Ink = new Color32(10, 10, 10, 255);
        private static readonly Color32 Bar = new Color32(5, 5, 5, 255);
        private static readonly Color32 CardFill = new Color32(20, 23, 29, 255);
        private static readonly Color32 OnDark = new Color32(214, 217, 222, 255);
        private static readonly Color32 OnBright = new Color32(8, 8, 10, 255);

        private const float PanelWidth = 5.2f;
        private const float PanelHeight = 2.5f;
        private const float PanelCenterY = 1.78f;

        [MenuItem("GameObject/Legends Alley/Event Info Wall", false, 14)]
        private static void SpawnSign(MenuCommand command)
        {
            if (AlleyStaffOnly.Blocked("The event info wall")) return;
            AlleyPrefabBuilder.EnsureProgramAsset();
            GameObject sign = Build();
            GameObjectUtility.SetParentAndAlign(sign, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(sign, "Create Event Info Wall");
            Selection.activeGameObject = sign;
            EditorSceneManager.MarkSceneDirty(sign.scene);
        }

        public static GameObject Build()
        {
            var root = new GameObject("Event Info Wall");
            var marker = root.AddComponent<AlleyEventSign>();

            Material slab = SignMaterial("AlleySignDark", new Color(0.055f, 0.06f, 0.07f), 0.35f);
            Material pink = SignMaterial("AlleySignPink", new Color(1f, 0f, 0.478f), 0.25f);
            Material purple = SignMaterial("AlleySignPurple", new Color(0.42f, 0.27f, 0.76f), 0.25f);
            Material teal = SignMaterial("AlleySignTeal", new Color(0.12f, 0.82f, 0.93f), 0.25f);
            Material gold = SignMaterial("AlleySignGold", new Color(1f, 0.84f, 0f), 0.25f);

            float halfWidth = PanelWidth * 0.5f;
            float panelBottom = PanelCenterY - PanelHeight * 0.5f;
            float panelTop = PanelCenterY + PanelHeight * 0.5f;

            float legX = halfWidth - 0.7f;
            Cube(root, "Leg L", new Vector3(-legX, panelBottom * 0.5f, 0f), new Vector3(0.2f, panelBottom, 0.2f), slab);
            Cube(root, "Leg R", new Vector3(legX, panelBottom * 0.5f, 0f), new Vector3(0.2f, panelBottom, 0.2f), slab);
            Cube(root, "Foot L", new Vector3(-legX, 0.03f, 0f), new Vector3(0.44f, 0.06f, 0.44f), pink);
            Cube(root, "Foot R", new Vector3(legX, 0.03f, 0f), new Vector3(0.44f, 0.06f, 0.44f), pink);

            Cube(root, "Panel", new Vector3(0f, PanelCenterY, 0f), new Vector3(PanelWidth, PanelHeight, 0.14f), slab);
            Cube(root, "Base Trim", new Vector3(0f, panelBottom + 0.03f, 0f), new Vector3(PanelWidth + 0.08f, 0.06f, 0.16f), pink);

            // accent run across the crown, same order as the rule on the face
            float left = -halfWidth;
            float[] shares = { 0.46f, 0.2f, 0.17f, 0.17f };
            Material[] colors = { pink, purple, teal, gold };
            string[] names = { "Strip Pink", "Strip Purple", "Strip Teal", "Strip Gold" };
            for (int i = 0; i < shares.Length; i++)
            {
                float width = PanelWidth * shares[i];
                Cube(root, names[i], new Vector3(left + width * 0.5f, panelTop + 0.05f, 0f),
                    new Vector3(width, 0.1f, 0.16f), colors[i]);
                left += width;
            }

            BuildFace(root, marker);
            return root;
        }

        private static void BuildFace(GameObject root, AlleyEventSign marker)
        {
            var canvasGo = new GameObject("Wall Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root.transform, false);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.AddComponent<VRCUiShape>();
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(3200f, 1520f);
            canvasRect.localScale = Vector3.one * 0.0016f;
            canvasRect.localPosition = new Vector3(0f, PanelCenterY, -0.085f);

            Block(canvasGo.transform, "Backdrop", Ink, new Vector2(3200f, 1520f));

            // title bar across the top
            Image bar = Block(canvasGo.transform, "Title Bar", Bar, new Vector2(3200f, 150f));
            ((RectTransform)bar.transform).anchoredPosition = new Vector2(0f, 685f);

            Image tick = Block(canvasGo.transform, "Title Tick", Pink, new Vector2(16f, 84f));
            ((RectTransform)tick.transform).anchoredPosition = new Vector2(-1540f, 685f);

            TMP_Text title = Label(canvasGo.transform, "Title", "LEGENDS ALLEY", new Vector2(1800f, 110f), 48f, 76f, Color.white);
            title.alignment = TextAlignmentOptions.Left;
            title.characterSpacing = 14f;
            ((RectTransform)title.transform).anchoredPosition = new Vector2(-600f, 685f);

            Image chip = Block(canvasGo.transform, "Event Chip", Pink, new Vector2(880f, 78f));
            ((RectTransform)chip.transform).anchoredPosition = new Vector2(1080f, 685f);
            TMP_Text subtitle = Label(chip.transform, "Subtitle", "BOOTH EVENT", new Vector2(840f, 60f), 26f, 40f, OnBright);
            subtitle.characterSpacing = 10f;

            // accent rule under the bar
            float left = -1600f;
            float[] shares = { 0.46f, 0.2f, 0.17f, 0.17f };
            Color32[] ruleColors = { Pink, Purple, Teal, Gold };
            string[] ruleNames = { "Rule Pink", "Rule Purple", "Rule Teal", "Rule Gold" };
            for (int i = 0; i < shares.Length; i++)
            {
                float width = 3200f * shares[i];
                Image piece = Block(canvasGo.transform, ruleNames[i], ruleColors[i], new Vector2(width, 10f));
                ((RectTransform)piece.transform).anchoredPosition = new Vector2(left + width * 0.5f, 605f);
                left += width;
            }

            // cards: one wide one on the left, a stack of three beside it
            var headings = new TMP_Text[4];
            var bodies = new TMP_Text[4];

            Card(canvasGo.transform, "Schedule", "EVENT SCHEDULE", Pink, Color.white,
                new Vector2(1740f, 1180f), new Vector2(-720f, -50f), 32f, 42f, out headings[0], out bodies[0]);
            Card(canvasGo.transform, "Partners", "PARTNERS", Purple, Color.white,
                new Vector2(1380f, 376f), new Vector2(870f, 352f), 26f, 34f, out headings[1], out bodies[1]);
            Card(canvasGo.transform, "Sponsors", "SPONSORS", Teal, OnBright,
                new Vector2(1380f, 376f), new Vector2(870f, -50f), 26f, 34f, out headings[2], out bodies[2]);
            Card(canvasGo.transform, "Crew", "EVENT CREW", Gold, OnBright,
                new Vector2(1380f, 376f), new Vector2(870f, -452f), 26f, 34f, out headings[3], out bodies[3]);

            marker.titleLabels = new[] { title };
            marker.subtitleLabels = new[] { subtitle };
            marker.panelHeadings = headings;
            marker.panelBodies = bodies;
            marker.panels = new[]
            {
                new AlleySignPanel
                {
                    heading = "Event Schedule",
                    body = "Loading the schedule from the alley site.",
                },
                new AlleySignPanel { heading = "Partners", body = "Everyone helping put the event on." },
                new AlleySignPanel { heading = "Sponsors", body = "Anyone backing the event." },
                new AlleySignPanel { heading = "Event Crew", body = "Loading the crew list from the alley site." },
            };
            marker.Apply();

            // schedule and crew come down live so staff can retime without a rebuild
            var feed = (AlleySignFeed)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleySignFeed));
            UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(feed);
            backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            feed.scheduleUrl = new VRCUrl(AlleyConfig.DefaultApiBase + "/api/public/sign/current/schedule.txt");
            feed.crewUrl = new VRCUrl(AlleyConfig.DefaultApiBase + "/api/public/sign/current/crew.txt");
            feed.eventNameLabel = (TextMeshProUGUI)subtitle;
            feed.scheduleLabel = (TextMeshProUGUI)bodies[0];
            feed.crewLabel = (TextMeshProUGUI)bodies[3];
            UdonSharpEditorUtility.CopyProxyToUdon(feed);
        }

        // one card: solid colour heading bar, flat body underneath
        private static void Card(Transform parent, string name, string heading, Color32 accent, Color headingColor,
            Vector2 size, Vector2 position, float bodyMin, float bodyMax, out TMP_Text headingLabel, out TMP_Text bodyLabel)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            // thin outline behind the fill, tinted from the heading colour
            Image edge = go.AddComponent<Image>();
            edge.color = Color.Lerp(accent, Color.black, 0.55f);
            edge.raycastTarget = false;

            Image body = Block(go.transform, "Fill", CardFill, new Vector2(size.x - 8f, size.y - 8f));

            Image head = Block(go.transform, "Head", accent, new Vector2(size.x - 8f, 70f));
            Pin((RectTransform)head.transform, new Vector2(0.5f, 1f), new Vector2(0f, -39f));

            headingLabel = Label(go.transform, "Heading", heading, new Vector2(size.x - 60f, 50f), 24f, 32f, headingColor);
            headingLabel.characterSpacing = 12f;
            Pin((RectTransform)headingLabel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -39f));

            bodyLabel = Label(go.transform, "Body", "", new Vector2(size.x - 90f, size.y - 130f), bodyMin, bodyMax, OnDark);
            bodyLabel.alignment = TextAlignmentOptions.TopLeft;
            bodyLabel.fontStyle = FontStyles.Normal;
            bodyLabel.characterSpacing = 0f;
            bodyLabel.lineSpacing = 10f;
            bodyLabel.enableWordWrapping = true;
            bodyLabel.overflowMode = TextOverflowModes.Truncate;
            Pin((RectTransform)bodyLabel.transform, new Vector2(0.5f, 1f), new Vector2(0f, -70f - (size.y - 130f) * 0.5f - 15f));
        }

        private static Material SignMaterial(string name, Color color, float gloss)
        {
            string path = "Assets/LegendsAlley/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            if (!AssetDatabase.IsValidFolder("Assets/LegendsAlley")) AssetDatabase.CreateFolder("Assets", "LegendsAlley");
            var material = new Material(Shader.Find("Standard"));
            material.color = color;
            material.SetFloat("_Glossiness", gloss);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void Cube(GameObject root, string name, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void Pin(RectTransform rect, Vector2 anchor, Vector2 offset)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = offset;
        }

        private static Image Block(Transform parent, string name, Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            ((RectTransform)go.transform).sizeDelta = size;
            return image;
        }

        private static TMP_Text Label(Transform parent, string name, string text, Vector2 size, float minSize, float maxSize, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = text;
            label.color = color;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = true;
            label.fontSizeMin = minSize;
            label.fontSizeMax = maxSize;
            label.characterSpacing = 4f;
            label.raycastTarget = false;
            ((RectTransform)go.transform).sizeDelta = size;
            return label;
        }
    }
}
