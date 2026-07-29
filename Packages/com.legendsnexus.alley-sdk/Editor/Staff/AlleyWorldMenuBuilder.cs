using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Video.Components.AVPro;
using VRC.SDKBase;
using VRC.Udon;

namespace LegendsNexus.Alley.Editor
{
    // builds the in world event menu: a pocket panel visitors summon anywhere
    // (hold right stick up in vr, M on desktop). event info reads live off the
    // site like the info walls, the booths tab is a full directory with warp
    // and group buttons that refills on every sync, and the music tab holds
    // the volume for the world's background music stream
    internal static class AlleyWorldMenuBuilder
    {
        // the 1h event ambience loop, streamed so it costs the world nothing
        private const string MusicUrl = "https://www.youtube.com/watch?v=_XlhyJRLgpQ";
        private const float DefaultMusicVolume = 0.35f;

        private static readonly Color32 Pink = new Color32(255, 0, 122, 255);
        private static readonly Color32 Purple = new Color32(107, 70, 193, 255);
        private static readonly Color32 Teal = new Color32(31, 209, 237, 255);
        private static readonly Color32 Gold = new Color32(255, 215, 0, 255);
        private static readonly Color32 CardFill = new Color32(13, 14, 17, 250);
        private static readonly Color32 RowIdle = new Color32(20, 22, 26, 255);
        private static readonly Color32 Line = new Color32(42, 45, 51, 255);
        private static readonly Color32 TextDim = new Color32(154, 160, 166, 255);
        private static readonly Color32 LabelIdle = new Color32(214, 217, 222, 255);
        private static readonly Color32 Ink = new Color32(10, 10, 10, 235);
        private static readonly Color32 HeadInk = new Color32(10, 10, 10, 255);

        private const float PanelW = 1360f;
        private const float PanelH = 780f;
        private const float SideW = 300f;
        // body area right of the sidebar with matched margins all around
        private const float BodyW = PanelW - SideW - 54f;
        private const float BodyX = (SideW + 18f) * 0.5f;
        private const float BodyH = PanelH - 36f;
        private const float HeadH = 64f;
        // content zone under the heading bar
        private const float ContentTop = BodyH * 0.5f - HeadH - 18f;

        [MenuItem("GameObject/Legends Alley/Staff/World Menu", true)]
        private static bool ValidateSpawnMenu() => AlleyStaffOnly.Allowed;

        [MenuItem("GameObject/Legends Alley/Staff/World Menu", false, 42)]
        private static void SpawnMenu(MenuCommand command)
        {
            if (AlleyStaffOnly.Blocked("The world menu")) return;
            AlleyPrefabBuilder.EnsureProgramAsset();
            GameObject menu = Build();
            GameObjectUtility.SetParentAndAlign(menu, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(menu, "Create World Menu");
            Selection.activeGameObject = menu;
            EditorSceneManager.MarkSceneDirty(menu.scene);
        }

        public static GameObject Build()
        {
            var root = new GameObject("Alley World Menu");
            var proxy = (AlleyWorldMenu)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyWorldMenu));
            UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
            backing.SyncMethod = Networking.SyncType.None;

            // the panel is the part that travels to the player. everything that
            // has to stay put in the world (teleport anchors, music) hangs off
            // the root instead
            var panel = new GameObject("Panel");
            panel.transform.SetParent(root.transform, false);
            var scaler = new GameObject("Scaler");
            scaler.transform.SetParent(panel.transform, false);

            Transform canvas = AlleyPrefabBuilder.MakeWorldCanvas(scaler.transform, "Menu Canvas",
                new Vector2(PanelW, PanelH), 0.001f, Vector3.zero);
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            canvas.gameObject.AddComponent<VRCUiShape>();
            var group = canvas.gameObject.AddComponent<CanvasGroup>();

            AlleyPrefabBuilder.AddImage(canvas, "Edge", null, Line, new Vector2(PanelW, PanelH));
            AlleyPrefabBuilder.AddImage(canvas, "Fill", null, CardFill, new Vector2(PanelW - 8f, PanelH - 8f));
            AlleyPrefabBuilder.AddAccentRun(canvas, PanelW - 8f, 8f, PanelH * 0.5f - 8f);

