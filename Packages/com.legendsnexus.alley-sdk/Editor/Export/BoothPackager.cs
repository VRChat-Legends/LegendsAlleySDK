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

        public static string CreatePackage(LegendsBooth booth, BoothStatsPayload stats, string[] shaders, AlleyEvent alleyEvent, CommunityInfo community)
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
                // fork probuilder meshes right away, the copy shares them with the
                // scene booth until then and destroying it would take them along
                ProBuilderBaker.MakeMeshesUnique(duplicate);
                // remember how the scene camera frames the booth before the copy
                // moves, creators aim Main Camera to pick their own preview shot
                Pose? previewPose = GetPreviewPose(booth.transform);
                float previewFov = Camera.main != null ? Camera.main.fieldOfView : 45f;
                // canonical pose so the plot's own transform decides where the booth
                // sits and which way the front faces at import time
                duplicate.transform.position = Vector3.zero;
                duplicate.transform.rotation = Quaternion.identity;
                PrepareForEvent(duplicate, alleyEvent.limits);

                if (!AssetDatabase.IsValidFolder(ExportFolder))
                {
                    AssetDatabase.CreateFolder("Assets", "LegendsAlleyExport");
                }
                // probuilder booths get baked into one mesh + one atlased material
                ProBuilderBaker.Bake(duplicate, ExportFolder, safeName);
                PrefabUtility.SaveAsPrefabAsset(duplicate, prefabPath, out bool prefabSaved);
                if (!prefabSaved)
                {
                    throw new InvalidOperationException("Unity refused to save the export prefab. Check the Console for the reason, fix the booth, and upload again.");
                }

                AssetDatabase.ExportPackage(
                    prefabPath,
                    Path.Combine(stagingDir, "booth.unitypackage"),
                    ExportPackageOptions.IncludeDependencies);

                // park the copy away from the scene so the preview only shows the booth
                duplicate.transform.position = new Vector3(0f, 4000f, 0f);
                File.WriteAllBytes(Path.Combine(stagingDir, "preview.png"), CapturePreview(duplicate, previewPose, previewFov));

                var metadata = new BoothMetadataPayload
                {
                    sdkVersion = AlleyConfig.SdkVersion,
                    eventId = alleyEvent.id,
                    communityId = community.id,
                    prefabName = safeName,
                    shaders = shaders ?? new string[0],
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
        private static void PrepareForEvent(GameObject root, EventLimits limits)
        {
            // leftover missing scripts make SaveAsPrefabAsset refuse the whole booth,
            // and they carry nothing the event world could run anyway
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
            }

            // editor sugar, the VRCAvatarPedestal underneath carries the real settings
            foreach (AlleyAvatarPedestal helper in root.GetComponentsInChildren<AlleyAvatarPedestal>(true))
            {
                UnityEngine.Object.DestroyImmediate(helper);
            }

            // usharp only syncs proxies to the backing udon behaviours on scene
            // save and world builds, neither of which happen during our export
            foreach (UdonSharp.UdonSharpBehaviour usharp in root.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))
            {
                UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(usharp);
            }

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

            float maxAudioRange = limits != null && limits.maxAudioRangeMeters > 0f ? limits.maxAudioRangeMeters : 0f;
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                source.spatialBlend = 1f;
                if (maxAudioRange > 0f && source.maxDistance > maxAudioRange)
                {
                    source.maxDistance = maxAudioRange;
                }
            }
            // vrchats own falloff comes from this component when present, clamping
            // just the audio source would leave a 40m+ Far wide open
            foreach (VRC.SDKBase.VRC_SpatialAudioSource spatial in root.GetComponentsInChildren<VRC.SDKBase.VRC_SpatialAudioSource>(true))
            {
                if (maxAudioRange > 0f && spatial.Far > maxAudioRange)
                {
                    spatial.Far = maxAudioRange;
                }
            }
        }

        // camera pose relative to the booth, so the same framing works on the
        // export copy wherever it ends up
        private static Pose? GetPreviewPose(Transform booth)
        {
            Camera camera = Camera.main;
            if (camera == null) return null;
            return new Pose(
                booth.InverseTransformPoint(camera.transform.position),
                Quaternion.Inverse(booth.rotation) * camera.transform.rotation);
        }

        private static byte[] CapturePreview(GameObject root, Pose? framing, float fov)
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

                float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
                if (framing.HasValue)
                {
                    camera.fieldOfView = fov;
                    camera.transform.position = root.transform.TransformPoint(framing.Value.position);
                    camera.transform.rotation = root.transform.rotation * framing.Value.rotation;
                }
                else
                {
                    camera.fieldOfView = 45f;
                    Vector3 direction = new Vector3(1f, 0.7f, 1f).normalized;
                    camera.transform.position = bounds.center + direction * radius * 2.2f;
                    camera.transform.LookAt(bounds.center);
                }
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = Mathf.Max(radius * 10f, Vector3.Distance(camera.transform.position, bounds.center) + radius * 4f);

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
