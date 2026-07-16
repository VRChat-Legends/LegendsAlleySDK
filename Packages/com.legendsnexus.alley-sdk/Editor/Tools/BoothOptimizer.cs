using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace LegendsNexus.Alley.Editor
{
    // scene tool that squeezes a whole booth down to one mesh and a handful of
    // atlased materials. works on a copy, the original booth just gets disabled
    internal static class BoothOptimizer
    {
        public class Settings
        {
            public int TargetMaterialCount = 1;
            public int AtlasSize = 2048;
            public bool BakeTint = true;
            public bool GenerateLightmapUvs = true;
            public HashSet<Material> AtlasMaterials = new HashSet<Material>();
        }

        public class Result
        {
            public GameObject Optimized;
            public int RenderersBefore;
            public int MaterialsBefore;
            public int RenderersAfter;
            public int MaterialsAfter;
            public string Error;
        }

        public class MaterialEntry
        {
            public Material Material;
            public bool IsOpaque;
            public bool HasTexture;
            public int UseCount;
        }

        private class Piece
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();
            public Material Material;
            public Vector2 UvMin;
            public Vector2 UvSpan;
            public int Bucket = -1;
            public Rect CellRect;
        }

        public static List<MaterialEntry> ScanMaterials(GameObject root)
        {
            var order = new List<Material>();
            var byMaterial = new Dictionary<Material, MaterialEntry>();
            if (root == null) return new List<MaterialEntry>();

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    if (!byMaterial.TryGetValue(material, out MaterialEntry entry))
                    {
                        entry = new MaterialEntry
                        {
                            Material = material,
                            IsOpaque = material.renderQueue < (int)RenderQueue.AlphaTest,
                            HasTexture = material.mainTexture != null,
                        };
                        byMaterial[material] = entry;
                        order.Add(material);
                    }
                    byMaterial[material].UseCount++;
                }
            }

            var list = new List<MaterialEntry>();
            foreach (Material material in order) list.Add(byMaterial[material]);
            return list;
        }

        public static Result Optimize(GameObject source, Settings settings)
        {
            var result = new Result();
            if (source == null)
            {
                result.Error = "Drop your booth into the Booth root field first.";
                return result;
            }
            if (!source.scene.IsValid())
            {
                result.Error = "Drag the booth from the scene hierarchy, not from the project window.";
                return result;
            }

            CountStats(source, out result.RenderersBefore, out result.MaterialsBefore);
            if (result.RenderersBefore == 0)
            {
                result.Error = "No mesh renderers found on that object.";
                return result;
            }

            GameObject copy = null;
            try
            {
                EditorUtility.DisplayProgressBar("Booth Optimizer", "Copying the booth...", 0.05f);
                copy = Object.Instantiate(source, source.transform.parent);
                copy.name = source.name + " (Optimized)";
                Undo.RegisterCreatedObjectUndo(copy, "Optimize Booth");

                // instantiated probuilder pieces share meshes with the source until forked
                ProBuilderBaker.MakeMeshesUnique(copy);
                ProBuilderBaker.RebuildMeshes(copy);

                EditorUtility.DisplayProgressBar("Booth Optimizer", "Reading meshes...", 0.15f);
                var combinedParts = new List<(MeshFilter filter, MeshRenderer renderer)>();
                List<Piece> pieces = GatherPieces(copy, combinedParts);
                if (pieces.Count == 0)
                {
                    Object.DestroyImmediate(copy);
                    result.Error = "No usable meshes found on that object.";
                    return result;
                }

                // split into pieces that get atlased and pieces that ride along untouched
                var atlased = new List<Piece>();
                var passthroughSlots = new List<Material>();
                var passthroughPieces = new Dictionary<Material, List<Piece>>();
                foreach (Piece piece in pieces)
                {
                    if (piece.Material == null || settings.AtlasMaterials.Contains(piece.Material))
                    {
                        atlased.Add(piece);
                    }
                    else
                    {
                        if (!passthroughPieces.TryGetValue(piece.Material, out List<Piece> group))
                        {
                            group = new List<Piece>();
                            passthroughPieces[piece.Material] = group;
                            passthroughSlots.Add(piece.Material);
                        }
                        group.Add(piece);
                    }
                }

                int bucketCount = AssignBuckets(atlased, settings.TargetMaterialCount);

                string folder = CreateAssetFolder(source.name);
                string safeName = Sanitize(source.name);

                var slotMaterials = new List<Material>();
                var slotPieces = new List<List<Piece>>();

                for (int bucket = 0; bucket < bucketCount; bucket++)
                {
                    EditorUtility.DisplayProgressBar("Booth Optimizer", $"Baking atlas {bucket + 1} of {bucketCount}...", 0.25f + 0.4f * (bucket / (float)bucketCount));
                    var members = new List<Piece>();
                    foreach (Piece piece in atlased)
                    {
                        if (piece.Bucket == bucket) members.Add(piece);
                    }
                    if (members.Count == 0) continue;

                    Material bucketMaterial = BakeBucket(members, settings, folder, $"{safeName}_Atlas{bucket + 1}");
                    slotMaterials.Add(bucketMaterial);
                    slotPieces.Add(members);
                }

                foreach (Material material in passthroughSlots)
                {
                    slotMaterials.Add(material);
                    slotPieces.Add(passthroughPieces[material]);
                }

                EditorUtility.DisplayProgressBar("Booth Optimizer", "Building the combined mesh...", 0.7f);
                Mesh combined = BuildCombinedMesh(slotPieces, safeName);
                if (settings.GenerateLightmapUvs)
                {
                    EditorUtility.DisplayProgressBar("Booth Optimizer", "Generating lightmap UVs...", 0.8f);
                    Unwrapping.GenerateSecondaryUVSet(combined);
                }
                AssetDatabase.CreateAsset(combined, folder + "/" + safeName + "_Mesh.asset");

                EditorUtility.DisplayProgressBar("Booth Optimizer", "Rebuilding the copy...", 0.9f);
                ProBuilderBaker.StripComponents(copy);
                foreach ((MeshFilter filter, MeshRenderer renderer) in combinedParts)
                {
                    if (renderer != null) Object.DestroyImmediate(renderer);
                    if (filter != null) Object.DestroyImmediate(filter);
                }

                var holder = new GameObject("Booth Mesh (Optimized)");
                holder.transform.SetParent(copy.transform, false);
                holder.AddComponent<MeshFilter>().sharedMesh = combined;
                holder.AddComponent<MeshRenderer>().sharedMaterials = slotMaterials.ToArray();

                PruneEmptyChildren(copy.transform);

                AssetDatabase.SaveAssets();

                Undo.RecordObject(source, "Optimize Booth");
                source.SetActive(false);

                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(copy.scene);
                Selection.activeGameObject = copy;
                EditorGUIUtility.PingObject(copy);

                CountStats(copy, out result.RenderersAfter, out result.MaterialsAfter);
                result.Optimized = copy;
                return result;
            }
            catch (System.Exception e)
            {
                if (copy != null) Object.DestroyImmediate(copy);
                if (source != null) source.SetActive(true);
                Debug.LogException(e);
                result.Error = "Optimization failed: " + e.Message;
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void CountStats(GameObject root, out int renderers, out int materials)
        {
            renderers = 0;
            var distinct = new HashSet<Material>();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled) continue;
                renderers++;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null) distinct.Add(material);
                }
            }
            materials = distinct.Count;
        }

        private static List<Piece> GatherPieces(GameObject root, List<(MeshFilter, MeshRenderer)> parts)
        {
            var pieces = new List<Piece>();
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                parts.Add((filter, renderer));

                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                Vector2[] uvs = mesh.uv;
                Material[] materials = renderer.sharedMaterials;
                Matrix4x4 toRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    int[] triangles = mesh.GetTriangles(sub);
                    if (triangles.Length == 0) continue;

                    var piece = new Piece
                    {
                        Material = materials.Length > 0 ? materials[Mathf.Min(sub, materials.Length - 1)] : null,
                    };
                    var remap = new Dictionary<int, int>();
                    foreach (int index in triangles)
                    {
                        if (!remap.TryGetValue(index, out int mapped))
                        {
                            mapped = piece.Positions.Count;
                            remap[index] = mapped;
                            piece.Positions.Add(toRoot.MultiplyPoint3x4(vertices[index]));
                            Vector3 normal = normals.Length == vertices.Length ? normals[index] : Vector3.up;
                            piece.Normals.Add(toRoot.MultiplyVector(normal).normalized);
                            piece.Uvs.Add(uvs.Length == vertices.Length ? uvs[index] : Vector2.zero);
                        }
                        piece.Indices.Add(mapped);
                    }
                    pieces.Add(piece);
                }
            }
            return pieces;
        }

        // spread the atlased materials over the target bucket count, balanced by
        // texture area so one atlas does not end up hogging all the resolution
        private static int AssignBuckets(List<Piece> atlased, int targetCount)
        {
            var materials = new List<Material>();
            var areas = new Dictionary<Material, float>();
            bool hasNullMaterial = false;
            foreach (Piece piece in atlased)
            {
                if (piece.Material == null)
                {
                    hasNullMaterial = true;
                    continue;
                }
                if (!areas.ContainsKey(piece.Material))
                {
                    var texture = piece.Material.mainTexture;
                    areas[piece.Material] = texture != null ? texture.width * (float)texture.height : 64 * 64;
                    materials.Add(piece.Material);
                }
            }

            int bucketCount = Mathf.Clamp(targetCount, 1, 4);
            if (materials.Count + (hasNullMaterial ? 1 : 0) == 0) return 0;
            bucketCount = Mathf.Min(bucketCount, materials.Count + (hasNullMaterial ? 1 : 0));

            materials.Sort((a, b) => areas[b].CompareTo(areas[a]));
            var bucketArea = new float[bucketCount];
            var bucketOf = new Dictionary<Material, int>();
            foreach (Material material in materials)
            {
                int lightest = 0;
                for (int i = 1; i < bucketCount; i++)
                {
                    if (bucketArea[i] < bucketArea[lightest]) lightest = i;
                }
                bucketOf[material] = lightest;
                bucketArea[lightest] += areas[material];
            }

            foreach (Piece piece in atlased)
            {
                piece.Bucket = piece.Material == null ? 0 : bucketOf[piece.Material];
            }
            return bucketCount;
        }

        private static Material BakeBucket(List<Piece> members, Settings settings, string folder, string atlasName)
        {
            var cells = new Texture2D[members.Count];
            bool anyCutout = false;
            for (int i = 0; i < members.Count; i++)
            {
                Piece piece = members[i];
                PrepareUvSpan(piece);
                cells[i] = BakeCell(piece, settings);
                if (piece.Material != null && piece.Material.renderQueue >= (int)RenderQueue.AlphaTest) anyCutout = true;
            }

            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            Rect[] rects = atlas.PackTextures(cells, 4, settings.AtlasSize, false);
            foreach (Texture2D cell in cells) Object.DestroyImmediate(cell);
            if (rects == null) throw new System.Exception("Texture packing failed, try a bigger atlas size.");

            for (int i = 0; i < members.Count; i++) members[i].CellRect = rects[i];

            atlas.name = atlasName;
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.filterMode = FilterMode.Bilinear;
            AssetDatabase.CreateAsset(atlas, folder + "/" + atlasName + ".asset");

            var material = new Material(Shader.Find("Standard")) { name = atlasName + "_Mat" };
            material.mainTexture = atlas;
            if (anyCutout)
            {
                // standard shader cutout mode, alpha from the atlas keeps holes as holes
                material.SetFloat("_Mode", 1f);
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.Zero);
                material.SetInt("_ZWrite", 1);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            AssetDatabase.CreateAsset(material, folder + "/" + atlasName + "_Mat.asset");
            return material;
        }

        // material tiling gets folded into the uvs so the baked cell can cover the
        // real repeat range, exactly how the probuilder baker handles it
        private static void PrepareUvSpan(Piece piece)
        {
            Vector2 scale = piece.Material != null ? piece.Material.mainTextureScale : Vector2.one;
            Vector2 offset = piece.Material != null ? piece.Material.mainTextureOffset : Vector2.zero;
            for (int i = 0; i < piece.Uvs.Count; i++)
            {
                piece.Uvs[i] = Vector2.Scale(piece.Uvs[i], scale) + offset;
            }

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector2 uv in piece.Uvs)
            {
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }
            piece.UvMin = min;
            piece.UvSpan = Vector2.Max(max - min, new Vector2(0.001f, 0.001f));
        }

        private static Texture2D BakeCell(Piece piece, Settings settings)
        {
            Texture2D source = piece.Material != null ? piece.Material.mainTexture as Texture2D : null;
            Color tint = Color.white;
            if (settings.BakeTint && piece.Material != null && piece.Material.HasProperty("_Color"))
            {
                tint = piece.Material.color;
            }

            if (source == null)
            {
                var solid = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                var fill = new Color[64];
                Color solidColor = piece.Material != null && piece.Material.HasProperty("_Color") ? piece.Material.color : Color.white;
                for (int i = 0; i < fill.Length; i++) fill[i] = solidColor;
                solid.SetPixels(fill);
                solid.Apply();
                return solid;
            }

            int cap = Mathf.Max(64, settings.AtlasSize / 2);
            int width = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(source.width * piece.UvSpan.x)), 32, cap);
            int height = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(source.height * piece.UvSpan.y)), 32, cap);

            RenderTexture target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            TextureWrapMode previousWrap = source.wrapMode;
            try
            {
                source.wrapMode = TextureWrapMode.Repeat;
                Graphics.Blit(source, target, piece.UvSpan, piece.UvMin);
                RenderTexture.active = target;
                var cell = new Texture2D(width, height, TextureFormat.RGBA32, false);
                cell.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                if (tint != Color.white)
                {
                    Color[] pixels = cell.GetPixels();
                    for (int i = 0; i < pixels.Length; i++) pixels[i] *= tint;
                    cell.SetPixels(pixels);
                }
                cell.Apply();
                return cell;
            }
            finally
            {
                source.wrapMode = previousWrap;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Mesh BuildCombinedMesh(List<List<Piece>> slotPieces, string safeName)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var slotIndices = new List<List<int>>();

            foreach (List<Piece> slot in slotPieces)
            {
                var indices = new List<int>();
                foreach (Piece piece in slot)
                {
                    int offset = positions.Count;
                    positions.AddRange(piece.Positions);
                    normals.AddRange(piece.Normals);
                    bool remap = piece.Bucket >= 0;
                    for (int v = 0; v < piece.Uvs.Count; v++)
                    {
                        if (remap)
                        {
                            Vector2 normalized = piece.Uvs[v] - piece.UvMin;
                            normalized.x /= piece.UvSpan.x;
                            normalized.y /= piece.UvSpan.y;
                            uvs.Add(new Vector2(
                                piece.CellRect.x + normalized.x * piece.CellRect.width,
                                piece.CellRect.y + normalized.y * piece.CellRect.height));
                        }
                        else
                        {
                            uvs.Add(piece.Uvs[v]);
                        }
                    }
                    foreach (int index in piece.Indices) indices.Add(offset + index);
                }
                slotIndices.Add(indices);
            }

            var mesh = new Mesh { name = safeName + "_Optimized" };
            if (positions.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = slotIndices.Count;
            for (int slot = 0; slot < slotIndices.Count; slot++)
            {
                mesh.SetTriangles(slotIndices[slot], slot);
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static string CreateAssetFolder(string boothName)
        {
            string stamp = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string[] segments = { "LegendsAlley", "Optimized", Sanitize(boothName), stamp };
            string current = "Assets";
            foreach (string segment in segments)
            {
                string next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
                current = next;
            }
            return current;
        }

        private static string Sanitize(string name)
        {
            var builder = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                builder.Append(char.IsLetterOrDigit(c) ? c : '-');
            }
            string safe = builder.ToString().Trim('-');
            return string.IsNullOrEmpty(safe) ? "Booth" : safe;
        }

        // gameobjects that only carried visuals end up as empty husks, clear them
        // out but keep anything with colliders, scripts, lights or children
        private static void PruneEmptyChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                PruneEmptyChildren(root.GetChild(i));
            }
            if (root.parent == null) return;
            if (root.childCount == 0 && root.GetComponents<Component>().Length == 1)
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }
    }
}
