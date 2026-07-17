using System;
using System.Linq;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.Udon;

namespace LegendsNexus.Alley.Editor
{
    // builds and refills the physical booth directory board in event maps.
    // rows come straight from the BoothLocation plots in the open scenes, so
    // the board is rebuilt for free at the end of every sync
    internal static class AlleyDirectoryBuilder
    {
        private const string LogoFolder = "Assets/LegendsAlley/Booths/Logos";

        private static readonly Color32 Pink = new Color32(255, 0, 122, 255);
        private static readonly Color32 Purple = new Color32(107, 70, 193, 255);
        private static readonly Color32 Teal = new Color32(31, 209, 237, 255);
        private static readonly Color32 Gold = new Color32(255, 215, 0, 255);
        private static readonly Color32 CardDark = new Color32(16, 18, 22, 250);
        private static readonly Color32 RowDark = new Color32(31, 35, 43, 255);
        private static readonly Color32 TextDim = new Color32(154, 160, 166, 255);

        [MenuItem("GameObject/Legends Alley/Booth Directory Board", false, 13)]
        private static void SpawnBoard(MenuCommand command)
        {
            AlleyPrefabBuilder.EnsureProgramAsset();
            GameObject board = BuildBoard();
            GameObjectUtility.SetParentAndAlign(board, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(board, "Create Booth Directory Board");
            Selection.activeGameObject = board;
            Rebuild(board.GetComponent<AlleyDirectoryBoard>(), msg => Debug.Log("[LegendsAlley] " + msg));
        }

        [MenuItem("Tools/Legends Alley/Rebuild Booth Directories")]
        private static void RebuildMenu()
        {
            RebuildAll(msg => Debug.Log("[LegendsAlley] " + msg));
        }

        // called by the importer after every sync, safe when no board exists
        public static void RebuildAll(Action<string> log)
        {
            AlleyDirectoryBoard[] boards = UnityEngine.Object.FindObjectsOfType<AlleyDirectoryBoard>(true);
            if (boards.Length == 0) return;
            AlleyPrefabBuilder.EnsureProgramAsset();
            foreach (AlleyDirectoryBoard board in boards) Rebuild(board, log);
        }

        public static void Rebuild(AlleyDirectoryBoard board, Action<string> log)
        {
            if (board == null || board.listContent == null || board.anchorsRoot == null)
            {
                log("Directory board is missing its wiring, delete it and add a fresh one from the GameObject menu.");
                return;
            }

            for (int i = board.listContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(board.listContent.GetChild(i).gameObject);
            for (int i = board.anchorsRoot.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(board.anchorsRoot.GetChild(i).gameObject);

            BoothLocation[] occupied = BoothImporter.FindLocations()
                .Where(l => !string.IsNullOrEmpty(l.placedCommunityId))
                .OrderBy(l => l.PlotLabel, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>(AlleyConfig.PackageRoot + "/Runtime/Textures/AlleyRounded.png");
            Sprite disc = AssetDatabase.LoadAssetAtPath<Sprite>(AlleyConfig.PackageRoot + "/Runtime/Textures/AlleyDisc.png");

            foreach (BoothLocation plot in occupied)
                AddRow(board, plot, rounded, disc);

            if (board.emptyState != null) board.emptyState.SetActive(occupied.Length == 0);

            EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
            log($"Directory board lists {occupied.Length} booth(s).");
        }

        /* ─── rows ─── */

        private static void AddRow(AlleyDirectoryBoard board, BoothLocation plot, Sprite rounded, Sprite disc)
        {
            // teleport anchor out in the aisle, facing back at the booth
            var anchor = new GameObject($"Anchor {plot.PlotLabel}");
            anchor.transform.SetParent(board.anchorsRoot, false);
            anchor.transform.position = plot.transform.position + plot.transform.forward * board.teleportDistance;
            anchor.transform.rotation = Quaternion.LookRotation(-plot.transform.forward, Vector3.up);

            var row = new GameObject($"Row {plot.PlotLabel}", typeof(RectTransform));
            row.transform.SetParent(board.listContent, false);
            var layout = row.AddComponent<LayoutElement>();
            layout.minHeight = 150f;
            layout.preferredHeight = 150f;

            Image bg = AddImage(row.transform, "Bg", rounded, RowDark, Vector2.zero);
            bg.type = Image.Type.Sliced;
            bg.pixelsPerUnitMultiplier = 0.8f;
            bg.raycastTarget = true; // dragging a row scrolls the list
            Stretch((RectTransform)bg.transform);

            // community icon, logo when the sync grabbed one, tinted disc otherwise
            Sprite logo = EnsureLogoSprite(plot.placedCommunityId);
            if (logo != null)
            {
                Image icon = AddImage(row.transform, "Icon", logo, Color.white, new Vector2(110f, 110f));
                icon.preserveAspect = true;
                ((RectTransform)icon.transform).anchoredPosition = new Vector2(-395f, 0f);
            }
            else
            {
                Image holder = AddImage(row.transform, "Icon", disc, Purple, new Vector2(110f, 110f));
                ((RectTransform)holder.transform).anchoredPosition = new Vector2(-395f, 0f);
                string initial = string.IsNullOrEmpty(plot.placedCommunityName) ? "?" : plot.placedCommunityName.Substring(0, 1).ToUpperInvariant();
                TMP_Text letter = AddLabel(holder.transform, "Letter", initial, new Vector2(110f, 110f), 40f, 64f, Color.white);
                ((RectTransform)letter.transform).anchoredPosition = Vector2.zero;
            }

            TMP_Text name = AddLabel(row.transform, "Name", plot.placedCommunityName, new Vector2(380f, 60f), 26f, 42f, Color.white);
            name.alignment = TextAlignmentOptions.Left;
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            ((RectTransform)name.transform).anchoredPosition = new Vector2(-120f, 26f);

            TMP_Text plotLabel = AddLabel(row.transform, "Plot", "PLOT " + plot.PlotLabel.ToUpperInvariant(), new Vector2(380f, 40f), 18f, 26f, TextDim);
            plotLabel.alignment = TextAlignmentOptions.Left;
            ((RectTransform)plotLabel.transform).anchoredPosition = new Vector2(-120f, -34f);

            // the little udon brain for this row
            var proxy = (AlleyDirectoryEntry)UdonSharpComponentExtensions.AddUdonSharpComponent(row, typeof(AlleyDirectoryEntry));
            UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            proxy.teleportTarget = anchor.transform;
            proxy.groupId = plot.placedGroupId ?? "";
            UdonSharpEditorUtility.CopyProxyToUdon(proxy);

            AddRowButton(row.transform, rounded, "Teleport", "TELEPORT", Teal, new Color32(10, 12, 15, 255),
                new Vector2(205f, 0f), new Vector2(190f, 92f), backing, "OnTeleport");

            Button join = AddRowButton(row.transform, rounded, "Join", "JOIN", Pink, Color.white,
                new Vector2(395f, 0f), new Vector2(150f, 92f), backing, "OnJoin");
            if (string.IsNullOrEmpty(plot.placedGroupId)) join.gameObject.SetActive(false);
        }

        private static Button AddRowButton(Transform row, Sprite rounded, string name, string text,
            Color32 fill, Color textColor, Vector2 position, Vector2 size, UdonBehaviour backing, string eventName)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(row, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image face = go.AddComponent<Image>();
            face.sprite = rounded;
            face.type = Image.Type.Sliced;
            face.pixelsPerUnitMultiplier = 1.1f;
            face.color = fill;
            face.raycastTarget = true;

            TMP_Text label = AddLabel(go.transform, "Label", text, new Vector2(size.x - 20f, size.y - 20f), 20f, 34f, textColor);
            ((RectTransform)label.transform).anchoredPosition = Vector2.zero;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.86f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            button.colors = colors;
            UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, eventName);
            return button;
        }

        // logos land in a fixed spot during sync, keyed by community id
        private static Sprite EnsureLogoSprite(string communityId)
        {
            if (string.IsNullOrEmpty(communityId)
                || !communityId.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')) return null;
            string path = LogoFolder + "/" + communityId + ".png";
            if (!System.IO.File.Exists(path)) return null;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /* ─── the physical board ─── */

        private static GameObject BuildBoard()
        {
            Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>(AlleyConfig.PackageRoot + "/Runtime/Textures/AlleyRounded.png");
            var root = new GameObject("Booth Directory Board");
            var marker = root.AddComponent<AlleyDirectoryBoard>();

            var anchors = new GameObject("Teleport Anchors");
            anchors.transform.SetParent(root.transform, false);
            marker.anchorsRoot = anchors.transform;

            // frame: posts, panel, brand strip, matches the kit look
            Material dark = FrameMaterial("AlleyBoardDark", new Color(0.055f, 0.06f, 0.07f), 0.35f);
            Material pink = FrameMaterial("AlleyBoardPink", new Color(1f, 0f, 0.48f), 0.25f);
            Material purple = FrameMaterial("AlleyBoardPurple", new Color(0.42f, 0.27f, 0.76f), 0.25f);
            Material teal = FrameMaterial("AlleyBoardTeal", new Color(0.12f, 0.82f, 0.93f), 0.25f);
            Material gold = FrameMaterial("AlleyBoardGold", new Color(1f, 0.84f, 0f), 0.25f);

            FrameCube(root, "Post L", new Vector3(-1.32f, 1.8f, -0.02f), new Vector3(0.18f, 3.6f, 0.18f), dark);
            FrameCube(root, "Post R", new Vector3(1.32f, 1.8f, -0.02f), new Vector3(0.18f, 3.6f, 0.18f), dark);
            FrameCube(root, "Cap L", new Vector3(-1.32f, 3.64f, -0.02f), new Vector3(0.24f, 0.12f, 0.24f), pink);
            FrameCube(root, "Cap R", new Vector3(1.32f, 3.64f, -0.02f), new Vector3(0.24f, 0.12f, 0.24f), pink);
            FrameCube(root, "Panel", new Vector3(0f, 1.95f, 0.06f), new Vector3(2.5f, 3.15f, 0.1f), dark);
            FrameCube(root, "Base Trim", new Vector3(0f, 0.42f, 0.06f), new Vector3(2.56f, 0.05f, 0.12f), pink);

            float stripY = 3.55f;
            FrameCube(root, "Strip Pink", new Vector3(-0.59f, stripY, 0.06f), new Vector3(1.18f, 0.08f, 0.12f), pink);
            FrameCube(root, "Strip Purple", new Vector3(0.236f, stripY, 0.06f), new Vector3(0.472f, 0.08f, 0.12f), purple);
            FrameCube(root, "Strip Teal", new Vector3(0.649f, stripY, 0.06f), new Vector3(0.354f, 0.08f, 0.12f), teal);
            FrameCube(root, "Strip Gold", new Vector3(1.003f, stripY, 0.06f), new Vector3(0.354f, 0.08f, 0.12f), gold);

            // ui canvas floating just in front of the panel face
            var canvasGo = new GameObject("Directory Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root.transform, false);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.AddComponent<VRCUiShape>();
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(1200f, 1500f);
            canvasRect.localScale = Vector3.one * 0.002f;
            canvasRect.localPosition = new Vector3(0f, 1.95f, 0.115f);
            canvasRect.localRotation = Quaternion.Euler(0f, 180f, 0f);

            TMP_Text title = AddLabel(canvasGo.transform, "Title", "BOOTH DIRECTORY", new Vector2(1000f, 90f), 48f, 72f, Color.white);
            ((RectTransform)title.transform).anchoredPosition = new Vector2(0f, 650f);
            title.characterSpacing = 10f;

            TMP_Text subtitle = AddLabel(canvasGo.transform, "Subtitle", "TELEPORT STRAIGHT TO ANY BOOTH", new Vector2(1000f, 46f), 22f, 30f, Pink);
            ((RectTransform)subtitle.transform).anchoredPosition = new Vector2(0f, 585f);
            subtitle.characterSpacing = 8f;

            Image divider = AddImage(canvasGo.transform, "Divider", null, new Color32(42, 45, 51, 255), new Vector2(1080f, 3f));
            ((RectTransform)divider.transform).anchoredPosition = new Vector2(0f, 545f);

            // scroll area
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(canvasGo.transform, false);
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.anchoredPosition = new Vector2(-20f, -105f);
            scrollRect.sizeDelta = new Vector2(1060f, 1240f);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRect = (RectTransform)viewportGo.transform;
            Stretch(viewportRect);
            viewportGo.AddComponent<RectMask2D>();
            Image viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.004f); // invisible drag surface
            viewportImage.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0.5f, 1f);
            content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(1020f, 0f);
            var vertical = contentGo.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 16f;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            vertical.padding = new RectOffset(0, 0, 8, 8);
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // scrollbar
            var barGo = new GameObject("Scrollbar", typeof(RectTransform));
            barGo.transform.SetParent(canvasGo.transform, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchoredPosition = new Vector2(556f, -105f);
            barRect.sizeDelta = new Vector2(26f, 1240f);
            Image track = barGo.AddComponent<Image>();
            track.sprite = rounded;
            track.type = Image.Type.Sliced;
            track.pixelsPerUnitMultiplier = 2.5f;
            track.color = new Color32(30, 33, 41, 255);

            var slideGo = new GameObject("Sliding Area", typeof(RectTransform));
            slideGo.transform.SetParent(barGo.transform, false);
            var slideRect = (RectTransform)slideGo.transform;
            Stretch(slideRect);
            slideRect.sizeDelta = new Vector2(-8f, -8f);

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(slideGo.transform, false);
            Image handle = handleGo.AddComponent<Image>();
            handle.sprite = rounded;
            handle.type = Image.Type.Sliced;
            handle.pixelsPerUnitMultiplier = 2.5f;
            handle.color = Pink;
            handle.raycastTarget = true;
            Stretch((RectTransform)handleGo.transform);

            var scrollbar = barGo.AddComponent<Scrollbar>();
            scrollbar.handleRect = (RectTransform)handleGo.transform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // empty state
            TMP_Text empty = AddLabel(viewportGo.transform, "Empty State", "NO BOOTHS PLACED YET\n<size=60%>run a booth sync and this board fills itself</size>", new Vector2(900f, 300f), 30f, 44f, TextDim);
            ((RectTransform)empty.transform).anchoredPosition = new Vector2(0f, 60f);

            // footer brand
            TMP_Text footer = AddLabel(canvasGo.transform, "Footer", "LEGENDS ALLEY", new Vector2(600f, 40f), 18f, 24f, TextDim);
            ((RectTransform)footer.transform).anchoredPosition = new Vector2(0f, -742f);
            footer.characterSpacing = 14f;

            marker.listContent = content;
            marker.emptyState = empty.gameObject;
            return root;
        }

        private static Material FrameMaterial(string name, Color color, float gloss)
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

        private static void FrameCube(GameObject root, string name, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        /* ─── small ui helpers, same shapes as the prefab builder ─── */

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Image AddImage(Transform parent, string name, Sprite sprite, Color color, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            ((RectTransform)go.transform).sizeDelta = size;
            return image;
        }

        private static TMP_Text AddLabel(Transform parent, string name, string text, Vector2 size, float minSize, float maxSize, Color color)
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
