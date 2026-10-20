using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDKBase;

namespace LegendsNexus.Alley.Editor
{
    // booths built on the website booth builder. they sit in the staff booth list
    // next to the uploaded packages and go through the same sync, place and
    // shuffle. the website only keeps settings and images, so placing one builds
    // a prefab variant of the world's Alley Booth v2: banners, recoloured
    // palette, item placements, add-ons, the backdrop slideshow and the cutout
    internal static class WebBooths
    {
        public const string Source = "web";
        private const string DefaultApi = "https://api.vrchatlegends.com";
        // only for testing against a local website, the live site needs nothing set
        public const string ApiOverridePref = "LegendsAlley.WebBoothsApi";
        private const string ImportRoot = "Assets/LegendsAlley/Booths";

        // the website sits behind Cloudflare, which turns away requests with no
        // User-Agent (error 1010), so this names the SDK like AlleyHttp does
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.Add("User-Agent", $"LegendsAlleySDK/{AlleyConfig.SdkVersion}");
            return client;
        }
        private static readonly Dictionary<string, JObject> Configs = new Dictionary<string, JObject>();

        public static bool IsWeb(StaffBooth booth) => booth != null && booth.source == Source;

        private static Uri ApiBase()
        {
            string raw = EditorPrefs.GetString(ApiOverridePref, "").Trim();
            if (string.IsNullOrEmpty(raw)) raw = DefaultApi;
            return new Uri(raw.TrimEnd('/') + "/");
        }

