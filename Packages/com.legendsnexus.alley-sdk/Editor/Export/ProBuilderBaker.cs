using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if ALLEY_PROBUILDER
using UnityEngine.ProBuilder;
#endif

namespace LegendsNexus.Alley.Editor
{
    // booths built out of probuilder pieces get baked at package time: every piece
    // becomes one combined mesh with a single atlased material. keeps the upload
    // light and means the event world does not need probuilder installed at all
    internal static class ProBuilderBaker
    {
        public class BakeResult
        {
            public int PieceCount;
            public int AtlasSize;
        }

        public static int CountMeshes(GameObject root)
        {
#if ALLEY_PROBUILDER
            return root.GetComponentsInChildren<ProBuilderMesh>(true).Length;
#else
            return 0;
#endif
        }

        // probuilder rebuilds its unity meshes lazily after a domain reload, so
        // force any missing ones back into existence before counting triangles
        public static void RebuildMeshes(GameObject root)
        {
#if ALLEY_PROBUILDER
            foreach (ProBuilderMesh pb in root.GetComponentsInChildren<ProBuilderMesh>(true))
            {
                MeshFilter filter = pb.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null) continue;
                try { pb.ToMesh(); pb.Refresh(); } catch { }
            }
#endif
        }

        // renderers living on probuilder pieces, the analyzer treats these as
        // one renderer since that is what the bake produces
        public static HashSet<Renderer> CollectPieceRenderers(GameObject root)
        {
            var set = new HashSet<Renderer>();
#if ALLEY_PROBUILDER
            foreach (ProBuilderMesh pb in root.GetComponentsInChildren<ProBuilderMesh>(true))
            {
                Renderer renderer = pb.GetComponent<Renderer>();
                if (renderer != null) set.Add(renderer);
            }
#endif
            return set;
        }

        // instantiated probuilder objects share the source mesh until forked, so
        // destroying a copy would nuke the scene booth's meshes. fork them first
        public static void MakeMeshesUnique(GameObject root)
        {
#if ALLEY_PROBUILDER
            foreach (ProBuilderMesh pb in root.GetComponentsInChildren<ProBuilderMesh>(true))
            {
                try { pb.MakeUnique(); } catch { }
            }
#endif
        }

#if ALLEY_PROBUILDER
        private class Piece
        {
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();
            public Material Material;
            public Vector2 UvMin;
            public Vector2 UvSpan;
        }

        public static BakeResult Bake(GameObject root, string assetFolder, string safeName)
        {
            ProBuilderMesh[] pbMeshes = root.GetComponentsInChildren<ProBuilderMesh>(true);
            if (pbMeshes.Length == 0) return null;
            RebuildMeshes(root);

            List<Piece> pieces = GatherPieces(root, pbMeshes);
            if (pieces.Count == 0) return null;

            // one atlas cell per piece so tiled uvs get baked out instead of breaking
            var cells = new Texture2D[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                cells[i] = BakeCell(pieces[i]);
            }

            var atlas = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            Rect[] rects = atlas.PackTextures(cells, 4, 2048, false);
            foreach (Texture2D cell in cells) Object.DestroyImmediate(cell);
            if (rects == null)
            {
                Object.DestroyImmediate(atlas);
                return null;
            }
            atlas.name = safeName + "_Atlas";
            atlas.wrapMode = TextureWrapMode.Clamp;
            atlas.filterMode = FilterMode.Bilinear;

            Mesh combined = BuildCombinedMesh(pieces, rects, safeName);

            var material = new Material(Shader.Find("Standard")) { name = safeName + "_Baked" };
            material.mainTexture = atlas;

            AssetDatabase.CreateAsset(atlas, assetFolder + "/" + safeName + "_Atlas.asset");
            AssetDatabase.CreateAsset(material, assetFolder + "/" + safeName + "_Material.asset");
            AssetDatabase.CreateAsset(combined, assetFolder + "/" + safeName + "_Mesh.asset");

            ReplacePieces(root, pbMeshes, combined, material);

            return new BakeResult { PieceCount = pbMeshes.Length, AtlasSize = atlas.width };
        }

