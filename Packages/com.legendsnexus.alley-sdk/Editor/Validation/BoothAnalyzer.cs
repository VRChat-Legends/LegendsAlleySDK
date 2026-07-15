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
        public static BoothReport Analyze(LegendsBooth booth, EventLimits limits)
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
            stats.audioSources = root.GetComponentsInChildren<AudioSource>(true).Length;

            CollectAssetStats(root, stats);
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

            if (limits != null) BuildChecklist(report, limits);
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

        // texture list, vram guess, and on disk size all come from the dependency walk
        private static void CollectAssetStats(GameObject root, BoothStatsPayload stats)
        {
            var textures = new HashSet<Texture>();
            var meshes = new HashSet<Mesh>();
            long diskBytes = 0;
            var countedPaths = new HashSet<string>();

            foreach (Object dependency in EditorUtility.CollectDependencies(new Object[] { root }))
            {
                if (dependency is Texture texture) textures.Add(texture);
                if (dependency is Mesh mesh) meshes.Add(mesh);

                string path = AssetDatabase.GetAssetPath(dependency);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets") || !countedPaths.Add(path)) continue;
                var info = new FileInfo(path);
                if (info.Exists) diskBytes += info.Length;
            }

            long vramBytes = 0;
            int maxResolution = 0;
            foreach (Texture texture in textures)
            {
                vramBytes += Profiler.GetRuntimeMemorySizeLong(texture) / 2;
                maxResolution = Mathf.Max(maxResolution, Mathf.Max(texture.width, texture.height));
            }
            foreach (Mesh mesh in meshes)
            {
                vramBytes += Profiler.GetRuntimeMemorySizeLong(mesh) / 2;
            }

            stats.uniqueTextures = textures.Count;
            stats.maxTextureResolution = maxResolution;
            stats.vramMB = Mathf.Round(vramBytes / 1048576f * 10f) / 10f;
            stats.buildSizeMB = Mathf.Round(diskBytes / 1048576f * 10f) / 10f;
        }

        private static void CollectBlockers(GameObject root, BoothReport report)
        {
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

        private static void BuildChecklist(BoothReport report, EventLimits limits)
        {
            BoothStatsPayload stats = report.Stats;
            BoundsLimit bounds = limits.maxBoundsMeters ?? new BoundsLimit { x = 5, y = 5, z = 5 };

            bool boundsOk = stats.boundsMeters.x <= bounds.x && stats.boundsMeters.y <= bounds.y && stats.boundsMeters.z <= bounds.z;
            report.Rows.Add(new CheckRow
            {
                Label = "Size",
                Value = $"{stats.boundsMeters.x} x {stats.boundsMeters.y} x {stats.boundsMeters.z}m",
                Limit = $"{bounds.x} x {bounds.y} x {bounds.z}m",
                Severity = boundsOk ? CheckSeverity.Pass : CheckSeverity.Fail,
                Hint = boundsOk ? null : "Shrink the booth so it fits inside the size box.",
            });

            AddCount(report, "Triangles", stats.triangles, limits.maxTriangles);
            AddCount(report, "Build size (MB)", stats.buildSizeMB, limits.maxBuildSizeMB);
            AddCount(report, "Memory estimate (MB)", stats.vramMB, limits.maxVramMB);
            AddCount(report, "Material slots", stats.materialSlots, limits.maxMaterialSlots);
            AddCount(report, "Unique textures", stats.uniqueTextures, limits.maxUniqueTextures);
            AddCount(report, "Largest texture", stats.maxTextureResolution, limits.maxTextureResolution);
            AddCount(report, "Static meshes", stats.staticMeshes, limits.maxStaticMeshes);
            AddCount(report, "Skinned meshes", stats.skinnedMeshes, limits.maxSkinnedMeshes);
            AddCount(report, "Particle systems", stats.particleSystems, limits.maxParticleSystems);
            AddCount(report, "Total particles", stats.totalParticles, limits.maxTotalParticles);
            AddCount(report, "Animators", stats.animators, limits.maxAnimators);
            AddCount(report, "Animation clips", stats.animationClips, limits.maxAnimationClips);
            AddGated(report, "Udon scripts", stats.udonScripts, limits.maxUdonScripts, limits.allowUdon);
            AddGated(report, "Pickups", stats.pickups, limits.maxPickups, limits.allowPickups);
            AddGated(report, "Avatar pedestals", stats.avatarPedestals, limits.maxAvatarPedestals, limits.allowPedestals);
            AddGated(report, "Portals", stats.portals, limits.maxPortals, limits.allowPortals);
            AddCount(report, "Text components", stats.textComponents, limits.maxTextComponents);
            AddCount(report, "Audio sources", stats.audioSources, limits.maxAudioSources);
        }

        private static void AddCount(BoothReport report, string label, float value, float limit)
        {
            CheckSeverity severity = value > limit ? CheckSeverity.Fail
                : value >= limit * 0.9f ? CheckSeverity.Warn
                : CheckSeverity.Pass;
            report.Rows.Add(new CheckRow
            {
                Label = label,
                Value = value.ToString("0.#"),
                Limit = limit.ToString("0.#"),
                Severity = severity,
                Hint = severity == CheckSeverity.Fail ? $"Bring {label.ToLower()} down to {limit:0.#} or less." : null,
            });
        }

        private static void AddGated(BoothReport report, string label, int value, int limit, bool allowed)
        {
            if (!allowed && value > 0)
            {
                report.Rows.Add(new CheckRow
                {
                    Label = label,
                    Value = value.ToString(),
                    Limit = "not allowed",
                    Severity = CheckSeverity.Fail,
                    Hint = $"This event does not allow {label.ToLower()}, remove them.",
                });
                return;
            }
            AddCount(report, label, value, limit);
        }
    }
}