        // approved web booths as staff booths. sha256 is a version marker, so the
        // sync skips plots that already hold this version like it does packages
        public static async Task<StaffBooth[]> Fetch(string alleyToken, Action<string> log)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiBase(), "api/alley/prefab-booths/sdk"));
                request.Headers.Add("X-Alley-Token", alleyToken ?? "");
                HttpResponseMessage response = await Client.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    string error = null;
                    try { error = (string)JObject.Parse(body)["error"]; } catch { }
                    log($"Could not get the website booths ({(int)response.StatusCode}): {error ?? "no details"}");
                    return Array.Empty<StaffBooth>();
                }

                var list = new List<StaffBooth>();
                foreach (JObject config in (ParseJson(body)["booths"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    if ((int?)config["schema"] != 1 || (string)config["prefab"]?["id"] != "alley-booth-v2") continue;
                    string id = (string)config["id"];
                    string communityId = (string)config["communityId"];
                    StaffCommunity community = StaffCommunityCache.Communities.FirstOrDefault(c => c != null && c.id == communityId);
                    Configs[id] = config;
                    list.Add(new StaffBooth
                    {
                        id = id,
                        source = Source,
                        communityId = communityId,
                        communityName = (string)config["communityName"] ?? community?.name ?? id,
                        communitySlug = community?.slug ?? "",
                        groupId = (string)config["fields"]?["groupId"] ?? "",
                        description = community?.description ?? "",
                        logoUrl = community?.logoUrl ?? "",
                        version = (int?)config["version"] ?? 0,
                        status = "active",
                        sha256 = $"web:{id}:v{config["version"]}",
                        prefabName = "Alley Booth v2",
                        shaders = Array.Empty<string>(),
                        uploadedAt = (string)config["reviewedAt"] ?? (string)config["submittedAt"] ?? "",
                    });
                }
                return list.ToArray();
            }
            catch (Exception e)
            {
                log($"Could not reach the website for web booths: {e.Message}");
                return Array.Empty<StaffBooth>();
            }
        }

        // a community has one booth on the map: when it has both an uploaded
        // package and a website booth, the newer one wins
        public static StaffBooth[] Merge(StaffBooth[] packages, StaffBooth[] web)
        {
            var byCommunity = new Dictionary<string, StaffBooth>();
            var order = new List<string>();
            foreach (StaffBooth booth in packages.Concat(web))
            {
                string key = string.IsNullOrEmpty(booth.communityId) ? booth.id : booth.communityId;
                if (!byCommunity.TryGetValue(key, out StaffBooth existing))
                {
                    byCommunity[key] = booth;
                    order.Add(key);
                }
                else if (UploadedAt(booth) > UploadedAt(existing))
                {
                    byCommunity[key] = booth;
                }
            }
            return order.Select(k => byCommunity[k]).ToArray();
        }

        // dates stay the text the server sent; Newtonsoft would otherwise turn
        // them into local culture strings that no longer sort
        private static JObject ParseJson(string body)
        {
            using (var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(body)) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })
            {
                return JObject.Load(reader);
            }
        }

        private static DateTimeOffset UploadedAt(StaffBooth booth)
        {
            return DateTimeOffset.TryParse(booth.uploadedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset at) ? at : DateTimeOffset.MinValue;
        }

        public static async Task<bool> BuildSafe(StaffBooth booth, BoothLocation location, string folder, Action<string> log)
        {
            try
            {
                await Build(booth, location, folder, log);
                return true;
            }
            catch (Exception e)
            {
                log($"Could not build {booth.communityName}'s web booth: {e.Message}");
                Debug.LogException(e);
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ---------------------------------------------------------------- build

        private static async Task Build(StaffBooth staffBooth, BoothLocation location, string boothFolder, Action<string> log)
        {
            if (!Configs.TryGetValue(staffBooth.id, out JObject booth)) throw new Exception("its settings were not fetched, sync again");
            string name = staffBooth.communityName;
            string basePrefab = (string)booth["prefab"]?["unityPrefab"];
            GameObject baseAsset = !string.IsNullOrEmpty(basePrefab) && basePrefab.StartsWith("Assets/")
                ? AssetDatabase.LoadAssetAtPath<GameObject>(basePrefab)
                : null;
            if (baseAsset == null) throw new Exception($"this project has no Alley Booth v2 at {basePrefab}, import the staff world kit");
            string baseRoot = Path.GetDirectoryName(basePrefab).Replace('\\', '/');

            string folder = $"{ImportRoot}/{boothFolder}/Web";
            Directory.CreateDirectory(folder + "/Textures");
            Directory.CreateDirectory(folder + "/Materials");
            File.WriteAllText(folder + "/booth.json", booth.ToString());

            // ---- download everything, then one import pass
            EditorUtility.DisplayProgressBar("Legends Alley", $"Downloading {name}'s web booth...", 0.2f);
            var files = new Dictionary<string, string>();
            JObject images = booth["images"] as JObject ?? new JObject();
            foreach (string slot in new[] { "sign", "backdrop", "screen", "groupIcon" })
            {
                byte[] bytes = await Download((string)images[slot]?["url"]);
                if (bytes == null) continue;
                string path = $"{folder}/Textures/{slot}.png";
                File.WriteAllBytes(path, bytes);
                files[slot] = path;
            }

            var slidePaths = new List<string>();
            JObject backdrop = booth["backdrop"] as JObject;
            bool slideshow = (string)backdrop?["mode"] == "slideshow";
            if (slideshow)
            {
                int i = 0;
                foreach (JObject slide in (backdrop["slides"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    byte[] bytes = await Download((string)slide["url"]);
                    if (bytes == null) continue;
                    string path = $"{folder}/Textures/slide-{++i:00}.png";
                    File.WriteAllBytes(path, bytes);
                    slidePaths.Add(path);
                }
            }

            JObject items = booth["items"] as JObject ?? new JObject();
            JObject cutout = items["cutout"] as JObject;
            byte[] cutoutBytes = (bool?)cutout?["enabled"] == true ? await Download((string)cutout["image"]?["url"]) : null;

            EditorUtility.DisplayProgressBar("Legends Alley", $"Building {name}'s web booth...", 0.6f);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // ---- textures and materials
            CopyImport(baseRoot + "/Textures/Banner_Sign.png", files.GetValueOrDefault("sign"));
            CopyImport(baseRoot + "/Textures/Banner_Back.png", files.GetValueOrDefault("backdrop"));
            CopyImport(baseRoot + "/Textures/Banner_Screen.png", files.GetValueOrDefault("screen"));
            files.TryGetValue("groupIcon", out string iconPath);
            if (iconPath != null) ImportSprite(iconPath);

            var swap = new Dictionary<Material, Material>();
            if (files.TryGetValue("sign", out string signPath)) Swap(swap, baseRoot, "Banner_Sign", CopyMaterial(baseRoot, folder, "Banner_Sign", signPath));
            if (files.TryGetValue("backdrop", out string backPath)) Swap(swap, baseRoot, "Banner_Back", CopyMaterial(baseRoot, folder, "Banner_Back", backPath));
            Material palette = BuildPalette(baseRoot, folder, booth["colors"] as JObject);
            if (palette != null) Swap(swap, baseRoot, "Booth_Palette", palette);

            Material slidesMaterial = null;
            int columns = 1, rows = 1;
            if (slideshow && slidePaths.Count > 0) slidesMaterial = BakeSlides(baseRoot, folder, slidePaths, out columns, out rows);

            // ---- the variant: an instance of the base prefab, edited, saved back as a variant
            for (int c = location.transform.childCount - 1; c >= 0; c--)
            {
                UnityEngine.Object.DestroyImmediate(location.transform.GetChild(c).gameObject);
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseAsset, location.transform);
            instance.name = name + " Booth";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;

            try
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = renderer.sharedMaterials;
                    bool changed = false;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (mats[m] != null && swap.TryGetValue(mats[m], out Material replacement))
                        {
                            mats[m] = replacement;
                            changed = true;
                        }
                    }
                    if (changed) renderer.sharedMaterials = mats;
                }

                JObject fields = booth["fields"] as JObject ?? new JObject();
                // the creator marker is for building a booth, not placing one: uploaded
                // packages have it stripped, and left on it lists the booth under the
                // signed in community in the SDK's Your Booth picker
                LegendsBooth marker = instance.GetComponent<LegendsBooth>();
                if (marker != null) UnityEngine.Object.DestroyImmediate(marker);

                ApplyVideo(instance, items, fields, files.GetValueOrDefault("screen"));
                ApplyGroup(instance, items, fields, iconPath, (string)fields["displayName"] ?? name);
                ApplyBackdrop(instance, slidesMaterial, columns, rows, slidePaths.Count, (float?)backdrop?["secondsPerSlide"] ?? 6f);
                PlaceAddon(instance, items["portal"] as JObject, "Alley Portal", go => go.GetComponent<AlleyPortal>().worldId = (string)items["portal"]["worldId"] ?? "");
                PlaceAddon(instance, items["avatar"] as JObject, "Alley Avatar Pedestal", go => go.GetComponent<AlleyAvatarPedestal>().avatarId = (string)items["avatar"]["avatarId"] ?? "");
                if (cutoutBytes != null) BuildCutout(instance, folder, cutout, cutoutBytes);

                string prefabPath = $"{folder}/{instance.name}.prefab";
                PrefabUtility.SaveAsPrefabAssetAndConnect(instance, prefabPath, InteractionMode.AutomatedAction);
            }
            catch
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                throw;
            }

            location.placedCommunityId = staffBooth.communityId ?? "";
            location.placedCommunityName = name;
            location.placedGroupId = staffBooth.groupId ?? "";
            location.placedDescription = staffBooth.description ?? "";
            location.placedVersion = staffBooth.version;
            location.placedSha256 = staffBooth.sha256;
            EditorUtility.SetDirty(location);
            EditorSceneManager.MarkSceneDirty(location.gameObject.scene);
            AssetDatabase.SaveAssets();
            log($"Placed {name} v{staffBooth.version} (website booth) on plot {location.PlotLabel}.");
        }

        // images only come from the website itself, never another host the json names
        private static async Task<byte[]> Download(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || uri.Host != ApiBase().Host)
            {
                throw new Exception($"refused an image from another host: {url}");
            }
            return await Client.GetByteArrayAsync(uri);
        }

        // ---------------------------------------------------------------- pieces

        private static void ApplyVideo(GameObject root, JObject items, JObject fields, string screenPath)
        {
            Transform video = root.transform.Find("Alley Video Player");
            if (video == null) return;
            JObject item = items["video"] as JObject;
            Place(video, item);
            video.gameObject.SetActive((bool?)item?["enabled"] != false);

            JObject controls = items["controls"] as JObject;
            Transform bar = video.Find("Control Bar");
            if (bar != null && controls?["unityLocalPosition"] is JArray local && local.Count == 3)
            {
                bar.localPosition = new Vector3((float)local[0], (float)local[1], (float)local[2]);
                bar.localEulerAngles = new Vector3((float?)controls["tiltX"] ?? 25f, 180f + ((float?)controls["rotationY"] ?? 0f), 0f);
            }

            AlleyVideoPlayer player = video.GetComponent<AlleyVideoPlayer>();
            if (player == null) return;
            string url = (string)fields["videoUrl"];
            player.videoUrl = string.IsNullOrEmpty(url) ? VRCUrl.Empty : new VRCUrl(url);
            if (screenPath != null) player.idleImage = AssetDatabase.LoadAssetAtPath<Texture2D>(screenPath);
            UdonSharpEditorUtility.CopyProxyToUdon(player);
        }

        private static void ApplyGroup(GameObject root, JObject items, JObject fields, string iconPath, string displayName)
        {
            Transform group = root.transform.Find("Alley Group Button");
            if (group == null) return;
            JObject item = items["group"] as JObject;
            Place(group, item);
            group.gameObject.SetActive((bool?)item?["enabled"] != false);
            AlleyGroupButton button = group.GetComponent<AlleyGroupButton>();
            if (button == null) return;
            button.groupId = (string)fields["groupId"] ?? "";
            if (button.nameLabel != null)
            {
                button.nameLabel.text = displayName;
                EditorUtility.SetDirty(button.nameLabel);
            }
            if (iconPath != null && button.logoTarget != null)
            {
                button.logoTarget.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
                button.logoTarget.enabled = button.logoTarget.sprite != null;
                EditorUtility.SetDirty(button.logoTarget);
            }
            UdonSharpEditorUtility.CopyProxyToUdon(button);
        }

        private static void ApplyBackdrop(GameObject root, Material atlas, int columns, int rows, int count, float seconds)
        {
            Transform showTransform = root.transform.Find("Model/Backdrop Slideshow");
            if (showTransform == null) return;
            if (atlas == null)
            {
                showTransform.gameObject.SetActive(false);
                return;
            }
            showTransform.gameObject.SetActive(true);
            AlleySlideshow show = showTransform.GetComponent<AlleySlideshow>();
            if (show == null) return;
            if (show.target != null) show.target.sharedMaterial = atlas;
            show.slideCount = count;
            show.columns = columns;
            show.rows = rows;
            show.secondsPerSlide = Mathf.Clamp(seconds, 1f, 30f);
            show.autoAdvance = true;
            UdonSharpEditorUtility.CopyProxyToUdon(show);
        }

        private static void PlaceAddon(GameObject root, JObject item, string objectName, Action<GameObject> setup)
        {
            if ((bool?)item?["enabled"] != true) return;
            string path = (string)item["sdkPrefab"];
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Packages/com.legendsnexus.alley-sdk/")) return;
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            go.name = objectName;
            Place(go.transform, item);
            go.transform.localScale = Vector3.one * ((float?)item["scale"] ?? 1f);
            setup(go);
            foreach (UdonSharpBehaviour behaviour in go.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            }
        }

        private static void Place(Transform t, JObject item)
        {
            if (!(item?["position"] is JArray p) || p.Count != 3) return;
            t.localPosition = new Vector3((float)p[0], (float)p[1], (float)p[2]);
            t.localEulerAngles = new Vector3(0f, (float?)item["rotationY"] ?? 0f, 0f);
        }

        // the booth palette is a grid of solid cells. the website sends the cells
        // to repaint; glow cells go into the glow mask as well
        private static Material BuildPalette(string baseRoot, string folder, JObject colors)
        {
            if (colors == null || (bool?)colors["changed"] != true) return null;
            int grid = (int?)colors["grid"] ?? 4;
            Texture2D pal = ReadPng(baseRoot + "/Textures/Booth_Palette.png");
            Texture2D glow = ReadPng(baseRoot + "/Textures/Booth_Glow.png");
            foreach (JProperty slotProp in (colors["slots"] as JObject ?? new JObject()).Properties())
            {
                var slot = slotProp.Value as JObject;
                if (slot == null || !(slot["cell"] is JArray cell) || cell.Count != 2) continue;
                if (!ColorUtility.TryParseHtmlString((string)slot["color"], out Color color)) continue;
                int row = (int)cell[0], col = (int)cell[1];
                Paint(pal, row, col, grid, color);
                if ((bool?)slot["glow"] == true) Paint(glow, row, col, grid, color);
            }
            string palPath = folder + "/Textures/Booth_Palette.png";
            string glowPath = folder + "/Textures/Booth_Glow.png";
            File.WriteAllBytes(palPath, pal.EncodeToPNG());
            File.WriteAllBytes(glowPath, glow.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(pal);
            UnityEngine.Object.DestroyImmediate(glow);
            AssetDatabase.ImportAsset(palPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(glowPath, ImportAssetOptions.ForceSynchronousImport);
            CopyImport(baseRoot + "/Textures/Booth_Palette.png", palPath);
            CopyImport(baseRoot + "/Textures/Booth_Glow.png", glowPath);

            Material material = CopyMaterial(baseRoot, folder, "Booth_Palette", palPath);
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(glowPath));
            EditorUtility.SetDirty(material);
            return material;
        }

        // cells count rows from the top, unity pixels from the bottom
        private static void Paint(Texture2D tex, int row, int col, int grid, Color color)
        {
            int cell = tex.width / grid;
            int y = tex.height - (row + 1) * cell;
            tex.SetPixels32(col * cell, y, cell, cell, Enumerable.Repeat((Color32)color, cell * cell).ToArray());
            tex.Apply();
        }

        // wide cells so slides keep their resolution; the slideshow windows the
        // material onto one cell at a time, so any cell shape works
        private static Material BakeSlides(string baseRoot, string folder, List<string> slides, out int columns, out int rows)
        {
            columns = Mathf.CeilToInt(Mathf.Sqrt(slides.Count));
            rows = Mathf.CeilToInt(slides.Count / (float)columns);
            int cellW = Mathf.Min(2048, 4096 / columns);
            int cellH = Mathf.RoundToInt(cellW * 916f / 2048f);
            var atlas = new Texture2D(cellW * columns, cellH * rows, TextureFormat.RGBA32, false);
            atlas.SetPixels32(Enumerable.Repeat(new Color32(8, 8, 10, 255), atlas.width * atlas.height).ToArray());
            for (int i = 0; i < slides.Count; i++)
            {
                Texture2D slide = ReadPng(slides[i]);
                Color32[] pixels = Resample(slide, cellW, cellH);
                UnityEngine.Object.DestroyImmediate(slide);
                atlas.SetPixels32((i % columns) * cellW, (rows - i / columns - 1) * cellH, cellW, cellH, pixels);
            }
            atlas.Apply();
            string path = folder + "/Textures/Backdrop_Slides.png";
            File.WriteAllBytes(path, atlas.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(atlas);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();

            Material material = CopyMaterial(baseRoot, folder, "Backdrop_Slides", path, "Banner_Back");
            material.SetTextureScale("_MainTex", new Vector2(1f / columns, 1f / rows));
            material.SetTextureOffset("_MainTex", new Vector2(0f, 1f - 1f / rows));
            EditorUtility.SetDirty(material);
            return material;
        }

        // the cardboard standee, the same pipeline the website previews with: the
        // print on the front, cardboard on the back and edges, a folding stand
        private const string CardboardTexture = "Packages/com.legendsnexus.alley-sdk/Runtime/Textures/AlleyCardboard.png";

        private static void BuildCutout(GameObject root, string folder, JObject item, byte[] bytes)
        {
            string texPath = folder + "/Textures/cutout.png";
            File.WriteAllBytes(texPath, bytes);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texPath);
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();

            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!source.LoadImage(bytes)) throw new Exception("could not read the cutout picture");
            StandeeBuilder.Result built;
            try
            {
                built = StandeeBuilder.Build(source, new StandeeBuilder.Settings
                {
                    height = (float?)item["height"] ?? 1.6f,
                    threshold = (float?)item["threshold"] ?? 0.5f,
                    thickness = (float?)item["thickness"] ?? 0.012f,
                    stand = (bool?)item["stand"] != false,
                });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            string meshPath = folder + "/Cutout.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = built.mesh;
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                EditorUtility.CopySerialized(built.mesh, mesh);
                UnityEngine.Object.DestroyImmediate(built.mesh);
            }
            EditorUtility.SetDirty(mesh);

            Material print = CutoutMaterial(folder + "/Materials/Cutout.mat", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            Material cardboard = CutoutMaterial(folder + "/Materials/Cardboard.mat", AssetDatabase.LoadAssetAtPath<Texture2D>(CardboardTexture));

            var go = new GameObject("Cardboard Cutout");
            go.transform.SetParent(root.transform, false);
            Place(go.transform, item);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { print, cardboard };
            var collider = go.AddComponent<BoxCollider>();
            collider.center = mesh.bounds.center;
            collider.size = mesh.bounds.size;
        }

        private static Material CutoutMaterial(string path, Texture2D texture)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = Shader.Find("Standard");
            material.mainTexture = texture;
            material.SetFloat("_Glossiness", 0.08f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------------------------------------------------------------- helpers

        private static void Swap(Dictionary<Material, Material> swap, string baseRoot, string name, Material replacement)
        {
            Material original = AssetDatabase.LoadAssetAtPath<Material>($"{baseRoot}/Materials/{name}.mat");
            if (original != null) swap[original] = replacement;
        }

        private static Material CopyMaterial(string baseRoot, string folder, string name, string texturePath, string baseName = null)
        {
            string path = $"{folder}/Materials/{name}.mat";
            Material source = AssetDatabase.LoadAssetAtPath<Material>($"{baseRoot}/Materials/{baseName ?? name}.mat");
            if (source == null) throw new Exception($"Alley Booth v2 is missing its {baseName ?? name} material");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.CopyPropertiesFromMaterial(source);
            }
            material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CopyImport(string fromPath, string toPath)
        {
            if (toPath == null) return;
            var from = AssetImporter.GetAtPath(fromPath) as TextureImporter;
            var to = AssetImporter.GetAtPath(toPath) as TextureImporter;
            if (from == null || to == null) return;
            var settings = new TextureImporterSettings();
            from.ReadTextureSettings(settings);
            to.SetTextureSettings(settings);
            to.textureCompression = from.textureCompression;
            to.maxTextureSize = from.maxTextureSize;
            to.SaveAndReimport();
        }

        private static void ImportSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        // reads the file itself so the import settings do not have to be readable
        private static Texture2D ReadPng(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) throw new Exception("could not read " + path);
            return tex;
        }

        private static Color32[] Resample(Texture2D source, int width, int height)
        {
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var flat = new Texture2D(width, height, TextureFormat.RGBA32, false);
                flat.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                flat.Apply();
                Color32[] pixels = flat.GetPixels32();
                UnityEngine.Object.DestroyImmediate(flat);
                return pixels;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