        private static List<Piece> GatherPieces(GameObject root, ProBuilderMesh[] pbMeshes)
        {
            var pieces = new List<Piece>();
            foreach (ProBuilderMesh pb in pbMeshes)
            {
                var filter = pb.GetComponent<MeshFilter>();
                var renderer = pb.GetComponent<MeshRenderer>();
                if (filter == null || renderer == null || filter.sharedMesh == null) continue;

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

                    Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                    Vector2 max = new Vector2(float.MinValue, float.MinValue);
                    foreach (Vector2 uv in piece.Uvs)
                    {
                        min = Vector2.Min(min, uv);
                        max = Vector2.Max(max, uv);
                    }
                    piece.UvMin = min;
                    piece.UvSpan = Vector2.Max(max - min, new Vector2(0.001f, 0.001f));
                    pieces.Add(piece);
                }
            }
            return pieces;
        }

        // renders the material's texture across the piece's uv range so tiling
        // survives the atlas, then tints it with the material color
        private static Texture2D BakeCell(Piece piece)
        {
            Texture2D source = piece.Material != null ? piece.Material.mainTexture as Texture2D : null;
            Color tint = piece.Material != null && piece.Material.HasProperty("_Color") ? piece.Material.color : Color.white;

            if (source == null)
            {
                var solid = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                var fill = new Color[64];
                for (int i = 0; i < fill.Length; i++) fill[i] = tint;
                solid.SetPixels(fill);
                solid.Apply();
                return solid;
            }

            int width = CellSize(source.width, piece.UvSpan.x);
            int height = CellSize(source.height, piece.UvSpan.y);

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

        private static int CellSize(int textureSize, float span)
        {
            return Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(textureSize * span)), 32, 1024);
        }

        private static Mesh BuildCombinedMesh(List<Piece> pieces, Rect[] rects, string safeName)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                Rect rect = rects[i];
                int offset = positions.Count;
                positions.AddRange(piece.Positions);
                normals.AddRange(piece.Normals);
                for (int v = 0; v < piece.Uvs.Count; v++)
                {
                    Vector2 normalized = (piece.Uvs[v] - piece.UvMin);
                    normalized.x /= piece.UvSpan.x;
                    normalized.y /= piece.UvSpan.y;
                    uvs.Add(new Vector2(rect.x + normalized.x * rect.width, rect.y + normalized.y * rect.height));
                }
                foreach (int index in piece.Indices) indices.Add(offset + index);
            }

            var mesh = new Mesh { name = safeName + "_Combined" };
            if (positions.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static void ReplacePieces(GameObject root, ProBuilderMesh[] pbMeshes, Mesh combined, Material material)
        {
            var baked = new GameObject("Booth Mesh (Baked)");
            baked.transform.SetParent(root.transform, false);
            baked.AddComponent<MeshFilter>().sharedMesh = combined;
            baked.AddComponent<MeshRenderer>().sharedMaterial = material;

            var shells = new List<GameObject>();
            foreach (ProBuilderMesh pb in pbMeshes)
            {
                if (pb == null) continue;
                GameObject go = pb.gameObject;
                // events are box collider only, and a mesh collider would point
                // at a destroyed probuilder mesh anyway
                var collider = go.GetComponent<MeshCollider>();
                if (collider != null) Object.DestroyImmediate(collider);
                // probuilder requires the filter and renderer, so it goes first
                Object.DestroyImmediate(pb);
                var filter = go.GetComponent<MeshFilter>();
                if (filter != null) Object.DestroyImmediate(filter);
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null) Object.DestroyImmediate(renderer);
                shells.Add(go);
            }

            // drop shells that ended up as empty transforms, keep anything with children or extra components
            foreach (GameObject shell in shells)
            {
                if (shell == null) continue;
                if (shell.transform.childCount == 0 && shell.GetComponents<Component>().Length == 1)
                {
                    Object.DestroyImmediate(shell);
                }
            }
        }
#else
        public static BakeResult Bake(GameObject root, string assetFolder, string safeName)
        {
            return null;
        }
#endif
    }
}
