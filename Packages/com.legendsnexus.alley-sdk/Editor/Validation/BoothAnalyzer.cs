using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using VRC.SDK3.Components;
using VRC.Udon;

namespace LegendsNexus.Alley.Editor
{
    // walks a booth and turns what it finds into the stats payload plus a
    // human readable checklist against the selected event limits
    internal static class BoothAnalyzer
    {
        public static BoothReport Analyze(LegendsBooth booth, EventLimits limits, bool limitsBypass = false)
        {
            var report = new BoothReport();
            GameObject root = booth.gameObject;
            ProBuilderBaker.RebuildMeshes(root);

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            SkinnedMeshRenderer[] skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);

            BoothStatsPayload stats = report.Stats;
            stats.boundsMeters = MeasureBounds(renderers);
            stats.triangles = CountTriangles(meshFilters, skinned, root);
            stats.staticMeshes = meshFilters.Length;
            stats.skinnedMeshes = skinned.Length;
            stats.materialSlots = CountMaterialSlots(renderers);
            stats.particleSystems = particles.Length;
            stats.totalParticles = CountMaxParticles(particles);
            stats.animators = animators.Length;
            stats.animationClips = CountClips(animators);
            stats.udonScripts = root.GetComponentsInChildren<UdonBehaviour>(true).Length;
            stats.pickups = root.GetComponentsInChildren<VRCPickup>(true).Length;
            stats.avatarPedestals = root.GetComponentsInChildren<VRCAvatarPedestal>(true).Length;
            stats.portals = root.GetComponentsInChildren<VRCPortalMarker>(true).Length;
            stats.textComponents = CountTextComponents(root);
            AudioSource[] audioSources = root.GetComponentsInChildren<AudioSource>(true);
            stats.audioSources = audioSources.Length;
            foreach (AudioSource source in audioSources)
            {
                stats.audioRangeMeters = Mathf.Max(stats.audioRangeMeters, source.maxDistance);
            }
            stats.nonBoxColliders = CountNonBoxColliders(root);

            // probuilder pieces collapse to one renderer with one material at
            // package time, so estimate against what actually ships
            HashSet<Renderer> pbRenderers = ProBuilderBaker.CollectPieceRenderers(root);
            EstimateRendering(renderers, pbRenderers, stats, report);

            CollectAssetStats(root, stats, report);
            CollectBlockers(root, report);

            int pbMeshes = ProBuilderBaker.CountMeshes(root);
            if (pbMeshes > 0)
            {
                report.Rows.Add(new CheckRow
                {
                    Label = "ProBuilder",
                    Value = pbMeshes + " meshes",
                    Limit = "auto-optimized",
                    Severity = CheckSeverity.Pass,
                    Hint = "ProBuilder detected on booth, optimizations will be done at upload: meshes combined into one and textures atlased into a single material.",
                });
            }

            if (limits != null) BuildChecklist(report, limits, limitsBypass);
            return report;
        }

        private static BoundsLimit MeasureBounds(Renderer[] renderers)
        {
            if (renderers.Length == 0) return new BoundsLimit();
            Vector3 min = renderers[0].bounds.min;
            Vector3 max = renderers[0].bounds.max;
            foreach (Renderer renderer in renderers)
            {
                min = Vector3.Min(min, renderer.bounds.min);
                max = Vector3.Max(max, renderer.bounds.max);
            }
            Vector3 size = max - min;
            return new BoundsLimit
            {
                x = Mathf.Ceil(size.x * 10f) / 10f,
                y = Mathf.Ceil(size.y * 10f) / 10f,
                z = Mathf.Ceil(size.z * 10f) / 10f,
            };
        }

