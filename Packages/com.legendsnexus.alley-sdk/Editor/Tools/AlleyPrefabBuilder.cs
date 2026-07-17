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
        private static readonly Color32 CardDark = new Color32(16, 18, 22, 250);

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

        [MenuItem("Tools/Legends Alley/Dev/Rebuild Bundled Prefabs")]
        public static void RebuildAll()
        {
            EnsureFolders();
            EnsureProgramAsset();
            Sprite disc = EnsureCircleSprite("AlleyDisc", false);
            Sprite ring = EnsureCircleSprite("AlleyRing", true);
            Sprite rounded = EnsureShapeSprite("AlleyRounded", 64, RoundedSpriteDistance, new Vector4(24f, 24f, 24f, 24f));
            Sprite play = EnsureShapeSprite("AlleyPlayIcon", 64, PlayIconDistance, Vector4.zero);
            Sprite pause = EnsureShapeSprite("AlleyPauseIcon", 64, PauseIconDistance, Vector4.zero);
            BuildGroupButton(disc, ring);
            BuildAvatarPedestal();
            BuildVideoPlayer(disc, ring, rounded, play, pause);
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
            EnsureProgram("AlleyDirectoryEntry");
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

        private static void BuildGroupButton(Sprite disc, Sprite ring)
        {
            var root = new GameObject("Alley Group Button");
            try
            {
                var collider = root.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(0.42f, 0.42f, 0.08f);

                var proxy = (AlleyGroupButton)UdonSharpComponentExtensions.AddUdonSharpComponent(root, typeof(AlleyGroupButton));
                UdonBehaviour backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(proxy);
                backing.interactText = "Visit Group";
                backing.proximity = 3f;
                backing.SyncMethod = VRC.SDKBase.Networking.SyncType.None;

                Transform face = MakeWorldCanvas(root.transform, "Button Face", new Vector2(512f, 512f), 0.00082f, Vector3.zero);
                AddImage(face, "Ring", ring, Pink, new Vector2(512f, 512f));
                AddImage(face, "Disc", disc, CardDark, new Vector2(470f, 470f));
                AddLabel(face, "Label", "VISIT GROUP", new Vector2(320f, 250f), 40f, 96f, Color.white);

                SavePrefab(root, "Alley Group Button");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // proximity driven booth video player: avpro engine so youtube links and
        // vrcdn streams both work, a screen quad, a short range speaker, and a
        // control bar with play/pause, volume, and a status readout
        private static void BuildVideoPlayer(Sprite disc, Sprite ring, Sprite rounded, Sprite playIcon, Sprite pauseIcon)
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

                Transform bar = MakeWorldCanvas(root.transform, "Control Bar", new Vector2(1024f, 160f), 0.0015625f,
                    new Vector3(0f, -(ScreenHeight * 0.5f) - 0.15f, 0f));
                bar.gameObject.AddComponent<GraphicRaycaster>();
                bar.gameObject.AddComponent<VRCUiShape>();

                Image backdrop = AddImage(bar, "Backdrop", rounded, CardDark, new Vector2(1024f, 160f));
                backdrop.type = Image.Type.Sliced;
                backdrop.pixelsPerUnitMultiplier = 0.55f;

                // play/pause, mirrors the group button look
                var buttonRoot = new GameObject("Play Button", typeof(RectTransform));
                buttonRoot.transform.SetParent(bar, false);
                ((RectTransform)buttonRoot.transform).anchoredPosition = new Vector2(-420f, 0f);
                ((RectTransform)buttonRoot.transform).sizeDelta = new Vector2(120f, 120f);
                AddImage(buttonRoot.transform, "Ring", ring, Pink, new Vector2(120f, 120f));
                Image face = AddImage(buttonRoot.transform, "Face", disc, new Color32(30, 33, 41, 255), new Vector2(104f, 104f));
                face.raycastTarget = true;
                // the sprite itself already sits a touch right of center so the
                // triangle reads centered, no extra offset needed here
                Image playImage = AddImage(face.transform, "Play Icon", playIcon, Color.white, new Vector2(52f, 52f));
                Image pauseImage = AddImage(face.transform, "Pause Icon", pauseIcon, Color.white, new Vector2(52f, 52f));
                pauseImage.gameObject.SetActive(false);
                var button = buttonRoot.AddComponent<Button>();
                button.targetGraphic = face;
                UnityEventTools.AddStringPersistentListener(button.onClick, backing.SendCustomEvent, "OnPlayPauseClick");

                Slider volume = AddVolumeSlider(bar, rounded, disc, new Vector2(-90f, 0f), new Vector2(420f, 90f));
                UnityEventTools.AddStringPersistentListener(volume.onValueChanged, backing.SendCustomEvent, "OnVolumeChanged");

                TMP_Text status = AddLabel(bar, "Status", "READY", new Vector2(320f, 110f), 24f, 40f, new Color32(183, 190, 205, 255));
                ((RectTransform)status.transform).anchoredPosition = new Vector2(315f, 0f);

                proxy.videoPlayer = avpro;
                proxy.audioSource = audio;
                proxy.volumeSlider = volume;
                proxy.statusText = (TextMeshProUGUI)status;
                proxy.playIcon = playImage.gameObject;
                proxy.pauseIcon = pauseImage.gameObject;
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

        // slider built by hand so the sprites and colors match the rest of the kit
        private static Slider AddVolumeSlider(Transform parent, Sprite rounded, Sprite disc, Vector2 position, Vector2 size)
        {
            var go = new GameObject("Volume", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image rail = AddImage(go.transform, "Rail", rounded, new Color32(42, 46, 56, 255), Vector2.zero);
            rail.type = Image.Type.Sliced;
            rail.pixelsPerUnitMultiplier = 3f;
            // clicking anywhere on the track jumps the handle there
            rail.raycastTarget = true;
            var railRect = (RectTransform)rail.transform;
            railRect.anchorMin = new Vector2(0f, 0.5f);
            railRect.anchorMax = new Vector2(1f, 0.5f);
            railRect.sizeDelta = new Vector2(0f, 18f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(go.transform, false);
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.sizeDelta = new Vector2(-24f, 18f);

            Image fill = AddImage(fillArea.transform, "Fill", rounded, Pink, Vector2.zero);
            fill.type = Image.Type.Sliced;
            fill.pixelsPerUnitMultiplier = 3f;
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = new Vector2(12f, 0f);

            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(go.transform, false);
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.anchorMin = new Vector2(0f, 0.5f);
            handleAreaRect.anchorMax = new Vector2(1f, 0.5f);
            handleAreaRect.sizeDelta = new Vector2(-44f, 44f);

            Image handle = AddImage(handleArea.transform, "Handle", disc, Color.white, new Vector2(44f, 44f));
            handle.raycastTarget = true;
            // the slider stretches the handle across the slide area vertically,
            // keep the width fixed so it stays a circle
            ((RectTransform)handle.transform).sizeDelta = new Vector2(44f, 0f);

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
