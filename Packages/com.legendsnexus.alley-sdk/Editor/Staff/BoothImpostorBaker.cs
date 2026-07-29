using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace LegendsNexus.Alley.Editor
{
    // bakes vrchat-avatar-impostor style stand-ins for placed booths. eight
    // orthographic views around the booth plus a top view get packed into one
    // atlas, shown on a crossed-plane star with a roof cap so the silhouette
    // holds up from any angle. a cheaper 4 plane version takes over further out,
    // then the booth culls entirely. plain LODGroup, no custom shaders
    internal static class BoothImpostorBaker
    {
        private const string ImpostorNearName = "Booth Impostor";
        private const string ImpostorFarName = "Booth Impostor Far";
        private const string LodControlName = "Impostor LOD";
        private const int ViewResolution = 256;
        private const int AtlasCells = 4;
        private const int CaptureLayer = 31;

        // meters, assuming a 60 degree reference fov. vrchat lod bias scales these
        // up which only makes booths hold quality longer, never worse
        private const float FullQualityMeters = 20f;
        private const float NearImpostorMeters = 25f;
        private const float FarImpostorMeters = 30f;

        public class Summary
        {
            public int Baked;
            public int Skipped;
        }

        private struct View
        {
            public Vector3 LocalDir;
            public Vector3 LocalUp;
            public float HalfWidth;
            public float HalfHeight;
            public float Depth;
            public Vector2Int Cell;
        }

        public static Summary BakeAllPlacedBooths(System.Action<string> log)
        {
            var summary = new Summary();
            var plots = new List<BoothLocation>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    plots.AddRange(root.GetComponentsInChildren<BoothLocation>());
                }
            }

            foreach (BoothLocation plot in plots)
            {
                if (!plot.HasBooth) continue;
                GameObject booth = plot.transform.GetChild(0).gameObject;
                try
                {
                    if (Bake(booth, plot.PlotLabel))
                    {
                        summary.Baked++;
                        log?.Invoke($"{plot.PlotLabel}: impostor baked for {plot.placedCommunityName}.");
                    }
                    else
                    {
                        summary.Skipped++;
                        log?.Invoke($"{plot.PlotLabel}: no renderers to bake, skipped.");
                    }
                }
                catch (System.Exception e)
                {
                    summary.Skipped++;
                    log?.Invoke($"{plot.PlotLabel}: bake failed, {e.Message}");
                }
            }

            WirePerformanceMode(plots, log);

            if (summary.Baked > 0)
            {
                AssetDatabase.SaveAssets();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
            return summary;
        }

        // hands every baked booth to the world menu's performance switch so
        // visitors can trade booth detail for frames. udon cannot touch
        // LODGroup, so the switch gets the lod control child plus direct
        // impostor and renderer references instead. safe when no menu exists
        private static void WirePerformanceMode(List<BoothLocation> plots, System.Action<string> log)
        {
            AlleyPerformanceMode[] modes = Object.FindObjectsOfType<AlleyPerformanceMode>(true);
            if (modes.Length == 0) return;

            var roots = new List<GameObject>();
            var controls = new List<GameObject>();
            var nears = new List<GameObject>();
            var fars = new List<GameObject>();
            var reals = new List<Renderer>();
            var starts = new List<int>();
            var counts = new List<int>();
            foreach (BoothLocation plot in plots)
            {
                if (!plot.HasBooth) continue;
                GameObject booth = plot.transform.GetChild(0).gameObject;
                Transform control = booth.transform.Find(LodControlName);
                Transform near = booth.transform.Find(ImpostorNearName);
                Transform far = booth.transform.Find(ImpostorFarName);
                if (control == null || near == null || far == null) continue;

                roots.Add(booth);
                controls.Add(control.gameObject);
                nears.Add(near.gameObject);
                fars.Add(far.gameObject);
                starts.Add(reals.Count);
                int count = 0;
                foreach (Renderer renderer in booth.GetComponentsInChildren<Renderer>())
                {
                    Transform t = renderer.transform;
                    if (t == near || t == far || t == control || !renderer.enabled) continue;
                    reals.Add(renderer);
                    count++;
                }
                counts.Add(count);
            }

            foreach (AlleyPerformanceMode mode in modes)
            {
                mode.boothRoots = roots.ToArray();
                mode.lodControls = controls.ToArray();
                mode.nearImpostors = nears.ToArray();
                mode.farImpostors = fars.ToArray();
                mode.realRenderers = reals.ToArray();
                mode.realStarts = starts.ToArray();
                mode.realCounts = counts.ToArray();
                UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(mode);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(mode.gameObject.scene);
            }
            log?.Invoke($"Performance switch tracks {roots.Count} booth(s).");
        }

        public static bool Bake(GameObject boothRoot, string plotLabel)
        {
            // re-runs replace whatever the last bake left behind
            foreach (string name in new[] { ImpostorNearName, ImpostorFarName, LodControlName })
            {
                Transform previous = boothRoot.transform.Find(name);
                if (previous != null) Object.DestroyImmediate(previous.gameObject);
            }
            var oldGroup = boothRoot.GetComponent<LODGroup>();
            if (oldGroup != null) Object.DestroyImmediate(oldGroup);

            // real lod holds every renderer so particles cull with the booth, but
            // bounds and capture framing only trust mesh renderers
            var renderers = new List<Renderer>();
            var meshRenderers = new List<Renderer>();
            foreach (Renderer renderer in boothRoot.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled) continue;
                renderers.Add(renderer);
                if (renderer is MeshRenderer) meshRenderers.Add(renderer);
            }
            if (meshRenderers.Count == 0) return false;

            Bounds localBounds = ComputeLocalBounds(boothRoot, meshRenderers);
            View[] views = BuildViews(localBounds);
            Texture2D rawAtlas = CaptureAtlas(boothRoot, localBounds, views);

            string folder = CreateAssetFolder(plotLabel, boothRoot.name);
            string safeName = Sanitize(plotLabel + "-" + boothRoot.name);
            Texture2D atlas = SaveAtlasPng(rawAtlas, folder + "/" + safeName + "_Impostor.png");
            Object.DestroyImmediate(rawAtlas);

            // unlit cutout: the captures already contain the scene lighting, letting
            // the standard shader re-light them doubles up and nearby point lights
            // blow the quads out
            var material = new Material(Shader.Find("Unlit/Transparent Cutout")) { name = safeName + "_ImpostorMat" };
            material.mainTexture = atlas;
            material.SetFloat("_Cutoff", 0.5f);
            ReplaceAsset(material, folder + "/" + safeName + "_ImpostorMat.asset");

            // near star uses all eight side views plus the roof, far star only cardinals
            Mesh nearMesh = BuildImpostorMesh(localBounds, views, new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, true, safeName + "_ImpostorNear");
            ReplaceAsset(nearMesh, folder + "/" + safeName + "_ImpostorNear.asset");
            Mesh farMesh = BuildImpostorMesh(localBounds, views, new[] { 0, 2, 4, 6 }, false, safeName + "_ImpostorFar");
            ReplaceAsset(farMesh, folder + "/" + safeName + "_ImpostorFar.asset");

            Renderer nearRenderer = CreateImpostorChild(boothRoot, ImpostorNearName, nearMesh, material);
            Renderer farRenderer = CreateImpostorChild(boothRoot, ImpostorFarName, farMesh, material);

            // the group lives on its own child so the in world performance
            // switch can turn lod management off with a plain SetActive, udon
            // has no access to the LODGroup type itself
            var controlGo = new GameObject(LodControlName);
            controlGo.transform.SetParent(boothRoot.transform, false);
            controlGo.transform.localPosition = Vector3.zero;
            var group = controlGo.AddComponent<LODGroup>();
            var lods = new[]
            {
                new LOD(0.5f, renderers.ToArray()),
                new LOD(0.25f, new[] { nearRenderer }),
                new LOD(0.1f, new[] { farRenderer }),
            };
            group.SetLODs(lods);
            group.RecalculateBounds();

            // convert the meter targets into screen relative heights for this booth's size
            float size = group.size;
            lods[0].screenRelativeTransitionHeight = ScreenHeightAt(FullQualityMeters, size);
            lods[1].screenRelativeTransitionHeight = Mathf.Min(ScreenHeightAt(NearImpostorMeters, size), lods[0].screenRelativeTransitionHeight * 0.95f);
            lods[2].screenRelativeTransitionHeight = Mathf.Min(ScreenHeightAt(FarImpostorMeters, size), lods[1].screenRelativeTransitionHeight * 0.95f);
            group.SetLODs(lods);
            group.fadeMode = LODFadeMode.None;
            return true;
        }

        private static float ScreenHeightAt(float meters, float size)
        {
            // relative height = size / (2 * d * tan(fov/2)), 60 degree fov
            return Mathf.Clamp(size / (meters * 1.1547f), 0.001f, 0.95f);
        }

        private static Renderer CreateImpostorChild(GameObject boothRoot, string name, Mesh mesh, Material material)
        {
            var child = new GameObject(name);
            child.transform.SetParent(boothRoot.transform, false);
            child.transform.localPosition = Vector3.zero;
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        // views 0-7 walk around the booth in 45 degree steps, view 8 looks straight
        // down. LocalDir points from booth center toward the camera
        private static View[] BuildViews(Bounds localBounds)
        {
            Vector3 e = localBounds.extents;
            var views = new View[9];
            for (int k = 0; k < 8; k++)
            {
                float a = k * 45f * Mathf.Deg2Rad;
                float s = Mathf.Sin(a);
                float c = Mathf.Cos(a);
                views[k] = new View
                {
                    LocalDir = new Vector3(s, 0f, c),
                    LocalUp = Vector3.up,
                    HalfWidth = Mathf.Abs(c) * e.x + Mathf.Abs(s) * e.z,
                    HalfHeight = e.y,
                    Depth = Mathf.Abs(s) * e.x + Mathf.Abs(c) * e.z,
                    Cell = new Vector2Int(k % AtlasCells, k / AtlasCells),
                };
            }
            views[8] = new View
            {
                LocalDir = Vector3.up,
                LocalUp = Vector3.forward,
                HalfWidth = e.x,
                HalfHeight = e.z,
                Depth = e.y,
                Cell = new Vector2Int(0, 2),
            };
            return views;
        }

        private static Bounds ComputeLocalBounds(GameObject root, List<Renderer> renderers)
        {
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            var bounds = new Bounds();
            bool first = true;
            foreach (Renderer renderer in renderers)
            {
                Bounds world = renderer.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3(
                        (corner & 1) == 0 ? world.min.x : world.max.x,
                        (corner & 2) == 0 ? world.min.y : world.max.y,
                        (corner & 4) == 0 ? world.min.z : world.max.z);
                    Vector3 local = toRoot.MultiplyPoint3x4(point);
                    if (first)
                    {
                        bounds = new Bounds(local, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(local);
                    }
                }
            }
            bounds.extents = Vector3.Max(bounds.extents, new Vector3(0.05f, 0.05f, 0.05f));
            return bounds;
        }

        // every view renders twice (black then white background) so the silhouette
        // alpha works with any shader instead of trusting whatever the material
        // writes to the alpha channel
        private static Texture2D CaptureAtlas(GameObject root, Bounds localBounds, View[] views)
        {
            int atlasSize = ViewResolution * AtlasCells;
            var atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, false);
            atlas.SetPixels32(new Color32[atlasSize * atlasSize]);
            Transform rootTransform = root.transform;
            Vector3 worldCenter = rootTransform.TransformPoint(localBounds.center);
            float maxScale = Mathf.Max(rootTransform.lossyScale.x, rootTransform.lossyScale.y, rootTransform.lossyScale.z);

            var originalLayers = new Dictionary<Transform, int>();
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                originalLayers[child] = child.gameObject.layer;
                child.gameObject.layer = CaptureLayer;
            }

            var cameraGo = new GameObject("Impostor Capture Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << CaptureLayer;
            camera.nearClipPlane = 0.01f;
            camera.enabled = false;

            RenderTexture rt = RenderTexture.GetTemporary(ViewResolution, ViewResolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousActive = RenderTexture.active;
            var black = new Texture2D(ViewResolution, ViewResolution, TextureFormat.RGBA32, false);
            var white = new Texture2D(ViewResolution, ViewResolution, TextureFormat.RGBA32, false);

            try
            {
                foreach (View view in views)
                {
                    Vector3 dir = (rootTransform.rotation * view.LocalDir).normalized;
                    Vector3 up = (rootTransform.rotation * view.LocalUp).normalized;
                    float depth = view.Depth * maxScale;
                    camera.transform.position = worldCenter + dir * (depth + 1f);
                    camera.transform.rotation = Quaternion.LookRotation(-dir, up);
                    camera.orthographicSize = Mathf.Max(view.HalfHeight, 0.01f) * maxScale;
                    camera.aspect = Mathf.Max(view.HalfWidth, 0.01f) / Mathf.Max(view.HalfHeight, 0.01f);
                    camera.farClipPlane = depth * 2f + 2f;
                    camera.targetTexture = rt;

                    camera.backgroundColor = Color.black;
                    camera.Render();
                    RenderTexture.active = rt;
                    black.ReadPixels(new Rect(0, 0, ViewResolution, ViewResolution), 0, 0);

                    camera.backgroundColor = Color.white;
                    camera.Render();
                    RenderTexture.active = rt;
                    white.ReadPixels(new Rect(0, 0, ViewResolution, ViewResolution), 0, 0);

                    Color32[] dark = black.GetPixels32();
                    Color32[] bright = white.GetPixels32();
                    var final = new Color32[dark.Length];
                    for (int i = 0; i < dark.Length; i++)
                    {
                        int diff = ((bright[i].r - dark[i].r) + (bright[i].g - dark[i].g) + (bright[i].b - dark[i].b)) / 3;
                        byte alpha = (byte)Mathf.Clamp(255 - diff, 0, 255);
                        final[i] = new Color32(dark[i].r, dark[i].g, dark[i].b, alpha);
                    }
                    atlas.SetPixels32(view.Cell.x * ViewResolution, view.Cell.y * ViewResolution, ViewResolution, ViewResolution, final);
                }

                atlas.Apply(false);
                return atlas;
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(black);
                Object.DestroyImmediate(white);
                Object.DestroyImmediate(cameraGo);
                foreach (KeyValuePair<Transform, int> entry in originalLayers)
                {
                    if (entry.Key != null) entry.Key.gameObject.layer = entry.Value;
                }
            }
        }

        // png import instead of a raw texture asset so the atlas actually gets
        // dxt + crunch compressed in the build
        private static Texture2D SaveAtlasPng(Texture2D atlas, string assetPath)
        {
            System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(assetPath), atlas.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = ViewResolution * AtlasCells;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = true;
            importer.compressionQuality = 75;
            importer.isReadable = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static Mesh BuildImpostorMesh(Bounds localBounds, View[] views, int[] sideViews, bool topCap, string meshName)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            Vector3 c = localBounds.center;
            Vector3 e = localBounds.extents;

            foreach (int k in sideViews)
            {
                View view = views[k];
                Vector3 right = Vector3.Cross(Vector3.up, view.LocalDir) * view.HalfWidth;
                AddQuad(positions, normals, uvs, indices, c, view.LocalDir, right, Vector3.up * e.y, CellRect(view.Cell));
            }

            if (topCap)
            {
                // roof sits at the actual top of the bounds so the box reads closed
                View top = views[8];
                Vector3 roofCenter = c + Vector3.up * e.y;
                AddQuad(positions, normals, uvs, indices, roofCenter, Vector3.up, Vector3.left * e.x, Vector3.forward * e.z, CellRect(top.Cell));
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Rect CellRect(Vector2Int cell)
        {
            // couple texels of inset so mips and bilinear do not bleed neighbor views in
            float cellUv = 1f / AtlasCells;
            float pad = 2f / (ViewResolution * AtlasCells);
            return new Rect(cell.x * cellUv + pad, cell.y * cellUv + pad, cellUv - pad * 2f, cellUv - pad * 2f);
        }

        // quad centered on the booth, facing `normal`, `right`/`up` are half extents.
        // uv x runs against `right` so the capture (camera looking back at the booth)
        // reads the correct way around instead of mirrored
        private static void AddQuad(List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<int> indices, Vector3 center, Vector3 normal, Vector3 right, Vector3 up, Rect uvRect)
        {
            int offset = positions.Count;
            positions.Add(center - right - up);
            positions.Add(center + right - up);
            positions.Add(center + right + up);
            positions.Add(center - right + up);
            for (int i = 0; i < 4; i++) normals.Add(normal);
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            indices.Add(offset + 0);
            indices.Add(offset + 1);
            indices.Add(offset + 2);
            indices.Add(offset + 0);
            indices.Add(offset + 2);
            indices.Add(offset + 3);
        }

        private static void ReplaceAsset(Object asset, string path)
        {
            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        private static string CreateAssetFolder(string plotLabel, string boothName)
        {
            string[] segments = { "LegendsAlley", "Impostors", Sanitize(plotLabel + "-" + boothName) };
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
    }
}