        private static int CountTriangles(MeshFilter[] filters, SkinnedMeshRenderer[] skinned, GameObject root)
        {
            long total = 0;
            foreach (MeshFilter filter in filters)
            {
                if (filter.sharedMesh != null) total += CountMeshTriangles(filter.sharedMesh);
            }
            foreach (SkinnedMeshRenderer renderer in skinned)
            {
                if (renderer.sharedMesh != null) total += CountMeshTriangles(renderer.sharedMesh);
            }
            foreach (ParticleSystemRenderer renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh != null)
                {
                    total += CountMeshTriangles(renderer.mesh);
                }
            }
            return (int)Mathf.Min(total, int.MaxValue);
        }

        private static long CountMeshTriangles(Mesh mesh)
        {
            long tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                tris += (long)mesh.GetIndexCount(i) / 3;
            }
            return tris;
        }

        private static int CountMaterialSlots(Renderer[] renderers)
        {
            int slots = 0;
            foreach (Renderer renderer in renderers) slots += renderer.sharedMaterials.Length;
            return slots;
        }

        private static int CountMaxParticles(ParticleSystem[] systems)
        {
            int total = 0;
            foreach (ParticleSystem system in systems) total += system.main.maxParticles;
            return total;
        }

        private static int CountClips(Animator[] animators)
        {
            var clips = new HashSet<AnimationClip>();
            foreach (Animator animator in animators)
            {
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                if (controller == null) continue;
                foreach (AnimationClip clip in controller.animationClips)
                {
                    if (clip != null) clips.Add(clip);
                }
            }
            return clips.Count;
        }

        private static int CountTextComponents(GameObject root)
        {
            return root.GetComponentsInChildren<TMP_Text>(true).Length
                + root.GetComponentsInChildren<UnityEngine.UI.Text>(true).Length
                + root.GetComponentsInChildren<TextMesh>(true).Length;
        }

        // texture list, vram guess, and on disk size all come from the dependency walk.
        // texture memory is estimated from the format the BUILD will use (import
        // settings), not whatever the editor cache holds right now
        private static void CollectAssetStats(GameObject root, BoothStatsPayload stats, BoothReport report)
        {
            var textures = new HashSet<Texture>();
            var meshes = new HashSet<Mesh>();
            long diskBytes = 0;
            var countedPaths = new HashSet<string>();

            foreach (Object dependency in EditorUtility.CollectDependencies(new Object[] { root }))
            {
                string path = AssetDatabase.GetAssetPath(dependency);
                // editor-only package assets (component icons and the like) never ship
                bool editorOnly = path.Contains("/Editor Resources/") || path.Contains("/Editor/");
                if (dependency is Texture texture && !editorOnly) textures.Add(texture);
                if (dependency is Mesh mesh) meshes.Add(mesh);

                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets") || !countedPaths.Add(path)) continue;
                var info = new FileInfo(path);
                if (info.Exists) diskBytes += info.Length;
            }

            long vramBytes = 0;
            int maxResolution = 0;
            int uncompressedCount = 0;
            long uncompressedBytes = 0;
            var uncompressedNames = new List<string>();
            foreach (Texture texture in textures)
            {
                long bytes = EstimateTextureBytes(texture, out bool uncompressed);
                vramBytes += bytes;
                if (uncompressed && texture.width * texture.height >= 512 * 512)
                {
                    uncompressedCount++;
                    uncompressedBytes += bytes;
                    if (uncompressedNames.Count < 6) uncompressedNames.Add(texture.name);
                }
                maxResolution = Mathf.Max(maxResolution, Mathf.Max(texture.width, texture.height));
            }
            foreach (Mesh mesh in meshes)
            {
                // GetRuntimeMemorySizeLong counts the cpu + gpu copy, halve for the gpu share
                vramBytes += Profiler.GetRuntimeMemorySizeLong(mesh) / 2;
            }

            if (uncompressedCount > 0)
            {
                report.Rows.Add(new CheckRow
                {
                    Label = "Uncompressed textures",
                    Value = $"{uncompressedCount} ({uncompressedBytes / 1048576f:0.#}MB)",
                    Limit = "compress them",
                    Severity = CheckSeverity.Warn,
                    Hint = $"These textures have compression turned off in their import settings and eat most of the memory budget: {string.Join(", ", uncompressedNames)}. Select them in the Project window and set Compression to Normal Quality, that usually shrinks them 4-6x.",
                });
            }

            stats.uniqueTextures = textures.Count;
            stats.maxTextureResolution = maxResolution;
            stats.vramMB = Mathf.Round(vramBytes / 1048576f * 10f) / 10f;
            stats.buildSizeMB = Mathf.Round(diskBytes / 1048576f * 10f) / 10f;
        }

        // rough gpu bytes for the format the standalone build will import this
        // texture as. falls back to the editor runtime size for anything exotic
        private static long EstimateTextureBytes(Texture texture, out bool uncompressed)
        {
            uncompressed = false;
            string formatName = null;
            bool mips = texture.mipmapCount > 1;

            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer != null)
            {
                formatName = importer.GetAutomaticFormat("Standalone").ToString();
                mips = importer.mipmapEnabled;
            }
            else if (texture is Texture2D plain)
            {
                formatName = plain.format.ToString();
            }

            if (string.IsNullOrEmpty(formatName) || formatName == "Automatic")
            {
                return Profiler.GetRuntimeMemorySizeLong(texture) / 2;
            }

            float bitsPerPixel = FormatBitsPerPixel(formatName, out uncompressed);
            double bytes = (double)texture.width * texture.height * bitsPerPixel / 8d;
            if (mips) bytes *= 4d / 3d;
            return (long)bytes;
        }

        private static float FormatBitsPerPixel(string formatName, out bool uncompressed)
        {
            uncompressed = false;
            if (formatName.Contains("DXT1") || formatName.Contains("BC4") || formatName.Contains("ETC_RGB4") || formatName.Contains("ETC2_RGB")) return 4f;
            if (formatName.Contains("DXT5") || formatName.Contains("BC5") || formatName.Contains("BC6H") || formatName.Contains("BC7") || formatName.Contains("ETC2_RGBA")) return 8f;
            if (formatName.Contains("Alpha8") || formatName.Contains("R8")) return 8f;
            if (formatName.Contains("RGBAHalf")) { uncompressed = true; return 64f; }
            if (formatName.Contains("RGBAFloat")) { uncompressed = true; return 128f; }
            if (formatName.Contains("16") && !formatName.Contains("RGBA64")) return 16f;
            if (formatName.Contains("RGB24")) { uncompressed = true; return 24f; }
            uncompressed = true;
            return 32f;
        }

        private static int CountNonBoxColliders(GameObject root)
        {
            int count = 0;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!(collider is BoxCollider)) count++;
            }
            return count;
        }

        // draw calls ~= material slots before batching, set passes ~= unique
        // materials. baked probuilder pieces count as one of each. also collects
        // the shader list the package will ship and flags off whitelist ones
        private static void EstimateRendering(Renderer[] renderers, HashSet<Renderer> pbRenderers, BoothStatsPayload stats, BoothReport report)
        {
            int drawCalls = 0;
            var uniqueMaterials = new HashSet<Material>();
            var shaders = new SortedSet<string>();
            var flagged = new HashSet<string>();

            foreach (Renderer renderer in renderers)
            {
                if (pbRenderers.Contains(renderer)) continue;
                Material[] materials = renderer.sharedMaterials;
                drawCalls += materials.Length;
                foreach (Material material in materials)
                {
                    if (material == null) continue;
                    uniqueMaterials.Add(material);
                    string shaderName = material.shader != null ? material.shader.name : "";
                    if (string.IsNullOrEmpty(shaderName)) continue;
                    shaders.Add(shaderName);
                    if (!AlleyShaderRules.IsAllowed(shaderName) && flagged.Add(shaderName))
                    {
                        report.Blockers.Add($"Shader \"{shaderName}\" is not on the event whitelist. Use {AlleyShaderRules.Description}.");
                    }
                }
            }

            if (pbRenderers.Count > 0)
            {
                drawCalls += 1;
                shaders.Add("Standard");
                stats.estimatedSetPasses = uniqueMaterials.Count + 1;
            }
            else
            {
                stats.estimatedSetPasses = uniqueMaterials.Count;
            }
            stats.estimatedDrawCalls = drawCalls;
            report.ShaderNames.AddRange(shaders);
        }

        private static void CollectBlockers(GameObject root, BoothReport report)
        {
            int missingScripts = 0;
            string missingOn = null;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
                if (count == 0) continue;
                missingScripts += count;
                if (missingOn == null) missingOn = child.gameObject.name;
            }
            if (missingScripts > 0)
            {
                report.Rows.Add(new CheckRow
                {
                    Label = "Missing scripts",
                    Value = missingScripts.ToString(),
                    Limit = "removed at upload",
                    Severity = CheckSeverity.Warn,
                    Hint = $"Found {missingScripts} missing script{(missingScripts > 1 ? "s" : "")} (first on \"{missingOn}\"). They get stripped from the upload copy automatically, remove them yourself if that is not what you want.",
                });
            }

            int probes = root.GetComponentsInChildren<ReflectionProbe>(true).Length;
            if (probes > 0)
            {
                report.Blockers.Add($"Remove {probes} reflection probe{(probes > 1 ? "s" : "")}, booths cannot include them.");
            }

            int directional = 0;
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type == LightType.Directional) directional++;
            }
            if (directional > 0)
            {
                report.Blockers.Add($"Remove {directional} directional light{(directional > 1 ? "s" : "")}, they affect the whole event world.");
            }
        }

        private static void BuildChecklist(BoothReport report, EventLimits limits, bool limitsBypass)
        {
            BoothStatsPayload stats = report.Stats;
            BoundsLimit bounds = limits.maxBoundsMeters ?? new BoundsLimit { x = 5, y = 5, z = 5 };

            if (limitsBypass)
            {
                report.Rows.Insert(0, new CheckRow
                {
                    Label = "Limits bypass",
                    Value = "granted",
                    Limit = "by staff",
                    Severity = CheckSeverity.Warn,
                    Hint = "Staff gave this community a limits bypass. Over-limit numbers show as warnings and will not block the upload, but please keep it reasonable, the event still has to run smooth for everyone.",
                });
            }

            bool boundsOk = stats.boundsMeters.x <= bounds.x && stats.boundsMeters.y <= bounds.y && stats.boundsMeters.z <= bounds.z;
            report.Rows.Add(new CheckRow
            {
                Label = "Size",
                Value = $"{stats.boundsMeters.x} x {stats.boundsMeters.y} x {stats.boundsMeters.z}m",
                Limit = $"{bounds.x} x {bounds.y} x {bounds.z}m",
                Severity = boundsOk ? CheckSeverity.Pass : limitsBypass ? CheckSeverity.Warn : CheckSeverity.Fail,
                Hint = boundsOk ? null : "Shrink the booth so it fits inside the size box.",
            });

            AddCount(report, "Triangles", stats.triangles, limits.maxTriangles, limitsBypass);
            AddCount(report, "Build size (MB)", stats.buildSizeMB, limits.maxBuildSizeMB, limitsBypass);
            AddCount(report, "Memory estimate (MB)", stats.vramMB, limits.maxVramMB, limitsBypass);
            AddCount(report, "Material slots", stats.materialSlots, limits.maxMaterialSlots, limitsBypass);
            AddCount(report, "Est. draw calls", stats.estimatedDrawCalls, limits.maxEstimatedDrawCalls, limitsBypass);
            AddCount(report, "Est. set passes", stats.estimatedSetPasses, limits.maxEstimatedSetPasses, limitsBypass);
            AddCount(report, "Unique textures", stats.uniqueTextures, limits.maxUniqueTextures, limitsBypass);
            AddCount(report, "Largest texture", stats.maxTextureResolution, limits.maxTextureResolution, limitsBypass);
            AddCount(report, "Static meshes", stats.staticMeshes, limits.maxStaticMeshes, limitsBypass);
            AddCount(report, "Skinned meshes", stats.skinnedMeshes, limits.maxSkinnedMeshes, limitsBypass);
            AddCount(report, "Particle systems", stats.particleSystems, limits.maxParticleSystems, limitsBypass);
            AddCount(report, "Total particles", stats.totalParticles, limits.maxTotalParticles, limitsBypass);
            AddCount(report, "Animators", stats.animators, limits.maxAnimators, limitsBypass);
            AddCount(report, "Animation clips", stats.animationClips, limits.maxAnimationClips, limitsBypass);
            AddGated(report, "Udon scripts", stats.udonScripts, limits.maxUdonScripts, limits.allowUdon, limitsBypass);
            AddGated(report, "Pickups", stats.pickups, limits.maxPickups, limits.allowPickups, limitsBypass);
            AddGated(report, "Avatar pedestals", stats.avatarPedestals, limits.maxAvatarPedestals, limits.allowPedestals, limitsBypass);
            AddGated(report, "Portals", stats.portals, limits.maxPortals, limits.allowPortals, limitsBypass);
            AddCount(report, "Text components", stats.textComponents, limits.maxTextComponents, limitsBypass);
            AddCount(report, "Audio sources", stats.audioSources, limits.maxAudioSources, limitsBypass);

            // range gets clamped by the packager, so this warns instead of blocking
            // and the uploaded stat reports what actually ships
            if (limits.maxAudioRangeMeters > 0f && stats.audioSources > 0)
            {
                bool audioOver = stats.audioRangeMeters > limits.maxAudioRangeMeters;
                report.Rows.Add(new CheckRow
                {
                    Label = "Audio range (m)",
                    Value = stats.audioRangeMeters.ToString("0.#"),
                    Limit = limits.maxAudioRangeMeters.ToString("0.#"),
                    Severity = audioOver ? CheckSeverity.Warn : CheckSeverity.Pass,
                    Hint = audioOver ? $"Sounds reach past {limits.maxAudioRangeMeters:0.#}m. The range gets clamped at upload so audio stays near your booth." : null,
                });
                if (audioOver) stats.audioRangeMeters = limits.maxAudioRangeMeters;
            }

            bool collidersOk = stats.nonBoxColliders <= limits.maxNonBoxColliders;
            report.Rows.Add(new CheckRow
            {
                Label = "Non-box colliders",
                Value = stats.nonBoxColliders.ToString(),
                Limit = limits.maxNonBoxColliders.ToString(),
                Severity = collidersOk ? CheckSeverity.Pass : limitsBypass ? CheckSeverity.Warn : CheckSeverity.Fail,
                Hint = collidersOk ? null : "Only box colliders are allowed. Replace mesh, sphere, capsule, wheel, and terrain colliders with box colliders.",
            });
        }

        private static void AddCount(BoothReport report, string label, float value, float limit, bool limitsBypass)
        {
            CheckSeverity severity = value > limit ? (limitsBypass ? CheckSeverity.Warn : CheckSeverity.Fail)
                : value >= limit * 0.9f ? CheckSeverity.Warn
                : CheckSeverity.Pass;
            report.Rows.Add(new CheckRow
            {
                Label = label,
                Value = value.ToString("0.#"),
                Limit = limit.ToString("0.#"),
                Severity = severity,
                Hint = value > limit
                    ? (limitsBypass ? $"Over the normal limit of {limit:0.#}, allowed by your limits bypass." : $"Bring {label.ToLower()} down to {limit:0.#} or less.")
                    : null,
            });
        }

        private static void AddGated(BoothReport report, string label, int value, int limit, bool allowed, bool limitsBypass)
        {
            if (!allowed && value > 0)
            {
                report.Rows.Add(new CheckRow
                {
                    Label = label,
                    Value = value.ToString(),
                    Limit = "not allowed",
                    Severity = limitsBypass ? CheckSeverity.Warn : CheckSeverity.Fail,
                    Hint = limitsBypass
                        ? $"This event normally does not allow {label.ToLower()}, allowed by your limits bypass."
                        : $"This event does not allow {label.ToLower()}, remove them.",
                });
                return;
            }
            AddCount(report, label, value, limit, limitsBypass);
        }
    }
}
