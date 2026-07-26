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
        private const float RowHeight = 104f;

        // straight off the sdk window: flat blocks, hard edges, one loud accent
        private static readonly Color32 Pink = new Color32(255, 0, 122, 255);
        private static readonly Color32 Purple = new Color32(107, 70, 193, 255);
        private static readonly Color32 Teal = new Color32(31, 209, 237, 255);
        private static readonly Color32 Gold = new Color32(255, 215, 0, 255);
        private static readonly Color32 Ink = new Color32(10, 10, 10, 255);
        private static readonly Color32 Bar = new Color32(5, 5, 5, 255);
        private static readonly Color32 Card = new Color32(13, 14, 17, 255);
        private static readonly Color32 RowIdle = new Color32(20, 22, 26, 255);
        private static readonly Color32 RowActive = new Color32(39, 27, 54, 255);
        private static readonly Color32 Line = new Color32(42, 45, 51, 255);
        private static readonly Color32 TextDim = new Color32(154, 160, 166, 255);
        private static readonly Color32 LabelIdle = new Color32(214, 217, 222, 255);

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
            if (board == null || board.listContent == null || board.anchorsRoot == null || board.kiosk == null)
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
                .OrderBy(l => l.placedCommunityName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AlleyDirectoryKiosk kiosk = board.kiosk;
            UdonBehaviour kioskBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(kiosk);

            int count = occupied.Length;
            var fills = new Image[count];
            var markers = new Image[count];
            var labels = new TextMeshProUGUI[count];
            var names = new string[count];
            var plots = new string[count];
            var bodies = new string[count];
            var groupIds = new string[count];
            var logos = new Sprite[count];
            var targets = new Transform[count];

            for (int i = 0; i < count; i++)
            {
                BoothLocation plot = occupied[i];
                names[i] = plot.placedCommunityName;
                plots[i] = plot.PlotLabel.ToUpperInvariant();
                bodies[i] = Tidy(plot.placedDescription);
                groupIds[i] = plot.placedGroupId ?? "";
                logos[i] = EnsureLogoSprite(plot.placedCommunityId);
                targets[i] = AddAnchor(board, plot);
                AddRow(board, kiosk, kioskBacking, i, plot, logos[i], out fills[i], out markers[i], out labels[i]);
            }

            kiosk.rowFills = fills;
            kiosk.rowMarkers = markers;
            kiosk.rowLabels = labels;
            kiosk.boothNames = names;
            kiosk.boothPlots = plots;
            kiosk.boothBodies = bodies;
            kiosk.boothGroupIds = groupIds;
            kiosk.boothLogos = logos;
            kiosk.boothTargets = targets;
            UdonSharpEditorUtility.CopyProxyToUdon(kiosk);

            if (board.emptyState != null) board.emptyState.SetActive(count == 0);
            if (board.countLabel != null) board.countLabel.text = count == 1 ? "1 BOOTH" : count + " BOOTHS";

            EditorSceneManager.MarkSceneDirty(board.gameObject.scene);
            log($"Directory board lists {count} booth(s).");
        }

        // descriptions come straight from the community profile, so keep the
        // board readable instead of letting a wall of text run off the panel
        private static string Tidy(string description)
        {
            if (string.IsNullOrEmpty(description)) return "";
            string trimmed = description.Replace("\r\n", "\n").Trim();
            return trimmed.Length <= 700 ? trimmed : trimmed.Substring(0, 697).TrimEnd() + "...";
        }

        /* ─── legend rows ─── */

        // teleport anchor out in the aisle, facing back at the booth
        private static Transform AddAnchor(AlleyDirectoryBoard board, BoothLocation plot)
        {
            var anchor = new GameObject($"Anchor {plot.PlotLabel}");
            anchor.transform.SetParent(board.anchorsRoot, false);
            anchor.transform.position = plot.transform.position + plot.transform.forward * board.teleportDistance;
            anchor.transform.rotation = Quaternion.LookRotation(-plot.transform.forward, Vector3.up);
            return anchor.transform;
        }

        private static void AddRow(AlleyDirectoryBoard board, AlleyDirectoryKiosk kiosk, UdonBehaviour kioskBacking,
            int index, BoothLocation plot, Sprite logo,
            out Image fill, out Image marker, out TextMeshProUGUI label)
        {
            var row = new GameObject($"Row {index:00} {plot.placedCommunityName}", typeof(RectTransform));
            row.transform.SetParent(board.listContent, false);
            var layout = row.AddComponent<LayoutElement>();
            layout.minHeight = RowHeight;
            layout.preferredHeight = RowHeight;

            fill = Block(row.transform, "Fill", RowIdle, Vector2.zero);
            fill.raycastTarget = true; // dragging a row scrolls the list
            Stretch((RectTransform)fill.transform);

            // hard edged marker down the leading edge, lights up on the pick
            marker = Block(row.transform, "Marker", Line, new Vector2(12f, RowHeight));
            Pin((RectTransform)marker.transform, new Vector2(0f, 0.5f), new Vector2(6f, 0f));

            if (logo != null)
            {
                Image icon = Block(row.transform, "Logo", Color.white, new Vector2(66f, 66f));
                icon.sprite = logo;
                icon.preserveAspect = true;
                Pin((RectTransform)icon.transform, new Vector2(0f, 0.5f), new Vector2(70f, 0f));
            }
            else
            {
                Image holder = Block(row.transform, "Logo", Purple, new Vector2(66f, 66f));
                Pin((RectTransform)holder.transform, new Vector2(0f, 0.5f), new Vector2(70f, 0f));
                TMP_Text initial = Label(holder.transform, "Letter", Initial(plot.placedCommunityName),
                    new Vector2(66f, 66f), 28f, 38f, Color.white);
                initial.alignment = TextAlignmentOptions.Center;
            }

            label = (TextMeshProUGUI)Label(row.transform, "Name", plot.placedCommunityName,
                new Vector2(560f, 48f), 22f, 30f, LabelIdle);
            label.alignment = TextAlignmentOptions.Left;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            Pin((RectTransform)label.transform, new Vector2(0f, 0.5f), new Vector2(400f, 0f));

            TMP_Text plotTag = Label(row.transform, "Plot", plot.PlotLabel.ToUpperInvariant(),
                new Vector2(200f, 40f), 16f, 22f, TextDim);
            plotTag.alignment = TextAlignmentOptions.Right;
            Pin((RectTransform)plotTag.transform, new Vector2(1f, 0.5f), new Vector2(-130f, 0f));

            // the little udon brain for this row, it just hands its index over
            var proxy = (AlleyDirectoryEntry)UdonSharpComponentExtensions.AddUdonSharpComponent(row, typeof(AlleyDirectoryEntry));
            UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
            proxy.kiosk = kiosk;
            proxy.index = index;
            UdonSharpEditorUtility.CopyProxyToUdon(proxy);

            var button = row.AddComponent<Button>();
            button.targetGraphic = fill;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.35f, 1.35f, 1.45f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.9f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;
            UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, "OnSelect");
        }

        private static string Initial(string name)
        {
            return string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
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
            var root = new GameObject("Booth Directory Board");
            var marker = root.AddComponent<AlleyDirectoryBoard>();

            var anchors = new GameObject("Teleport Anchors");
            anchors.transform.SetParent(root.transform, false);
            marker.anchorsRoot = anchors.transform;

            // frame: slab on two legs with a brand strip across the top
            Material dark = FrameMaterial("AlleyBoardDark", new Color(0.055f, 0.06f, 0.07f), 0.35f);
            Material pink = FrameMaterial("AlleyBoardPink", new Color(1f, 0f, 0.48f), 0.25f);
            Material purple = FrameMaterial("AlleyBoardPurple", new Color(0.42f, 0.27f, 0.76f), 0.25f);
            Material teal = FrameMaterial("AlleyBoardTeal", new Color(0.12f, 0.82f, 0.93f), 0.25f);
            Material gold = FrameMaterial("AlleyBoardGold", new Color(1f, 0.84f, 0f), 0.25f);

            FrameCube(root, "Leg L", new Vector3(-1.16f, 0.32f, -0.02f), new Vector3(0.18f, 0.64f, 0.18f), dark);
            FrameCube(root, "Leg R", new Vector3(1.16f, 0.32f, -0.02f), new Vector3(0.18f, 0.64f, 0.18f), dark);
            FrameCube(root, "Foot L", new Vector3(-1.16f, 0.03f, -0.02f), new Vector3(0.38f, 0.06f, 0.38f), pink);
            FrameCube(root, "Foot R", new Vector3(1.16f, 0.03f, -0.02f), new Vector3(0.38f, 0.06f, 0.38f), pink);
            FrameCube(root, "Panel", new Vector3(0f, 1.55f, 0.06f), new Vector3(2.9f, 1.82f, 0.12f), dark);
            FrameCube(root, "Base Trim", new Vector3(0f, 0.67f, 0.06f), new Vector3(2.98f, 0.05f, 0.14f), pink);

            // strip across the crown. the canvas in front of it is flipped a half
            // turn, so the blocks run the other way to read the same from the front
            float stripY = 2.49f;
            float left = -1.45f;
            float[] shares = { 0.17f, 0.17f, 0.2f, 0.46f };
            Material[] stripColors = { gold, teal, purple, pink };
            string[] stripNames = { "Strip Gold", "Strip Teal", "Strip Purple", "Strip Pink" };
            for (int i = 0; i < shares.Length; i++)
            {
                float width = 2.9f * shares[i];
                FrameCube(root, stripNames[i], new Vector3(left + width * 0.5f, stripY, 0.06f),
                    new Vector3(width, 0.07f, 0.14f), stripColors[i]);
                left += width;
            }

            // ui canvas floating just in front of the panel face
            var canvasGo = new GameObject("Directory Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(root.transform, false);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<GraphicRaycaster>();
            canvasGo.AddComponent<VRCUiShape>();
            var canvasRect = (RectTransform)canvasGo.transform;
            canvasRect.sizeDelta = new Vector2(2240f, 1400f);
            canvasRect.localScale = Vector3.one * 0.00125f;
            canvasRect.localPosition = new Vector3(0f, 1.55f, 0.125f);
            canvasRect.localRotation = Quaternion.Euler(0f, 180f, 0f);

            Block(canvasGo.transform, "Backdrop", Ink, Vector2.zero)
                .rectTransform.sizeDelta = new Vector2(2240f, 1400f);

            BuildHeader(canvasGo.transform, marker);
            RectTransform list = BuildListPanel(canvasGo.transform, marker);
            BuildDetailPanel(canvasGo.transform, marker, root);

            // footer brand line
            TMP_Text footer = Label(canvasGo.transform, "Footer", "LEGENDS ALLEY", new Vector2(700f, 34f), 16f, 20f, TextDim);
            ((RectTransform)footer.transform).anchoredPosition = new Vector2(-760f, -668f);
            footer.alignment = TextAlignmentOptions.Left;
            footer.characterSpacing = 16f;

            marker.listContent = list;
            return root;
        }

        private static void BuildHeader(Transform canvas, AlleyDirectoryBoard marker)
        {
            Image bar = Block(canvas, "Header", Bar, new Vector2(2240f, 140f));
            ((RectTransform)bar.transform).anchoredPosition = new Vector2(0f, 630f);

            Image tick = Block(canvas, "Header Tick", Pink, new Vector2(14f, 74f));
            ((RectTransform)tick.transform).anchoredPosition = new Vector2(-1058f, 630f);

            TMP_Text title = Label(canvas, "Title", "BOOTH DIRECTORY", new Vector2(1100f, 90f), 40f, 62f, Color.white);
            title.alignment = TextAlignmentOptions.Left;
            title.characterSpacing = 10f;
            ((RectTransform)title.transform).anchoredPosition = new Vector2(-470f, 642f);

            TMP_Text sub = Label(canvas, "Subtitle", "PICK A COMMUNITY TO READ ABOUT IT AND WARP OVER",
                new Vector2(1100f, 34f), 16f, 22f, TextDim);
            sub.alignment = TextAlignmentOptions.Left;
            sub.characterSpacing = 6f;
            ((RectTransform)sub.transform).anchoredPosition = new Vector2(-470f, 596f);

            TMP_Text count = Label(canvas, "Count", "0 BOOTHS", new Vector2(420f, 60f), 22f, 34f, Pink);
            count.alignment = TextAlignmentOptions.Right;
            count.characterSpacing = 8f;
            ((RectTransform)count.transform).anchoredPosition = new Vector2(870f, 630f);
            marker.countLabel = count;

            // accent strip under the header, same run of widths as the crown
            float left = -1120f;
            float[] shares = { 0.46f, 0.2f, 0.17f, 0.17f };
            Color32[] colors = { Pink, Purple, Teal, Gold };
            string[] names = { "Rule Pink", "Rule Purple", "Rule Teal", "Rule Gold" };
            for (int i = 0; i < shares.Length; i++)
            {
                float width = 2240f * shares[i];
                Image piece = Block(canvas, names[i], colors[i], new Vector2(width, 8f));
                ((RectTransform)piece.transform).anchoredPosition = new Vector2(left + width * 0.5f, 556f);
                left += width;
            }
        }

        private static RectTransform BuildListPanel(Transform canvas, AlleyDirectoryBoard marker)
        {
            var panel = new GameObject("Legends", typeof(RectTransform));
            panel.transform.SetParent(canvas, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.sizeDelta = new Vector2(1040f, 1064f);
            panelRect.anchoredPosition = new Vector2(-570f, -18f);

            Image back = Block(panel.transform, "Fill", Card, Vector2.zero);
            Stretch((RectTransform)back.transform);

            Image head = Block(panel.transform, "Head", Bar, new Vector2(1040f, 70f));
            Pin((RectTransform)head.transform, new Vector2(0.5f, 1f), new Vector2(0f, -35f));

            TMP_Text heading = Label(panel.transform, "Heading", "LEGENDS", new Vector2(600f, 44f), 20f, 28f, Color.white);
            heading.alignment = TextAlignmentOptions.Left;
            heading.characterSpacing = 12f;
            Pin((RectTransform)heading.transform, new Vector2(0f, 1f), new Vector2(354f, -35f));

            Image headTick = Block(panel.transform, "Head Tick", Teal, new Vector2(10f, 34f));
            Pin((RectTransform)headTick.transform, new Vector2(0f, 1f), new Vector2(24f, -35f));

            // scroll area under the heading
            var scrollGo = new GameObject("Scroll", typeof(RectTransform));
            scrollGo.transform.SetParent(panel.transform, false);
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.sizeDelta = new Vector2(996f, 954f);
            scrollRect.anchoredPosition = new Vector2(-8f, -52f);

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
            content.sizeDelta = new Vector2(960f, 0f);
            var vertical = contentGo.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 6f;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            vertical.padding = new RectOffset(0, 0, 6, 6);
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // slim square scrollbar hugging the panel edge
            var barGo = new GameObject("Scrollbar", typeof(RectTransform));
            barGo.transform.SetParent(panel.transform, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.sizeDelta = new Vector2(16f, 954f);
            barRect.anchoredPosition = new Vector2(500f, -52f);
            Image track = barGo.AddComponent<Image>();
            track.color = new Color32(24, 26, 31, 255);

            var slideGo = new GameObject("Sliding Area", typeof(RectTransform));
            slideGo.transform.SetParent(barGo.transform, false);
            var slideRect = (RectTransform)slideGo.transform;
            Stretch(slideRect);
            slideRect.sizeDelta = Vector2.zero;

            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(slideGo.transform, false);
            Image handle = handleGo.AddComponent<Image>();
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

            TMP_Text empty = Label(viewportGo.transform, "Empty State",
                "NO BOOTHS PLACED YET\n<size=65%><color=#9AA0A6>run a booth sync and this board fills itself</color></size>",
                new Vector2(880f, 260f), 24f, 32f, TextDim);
            ((RectTransform)empty.transform).anchoredPosition = new Vector2(0f, 40f);
            marker.emptyState = empty.gameObject;

            return content;
        }

        private static void BuildDetailPanel(Transform canvas, AlleyDirectoryBoard marker, GameObject root)
        {
            var panel = new GameObject("Detail", typeof(RectTransform));
            panel.transform.SetParent(canvas, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.sizeDelta = new Vector2(1120f, 1064f);
            panelRect.anchoredPosition = new Vector2(550f, -18f);

            Image back = Block(panel.transform, "Fill", Card, Vector2.zero);
            Stretch((RectTransform)back.transform);

            // nothing picked yet
            var placeholder = new GameObject("Placeholder", typeof(RectTransform));
            placeholder.transform.SetParent(panel.transform, false);
            Stretch((RectTransform)placeholder.transform);
            TMP_Text prompt = Label(placeholder.transform, "Prompt",
                "SELECT A LEGEND\n<size=60%><color=#9AA0A6>their story and a warp button show up here</color></size>",
                new Vector2(900f, 260f), 26f, 34f, TextDim);
            prompt.characterSpacing = 8f;

            // the filled in card
            var detail = new GameObject("Card", typeof(RectTransform));
            detail.transform.SetParent(panel.transform, false);
            Stretch((RectTransform)detail.transform);

            Image logoFrame = Block(detail.transform, "Logo Frame", new Color32(24, 26, 31, 255), new Vector2(200f, 200f));
            Pin((RectTransform)logoFrame.transform, new Vector2(0f, 1f), new Vector2(140f, -140f));

            Image logo = Block(logoFrame.transform, "Logo", Color.white, new Vector2(184f, 184f));
            logo.preserveAspect = true;

            var fallback = new GameObject("Logo Fallback", typeof(RectTransform));
            fallback.transform.SetParent(logoFrame.transform, false);
            ((RectTransform)fallback.transform).sizeDelta = new Vector2(184f, 184f);
            Image fallbackFill = fallback.AddComponent<Image>();
            fallbackFill.color = Purple;
            fallbackFill.raycastTarget = false;
            TMP_Text letter = Label(fallback.transform, "Letter", "?", new Vector2(184f, 184f), 56f, 96f, Color.white);

            TMP_Text name = Label(detail.transform, "Name", "COMMUNITY", new Vector2(780f, 96f), 34f, 54f, Color.white);
            name.alignment = TextAlignmentOptions.Left;
            name.characterSpacing = 4f;
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            Pin((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(660f, -110f));

            Image plotTag = Block(detail.transform, "Plot Tag", Pink, new Vector2(196f, 44f));
            Pin((RectTransform)plotTag.transform, new Vector2(0f, 1f), new Vector2(368f, -184f));
            TMP_Text plot = Label(plotTag.transform, "Plot", "PLOT A-01", new Vector2(190f, 40f), 16f, 22f, Color.white);
            plot.characterSpacing = 6f;

            Image rule = Block(detail.transform, "Rule", Line, new Vector2(1000f, 3f));
            Pin((RectTransform)rule.transform, new Vector2(0.5f, 1f), new Vector2(0f, -270f));

            TMP_Text body = Label(detail.transform, "Body", "", new Vector2(1000f, 520f), 26f, 32f, LabelIdle);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.fontStyle = FontStyles.Normal;
            body.characterSpacing = 0f;
            body.lineSpacing = 14f;
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Truncate;
            Pin((RectTransform)body.transform, new Vector2(0.5f, 1f), new Vector2(0f, -560f));

            var kiosk = (AlleyDirectoryKiosk)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyDirectoryKiosk));
            UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(kiosk);
            backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;

            Button teleport = PanelButton(detail.transform, "Teleport", "WARP TO BOOTH", Teal, new Color32(6, 8, 11, 255),
                new Vector2(-250f, 0f), backing, "OnTeleport");
            Button join = PanelButton(detail.transform, "Join", "OPEN GROUP PAGE", Pink, Color.white,
                new Vector2(250f, 0f), backing, "OnJoin");

            kiosk.placeholder = placeholder;
            kiosk.detail = detail;
            kiosk.nameLabel = (TextMeshProUGUI)name;
            kiosk.plotLabel = (TextMeshProUGUI)plot;
            kiosk.bodyLabel = (TextMeshProUGUI)body;
            kiosk.logoImage = logo;
            kiosk.logoFallback = fallback;
            kiosk.logoLetter = (TextMeshProUGUI)letter;
            kiosk.joinButton = join.gameObject;
            kiosk.rowIdle = RowIdle;
            kiosk.rowActive = RowActive;
            kiosk.markerIdle = Line;
            kiosk.markerActive = Pink;
            kiosk.labelIdle = LabelIdle;
            kiosk.labelActive = Color.white;
            UdonSharpEditorUtility.CopyProxyToUdon(kiosk);

            detail.SetActive(false);
            marker.kiosk = kiosk;
        }

        private static Button PanelButton(Transform parent, string name, string text, Color32 fill, Color textColor,
            Vector2 offset, UdonBehaviour backing, string eventName)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(480f, 104f);
            Pin(rect, new Vector2(0.5f, 0f), new Vector2(offset.x, 96f));

            Image face = go.AddComponent<Image>();
            face.color = fill;
            face.raycastTarget = true;

            TMP_Text label = Label(go.transform, "Label", text, new Vector2(440f, 60f), 22f, 30f, textColor);
            label.characterSpacing = 8f;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.86f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            button.colors = colors;
            UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, eventName);
            return button;
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

        /* ─── small ui helpers, flat blocks only, the look has no round corners ─── */

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        // anchors to an edge or corner of the parent, offset measured from there
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
