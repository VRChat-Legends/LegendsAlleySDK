using System.IO;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDK3.Video.Components.AVPro;
using VRC.Udon;

namespace LegendsNexus.Alley.Editor
{
    // rebuilds the bundled prefabs from code so tweaks stay reviewable and
    // rerunnable. staff dev tool, creators never need to touch this
    internal static class AlleyPrefabBuilder
    {
        private const string PrefabFolder = AlleyConfig.PackageRoot + "/Runtime/Prefabs";
        private const string TextureFolder = AlleyConfig.PackageRoot + "/Runtime/Textures";
        private const string ProgramFolder = AlleyConfig.PackageRoot + "/Runtime/Programs";
        private const string MaterialFolder = AlleyConfig.PackageRoot + "/Runtime/Materials";

        private static readonly Color32 Pink = new Color32(255, 0, 122, 255);
        private static readonly Color32 Purple = new Color32(107, 70, 193, 255);
        private static readonly Color32 Teal = new Color32(31, 209, 237, 255);
        private static readonly Color32 Gold = new Color32(255, 215, 0, 255);
        private static readonly Color32 Magenta = new Color32(186, 24, 156, 255);
        private static readonly Color32 CardDark = new Color32(16, 18, 22, 250);
        private static readonly Color32 CardFill = new Color32(13, 14, 17, 250);
        private static readonly Color32 RowIdle = new Color32(20, 22, 26, 255);
        private static readonly Color32 Line = new Color32(42, 45, 51, 255);
        private static readonly Color32 TextDim = new Color32(154, 160, 166, 255);
        private static readonly Color32 LabelIdle = new Color32(214, 217, 222, 255);

        // sketch's art is 1920x1080 layers, 0.0005 puts the card at 0.96m wide
        private const float CardWidth = 1920f;
        private const float CardHeight = 1080f;
        private const float CardScale = 0.0005f;
        // badge circle is 548px across so 512 leaves a thin white ring
        private const float LogoSize = 512f;
        private const float LogoOffsetX = 486f;
        private const float TextOffsetX = -310f;

        // measured ingame: the client draws the avatar picture as a rounded
        // square about 1.68m wide centered 1.35m above the placement transform.
        // the pedestals scale field does nothing but the placement transforms
        // scale applies, so the anchor gets shrunk until the picture is button ish
        private const float PlateSize = 1.68f;
        private const float PlateCenterOffset = 1.35f;
        private const float PictureSize = 0.5f;

        // 16:9 booth screen, creators can scale the root if they want it bigger
        private const float ScreenWidth = 1.6f;
        private const float ScreenHeight = 0.9f;

        // control bar canvas, sized so it ends up exactly as wide as the screen
        private const float BarWidth = 1024f;
        private const float BarHeight = 160f;
        private const float BarInner = BarWidth - 8f;
        private const float RowY = -6f;

        // just the avatar picture the client draws, no frame around it. root
        // pivot sits at the center of the picture with the placement anchor
        // hanging below. the collider is solid on purpose, vrchats own sample
        // pedestal uses a solid one and the interact needs it to register
        private static void BuildAvatarPedestal()
        {
            var root = new GameObject("Alley Avatar Pedestal");
            try
            {
                var helper = root.AddComponent<AlleyAvatarPedestal>();

                var collider = root.AddComponent<BoxCollider>();
                collider.size = new Vector3(PictureSize + 0.05f, PictureSize + 0.05f, 0.12f);

                var pedestal = root.AddComponent<VRCAvatarPedestal>();
                pedestal.ChangeAvatarsOnUse = true;

                // pressing is udon's job, the pedestal component alone is inert
                var interact = (AlleyPedestalInteract)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyPedestalInteract));
                UdonBehaviour interactBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(interact);
                interactBacking.interactText = "Use Avatar";
                interactBacking.proximity = 3f;
                interactBacking.SyncMethod = VRC.SDKBase.Networking.SyncType.None;

                float anchorScale = PictureSize / PlateSize;
                var anchor = new GameObject("Avatar Display").transform;
                anchor.SetParent(root.transform, false);
                anchor.localPosition = new Vector3(0f, -PlateCenterOffset * anchorScale, 0.01f);
                anchor.localScale = Vector3.one * anchorScale;
                pedestal.Placement = anchor;

