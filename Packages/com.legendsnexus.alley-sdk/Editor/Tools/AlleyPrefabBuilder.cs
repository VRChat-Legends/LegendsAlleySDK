using System.IO;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
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
            BuildGroupButton(disc, ring);
            BuildAvatarPedestal();
            AssetDatabase.SaveAssets();
            Debug.Log("[LegendsAlley] Bundled prefabs rebuilt.");
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { PrefabFolder, TextureFolder, ProgramFolder })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
        }

        // usharp behaviours need a program asset before they can go on objects,
        // and the assembly itself has to be registered with usharp first
        private static void EnsureProgramAsset()
        {
            string assemblyPath = AlleyConfig.PackageRoot + "/Runtime/LegendsNexus.Alley.Runtime.UdonSharp.asset";
            if (AssetDatabase.LoadAssetAtPath<UdonSharpAssemblyDefinition>(assemblyPath) == null)
            {
                var assembly = ScriptableObject.CreateInstance<UdonSharpAssemblyDefinition>();
                assembly.sourceAssembly = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(
                    AlleyConfig.PackageRoot + "/Runtime/LegendsNexus.Alley.Runtime.asmdef");
                AssetDatabase.CreateAsset(assembly, assemblyPath);
            }

            string path = ProgramFolder + "/AlleyGroupButton.asset";
            if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path) != null) return;

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AlleyConfig.PackageRoot + "/Runtime/AlleyGroupButton.cs");
            var program = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            program.sourceCsScript = script;
            AssetDatabase.CreateAsset(program, path);
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.CompileAllCsPrograms(true, true);
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
