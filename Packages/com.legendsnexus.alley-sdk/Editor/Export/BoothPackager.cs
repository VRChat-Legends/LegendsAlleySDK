using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // builds the upload zip: a prepared prefab exported as a unitypackage,
    // the metadata json, and a camera snapshot as the preview image
    internal static class BoothPackager
    {
        private const string ExportFolder = "Assets/LegendsAlleyExport";

        public static string CreatePackage(LegendsBooth booth, BoothStatsPayload stats, AlleyEvent alleyEvent, CommunityInfo community)
        {
            string safeName = MakeSafeName(booth.BoothName);
            string stagingDir = Path.Combine(Path.GetTempPath(), "LegendsAlley", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);

            GameObject duplicate = null;
            string prefabPath = ExportFolder + "/" + safeName + ".prefab";
            try
            {
                duplicate = UnityEngine.Object.Instantiate(booth.gameObject);
                duplicate.name = safeName;
                // canonical pose so the plot's own transform decides where the booth
                // sits and which way the front faces at import time
                duplicate.transform.position = Vector3.zero;
                duplicate.transform.rotation = Quaternion.identity;
                PrepareForEvent(duplicate);

                if (!AssetDatabase.IsValidFolder(ExportFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "LegendsAlleyExport");
                }
                // probuilder booths get baked into one mesh + one atlased material
                ProBuilderBaker.Bake(duplicate, ExportFolder, safeName);
                PrefabUtility.SaveAsPrefabAsset(duplicate, prefabPath);

                AssetDatabase.ExportPackage(
                    prefabPath,
                    Path.Combine(stagingDir, "booth.unitypackage"),
                    ExportPackageOptions.IncludeDependencies);

                File.WriteAllBytes(Path.Combine(stagingDir, "preview.png"), CapturePreview(duplicate));

                var metadata = new BoothMetadataPayload
                {
                    sdkVersion = AlleyConfig.SdkVersion,
                    eventId = alleyEvent.id,
                    communityId = community.id,
                    prefabName = safeName,
                    stats = stats,
                };
                File.WriteAllText(Path.Combine(stagingDir, "booth.json"), JsonUtility.ToJson(metadata, true));

                string zipPath = Path.Combine(Path.GetTempPath(), "LegendsAlley", safeName + "-" + Guid.NewGuid().ToString("N") + ".zip");
                ZipFile.CreateFromDirectory(stagingDir, zipPath);
                return zipPath;
            }
            finally
            {
                if (duplicate != null) UnityEngine.Object.DestroyImmediate(duplicate);
                // the export folder is scratch space, baked assets live there too
                if (AssetDatabase.IsValidFolder(ExportFolder))
                {
                    AssetDatabase.DeleteAsset(ExportFolder);
                }
                try { Directory.Delete(stagingDir, true); } catch { }
                AssetDatabase.Refresh();
            }
        }

        // fixes applied to the export copy only, the scene object stays untouched
        private static void PrepareForEvent(GameObject root)
        {
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional)
                {
                    UnityEngine.Object.DestroyImmediate(light);
                    continue;
                }
                light.lightmapBakeType = LightmapBakeType.Baked;
                light.intensity = Mathf.Clamp(light.intensity, 0f, 10f);
                light.range = Mathf.Clamp(light.range, 0f, 7f);
            }

            foreach (ReflectionProbe probe in root.GetComponentsInChildren<ReflectionProbe>(true))
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                source.spatialBlend = 1f;
            }
        }

        private static byte[] CapturePreview(GameObject root)
        {
            const int size = 512;

            Bounds bounds = ComputeBounds(root);
            var cameraObject = new GameObject("AlleyPreviewCamera");
            Camera camera = cameraObject.AddComponent<Camera>();
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 24);
            var previous = RenderTexture.active;
            try
            {
                camera.backgroundColor = new Color(0.04f, 0.04f, 0.04f, 1f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.fieldOfView = 45f;

                float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
                Vector3 direction = new Vector3(1f, 0.7f, 1f).normalized;
                camera.transform.position = bounds.center + direction * radius * 2.2f;
                camera.transform.LookAt(bounds.center);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = radius * 10f;

                camera.targetTexture = rt;
                camera.Render();

                RenderTexture.active = rt;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                texture.Apply();
                byte[] png = texture.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(texture);
                return png;
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static Bounds ComputeBounds(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static string MakeSafeName(string name)
        {
            var builder = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') builder.Append(c);
                else if (c == ' ') builder.Append('_');
            }
            string safe = builder.ToString();
            return string.IsNullOrEmpty(safe) ? "Booth" : safe.Substring(0, Math.Min(safe.Length, 64));
        }
    }
}