                helper.displayAnchor = anchor;

                SavePrefab(root, "Alley Avatar Pedestal");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // marker plus the trigger box that catches people walking in, same
        // dims as vrchats own sample prefab. the portal graphic spawns ingame
        private static void BuildPortal()
        {
            var root = new GameObject("Alley Portal");
            try
            {
                root.AddComponent<AlleyPortal>();
                var trigger = root.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(1f, 2f, 1f);
                trigger.center = new Vector3(0f, 1f, 0f);
                root.AddComponent<VRCPortalMarker>();
                SavePrefab(root, "Alley Portal");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [MenuItem("Tools/Legends Alley/Dev/Rebuild Bundled Prefabs")]
        public static void RebuildAll()
        {
            EnsureFolders();
            EnsureProgramAsset();
            Sprite disc = EnsureCircleSprite("AlleyDisc", false);
            Sprite rounded = EnsureShapeSprite("AlleyRounded", 64, RoundedSpriteDistance, new Vector4(24f, 24f, 24f, 24f));
            Sprite play = EnsureShapeSprite("AlleyPlayIcon", 64, PlayIconDistance, Vector4.zero);
            Sprite pause = EnsureShapeSprite("AlleyPauseIcon", 64, PauseIconDistance, Vector4.zero);
            Sprite spinner = EnsureShapeSprite("AlleySpinner", 64, SpinnerDistance, Vector4.zero);
            BuildGroupButton(disc, rounded);
            BuildAvatarPedestal();
            BuildPortal();
            BuildVideoPlayer(play, pause, spinner);
            BuildSlideshow(play);
            AssetDatabase.SaveAssets();
            Debug.Log("[LegendsAlley] Bundled prefabs rebuilt.");
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { PrefabFolder, TextureFolder, ProgramFolder, MaterialFolder })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
        }

        // usharp behaviours need a program asset before they can go on objects,
        // and the assembly itself has to be registered with usharp first
        internal static void EnsureProgramAsset()
        {
            string assemblyPath = AlleyConfig.PackageRoot + "/Runtime/LegendsNexus.Alley.Runtime.UdonSharp.asset";
            if (AssetDatabase.LoadAssetAtPath<UdonSharpAssemblyDefinition>(assemblyPath) == null)
            {
                var assembly = ScriptableObject.CreateInstance<UdonSharpAssemblyDefinition>();
                assembly.sourceAssembly = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(
                    AlleyConfig.PackageRoot + "/Runtime/LegendsNexus.Alley.Runtime.asmdef");
                AssetDatabase.CreateAsset(assembly, assemblyPath);
            }

            EnsureProgram("AlleyGroupButton");
            EnsureProgram("AlleyPedestalInteract");
            EnsureProgram("AlleyVideoPlayer");
            EnsureProgram("AlleyDirectoryKiosk");
            EnsureProgram("AlleyDirectoryEntry");
            EnsureProgram("AlleySignFeed");
            EnsureProgram("AlleyAnimationButton");
            EnsureProgram("AlleyPickupReset");
            EnsureProgram("AlleyTeleportButton");
            EnsureProgram("AlleySlideshow");
        }

        private static void EnsureProgram(string className)
        {
            string path = ProgramFolder + "/" + className + ".asset";
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path) != null) return;

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AlleyConfig.PackageRoot + "/Runtime/" + className + ".cs");
            var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript = script;
            AssetDatabase.CreateAsset(program, path);
            AssetDatabase.SaveAssets();
            // blocking compile, the prefab build right after needs it done
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions());
        }

        // simple antialiased white circle, tinted by the ui images that use it
        private static Sprite EnsureCircleSprite(string name, bool ring)
        {
            string path = TextureFolder + "/" + name + ".png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) != null) return AssetDatabase.LoadAssetAtPath<Sprite>(path);

            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            Vector2 center = new Vector2(size * 0.5f - 0.5f, size * 0.5f - 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(124f - d);
                    if (ring) alpha *= Mathf.Clamp01(d - 106f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static void BuildGroupButton(Sprite disc, Sprite rounded)
        {
            var root = new GameObject("Alley Group Button");
            try
            {
                // the plate, not the whole art canvas, so the press box matches
                var collider = root.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(CardWidth * CardScale, 708f * CardScale, 0.06f);

                var proxy = (AlleyGroupButton)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyGroupButton));
                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                backing.interactText = "Join Group";
                backing.proximity = 3f;
                backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;

                Transform card = MakeWorldCanvas(root.transform, "Card", new Vector2(CardWidth, CardHeight), CardScale, Vector3.zero);

                // sketch drew the three layers on one shared canvas, so they stack
                AddImage(card, "Back Plate", EnsureCardSprite("AlleyGroupCardBack"), Color.white, new Vector2(CardWidth, CardHeight));
                AddImage(card, "Front Plate", EnsureCardSprite("AlleyGroupCardFront"), Color.white, new Vector2(CardWidth, CardHeight));
                AddImage(card, "Badge", EnsureCardSprite("AlleyGroupCardBadge"), Color.white, new Vector2(CardWidth, CardHeight));

                // mask keeps square logos from spilling out of the badge circle
                var maskGo = new GameObject("Logo", typeof(RectTransform));
                maskGo.transform.SetParent(card, false);
                var maskRect = (RectTransform)maskGo.transform;
                maskRect.sizeDelta = new Vector2(LogoSize, LogoSize);
                maskRect.anchoredPosition = new Vector2(LogoOffsetX, 0f);
                var maskImage = maskGo.AddComponent<Image>();
                maskImage.sprite = disc;
                maskImage.raycastTarget = false;
                maskGo.AddComponent<Mask>().showMaskGraphic = false;

                Image logo = AddImage(maskGo.transform, "Group Logo", null, Color.white, new Vector2(LogoSize, LogoSize));
                logo.preserveAspect = true;
                // off until someone drops a sprite in, empty images paint white
                logo.enabled = false;
                // name on top, action underneath, both clear of the badge
                TMP_Text nameLabel = AddLabel(card, "Group Name", "YOUR COMMUNITY", new Vector2(980f, 250f), 44f, 132f, Color.white);
                ((RectTransform)nameLabel.transform).anchoredPosition = new Vector2(TextOffsetX, 92f);
                nameLabel.characterSpacing = 0f;

                Image chip = AddImage(card, "Action Chip", rounded, Color.white, new Vector2(700f, 140f));
                chip.type = Image.Type.Sliced;
                chip.pixelsPerUnitMultiplier = 0.5f;
                chip.rectTransform.anchoredPosition = new Vector2(TextOffsetX, -162f);
                TMP_Text action = AddLabel(card, "Action Label", "JOIN GROUP", new Vector2(580f, 110f), 32f, 58f, Magenta);
                ((RectTransform)action.transform).anchoredPosition = new Vector2(TextOffsetX, -162f);
                action.characterSpacing = 10f;

                proxy.nameLabel = (TextMeshProUGUI)nameLabel;
                proxy.logoTarget = logo;
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);

                SavePrefab(root, "Alley Group Button");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // slides live in one baked atlas, the bar just steps the index
        private static void BuildSlideshow(Sprite arrowIcon)
        {
            var root = new GameObject("Alley Slideshow");
            try
            {
                var proxy = (AlleySlideshow)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleySlideshow));
                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;
                root.AddComponent<AlleySlideshowSource>();

                var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
                board.name = "Board";
                Object.DestroyImmediate(board.GetComponent<Collider>());
                board.transform.SetParent(root.transform, false);
                board.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                board.transform.localScale = new Vector3(ScreenWidth, ScreenHeight, 1f);
                var boardRenderer = board.GetComponent<MeshRenderer>();
                boardRenderer.sharedMaterial = EnsureIdleMaterial();
                boardRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                const float slideBarHeight = 120f;
                Transform bar = MakeWorldCanvas(root.transform, "Slide Bar", new Vector2(BarWidth, slideBarHeight), 0.0015625f,
                    new Vector3(0f, -(ScreenHeight * 0.5f) - 0.12f, 0f));
                bar.gameObject.AddComponent<GraphicRaycaster>();
                bar.gameObject.AddComponent<VRCUiShape>();

                AddImage(bar, "Edge", null, Line, new Vector2(BarWidth, slideBarHeight));
                AddImage(bar, "Fill", null, CardFill, new Vector2(BarInner, slideBarHeight - 8f));
                AddAccentRun(bar, BarInner, 6f, (slideBarHeight - 8f) * 0.5f - 3f);

                Button prev = SlideButton(bar, "Prev Button", arrowIcon, -400f, 180f);
                Button next = SlideButton(bar, "Next Button", arrowIcon, 400f, 0f);
                UnityEventTools.AddStringPersistentListener(prev.onClick, backing.SendCustomEvent, "PreviousSlide");
                UnityEventTools.AddStringPersistentListener(next.onClick, backing.SendCustomEvent, "NextSlide");

                TMP_Text counter = AddLabel(bar, "Counter", "1 / 1", new Vector2(400f, 60f), 22f, 38f, LabelIdle);
                ((RectTransform)counter.transform).anchoredPosition = new Vector2(0f, -4f);

                proxy.target = boardRenderer;
                proxy.counterLabel = (TextMeshProUGUI)counter;
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);

                SavePrefab(root, "Alley Slideshow");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Button SlideButton(Transform bar, string name, Sprite icon, float x, float iconRotation)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(bar, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(80f, 80f);
            rect.anchoredPosition = new Vector2(x, -4f);

            AddImage(go.transform, "Edge", null, Pink, new Vector2(80f, 80f));
            Image face = AddImage(go.transform, "Face", null, RowIdle, new Vector2(72f, 72f));
            face.raycastTarget = true;
            Image arrow = AddImage(face.transform, "Arrow", icon, Color.white, new Vector2(34f, 34f));
            arrow.transform.localRotation = Quaternion.Euler(0f, 0f, iconRotation);

            var button = go.AddComponent<Button>();
            button.targetGraphic = face;
            return button;
        }

        // the layer art ships as plain pngs, first build flips them to sprites
        private static Sprite EnsureCardSprite(string name)
        {
            string path = TextureFolder + "/" + name + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("[LegendsAlley] Missing card art " + name);
                return null;
            }

            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // proximity driven booth video player: avpro engine so youtube links and
        // vrcdn streams both work, a screen quad, a short range speaker, and a
        // control bar with play/pause, volume, and a status readout
        private static void BuildVideoPlayer(Sprite playIcon, Sprite pauseIcon, Sprite spinnerIcon)
        {
            var root = new GameObject("Alley Video Player");
            try
            {
                var proxy = (AlleyVideoPlayer)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyVideoPlayer));
                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;

                // the video component has to live on the same object as the udon
                // behaviour or the OnVideo callbacks never arrive
                var avpro = root.AddComponent<VRCAVProVideoPlayer>();
                var avproSo = new SerializedObject(avpro);
                avproSo.FindProperty("autoPlay").boolValue = false;
                avproSo.FindProperty("loop").boolValue = false;
                avproSo.FindProperty("useLowLatency").boolValue = true;
                avproSo.FindProperty("maximumResolution").intValue = 720;
                avproSo.ApplyModifiedPropertiesWithoutUndo();

                var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
                screen.name = "Screen";
                Object.DestroyImmediate(screen.GetComponent<Collider>());
                screen.transform.SetParent(root.transform, false);
                screen.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                screen.transform.localScale = new Vector3(ScreenWidth, ScreenHeight, 1f);
                var screenRenderer = screen.GetComponent<MeshRenderer>();
                screenRenderer.sharedMaterial = EnsureScreenMaterial();
                screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var screenComponent = screen.AddComponent<VRCAVProVideoScreen>();
                var screenSo = new SerializedObject(screenComponent);
                screenSo.FindProperty("videoPlayer").objectReferenceValue = avpro;
                screenSo.FindProperty("textureProperty").stringValue = "_MainTex";
                // per instance material so booths never draw each others streams
                screenSo.FindProperty("useSharedMaterial").boolValue = false;
                screenSo.ApplyModifiedPropertiesWithoutUndo();

                // avpro leaves the last frame stuck on the quad when it stops
                var idle = GameObject.CreatePrimitive(PrimitiveType.Quad);
                idle.name = "Idle Screen";
                Object.DestroyImmediate(idle.GetComponent<Collider>());
                idle.transform.SetParent(root.transform, false);
                idle.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                idle.transform.localPosition = new Vector3(0f, 0f, 0.004f);
                idle.transform.localScale = new Vector3(ScreenWidth, ScreenHeight, 1f);
                var idleRenderer = idle.GetComponent<MeshRenderer>();
                idleRenderer.sharedMaterial = EnsureIdleMaterial();
                idleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var audioGo = new GameObject("Audio");
                audioGo.transform.SetParent(root.transform, false);
                var audio = audioGo.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1f;
                // linear actually reaches zero at max distance, log never does
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.minDistance = 0.5f;
                audio.maxDistance = 4f;
                audio.volume = 0.7f;
                audio.dopplerLevel = 0f;
                var speakerSo = new SerializedObject(audioGo.AddComponent<VRCAVProVideoSpeaker>());
                speakerSo.FindProperty("videoPlayer").objectReferenceValue = avpro;
                speakerSo.ApplyModifiedPropertiesWithoutUndo();
                // without this vrchat spatializes with its 40m default and the
                // audio source curve above never gets used
                var spatial = audioGo.AddComponent<VRCSpatialAudioSource>();
                spatial.Gain = 1f;
                spatial.Far = 4f;
                spatial.UseAudioSourceVolumeCurve = true;

                Transform bar = MakeWorldCanvas(root.transform, "Control Bar", new Vector2(BarWidth, BarHeight), 0.0015625f,
                    new Vector3(0f, -(ScreenHeight * 0.5f) - 0.15f, 0f));
                bar.gameObject.AddComponent<GraphicRaycaster>();
                bar.gameObject.AddComponent<VRCUiShape>();

                // hairline edge with the card inside it, cheaper than an outline
                AddImage(bar, "Edge", null, Line, new Vector2(BarWidth, BarHeight));
                AddImage(bar, "Fill", null, CardFill, new Vector2(BarInner, BarHeight - 8f));
                AddAccentRun(bar, BarInner, 6f, (BarHeight - 8f) * 0.5f - 3f);

                // play/pause, square with a pink edge like the sign cards
                var buttonRoot = new GameObject("Play Button", typeof(RectTransform));
                buttonRoot.transform.SetParent(bar, false);
                ((RectTransform)buttonRoot.transform).anchoredPosition = new Vector2(-428f, RowY);
                ((RectTransform)buttonRoot.transform).sizeDelta = new Vector2(108f, 108f);
                AddImage(buttonRoot.transform, "Edge", null, Pink, new Vector2(108f, 108f));
                Image face = AddImage(buttonRoot.transform, "Face", null, RowIdle, new Vector2(98f, 98f));
                face.raycastTarget = true;
                // the sprite itself already sits a touch right of center so the
                // triangle reads centered, no extra offset needed here
                Image playImage = AddImage(face.transform, "Play Icon", playIcon, Color.white, new Vector2(46f, 46f));
                Image pauseImage = AddImage(face.transform, "Pause Icon", pauseIcon, Color.white, new Vector2(46f, 46f));
                pauseImage.gameObject.SetActive(false);
                var button = buttonRoot.AddComponent<Button>();
                button.targetGraphic = face;
                UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, "OnPlayPauseClick");

                TMP_Text volumeTag = AddLabel(bar, "Volume Tag", "VOLUME", new Vector2(200f, 30f), 16f, 22f, TextDim);
                volumeTag.alignment = TextAlignmentOptions.Left;
                ((RectTransform)volumeTag.transform).anchoredPosition = new Vector2(-234f, RowY + 34f);

                Slider volume = AddVolumeSlider(bar, new Vector2(-96f, RowY - 8f), new Vector2(476f, 60f));
                UnityEventTools.AddStringPersistentListener(volume.onValueChanged, backing.SendCustomEvent, "OnVolumeChanged");

                AddImage(bar, "Divider", null, Line, new Vector2(2f, 84f))
                    .rectTransform.anchoredPosition = new Vector2(168f, RowY);

                TMP_Text statusTag = AddLabel(bar, "Status Tag", "STATUS", new Vector2(300f, 30f), 16f, 22f, TextDim);
                statusTag.alignment = TextAlignmentOptions.Right;
                ((RectTransform)statusTag.transform).anchoredPosition = new Vector2(332f, RowY + 34f);

                TMP_Text status = AddLabel(bar, "Status", "READY", new Vector2(300f, 60f), 22f, 40f, LabelIdle);
                status.alignment = TextAlignmentOptions.Right;
                // shrink long states instead of letting them wrap to two lines
                status.enableWordWrapping = false;
                ((RectTransform)status.transform).anchoredPosition = new Vector2(332f, RowY - 10f);

                // corner chip, dead centre just covers the booths own art
                Transform overlay = MakeWorldCanvas(root.transform, "Screen Overlay", new Vector2(1024f, 576f), 0.0015625f,
                    new Vector3(0f, 0f, 0.008f));
                var spinnerRoot = new GameObject("Loading Spinner", typeof(RectTransform));
                spinnerRoot.transform.SetParent(overlay, false);
                var spinnerRect = (RectTransform)spinnerRoot.transform;
                spinnerRect.sizeDelta = new Vector2(128f, 128f);
                spinnerRect.anchoredPosition = new Vector2(388f, -164f);
                AddImage(spinnerRoot.transform, "Plate", null, new Color32(5, 5, 5, 255), new Vector2(128f, 128f));
                Image spinner = AddImage(spinnerRoot.transform, "Arc", spinnerIcon, Pink, new Vector2(88f, 88f));
                spinnerRoot.SetActive(false);

                proxy.videoPlayer = avpro;
                proxy.audioSource = audio;
                proxy.volumeSlider = volume;
                proxy.statusText = (TextMeshProUGUI)status;
                proxy.playIcon = playImage.gameObject;
                proxy.pauseIcon = pauseImage.gameObject;
                proxy.idleScreen = idle;
                proxy.idleRenderer = idleRenderer;
                proxy.loadingIndicator = spinnerRoot;
                proxy.loadingSpinner = spinner.transform;
                // usharp only syncs proxy fields to the backing behaviour on scene
                // saves and world builds, neither happens during a prefab build
                UdonSharpEditorUtility.CopyProxyToUdon(proxy);

                SavePrefab(root, "Alley Video Player");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // the four brand colours in their usual run, pink leads and gold closes
        private static void AddAccentRun(Transform parent, float width, float height, float y)
        {
            var colours = new[] { Pink, Purple, Teal, Gold };
            var shares = new[] { 0.46f, 0.20f, 0.17f, 0.17f };
            var names = new[] { "Accent Pink", "Accent Purple", "Accent Teal", "Accent Gold" };

            float x = -width * 0.5f;
            for (int i = 0; i < colours.Length; i++)
            {
                float run = width * shares[i];
                AddImage(parent, names[i], null, colours[i], new Vector2(run, height))
                    .rectTransform.anchoredPosition = new Vector2(x + run * 0.5f, y);
                x += run;
            }
        }

        // flat rails and a chunky tick handle, no rounded caps anywhere
        private static Slider AddVolumeSlider(Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject("Volume", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            // clicking anywhere on the track jumps the handle there
            Image rail = AddImage(go.transform, "Rail", null, Line, Vector2.zero);
            rail.raycastTarget = true;
            var railRect = (RectTransform)rail.transform;
            railRect.anchorMin = new Vector2(0f, 0.5f);
            railRect.anchorMax = new Vector2(1f, 0.5f);
            railRect.sizeDelta = new Vector2(0f, 8f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.sizeDelta = new Vector2(-14f, 8f);

            Image fill = AddImage(fillArea.transform, "Fill", null, Pink, Vector2.zero);
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = new Vector2(14f, 0f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.anchorMin = new Vector2(0f, 0.5f);
            handleAreaRect.anchorMax = new Vector2(1f, 0.5f);
            handleAreaRect.sizeDelta = new Vector2(-14f, 48f);

            Image handle = AddImage(handleArea.transform, "Handle", null, Color.white, new Vector2(14f, 48f));
            handle.raycastTarget = true;
            // the slider stretches the handle down the slide area, keep the width
            // fixed so it stays a tick and not a bar
            ((RectTransform)handle.transform).sizeDelta = new Vector2(14f, 0f);

            var slider = go.AddComponent<Slider>();
            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handle;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.7f;
            return slider;
        }

        // dark idle screen on the sdks own avpro safe shader. the screen component
        // swaps in the live video texture on a per instance material at runtime
        private static Material EnsureScreenMaterial()
        {
            string path = MaterialFolder + "/AlleyVideoScreen.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            string texturePath = TextureFolder + "/AlleyScreenIdle.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath) == null)
            {
                var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                var pixels = new Color32[16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(4, 5, 7, 255);
                texture.SetPixels32(pixels);
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
            }

            var material = new Material(Shader.Find("Video/RealtimeEmissiveGamma"));
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetFloat("_Emission", 1f);
            material.SetFloat("_ApplyGamma", 0f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // the sakura plate creators get when they leave the idle image empty
        private static Material EnsureIdleMaterial()
        {
            Texture2D fallback = EnsureFallbackTexture();
            string path = MaterialFolder + "/AlleyVideoIdle.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Unlit/Texture"));
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.mainTexture != fallback)
            {
                material.mainTexture = fallback;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        // 1600x900 source, crunched down to 1024 wide
        private static Texture2D EnsureFallbackTexture()
        {
            string path = TextureFolder + "/AlleyVideoFallback.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError("[LegendsAlley] Missing AlleyVideoFallback.png");
                return null;
            }

            if (importer.maxTextureSize != 1024 || !importer.crunchedCompression)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Trilinear;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.crunchedCompression = true;
                importer.compressionQuality = 60;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // antialiased white shapes from little distance functions, tinted by the
        // ui images that use them. positive distance means inside the shape
        private static Sprite EnsureShapeSprite(string name, int size, System.Func<Vector2, float> distance, Vector4 border)
        {
            string path = TextureFolder + "/" + name + ".png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) != null) return AssetDatabase.LoadAssetAtPath<Sprite>(path);

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float alpha = Mathf.Clamp01(distance(new Vector2(x, y)) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static float RoundedSpriteDistance(Vector2 point)
        {
            Vector2 q = new Vector2(Mathf.Abs(point.x - 31.5f), Mathf.Abs(point.y - 31.5f)) - new Vector2(7.5f, 7.5f);
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            return 20f - (outside + inside);
        }

        private static float PlayIconDistance(Vector2 point)
        {
            var a = new Vector2(20f, 14f);
            var b = new Vector2(52f, 32f);
            var c = new Vector2(20f, 50f);
            return Mathf.Min(EdgeDistance(point, a, b), Mathf.Min(EdgeDistance(point, b, c), EdgeDistance(point, c, a)));
        }

        private static float PauseIconDistance(Vector2 point)
        {
            return Mathf.Max(PauseBarDistance(point, 21f), PauseBarDistance(point, 43f));
        }

        private static float PauseBarDistance(Vector2 point, float centerX)
        {
            Vector2 q = new Vector2(Mathf.Abs(point.x - centerX), Mathf.Abs(point.y - 32f)) - new Vector2(2f, 14f);
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
            return 4f - (outside + inside);
        }

        // ring with a quarter missing, the gap is what makes the spin read
        private static float SpinnerDistance(Vector2 point)
        {
            Vector2 d = point - new Vector2(31.5f, 31.5f);
            float ring = 3.5f - Mathf.Abs(d.magnitude - 22f);

            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            float sweep = angle <= 270f
                ? Mathf.Min(angle, 270f - angle)
                : -Mathf.Min(angle - 270f, 360f - angle);

            return Mathf.Min(ring, sweep);
        }

        // signed distance to the line a->b, positive on the winding side
        private static float EdgeDistance(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            return (edge.x * (point.y - a.y) - edge.y * (point.x - a.x)) / edge.magnitude;
        }

        // world space canvas turned to face the booth front (+Z of the root)
        private static Transform MakeWorldCanvas(Transform parent, string name, Vector2 size, float scale, Vector3 position)
        {
            var canvasGo = new GameObject(name, typeof(RectTransform));
            canvasGo.transform.SetParent(parent, false);
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * scale;
            rect.localPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 180f, 0f);
            return canvasGo.transform;
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
            // programmatic creation skips the default font hookup tmp does in the ui
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

        private static void SavePrefab(GameObject root, string name)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved) Debug.LogError("[LegendsAlley] Failed to save " + path);
        }
    }
}
