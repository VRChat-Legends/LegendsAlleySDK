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
        private const string ImportRoot = "Assets/LegendsAlley/Booths";
        private const string ExportFolder = "Assets/LegendsAlleyExport";

        public static event Action<string> Log = delegate { };
        public static bool IsRunning { get; private set; }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
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

        public static async Task<StaffBooth[]> FetchBooths(AlleyEvent alleyEvent)
        {
            var response = await AlleyHttp.GetJson<StaffBoothsResponse>(
                $"/api/admin/booths?eventId={Uri.EscapeDataString(alleyEvent.id)}&status=active", AlleySession.Token);
            return response?.booths ?? Array.Empty<StaffBooth>();
        }

        public static async Task Sync(AlleyEvent alleyEvent)
        {
            if (IsRunning) return;
            IsRunning = true;
            try
            {
                StaffBooth[] booths = await FetchBooths(alleyEvent);
                BoothLocation[] locations = FindLocations();

                Log($"Found {booths.Length} uploaded booth(s) and {locations.Length} plot(s) in the scene.");

                List<(BoothLocation location, StaffBooth booth)> plan = BuildPlan(booths, locations);
                ReportLeftovers(booths, locations, plan);
                await QueueAndRun(plan, true);
            }
            catch (Exception)
            {
                IsRunning = false;
                throw;
            }
        }

        // drop one booth on one plot, clearing any other plot that held that community
        public static async Task PlaceSingle(StaffBooth booth, BoothLocation location)
        {
            if (IsRunning || booth == null || location == null) return;
            if (location.locked)
            {
                Log($"Plot {location.PlotLabel} is locked, unlock it first.");
                return;
            }
            IsRunning = true;
            try
            {
                foreach (BoothLocation other in FindLocations())
                {
                    if (other == location || other.placedCommunityId != booth.communityId) continue;
                    ClearPlot(other);
                    Log($"Cleared {booth.communityName} off plot {other.PlotLabel}.");
                }
                await QueueAndRun(new List<(BoothLocation, StaffBooth)> { (location, booth) }, true);
            }
            catch (Exception)
            {
                IsRunning = false;
                throw;
            }
        }

        // wipes every unlocked plot and deals the booths back out in random order
        public static async Task Randomize(AlleyEvent alleyEvent)
        {
            if (IsRunning) return;
            IsRunning = true;
            try
            {
                StaffBooth[] booths = await FetchBooths(alleyEvent);
                var open = FindLocations().Where(l => !l.locked).ToList();
                Log($"Shuffling {booths.Length} booth(s) across {open.Count} unlocked plot(s)...");

                foreach (BoothLocation location in open) ClearPlot(location);

                var plan = new List<(BoothLocation, StaffBooth)>();
                var pool = booths.ToList();
                var rng = new System.Random();

                // reservations still win their plots
                foreach (BoothLocation location in open.ToArray())
                {
                    if (string.IsNullOrEmpty(location.reservedFor)) continue;
                    StaffBooth reserved = pool.FirstOrDefault(
                        b => string.Equals(b.communitySlug, location.reservedFor.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (reserved == null) continue;
                    plan.Add((location, reserved));
                    pool.Remove(reserved);
                    open.Remove(location);
                }

                List<BoothLocation> freeSlots = open.Where(l => string.IsNullOrEmpty(l.reservedFor)).OrderBy(_ => rng.Next()).ToList();
                List<StaffBooth> shuffled = pool.OrderBy(_ => rng.Next()).ToList();
                for (int i = 0; i < shuffled.Count && i < freeSlots.Count; i++)
                {
                    plan.Add((freeSlots[i], shuffled[i]));
                }
                if (shuffled.Count > freeSlots.Count)
                {
                    Log($"{shuffled.Count - freeSlots.Count} booth(s) have no free plot after the shuffle.");
                }

                await QueueAndRun(plan, false);
            }
            catch (Exception)
            {
                IsRunning = false;
                throw;
            }
        }

        private static async Task QueueAndRun(List<(BoothLocation location, StaffBooth booth)> plan, bool skipUpToDate)
        {
            var queue = ImportQueue.Load();
            queue.items.Clear();
            queue.importedCount = 0;
            queue.updatedCount = 0;
            queue.skippedCount = 0;

            var work = new List<(BoothLocation location, StaffBooth booth)>();
            foreach ((BoothLocation location, StaffBooth booth) in plan)
            {
                if (skipUpToDate && location.placedSha256 == booth.sha256 && location.HasBooth)
                {
                    queue.skippedCount++;
                    continue;
                }
                work.Add((location, booth));
            }

            if (work.Count == 0)
            {
                Log($"Everything is up to date. Skipped {queue.skippedCount} plot(s).");
                ImportQueue.Clear();
                IsRunning = false;
                return;
            }

            Log($"Downloading {work.Count} booth package(s)...");
            string[] packagePaths = await Task.WhenAll(work.Select(w => DownloadSafe(w.booth)));

            for (int i = 0; i < work.Count; i++)
            {
                if (packagePaths[i] == null) continue;
                (BoothLocation location, StaffBooth booth) = work[i];
                bool isUpdate = location.placedCommunityId == booth.communityId && location.HasBooth;
                if (isUpdate) queue.updatedCount++;
                else queue.importedCount++;

                queue.items.Add(new ImportItem
                {
                    boothId = booth.id,
                    communityId = booth.communityId,
                    communityName = booth.communityName,
                    communitySlug = booth.communitySlug,
                    prefabName = SanitizeName(booth.prefabName),
                    sha256 = booth.sha256,
                    version = booth.version,
                    locationPath = ScenePath(location.transform),
                    packagePath = packagePaths[i],
                    shaders = booth.shaders ?? new string[0],
                    stage = "pending",
                });
            }

            ImportQueue.Save(queue);

            if (queue.items.Count == 0)
            {
                Log($"Everything is up to date. Skipped {queue.skippedCount} plot(s).");
                ImportQueue.Clear();
                IsRunning = false;
                return;
            }

            ExtractAllAndPlace();
        }

        private static async Task<string> DownloadSafe(StaffBooth booth)
        {
            try
            {
                return await DownloadAndExtract(booth);
            }
            catch (Exception e)
            {
                Log($"Download failed for {booth.communityName}: {e.Message}");
                return null;
            }
        }

        // unitypackages are gzipped tars, so instead of paying for a full
        // AssetDatabase.ImportPackage cycle per booth we unpack every package
        // straight to its final folder and refresh once for the whole batch
        private static void ExtractAllAndPlace()
        {
            ImportQueueData queue = ImportQueue.Load();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var seenGuids = new HashSet<string>();
            int unpacked = 0;

            foreach (ImportItem item in queue.items)
            {
                if (item.stage != "pending") continue;
                BoothLocation location = FindLocationByPath(item.locationPath);
                if (location != null)
                {
                    for (int i = location.transform.childCount - 1; i >= 0; i--)
                    {
                        UnityEngine.Object.DestroyImmediate(location.transform.GetChild(i).gameObject);
                    }
                }
                try
                {
                    ExtractPackage(item, seenGuids);
                    item.stage = "place";
                    unpacked++;
                }
                catch (Exception e)
                {
                    Log($"Could not unpack {item.communityName}: {e.Message}");
                    item.stage = "failed";
                }
            }

            queue.items.RemoveAll(i => i.stage == "failed");
            // save before the refresh, a script compile can reload the domain and
            // the resume hook picks placement back up from here
            ImportQueue.Save(queue);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Log($"Unpacked {unpacked} booth(s) in {watch.Elapsed.TotalSeconds:0.0}s.");
            ProcessNext();
        }

        private class PackageEntry
        {
            public byte[] Asset;
            public byte[] Meta;
            public string Pathname;
        }

        private static void ExtractPackage(ImportItem item, HashSet<string> seenGuids)
        {
            string targetFolder = ImportRoot + "/" + FolderNameFor(item);
            if (AssetDatabase.IsValidFolder(targetFolder)) AssetDatabase.DeleteAsset(targetFolder);
            const string exportPrefix = ExportFolder + "/";

            foreach (KeyValuePair<string, PackageEntry> pair in ReadUnityPackage(item.packagePath))
            {
                PackageEntry entry = pair.Value;
                if (entry.Asset == null || string.IsNullOrEmpty(entry.Pathname)) continue;
                // packages are creator supplied, keep the paths honest
                if (!entry.Pathname.StartsWith("Assets/", StringComparison.Ordinal) || entry.Pathname.Contains("..")) continue;
                // same guid from an earlier booth this batch
                if (!seenGuids.Add(pair.Key)) continue;
                // shared dependency that genuinely lives elsewhere in the project.
                // guid lookups can be stale for the folder we just wiped, so only
                // trust mappings whose file is really still on disk outside it
                string existingPath = AssetDatabase.GUIDToAssetPath(pair.Key);
                if (!string.IsNullOrEmpty(existingPath)
                    && !existingPath.StartsWith(targetFolder + "/", StringComparison.Ordinal)
                    && File.Exists(existingPath)) continue;

                string relative = entry.Pathname.StartsWith(exportPrefix, StringComparison.Ordinal)
                    ? entry.Pathname.Substring(exportPrefix.Length)
                    : "deps/" + entry.Pathname.Substring("Assets/".Length);
                string finalPath = targetFolder + "/" + relative;
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                File.WriteAllBytes(finalPath, entry.Asset);
                if (entry.Meta != null) File.WriteAllBytes(finalPath + ".meta", entry.Meta);
            }
        }

        // minimal tar.gz reader for the unitypackage layout: {guid}/asset,
        // {guid}/asset.meta and {guid}/pathname entries
        private static Dictionary<string, PackageEntry> ReadUnityPackage(string packagePath)
        {
            var entries = new Dictionary<string, PackageEntry>();
            using (FileStream file = File.OpenRead(packagePath))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                byte[] header = new byte[512];
                while (ReadExact(gzip, header, 512))
                {
                    bool empty = true;
                    for (int i = 0; i < 512 && empty; i++) empty = header[i] == 0;
                    if (empty) break;

                    string name = ReadTarString(header, 0, 100);
                    long size = ReadTarOctal(header, 124, 12);
                    byte type = header[156];

                    var data = new byte[size];
                    if (size > 0 && !ReadExact(gzip, data, (int)size)) break;
                    long padding = (512 - size % 512) % 512;
                    if (padding > 0) ReadExact(gzip, new byte[padding], (int)padding);

                    if (type != (byte)'0' && type != 0) continue;
                    if (name.StartsWith("./", StringComparison.Ordinal)) name = name.Substring(2);
                    int slash = name.IndexOf('/');
                    if (slash <= 0) continue;
                    string guid = name.Substring(0, slash);
                    string part = name.Substring(slash + 1);

                    if (!entries.TryGetValue(guid, out PackageEntry entry))
                    {
                        entry = new PackageEntry();
                        entries[guid] = entry;
                    }
                    if (part == "asset") entry.Asset = data;
                    else if (part == "asset.meta") entry.Meta = data;
                    else if (part == "pathname") entry.Pathname = System.Text.Encoding.UTF8.GetString(data).Split('\n')[0].Trim();
                }
            }
            return entries;
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0) return false;
                offset += read;
            }
            return true;
        }

        private static string ReadTarString(byte[] header, int offset, int length)
        {
            int end = offset;
            while (end < offset + length && header[end] != 0) end++;
            return System.Text.Encoding.ASCII.GetString(header, offset, end - offset);
        }

        private static long ReadTarOctal(byte[] header, int offset, int length)
        {
            string text = ReadTarString(header, offset, length).Trim(' ', '\0');
            return string.IsNullOrEmpty(text) ? 0 : Convert.ToInt64(text, 8);
        }

        private static void ClearPlot(BoothLocation location)
        {
            for (int i = location.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(location.transform.GetChild(i).gameObject);
            }
            location.ClearPlacement();
            EditorSceneManager.MarkSceneDirty(location.gameObject.scene);
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

        /* ─── queue processing, placement only, assets land during the batch unpack ─── */

        private static void ProcessNext()
        {
            ImportQueueData queue = ImportQueue.Load();
            if (queue.items.Count == 0)
            {
                Finish(queue);
                return;
            }

            ImportItem item = queue.items[0];

            // a domain reload can land between unpack and refresh, pick the batch back up
            if (item.stage == "pending")
            {
                ExtractAllAndPlace();
                return;
            }

            BoothLocation location = FindLocationByPath(item.locationPath);
            if (location == null)
            {
                Log($"Plot for {item.communityName} disappeared from the scene, skipping it.");
                Advance(queue);
                return;
            }

            PlaceCurrent(queue, item, location);
        }

        private static void PlaceCurrent(ImportQueueData queue, ImportItem item, BoothLocation location)
        {
            try
            {
                string prefabPath = ImportRoot + "/" + FolderNameFor(item) + "/" + item.prefabName + ".prefab";
                StripBoothMarkers(prefabPath);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) throw new Exception($"could not find the booth prefab at {prefabPath}");

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, location.transform);
                instance.name = item.communityName + " Booth";
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                location.placedCommunityId = item.communityId;
                location.placedCommunityName = item.communityName;
                location.placedVersion = item.version;
                location.placedSha256 = item.sha256;
                EditorSceneManager.MarkSceneDirty(location.gameObject.scene);

                Log($"Placed {item.communityName} v{item.version} on plot {location.PlotLabel}.");
                WarnAboutMissingShaders(item);
            }
            catch (Exception e)
            {
                Log($"Could not place {item.communityName}: {e.Message}");
            }
            Advance(queue);
        }

        // booths self report their shader list at upload, tell staff when this
        // project is missing some so pink booths are not a mystery
        private static void WarnAboutMissingShaders(ImportItem item)
        {
            if (item.shaders == null) return;
            var missing = new List<string>();
            foreach (string name in item.shaders)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (Shader.Find(name) == null) missing.Add(name);
            }
            if (missing.Count == 0) return;
            Log($"Heads up: {item.communityName} uses shaders this project does not have: {string.Join(", ", missing)}. Import them or their booth will render broken.");
        }

        // readable folder per community, finding a booth among 50 should not
        // mean scanning random ids
        private static string FolderNameFor(ImportItem item)
        {
            if (!string.IsNullOrEmpty(item.communitySlug)) return item.communitySlug;
            string safe = SanitizeName(item.communityName);
            return string.IsNullOrEmpty(safe) ? item.communityId : safe;
        }

        // pull the creator marker out of the prefab asset itself so placed
        // instances dont show a removed component override in the inspector
        private static void StripBoothMarkers(string prefabPath)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) return;
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                LegendsBooth[] markers = contents.GetComponentsInChildren<LegendsBooth>(true);
                if (markers.Length == 0) return;
                foreach (LegendsBooth marker in markers)
                {
                    UnityEngine.Object.DestroyImmediate(marker);
                }
                PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
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
