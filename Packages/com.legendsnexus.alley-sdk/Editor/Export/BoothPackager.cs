using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // builds the upload zip: a prepared prefab exported as a unitypackage,
    // the metadata json, and a framed preview render of the booth
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
                File.WriteAllBytes(Path.Combine(stagingDir, "preview.png"), CapturePreview(duplicate));

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

            // same deal, the VRCPortalMarker underneath carries the world id
            foreach (AlleyPortal helper in root.GetComponentsInChildren<AlleyPortal>(true))
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

            // slides are already in the atlas, keeping the refs ships them twice
            foreach (AlleySlideshowSource source in root.GetComponentsInChildren<AlleySlideshowSource>(true))
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            float maxAudioRange = limits != null && limits.maxAudioRangeMeters > 0f ? limits.maxAudioRangeMeters : 0f;
            // video player speakers promise a 5m cap regardless of the event limit
            foreach (AlleyVideoPlayer player in root.GetComponentsInChildren<AlleyVideoPlayer>(true))
            {
                AudioSource speaker = player.audioSource;
                if (speaker == null) continue;
                speaker.maxDistance = Mathf.Min(speaker.maxDistance, 5f);
                var spatial = speaker.GetComponent<VRC.SDKBase.VRC_SpatialAudioSource>();
                if (spatial != null) spatial.Far = Mathf.Min(spatial.Far, 5f);
            }
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

            IsolateAudio(root);
        }

        // opt in, drags every sound down so it dies at the plot edge. voices dont
        // go through these components so they are untouched
        private static void IsolateAudio(GameObject root)
        {
            var booth = root.GetComponent<LegendsBooth>();
            if (booth == null || !booth.isolateBoothAudio) return;

            Vector3 limit = LegendsBooth.BoundsLimit;
            float reach = Mathf.Max(1f, new Vector2(limit.x, limit.z).magnitude * 0.5f);

            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
            {
                // measured from the booth origin so a speaker at the back still reaches the front
                float offset = Vector3.Distance(root.transform.position, source.transform.position);
                float range = Mathf.Max(1f, reach - offset);
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = Mathf.Min(source.minDistance, range * 0.25f);
                source.maxDistance = Mathf.Min(source.maxDistance, range);

                var spatial = source.GetComponent<VRC.SDKBase.VRC_SpatialAudioSource>();
                if (spatial == null) continue;
                spatial.Far = Mathf.Min(spatial.Far, range);
                spatial.Near = Mathf.Min(spatial.Near, range * 0.25f);
            }
        }

        // preview capture: the export copy gets a small photo studio of its own.
        // this used to copy whatever pose the scene's Main Camera happened to be
        // in, which almost always left the booth tiny and off to one side
        private const int PreviewSize = 1024;
        private const int PreviewSupersample = 2;
        private const int PreviewLayer = 31;

        private static byte[] CapturePreview(GameObject root)
        {
            Bounds bounds = ComputeBounds(root);
            float radius = Mathf.Max(bounds.extents.magnitude, 0.25f);
            int render = PreviewSize * PreviewSupersample;

            var originalLayers = new Dictionary<Transform, int>();
            var parkedLights = new List<Light>();
            var rig = new GameObject("AlleyPreviewRig") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Rendering.AmbientMode ambientMode = RenderSettings.ambientMode;
            Color ambientLight = RenderSettings.ambientLight;
            float ambientIntensity = RenderSettings.ambientIntensity;
            RenderTexture full = RenderTexture.GetTemporary(render, render, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture downscale = RenderTexture.GetTemporary(PreviewSize, PreviewSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previousActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                // only the booth is on the capture layer, so the rest of the scene
                // stays out of frame and the rig lights stay off everything else
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    originalLayers[child] = child.gameObject.layer;
                    child.gameObject.layer = PreviewLayer;
                }
                // a scene sun would light the booth from whatever angle the creator
                // left it at, including not at all on a night scene. park them so
                // every booth gets the same shot, the booth's own lights stay on
                foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
                {
                    if (light.type != LightType.Directional || !light.isActiveAndEnabled) continue;
                    if (light.transform.IsChildOf(root.transform)) continue;
                    light.enabled = false;
                    parkedLights.Add(light);
                }
                // and a fixed ambient on top, otherwise a booth built in a dark
                // scene ships a preview where the inside is a black hole
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.19f, 0.17f, 0.22f, 1f);
                RenderSettings.ambientIntensity = 1f;

                Camera camera = BuildPreviewRig(rig, root.transform.rotation, bounds, radius);
                camera.targetTexture = full;
                camera.Render();
                camera.targetTexture = null;

                // rendered at double size and scaled down, cheapest way to keep the
                // booth's edges clean without depending on project quality settings
                full.filterMode = FilterMode.Bilinear;
                Graphics.Blit(full, downscale);

                RenderTexture.active = downscale;
                texture = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0, 0, PreviewSize, PreviewSize), 0, 0);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(downscale);
                RenderTexture.ReleaseTemporary(full);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(rig);
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
                foreach (Light light in parkedLights)
                {
                    if (light != null) light.enabled = true;
                }
                foreach (KeyValuePair<Transform, int> entry in originalLayers)
                {
                    if (entry.Key != null) entry.Key.gameObject.layer = entry.Value;
                }
            }
        }

        // three quarter product shot: camera off the booth's front corner, keyed
        // from above with a cool fill and a pink rim so it matches the sdk look
        private static Camera BuildPreviewRig(GameObject rig, Quaternion boothRotation, Bounds bounds, float radius)
        {
            var cameraObject = new GameObject("Camera");
            cameraObject.transform.SetParent(rig.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.039f, 0.039f, 0.043f, 1f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.fieldOfView = 32f;
            camera.aspect = 1f;

            // booths are built facing local +Z, the FRONT arrow in the inspector,
            // so stand off that corner instead of shooting a flat elevation
            Vector3 direction = (boothRotation * new Vector3(0.62f, 0.40f, 1f)).normalized;
            Quaternion look = Quaternion.LookRotation(-direction, Vector3.up);
            // aim at the middle of what the lens actually sees, then pull back to
            // fit it, otherwise perspective leaves the booth sitting low in frame
            float distance = FitDistance(bounds, bounds.center, look, camera.fieldOfView);
            Vector3 aim = CentreAim(bounds, look, distance);
            distance = FitDistance(bounds, aim, look, camera.fieldOfView);
            camera.transform.position = aim + direction * distance;
            camera.transform.rotation = look;
            camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 3f);
            camera.farClipPlane = distance + radius * 6f + 10f;

            AddRigLight(rig, look * Quaternion.Euler(26f, -36f, 0f), new Color(1f, 0.97f, 0.94f), 1.3f, LightShadows.Soft);
            AddRigLight(rig, look * Quaternion.Euler(10f, 54f, 0f), new Color(0.74f, 0.79f, 1f), 0.55f, LightShadows.None);
            AddRigLight(rig, look * Quaternion.Euler(-12f, 168f, 0f), new Color(1f, 0.36f, 0.62f), 0.85f, LightShadows.None);
            return camera;
        }

        private static void AddRigLight(GameObject rig, Quaternion rotation, Color color, float intensity, LightShadows shadows)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(rig.transform, false);
            go.transform.rotation = rotation;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows;
            light.shadowStrength = 0.6f;
            light.cullingMask = 1 << PreviewLayer;
        }

        // pulls back just far enough that every corner of the booth lands inside
        // the frame, so a small booth fills it and a big one still fits whole
        private static float FitDistance(Bounds bounds, Vector3 aim, Quaternion look, float fieldOfView)
        {
            Quaternion inverse = Quaternion.Inverse(look);
            float tan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            float distance = 0f;
            for (int i = 0; i < 8; i++)
            {
                // corner measured from the aim point, along the camera's axes
                Vector3 local = inverse * (Corner(bounds, i) - aim);
                distance = Mathf.Max(distance, Mathf.Abs(local.y) / tan - local.z);
                distance = Mathf.Max(distance, Mathf.Abs(local.x) / tan - local.z);
            }
            return Mathf.Max(distance * 1.09f, 0.5f);
        }

        // the near corners of a box spread wider on screen than the far ones, so
        // aiming at the middle of the box hangs the booth off centre. a couple of
        // passes over the projected corners settles on an aim point that centres it
        private static Vector3 CentreAim(Bounds bounds, Quaternion look, float distance)
        {
            Quaternion inverse = Quaternion.Inverse(look);
            Vector3 aim = bounds.center;
            for (int pass = 0; pass < 3; pass++)
            {
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 local = inverse * (Corner(bounds, i) - aim);
                    float depth = Mathf.Max(distance + local.z, 0.01f);
                    minX = Mathf.Min(minX, local.x / depth);
                    maxX = Mathf.Max(maxX, local.x / depth);
                    minY = Mathf.Min(minY, local.y / depth);
                    maxY = Mathf.Max(maxY, local.y / depth);
                }
                var shift = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, 0f);
                aim += look * (shift * distance);
            }
            return aim;
        }

        private static Vector3 Corner(Bounds bounds, int index)
        {
            return new Vector3(
                (index & 1) == 0 ? bounds.min.x : bounds.max.x,
                (index & 2) == 0 ? bounds.min.y : bounds.max.y,
                (index & 4) == 0 ? bounds.min.z : bounds.max.z);
        }

        // only what actually renders counts. counting disabled renderers dragged
        // the box out to wherever their transform sat and shrank the booth to a dot
        private static Bounds ComputeBounds(GameObject root)
        {
            bool found = false;
            var bounds = new Bounds(root.transform.position, Vector3.one);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                Bounds candidate = renderer.bounds;
                if (candidate.size == Vector3.zero) continue;
                if (found)
                {
                    bounds.Encapsulate(candidate);
                }
                else
                {
                    bounds = candidate;
                    found = true;
                }
            }
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