            Image[] pills;
            TextMeshProUGUI[] pillLabels;
            BuildSidebar(canvas, backing, out pills, out pillLabels);

            // the performance switch lives on the root so the baker can find it
            var perf = (AlleyPerformanceMode)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyPerformanceMode));
            UdonBehaviour perfBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(perf);
            perfBacking.SyncMethod = Networking.SyncType.None;

            var panels = new GameObject[4];
            TextMeshProUGUI feedName;
            TextMeshProUGUI feedSchedule;
            panels[0] = BuildInfoPanel(canvas, out feedName, out feedSchedule);
            AlleyDirectoryBoard board;
            panels[1] = BuildBoothsPanel(canvas, root, out board);
            panels[2] = BuildCreditsPanel(canvas);
            Slider volume;
            TMP_Text percent;
            panels[3] = BuildSettingsPanel(canvas, backing, perf, perfBacking, out volume, out percent);
            for (int i = 1; i < panels.Length; i++) panels[i].SetActive(false);

            // live wording, same feed the info walls read
            var feed = (AlleySignFeed)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleySignFeed));
            UdonBehaviour feedBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(feed);
            feedBacking.SyncMethod = Networking.SyncType.None;
            feed.scheduleUrl = new VRCUrl(AlleyConfig.DefaultApiBase + "/api/public/sign/current/schedule.txt");
            feed.eventNameLabel = feedName;
            feed.scheduleLabel = feedSchedule;
            UdonSharpEditorUtility.CopyProxyToUdon(feed);

            // ---- hold to open radial, follows the head from the script ----
            var radial = new GameObject("Radial");
            radial.transform.SetParent(root.transform, false);
            Transform radialCanvas = AlleyPrefabBuilder.MakeWorldCanvas(radial.transform, "Radial Canvas",
                new Vector2(140f, 140f), 0.001f, Vector3.zero);
            Sprite disc = AssetDatabase.LoadAssetAtPath<Sprite>(AlleyConfig.PackageRoot + "/Runtime/Textures/AlleyDisc.png");
            AlleyPrefabBuilder.AddImage(radialCanvas, "Backdrop", disc, Ink, new Vector2(120f, 120f));
            Image fill = AlleyPrefabBuilder.AddImage(radialCanvas, "Fill", disc, Pink, new Vector2(100f, 100f));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = true;
            fill.fillAmount = 0f;

            // ---- sfx, quiet 2d one shots ----
            var sfxGo = new GameObject("SFX");
            sfxGo.transform.SetParent(root.transform, false);
            var sfx = sfxGo.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            sfx.spatialBlend = 0f;
            sfx.volume = 0.45f;
            var sfxSpatial = sfxGo.AddComponent<VRCSpatialAudioSource>();
            sfxSpatial.EnableSpatialization = false;
            sfxSpatial.Gain = 0f;

            // ---- background music, an audio only avpro stream ----
            var musicGo = new GameObject("Background Music");
            musicGo.transform.SetParent(root.transform, false);
            var avpro = musicGo.AddComponent<VRCAVProVideoPlayer>();
            var avproSo = new SerializedObject(avpro);
            avproSo.FindProperty("autoPlay").boolValue = true;
            avproSo.FindProperty("loop").boolValue = true;
            avproSo.FindProperty("useLowLatency").boolValue = false;
            avproSo.FindProperty("maximumResolution").intValue = 720;
            avproSo.FindProperty("videoURL").FindPropertyRelative("url").stringValue = MusicUrl;
            avproSo.ApplyModifiedPropertiesWithoutUndo();

            var music = musicGo.AddComponent<AudioSource>();
            music.playOnAwake = false;
            music.spatialBlend = 0f;
            music.volume = DefaultMusicVolume;
            music.dopplerLevel = 0f;
            var speakerSo = new SerializedObject(musicGo.AddComponent<VRCAVProVideoSpeaker>());
            speakerSo.FindProperty("videoPlayer").objectReferenceValue = avpro;
            speakerSo.ApplyModifiedPropertiesWithoutUndo();
            var musicSpatial = musicGo.AddComponent<VRCSpatialAudioSource>();
            musicSpatial.EnableSpatialization = false;
            musicSpatial.Gain = 0f;

            proxy.panel = panel;
            proxy.panelScaler = scaler.transform;
            proxy.panelGroup = group;
            proxy.radial = radial;
            proxy.radialFill = fill;
            proxy.sfxSource = sfx;
            proxy.openClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AlleyConfig.PackageRoot + "/Runtime/Audio/menu-open.ogg");
            proxy.closeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AlleyConfig.PackageRoot + "/Runtime/Audio/menu-close.ogg");
            proxy.clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AlleyConfig.PackageRoot + "/Runtime/Audio/menu-click.ogg");
            proxy.musicSource = music;
            proxy.musicSlider = volume;
            proxy.musicPercent = (TextMeshProUGUI)percent;
            proxy.tabPanels = panels;
            proxy.tabPills = pills;
            proxy.tabLabels = pillLabels;
            proxy.tabAccents = new Color[] { Pink, Purple, Gold, Teal };
            UdonSharpEditorUtility.CopyProxyToUdon(proxy);

            // the settings tab shares the menu's speaker and click blip
            perf.sfxSource = sfx;
            perf.clickClip = proxy.clickClip;
            UdonSharpEditorUtility.CopyProxyToUdon(perf);

            panel.SetActive(false);
            radial.SetActive(false);
            return root;
        }

        /* ─── sidebar ─── */

        private static void BuildSidebar(Transform canvas, UdonBehaviour backing,
            out Image[] pills, out TextMeshProUGUI[] pillLabels)
        {
            float sideX = -(PanelW * 0.5f) + SideW * 0.5f + 18f;
            AlleyPrefabBuilder.AddImage(canvas, "Sidebar", null, Ink, new Vector2(SideW, PanelH - 36f))
                .rectTransform.anchoredPosition = new Vector2(sideX, 0f);

            // the real logo, top left like everywhere else we brand things
            Sprite logo = EnsureLogoSprite();
            Image logoImage = AlleyPrefabBuilder.AddImage(canvas, "Logo", logo, Color.white, new Vector2(190f, 190f));
            logoImage.preserveAspect = true;
            logoImage.rectTransform.anchoredPosition = new Vector2(sideX, PanelH * 0.5f - 128f);
            AlleyPrefabBuilder.AddImage(canvas, "Logo Rule", null, Line, new Vector2(SideW - 60f, 2f))
                .rectTransform.anchoredPosition = new Vector2(sideX, PanelH * 0.5f - 244f);

            string[] tabNames = { "EVENT INFO", "BOOTHS", "CREDITS", "SETTINGS" };
            Color32[] accents = { Pink, Purple, Gold, Teal };
            pills = new Image[4];
            pillLabels = new TextMeshProUGUI[4];
            for (int i = 0; i < 4; i++)
            {
                var pillGo = new GameObject("Tab " + tabNames[i], typeof(RectTransform));
                pillGo.transform.SetParent(canvas, false);
                var pillRect = (RectTransform)pillGo.transform;
                pillRect.sizeDelta = new Vector2(SideW - 44f, 72f);
                pillRect.anchoredPosition = new Vector2(sideX, 64f - i * 88f);
                AlleyPrefabBuilder.AddImage(pillGo.transform, "Edge", null, accents[i], new Vector2(SideW - 44f, 72f));
                Image face = AlleyPrefabBuilder.AddImage(pillGo.transform, "Face", null, RowIdle, new Vector2(SideW - 50f, 66f));
                face.raycastTarget = true;
                TMP_Text label = AlleyPrefabBuilder.AddLabel(pillGo.transform, "Label", tabNames[i], new Vector2(SideW - 70f, 48f), 20f, 28f, LabelIdle);
                label.fontStyle = FontStyles.Bold;
                var button = pillGo.AddComponent<Button>();
                button.targetGraphic = face;
                UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, "OnTab" + i);
                pills[i] = face;
                pillLabels[i] = (TextMeshProUGUI)label;
            }

            TMP_Text closeHint = AlleyPrefabBuilder.AddLabel(canvas, "Close Hint",
                "Press M or flick up to close.\nWalking away closes it too.", new Vector2(SideW - 44f, 58f), 14f, 17f, TextDim);
            ((RectTransform)closeHint.transform).anchoredPosition = new Vector2(sideX, -(PanelH * 0.5f) + 100f);
            TMP_Text site = AlleyPrefabBuilder.AddLabel(canvas, "Site", "vrchatlegends.com", new Vector2(SideW - 44f, 30f), 17f, 22f, TextDim);
            ((RectTransform)site.transform).anchoredPosition = new Vector2(sideX, -(PanelH * 0.5f) + 48f);
        }

        // shared shell for a tab: heading bar with padded label, content below
        private static Transform PanelShell(Transform canvas, string name, string heading, Color32 accent)
        {
            var shell = new GameObject(name, typeof(RectTransform));
            shell.transform.SetParent(canvas, false);
            var rect = (RectTransform)shell.transform;
            rect.sizeDelta = new Vector2(BodyW, BodyH);
            rect.anchoredPosition = new Vector2(BodyX, 0f);

            AlleyPrefabBuilder.AddImage(shell.transform, "Heading Bar", null, accent, new Vector2(BodyW, HeadH))
                .rectTransform.anchoredPosition = new Vector2(0f, BodyH * 0.5f - HeadH * 0.5f);
            TMP_Text headingLabel = AlleyPrefabBuilder.AddLabel(shell.transform, "Heading", heading, new Vector2(BodyW - 56f, 46f), 24f, 34f, HeadInk);
            headingLabel.fontStyle = FontStyles.Bold;
            headingLabel.alignment = TextAlignmentOptions.Left;
            headingLabel.characterSpacing = 8f;
            ((RectTransform)headingLabel.transform).anchoredPosition = new Vector2(0f, BodyH * 0.5f - HeadH * 0.5f);
            return shell.transform;
        }

        /* ─── event info: live wording off the site ─── */

        private static GameObject BuildInfoPanel(Transform canvas, out TextMeshProUGUI feedName, out TextMeshProUGUI feedSchedule)
        {
            Transform shell = PanelShell(canvas, "Panel Info", "EVENT INFO", Pink);

            AlleyPrefabBuilder.AddImage(shell, "Event Chip", null, RowIdle, new Vector2(BodyW - 40f, 92f))
                .rectTransform.anchoredPosition = new Vector2(0f, ContentTop - 46f);
            AlleyPrefabBuilder.AddImage(shell, "Event Tick", null, Pink, new Vector2(10f, 56f))
                .rectTransform.anchoredPosition = new Vector2(-(BodyW - 40f) * 0.5f + 22f, ContentTop - 46f);
            TMP_Text nameLabel = AlleyPrefabBuilder.AddLabel(shell, "Event Name", "LEGENDS ALLEY", new Vector2(BodyW - 130f, 60f), 26f, 42f, Color.white);
            nameLabel.fontStyle = FontStyles.Bold;
            nameLabel.alignment = TextAlignmentOptions.Left;
            nameLabel.enableWordWrapping = false;
            nameLabel.overflowMode = TextOverflowModes.Ellipsis;
            ((RectTransform)nameLabel.transform).anchoredPosition = new Vector2(22f, ContentTop - 46f);
            feedName = (TextMeshProUGUI)nameLabel;

            TMP_Text schedTag = AlleyPrefabBuilder.AddLabel(shell, "Schedule Tag", "SCHEDULE", new Vector2(BodyW - 40f, 30f), 17f, 24f, TextDim);
            schedTag.alignment = TextAlignmentOptions.Left;
            schedTag.characterSpacing = 10f;
            ((RectTransform)schedTag.transform).anchoredPosition = new Vector2(0f, ContentTop - 128f);

            TMP_Text schedule = AlleyPrefabBuilder.AddLabel(shell, "Schedule Body", "Loading the schedule...", new Vector2(BodyW - 40f, 330f), 18f, 24f, LabelIdle);
            schedule.alignment = TextAlignmentOptions.TopLeft;
            schedule.lineSpacing = 10f;
            schedule.overflowMode = TextOverflowModes.Truncate;
            ((RectTransform)schedule.transform).anchoredPosition = new Vector2(0f, ContentTop - 318f);
            feedSchedule = (TextMeshProUGUI)schedule;

            AlleyPrefabBuilder.AddImage(shell, "Info Rule", null, Line, new Vector2(BodyW - 40f, 2f))
                .rectTransform.anchoredPosition = new Vector2(0f, -(BodyH * 0.5f) + 118f);
            TMP_Text welcome = AlleyPrefabBuilder.AddLabel(shell, "Welcome",
                "Every booth in this hall was built by a VRChat community. Walk up and press things: " +
                "screens play, group buttons join, pedestals dress you, portals travel. The BOOTHS tab " +
                "lists everyone and warps you straight to them.",
                new Vector2(BodyW - 40f, 96f), 16f, 21f, TextDim);
            welcome.alignment = TextAlignmentOptions.TopLeft;
            ((RectTransform)welcome.transform).anchoredPosition = new Vector2(0f, -(BodyH * 0.5f) + 62f);
            return shell.gameObject;
        }

        /* ─── settings: music volume plus the performance switch ─── */

        private static GameObject BuildSettingsPanel(Transform canvas, UdonBehaviour backing,
            AlleyPerformanceMode perf, UdonBehaviour perfBacking, out Slider volume, out TMP_Text percent)
        {
            Transform shell = PanelShell(canvas, "Panel Settings", "SETTINGS", Teal);

            TMP_Text tag = AlleyPrefabBuilder.AddLabel(shell, "Volume Tag", "EVENT MUSIC VOLUME", new Vector2(BodyW - 40f, 32f), 17f, 24f, TextDim);
            tag.alignment = TextAlignmentOptions.Left;
            tag.characterSpacing = 10f;
            ((RectTransform)tag.transform).anchoredPosition = new Vector2(0f, ContentTop - 26f);

            volume = AlleyPrefabBuilder.AddVolumeSlider(shell, new Vector2(-80f, ContentTop - 96f), new Vector2(BodyW - 320f, 70f));
            volume.value = DefaultMusicVolume;
            UnityEventTools.AddStringPersistentListener(volume.onValueChanged, backing.SendCustomEvent, "OnMusicVolume");

            TMP_Text pct = AlleyPrefabBuilder.AddLabel(shell, "Music Percent", "35%", new Vector2(140f, 60f), 26f, 42f, LabelIdle);
            pct.alignment = TextAlignmentOptions.Right;
            ((RectTransform)pct.transform).anchoredPosition = new Vector2((BodyW - 40f) * 0.5f - 96f, ContentTop - 96f);
            percent = pct;

            AlleyPrefabBuilder.AddImage(shell, "Settings Rule", null, Line, new Vector2(BodyW - 40f, 2f))
                .rectTransform.anchoredPosition = new Vector2(0f, ContentTop - 168f);

            TMP_Text perfTag = AlleyPrefabBuilder.AddLabel(shell, "Perf Tag", "BOOTH DETAIL", new Vector2(BodyW - 40f, 32f), 17f, 24f, TextDim);
            perfTag.alignment = TextAlignmentOptions.Left;
            perfTag.characterSpacing = 10f;
            ((RectTransform)perfTag.transform).anchoredPosition = new Vector2(0f, ContentTop - 212f);

            // three way switch, balanced ships as the default
            string[] modeNames = { "PERFORMANCE", "BALANCED", "QUALITY" };
            string[] modeEvents = { "OnPerformance", "OnBalanced", "OnQuality" };
            var modePills = new Image[3];
            var modeLabels = new TextMeshProUGUI[3];
            float pillWidth = (BodyW - 40f - 32f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var pillGo = new GameObject("Mode " + modeNames[i], typeof(RectTransform));
                pillGo.transform.SetParent(shell, false);
                var pillRect = (RectTransform)pillGo.transform;
                pillRect.sizeDelta = new Vector2(pillWidth, 78f);
                pillRect.anchoredPosition = new Vector2((i - 1) * (pillWidth + 16f), ContentTop - 288f);
                AlleyPrefabBuilder.AddImage(pillGo.transform, "Edge", null, Teal, new Vector2(pillWidth, 78f));
                Image face = AlleyPrefabBuilder.AddImage(pillGo.transform, "Face", null, i == 1 ? (Color)Teal : (Color)RowIdle, new Vector2(pillWidth - 6f, 72f));
                face.raycastTarget = true;
                TMP_Text label = AlleyPrefabBuilder.AddLabel(pillGo.transform, "Label", modeNames[i], new Vector2(pillWidth - 24f, 46f), 17f, 24f,
                    i == 1 ? new Color32(10, 10, 10, 255) : LabelIdle);
                label.fontStyle = FontStyles.Bold;
                var button = pillGo.AddComponent<Button>();
                button.targetGraphic = face;
                UnityEventTools.AddStringPersistentListener(button.onClick, perfBacking.SendCustomEvent, modeEvents[i]);
                modePills[i] = face;
                modeLabels[i] = (TextMeshProUGUI)label;
            }

            TMP_Text hint = AlleyPrefabBuilder.AddLabel(shell, "Perf Hint",
                "The event's tuned default. Full booths up close, baked stand ins further out.",
                new Vector2(BodyW - 40f, 90f), 15f, 20f, TextDim);
            hint.alignment = TextAlignmentOptions.TopLeft;
            ((RectTransform)hint.transform).anchoredPosition = new Vector2(0f, ContentTop - 396f);

            perf.modePills = modePills;
            perf.modeLabels = modeLabels;
            perf.modeHint = (TextMeshProUGUI)hint;
            return shell.gameObject;
        }

        /* ─── booths: a real directory, refilled by every sync ─── */

        private static GameObject BuildBoothsPanel(Transform canvas, GameObject root, out AlleyDirectoryBoard board)
        {
            Transform shell = PanelShell(canvas, "Panel Booths", "BOOTHS", Purple);

            board = root.AddComponent<AlleyDirectoryBoard>();
            var anchors = new GameObject("Teleport Anchors");
            anchors.transform.SetParent(root.transform, false);
            board.anchorsRoot = anchors.transform;

            TMP_Text count = AlleyPrefabBuilder.AddLabel(shell, "Count", "0 BOOTHS", new Vector2(260f, 40f), 18f, 26f, HeadInk);
            count.alignment = TextAlignmentOptions.Right;
            count.fontStyle = FontStyles.Bold;
            ((RectTransform)count.transform).anchoredPosition = new Vector2((BodyW - 56f) * 0.5f - 120f, BodyH * 0.5f - HeadH * 0.5f);
            board.countLabel = count;

            // scrolling list on top, detail strip pinned to the bottom
            const float listH = 380f;
            var listArea = new GameObject("List", typeof(RectTransform));
            listArea.transform.SetParent(shell, false);
            var listRect = (RectTransform)listArea.transform;
            listRect.sizeDelta = new Vector2(BodyW - 40f, listH);
            listRect.anchoredPosition = new Vector2(0f, ContentTop - listH * 0.5f + 6f);

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(listArea.transform, false);
            var viewportRect = (RectTransform)viewportGo.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(0f, 0f);
            viewportRect.offsetMax = new Vector2(-26f, 0f);
            viewportGo.AddComponent<RectMask2D>();
            Image viewportImage = viewportGo.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.004f);
            viewportImage.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var content = (RectTransform)contentGo.transform;
            content.anchorMin = new Vector2(0.5f, 1f);
            content.anchorMax = new Vector2(0.5f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(BodyW - 66f, 0f);
            var vertical = contentGo.AddComponent<VerticalLayoutGroup>();
            vertical.spacing = 6f;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandWidth = true;
            vertical.childForceExpandHeight = false;
            vertical.padding = new RectOffset(0, 0, 4, 4);
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            board.listContent = content;

            // slim scrollbar hugging the right edge
            var barGo = new GameObject("Scrollbar", typeof(RectTransform));
            barGo.transform.SetParent(listArea.transform, false);
            var barRect = (RectTransform)barGo.transform;
            barRect.anchorMin = new Vector2(1f, 0f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(1f, 0.5f);
            barRect.sizeDelta = new Vector2(14f, 0f);
            barRect.anchoredPosition = Vector2.zero;
            Image track = barGo.AddComponent<Image>();
            track.color = new Color32(24, 26, 31, 255);
            var slideGo = new GameObject("Sliding Area", typeof(RectTransform));
            slideGo.transform.SetParent(barGo.transform, false);
            var slideRect = (RectTransform)slideGo.transform;
            slideRect.anchorMin = Vector2.zero;
            slideRect.anchorMax = Vector2.one;
            slideRect.offsetMin = Vector2.zero;
            slideRect.offsetMax = Vector2.zero;
            var handleGo = new GameObject("Handle", typeof(RectTransform));
            handleGo.transform.SetParent(slideGo.transform, false);
            Image handle = handleGo.AddComponent<Image>();
            handle.color = Purple;
            handle.raycastTarget = true;
            var handleRect = (RectTransform)handleGo.transform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            var scrollbar = barGo.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            var scroll = listArea.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewportRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            TMP_Text empty = AlleyPrefabBuilder.AddLabel(viewportGo.transform, "Empty State",
                "NO BOOTHS PLACED YET\n<size=70%><color=#9AA0A6>this list fills itself after the next booth sync</color></size>",
                new Vector2(BodyW - 140f, 200f), 20f, 28f, TextDim);
            board.emptyState = empty.gameObject;

            // detail strip: logo, name, plot, blurb, and the two action buttons
            const float detailH = 218f;
            var detail = new GameObject("Detail Strip", typeof(RectTransform));
            detail.transform.SetParent(shell, false);
            var detailRect = (RectTransform)detail.transform;
            detailRect.sizeDelta = new Vector2(BodyW - 40f, detailH);
            detailRect.anchoredPosition = new Vector2(0f, -(BodyH * 0.5f) + detailH * 0.5f + 22f);
            AlleyPrefabBuilder.AddImage(detail.transform, "Fill", null, RowIdle, new Vector2(BodyW - 40f, detailH));

            var placeholder = new GameObject("Placeholder", typeof(RectTransform));
            placeholder.transform.SetParent(detail.transform, false);
            ((RectTransform)placeholder.transform).sizeDelta = new Vector2(BodyW - 40f, detailH);
            TMP_Text prompt = AlleyPrefabBuilder.AddLabel(placeholder.transform, "Prompt",
                "PICK A COMMUNITY ABOVE\n<size=70%><color=#9AA0A6>their story, group page, and a warp button land here</color></size>",
                new Vector2(BodyW - 120f, 120f), 18f, 26f, TextDim);
            prompt.characterSpacing = 6f;

            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(detail.transform, false);
            ((RectTransform)card.transform).sizeDelta = new Vector2(BodyW - 40f, detailH);
            float leftEdge = -(BodyW - 40f) * 0.5f;

            Image logoFrame = AlleyPrefabBuilder.AddImage(card.transform, "Logo Frame", null, new Color32(24, 26, 31, 255), new Vector2(150f, 150f));
            logoFrame.rectTransform.anchoredPosition = new Vector2(leftEdge + 100f, 10f);
            Image logo = AlleyPrefabBuilder.AddImage(logoFrame.transform, "Logo", null, Color.white, new Vector2(138f, 138f));
            logo.preserveAspect = true;
            var fallback = new GameObject("Logo Fallback", typeof(RectTransform));
            fallback.transform.SetParent(logoFrame.transform, false);
            ((RectTransform)fallback.transform).sizeDelta = new Vector2(138f, 138f);
            Image fallbackFill = fallback.AddComponent<Image>();
            fallbackFill.color = Purple;
            fallbackFill.raycastTarget = false;
            TMP_Text letter = AlleyPrefabBuilder.AddLabel(fallback.transform, "Letter", "?", new Vector2(138f, 138f), 44f, 72f, Color.white);

            TMP_Text name = AlleyPrefabBuilder.AddLabel(card.transform, "Name", "COMMUNITY", new Vector2(440f, 52f), 22f, 34f, Color.white);
            name.alignment = TextAlignmentOptions.Left;
            name.fontStyle = FontStyles.Bold;
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;
            ((RectTransform)name.transform).anchoredPosition = new Vector2(leftEdge + 420f, 66f);

            Image plotTag = AlleyPrefabBuilder.AddImage(card.transform, "Plot Tag", null, Pink, new Vector2(150f, 36f));
            plotTag.rectTransform.anchoredPosition = new Vector2(leftEdge + 270f, 18f);
            TMP_Text plot = AlleyPrefabBuilder.AddLabel(plotTag.transform, "Plot", "PLOT A-01", new Vector2(144f, 32f), 14f, 19f, Color.white);
            plot.characterSpacing = 4f;

            TMP_Text body = AlleyPrefabBuilder.AddLabel(card.transform, "Body", "", new Vector2(560f, 120f), 15f, 19f, LabelIdle);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.lineSpacing = 6f;
            body.overflowMode = TextOverflowModes.Truncate;
            ((RectTransform)body.transform).anchoredPosition = new Vector2(leftEdge + 480f, -46f);

            var kiosk = (AlleyDirectoryKiosk)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyDirectoryKiosk));
            UdonBehaviour kioskBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(kiosk);
            kioskBacking.SyncMethod = Networking.SyncType.None;

            float rightEdge = (BodyW - 40f) * 0.5f;
            Button teleport = StripButton(card.transform, "Teleport", "WARP TO BOOTH", Teal, new Color32(6, 8, 11, 255),
                new Vector2(rightEdge - 160f, 52f), kioskBacking, "OnTeleport");
            Button join = StripButton(card.transform, "Join", "OPEN GROUP PAGE", Pink, Color.white,
                new Vector2(rightEdge - 160f, -46f), kioskBacking, "OnJoin");

            kiosk.placeholder = placeholder;
            kiosk.detail = card;
            kiosk.nameLabel = (TextMeshProUGUI)name;
            kiosk.plotLabel = (TextMeshProUGUI)plot;
            kiosk.bodyLabel = (TextMeshProUGUI)body;
            kiosk.logoImage = logo;
            kiosk.logoFallback = fallback;
            kiosk.logoLetter = (TextMeshProUGUI)letter;
            kiosk.joinButton = join.gameObject;
            kiosk.rowIdle = RowIdle;
            kiosk.rowActive = new Color32(39, 27, 54, 255);
            kiosk.markerIdle = Line;
            kiosk.markerActive = Pink;
            kiosk.labelIdle = LabelIdle;
            kiosk.labelActive = Color.white;
            UdonSharpEditorUtility.CopyProxyToUdon(kiosk);

            card.SetActive(false);
            board.kiosk = kiosk;
            return shell.gameObject;
        }

        private static Button StripButton(Transform parent, string name, string text, Color32 fill, Color textColor,
            Vector2 position, UdonBehaviour backing, string eventName)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(290f, 76f);
            rect.anchoredPosition = position;

            Image face = go.AddComponent<Image>();
            face.color = fill;
            face.raycastTarget = true;

            TMP_Text label = AlleyPrefabBuilder.AddLabel(go.transform, "Label", text, new Vector2(270f, 44f), 16f, 22f, textColor);
            label.fontStyle = FontStyles.Bold;
            label.characterSpacing = 4f;

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            var colors = button.colors;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.86f);
            colors.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            button.colors = colors;
            UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, eventName);
            return button;
        }

        /* ─── credits ─── */

        private static GameObject BuildCreditsPanel(Transform canvas)
        {
            Transform shell = PanelShell(canvas, "Panel Credits", "CREDITS", Gold);
            TMP_Text bodyText = AlleyPrefabBuilder.AddLabel(shell, "Credits Body",
                "Legends Alley is a VRChat Legends event.\n\n" +
                "Every booth in this hall was designed and built by its own community with the " +
                "Legends Alley SDK, then dropped onto its plot exactly as they shipped it. " +
                "The hall, the tools, and the event come from the VRChat Legends staff team.\n\n" +
                "Want your community here next time? Applications open on the website before every event.",
                new Vector2(BodyW - 40f, 330f), 17f, 23f, LabelIdle);
            bodyText.alignment = TextAlignmentOptions.TopLeft;
            bodyText.lineSpacing = 10f;
            ((RectTransform)bodyText.transform).anchoredPosition = new Vector2(0f, ContentTop - 190f);

            AlleyPrefabBuilder.AddImage(shell, "Site Chip", null, RowIdle, new Vector2(BodyW - 40f, 84f))
                .rectTransform.anchoredPosition = new Vector2(0f, -(BodyH * 0.5f) + 74f);
            TMP_Text site = AlleyPrefabBuilder.AddLabel(shell, "Site", "vrchatlegends.com      |      discord.gg/6xPkZ7Dxp9", new Vector2(BodyW - 90f, 46f), 18f, 26f, Gold);
            ((RectTransform)site.transform).anchoredPosition = new Vector2(0f, -(BodyH * 0.5f) + 74f);
            return shell.gameObject;
        }

        // logo lives in the sdk window art, mirrored into runtime as a sprite
        private static Sprite EnsureLogoSprite()
        {
            string path = AlleyConfig.PackageRoot + "/Runtime/Textures/AlleyLogo.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 512;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
