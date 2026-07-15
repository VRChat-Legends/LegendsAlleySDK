using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendsNexus.Alley.Editor
{
    // staff side sync: match uploaded booths to BoothLocation plots, download
    // whatever changed, and rebuild only those plots. everything else is skipped
    internal static class BoothImporter
    {
        private const string ImportRoot = "Assets/AlleyBooths";
        private const string ExportFolder = "Assets/LegendsAlleyExport";

        public static event Action<string> Log = delegate { };
        public static bool IsRunning { get; private set; }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            AssetDatabase.importPackageCompleted += OnImportCompleted;
            AssetDatabase.importPackageFailed += OnImportFailed;
            AssetDatabase.importPackageCancelled += OnImportCancelled;

            if (ImportQueue.HasWork())
            {
                EditorApplication.delayCall += () =>
                {
                    IsRunning = true;
                    Log("Resuming booth import after the script reload...");
                    ProcessNext();
                };
            }
        }

        public static async Task Sync(AlleyEvent alleyEvent)
        {
            if (IsRunning) return;
            IsRunning = true;
            try
            {
                var response = await AlleyHttp.GetJson<StaffBoothsResponse>(
                    $"/api/admin/booths?eventId={Uri.EscapeDataString(alleyEvent.id)}&status=active", AlleySession.Token);
                StaffBooth[] booths = response?.booths ?? Array.Empty<StaffBooth>();
                BoothLocation[] locations = FindLocations();

                Log($"Found {booths.Length} uploaded booth(s) and {locations.Length} plot(s) in the scene.");

                var queue = ImportQueue.Load();
                queue.items.Clear();
                queue.importedCount = 0;
                queue.updatedCount = 0;
                queue.skippedCount = 0;

                List<(BoothLocation location, StaffBooth booth)> plan = BuildPlan(booths, locations);

                foreach ((BoothLocation location, StaffBooth booth) in plan)
                {
                    if (location.placedSha256 == booth.sha256 && location.HasBooth)
                    {
                        queue.skippedCount++;
                        continue;
                    }

                    bool isUpdate = location.placedCommunityId == booth.communityId && location.HasBooth;
                    Log($"{(isUpdate ? "Updating" : "Placing")} {booth.communityName} v{booth.version} on plot {location.PlotLabel}...");

                    string packagePath;
                    try
                    {
                        packagePath = await DownloadAndExtract(booth);
                    }
                    catch (Exception e)
                    {
                        Log($"Download failed for {booth.communityName}: {e.Message}");
                        continue;
                    }

                    if (isUpdate) queue.updatedCount++;
                    else queue.importedCount++;

                    queue.items.Add(new ImportItem
                    {
                        boothId = booth.id,
                        communityId = booth.communityId,
                        communityName = booth.communityName,
                        prefabName = SanitizeName(booth.prefabName),
                        sha256 = booth.sha256,
                        version = booth.version,
                        locationPath = ScenePath(location.transform),
                        packagePath = packagePath,
                        stage = "pending",
                    });
                }

                ReportLeftovers(booths, locations, plan);
                ImportQueue.Save(queue);

                if (queue.items.Count == 0)
                {
                    Log($"Everything is up to date. Skipped {queue.skippedCount} plot(s).");
                    ImportQueue.Clear();
                    IsRunning = false;
                    return;
                }

                ProcessNext();
            }
            catch (Exception)
            {
                IsRunning = false;
                throw;
            }
        }

        private static List<(BoothLocation, StaffBooth)> BuildPlan(StaffBooth[] booths, BoothLocation[] locations)
        {
            var plan = new List<(BoothLocation, StaffBooth)>();
            var unassignedBooths = booths.OrderBy(b => b.uploadedAt, StringComparer.Ordinal).ToList();
            var freeLocations = locations.Where(l => !l.locked).OrderBy(l => l.PlotLabel, StringComparer.OrdinalIgnoreCase).ToList();

            // plots keep the community they already hold, so re-running never shuffles the map
            foreach (BoothLocation location in freeLocations.ToArray())
            {
                StaffBooth existing = unassignedBooths.FirstOrDefault(b => b.communityId == location.placedCommunityId);
                if (existing == null) continue;
                plan.Add((location, existing));
                unassignedBooths.Remove(existing);
                freeLocations.Remove(location);
            }

            // reservations by community slug
            foreach (BoothLocation location in freeLocations.ToArray())
            {
                if (string.IsNullOrEmpty(location.reservedFor)) continue;
                StaffBooth reserved = unassignedBooths.FirstOrDefault(
                    b => string.Equals(b.communitySlug, location.reservedFor.Trim(), StringComparison.OrdinalIgnoreCase));
                if (reserved == null) continue;
                plan.Add((location, reserved));
                unassignedBooths.Remove(reserved);
                freeLocations.Remove(location);
            }

            // everyone else fills free plots in plot order, first uploaded first placed
            foreach (StaffBooth booth in unassignedBooths.ToArray())
            {
                BoothLocation slot = freeLocations.FirstOrDefault(l => string.IsNullOrEmpty(l.reservedFor) && !l.HasBooth)
                    ?? freeLocations.FirstOrDefault(l => string.IsNullOrEmpty(l.reservedFor));
                if (slot == null) break;
                plan.Add((slot, booth));
                unassignedBooths.Remove(booth);
                freeLocations.Remove(slot);
            }

            return plan;
        }

        private static void ReportLeftovers(StaffBooth[] booths, BoothLocation[] locations, List<(BoothLocation location, StaffBooth booth)> plan)
        {
            var placedBoothIds = new HashSet<string>(plan.Select(p => p.booth.id));
            foreach (StaffBooth booth in booths)
            {
                if (!placedBoothIds.Contains(booth.id))
                {
                    Log($"No free plot for {booth.communityName}, add more Booth Locations to fit it.");
                }
            }

            var plannedLocations = new HashSet<BoothLocation>(plan.Select(p => p.location));
            foreach (BoothLocation location in locations)
            {
                if (location.HasBooth && !plannedLocations.Contains(location)
                    && booths.All(b => b.communityId != location.placedCommunityId))
                {
                    Log($"Plot {location.PlotLabel} holds {location.placedCommunityName} which no longer has an active booth. Clear it from the plot inspector if that is intended.");
                }
            }
        }

        private static async Task<string> DownloadAndExtract(StaffBooth booth)
        {
            byte[] zipBytes = await AlleyHttp.GetBytes($"/api/admin/booths/{booth.id}/download", AlleySession.Token);

            string workDir = Path.Combine(Path.GetTempPath(), "LegendsAlleyImport", booth.communityId);
            if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
            Directory.CreateDirectory(workDir);

            string zipPath = Path.Combine(workDir, "booth.zip");
            File.WriteAllBytes(zipPath, zipBytes);

            string packagePath = Path.Combine(workDir, "booth.unitypackage");
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                ZipArchiveEntry entry = archive.GetEntry("booth.unitypackage");
                if (entry == null) throw new Exception("the upload is missing its unitypackage");
                entry.ExtractToFile(packagePath, true);
            }
            return packagePath;
        }

        /* ─── queue processing, one plot at a time ─── */

        private static void ProcessNext()
        {
            ImportQueueData queue = ImportQueue.Load();
            if (queue.items.Count == 0)
            {
                Finish(queue);
                return;
            }

            ImportItem item = queue.items[0];
            BoothLocation location = FindLocationByPath(item.locationPath);
            if (location == null)
            {
                Log($"Plot for {item.communityName} disappeared from the scene, skipping it.");
                Advance(queue);
                return;
            }

            if (item.stage == "place")
            {
                PlaceCurrent(queue, item, location);
                return;
            }

            if (item.stage == "importing" && PrefabPathFor(item) is string existing && AssetDatabase.LoadAssetAtPath<GameObject>(existing) != null)
            {
                PlaceCurrent(queue, item, location);
                return;
            }

            if (!File.Exists(item.packagePath))
            {
                Log($"Lost the downloaded package for {item.communityName}, run the sync again.");
                Advance(queue);
                return;
            }

            // clear the old copy first so the fresh import cant tangle with it
            for (int i = location.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(location.transform.GetChild(i).gameObject);
            }
            string oldFolder = ImportRoot + "/" + item.communityId;
            if (AssetDatabase.IsValidFolder(oldFolder)) AssetDatabase.DeleteAsset(oldFolder);

            item.stage = "importing";
            ImportQueue.Save(queue);
            AssetDatabase.ImportPackage(item.packagePath, false);
        }

        private static void OnImportCompleted(string packageName)
        {
            ImportQueueData queue = ImportQueue.Load();
            if (queue.items.Count == 0) return;
            ImportItem item = queue.items[0];
            if (item.stage != "importing") return;

            item.stage = "place";
            ImportQueue.Save(queue);

            BoothLocation location = FindLocationByPath(item.locationPath);
            if (location == null)
            {
                Log($"Plot for {item.communityName} disappeared from the scene, skipping it.");
                Advance(queue);
                return;
            }
            PlaceCurrent(queue, item, location);
        }

        private static void OnImportFailed(string packageName, string error)
        {
            ImportQueueData queue = ImportQueue.Load();
            if (queue.items.Count == 0) return;
            Log($"Import failed for {queue.items[0].communityName}: {error}");
            Advance(queue);
        }

        private static void OnImportCancelled(string packageName)
        {
            OnImportFailed(packageName, "cancelled");
        }

        private static void PlaceCurrent(ImportQueueData queue, ImportItem item, BoothLocation location)
        {
            try
            {
                string prefabPath = MoveImportedAssets(item);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) throw new Exception($"could not find the booth prefab at {prefabPath}");

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, location.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                foreach (LegendsBooth marker in instance.GetComponentsInChildren<LegendsBooth>(true))
                {
                    UnityEngine.Object.DestroyImmediate(marker, true);
                }

                location.placedCommunityId = item.communityId;
                location.placedCommunityName = item.communityName;
                location.placedVersion = item.version;
                location.placedSha256 = item.sha256;
                EditorSceneManager.MarkSceneDirty(location.gameObject.scene);

                Log($"Placed {item.communityName} v{item.version} on plot {location.PlotLabel}.");
            }
            catch (Exception e)
            {
                Log($"Could not place {item.communityName}: {e.Message}");
            }
            Advance(queue);
        }

        private static string MoveImportedAssets(ImportItem item)
        {
            string target = ImportRoot + "/" + item.communityId;
            if (AssetDatabase.IsValidFolder(ExportFolder))
            {
                if (!AssetDatabase.IsValidFolder(ImportRoot)) AssetDatabase.CreateFolder("Assets", "AlleyBooths");
                if (AssetDatabase.IsValidFolder(target)) AssetDatabase.DeleteAsset(target);
                string error = AssetDatabase.MoveAsset(ExportFolder, target);
                if (!string.IsNullOrEmpty(error)) throw new Exception(error);
            }
            return target + "/" + item.prefabName + ".prefab";
        }

        private static string PrefabPathFor(ImportItem item)
        {
            string moved = ImportRoot + "/" + item.communityId + "/" + item.prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(moved) != null) return moved;
            string fresh = ExportFolder + "/" + item.prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(fresh) != null) return fresh;
            return null;
        }

        private static void Advance(ImportQueueData queue)
        {
            if (queue.items.Count > 0)
            {
                try { File.Delete(queue.items[0].packagePath); } catch { }
                queue.items.RemoveAt(0);
            }
            ImportQueue.Save(queue);
            EditorApplication.delayCall += ProcessNext;
        }

        private static void Finish(ImportQueueData queue)
        {
            IsRunning = false;
            ImportQueue.Clear();
            Log($"Sync finished. Placed {queue.importedCount}, updated {queue.updatedCount}, skipped {queue.skippedCount} already up to date.");
        }

        /* ─── scene helpers ─── */

        public static BoothLocation[] FindLocations()
        {
            var found = new List<BoothLocation>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    found.AddRange(root.GetComponentsInChildren<BoothLocation>(true));
                }
            }
            return found.ToArray();
        }

        private static BoothLocation FindLocationByPath(string path)
        {
            foreach (BoothLocation location in FindLocations())
            {
                if (ScenePath(location.transform) == path) return location;
            }
            return null;
        }

        private static string ScenePath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static string SanitizeName(string name)
        {
            var builder = new System.Text.StringBuilder();
            foreach (char c in name ?? "")
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') builder.Append(c);
            }
            return builder.Length == 0 ? "Booth" : builder.ToString();
        }
    }
}
