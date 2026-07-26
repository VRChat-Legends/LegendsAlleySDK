using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // packs a booth's slides into one grid atlas so a deck costs one texture
    internal static class AlleySlideshowBaker
    {
        private const string OutputRoot = "Assets/LegendsAlley/Slideshows";

        public static bool Bake(AlleySlideshowSource source, AlleySlideshow show, out string message)
        {
            message = "";
            if (source == null || show == null)
            {
                message = "Put the slideshow source and the slideshow on the same object.";
                return false;
            }

            Texture2D[] slides = Clean(source.slides);
            if (slides.Length == 0)
            {
                message = "Add at least one image to the slides list first.";
                return false;
            }

            int max = MaxSlides();
            if (slides.Length > max)
            {
                message = $"This event allows {max} slides and you have {slides.Length}. Trim the list and bake again.";
                return false;
            }

            int columns = Mathf.CeilToInt(Mathf.Sqrt(slides.Length));
            int rows = Mathf.CeilToInt(slides.Length / (float)columns);
            int atlasSize = Mathf.Clamp(Mathf.ClosestPowerOfTwo(source.atlasSize), 512, 4096);
            int cell = Mathf.Max(4, (atlasSize / Mathf.Max(columns, rows)) & ~3);

            var atlas = new Texture2D(cell * columns, cell * rows, TextureFormat.RGBA32, false);
            Fill(atlas, new Color32(8, 8, 10, 255));

            for (int i = 0; i < slides.Length; i++)
            {
                Color32[] pixels = Resample(slides[i], cell);
                if (pixels == null)
                {
                    message = $"Could not read \"{slides[i].name}\". Try a plain png or jpg.";
                    UnityEngine.Object.DestroyImmediate(atlas);
                    return false;
                }
                int column = i % columns;
                int row = i / columns;
                // rows fill from the top, unity textures start at the bottom
                atlas.SetPixels32(column * cell, (rows - row - 1) * cell, cell, cell, pixels);
            }
            atlas.Apply();

            string folder = Folder(source.gameObject.name);
            string path = folder + "/atlas.png";
            File.WriteAllBytes(path, atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            ImportAtlas(path, atlasSize);

            Material material = EnsureMaterial(folder, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            if (show.target != null)
            {
                show.target.sharedMaterial = material;
                EditorUtility.SetDirty(show.target);
            }

            show.slideCount = slides.Length;
            show.columns = columns;
            show.rows = rows;
            source.bakedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            EditorUtility.SetDirty(show);
            EditorUtility.SetDirty(source);
            AssetDatabase.SaveAssets();

            message = $"Baked {slides.Length} slides into a {cell * columns}x{cell * rows} atlas.";
            return true;
        }

        public static int MaxSlides()
        {
            AlleyEvent selected = AlleySession.SelectedEvent;
            int limit = selected != null && selected.limits != null ? selected.limits.maxSlideshowImages : 0;
            return limit > 0 ? limit : 12;
        }

        private static Texture2D[] Clean(Texture2D[] slides)
        {
            if (slides == null) return new Texture2D[0];
            int count = 0;
            foreach (Texture2D slide in slides) if (slide != null) count++;

            var kept = new Texture2D[count];
            int next = 0;
            foreach (Texture2D slide in slides) if (slide != null) kept[next++] = slide;
            return kept;
        }

        // blit through a render texture so unreadable imports still work
        private static Color32[] Resample(Texture2D source, int cell)
        {
            RenderTexture rt = RenderTexture.GetTemporary(cell, cell, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var flat = new Texture2D(cell, cell, TextureFormat.RGBA32, false);
                flat.ReadPixels(new Rect(0, 0, cell, cell), 0, 0);
                flat.Apply();
                Color32[] pixels = flat.GetPixels32();
                UnityEngine.Object.DestroyImmediate(flat);
                return pixels;
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static void Fill(Texture2D texture, Color32 color)
        {
            var pixels = new Color32[texture.width * texture.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels32(pixels);
        }

        private static string Folder(string name)
        {
            string safe = "";
            foreach (char c in name)
            {
                safe += char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-';
            }
            if (safe.Length == 0) safe = "slideshow";

            EnsureFolder("Assets/LegendsAlley");
            EnsureFolder(OutputRoot);
            EnsureFolder(OutputRoot + "/" + safe);
            return OutputRoot + "/" + safe;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void ImportAtlas(string path, int atlasSize)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            // clamp or the neighbouring cell bleeds in at the edges
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = atlasSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = true;
            importer.compressionQuality = 70;
            importer.SaveAndReimport();
        }

        private static Material EnsureMaterial(string folder, Texture2D atlas)
        {
            string path = folder + "/slideshow.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Unlit/Texture"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = atlas;
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
