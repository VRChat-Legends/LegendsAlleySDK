using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    public class AlleySdkWindow : EditorWindow
    {
        [MenuItem("Legends Alley/SDK Window", priority = 10)]
        public static void ShowWindow()
        {
            var window = GetWindow<AlleySdkWindow>();
            window.titleContent = new GUIContent("Legends Alley SDK");
            window.minSize = new Vector2(340, 560);
        }

        private VisualElement _eventStrip;
        private Dictionary<string, Button> _tabButtons;
        private Dictionary<string, VisualElement> _tabPages;
        private string _currentTab;
        private static readonly string[] TabOrder = { "signin", "booth", "community", "staff", "tools", "settings" };
        private Button _loginButton;
        private Button _loginCancelButton;
        private Button _signOutButton;
        private Button _refreshButton;
        private ObjectField _optimizeTarget;
        private SliderInt _optimizeMaterials;
        private DropdownField _optimizeAtlasSize;
        private Toggle _optimizeTint;
        private Toggle _optimizeLightmap;
        private VisualElement _optimizeList;
        private Label _optimizeListHint;
        private Button _optimizeButton;
        private Label _optimizeSummary;
        private readonly List<(BoothOptimizer.MaterialEntry entry, Toggle toggle)> _optimizeEntries = new List<(BoothOptimizer.MaterialEntry, Toggle)>();
        private Button _checkButton;
        private Button _uploadButton;
        private DropdownField _eventDropdown;
        private DropdownField _boothDropdown;
        private Label _statusLabel;
        private Label _eventInfo;
        private Label _noBoothMessage;
        private VisualElement _checklist;
        private VisualElement _blockers;
        private ProgressBar _uploadProgress;

        private LegendsBooth[] _booths = Array.Empty<LegendsBooth>();
        private BoothReport _report;
        private CancellationTokenSource _loginCancel;
        private bool _busy;
        private Button _staffSyncButton;
        private Button _impostorsButton;
        private Label _staffPlotsSummary;
        private VisualElement _staffPlotStats;
        private VisualElement _staffLogSection;
        private ScrollView _staffLog;
        private TextField _communityDescription;
        private TextField _communityInvite;
        private Button _profileSaveButton;
        private Label _descriptionCount;
        private ScrollView _staffCommunities;
        private Label _staffCommunitiesSummary;
        private DropdownField _placeBoothDropdown;
        private DropdownField _placePlotDropdown;
        private Button _placeButton;
        private Button _randomizeButton;
        private StaffBooth[] _staffBooths = Array.Empty<StaffBooth>();
        private BoothLocation[] _placePlots = Array.Empty<BoothLocation>();
        private static readonly Dictionary<string, Texture2D> LogoCache = new Dictionary<string, Texture2D>();
        private VisualElement _confirmOverlay;
        private VisualElement _confirmSheet;
        private TaskCompletionSource<bool> _confirmTcs;
        private static readonly Dictionary<VisualElement, IVisualElementScheduledItem> RunningAnims =
            new Dictionary<VisualElement, IVisualElementScheduledItem>();
        private VisualElement _checkWrap;
        private VisualElement _uploadWrap;
        private VisualElement _ticker;
        private VisualElement _statusBar;
        private Button _logoButton;
        private IVisualElementScheduledItem _tickerAnim;

        private void OnEnable()
        {
            AlleySession.Changed += OnSessionChanged;
            BoothImporter.Log += OnImporterLog;
        }

        private void OnDisable()
        {
            AlleySession.Changed -= OnSessionChanged;
            BoothImporter.Log -= OnImporterLog;
            _loginCancel?.Cancel();
        }

        public void CreateGUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AlleyConfig.PackageRoot + "/Editor/Window/AlleyWindow.uxml");
            var styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(AlleyConfig.PackageRoot + "/Editor/Window/AlleyWindow.uss");
            tree.CloneTree(rootVisualElement);
            rootVisualElement.styleSheets.Add(styles);

            _loginButton = rootVisualElement.Q<Button>("login-button");
            _loginCancelButton = rootVisualElement.Q<Button>("login-cancel-button");
            _signOutButton = rootVisualElement.Q<Button>("signout-button");
            _refreshButton = rootVisualElement.Q<Button>("refresh-button");
            _checkButton = rootVisualElement.Q<Button>("check-button");
            _uploadButton = rootVisualElement.Q<Button>("upload-button");
            _eventDropdown = rootVisualElement.Q<DropdownField>("event-dropdown");
            _boothDropdown = rootVisualElement.Q<DropdownField>("booth-dropdown");
            _statusLabel = rootVisualElement.Q<Label>("status-label");
            _eventInfo = rootVisualElement.Q<Label>("event-info");
            _noBoothMessage = rootVisualElement.Q<Label>("no-booth-message");
            _checklist = rootVisualElement.Q("checklist");
            _blockers = rootVisualElement.Q("blockers");
            _uploadProgress = rootVisualElement.Q<ProgressBar>("upload-progress");
            _staffSyncButton = rootVisualElement.Q<Button>("staff-sync-button");
            _impostorsButton = rootVisualElement.Q<Button>("impostors-button");
            _staffPlotsSummary = rootVisualElement.Q<Label>("staff-plots-summary");
            _staffPlotStats = rootVisualElement.Q("staff-plot-stats");
            _staffLogSection = rootVisualElement.Q("staff-log-section");
            _staffLog = rootVisualElement.Q<ScrollView>("staff-log");
            _checkWrap = rootVisualElement.Q("check-wrap");
            _uploadWrap = rootVisualElement.Q("upload-wrap");
            _ticker = rootVisualElement.Q("status-ticker");
            _statusBar = rootVisualElement.Q("status-bar");
            _logoButton = rootVisualElement.Q<Button>("logo-button");
            _eventStrip = rootVisualElement.Q("event-strip");
            _communityDescription = rootVisualElement.Q<TextField>("community-description");
            _communityInvite = rootVisualElement.Q<TextField>("community-invite");
            _profileSaveButton = rootVisualElement.Q<Button>("profile-save-button");
            _descriptionCount = rootVisualElement.Q<Label>("description-count");
            _staffCommunities = rootVisualElement.Q<ScrollView>("staff-communities");
            _staffCommunitiesSummary = rootVisualElement.Q<Label>("staff-communities-summary");
            _placeBoothDropdown = rootVisualElement.Q<DropdownField>("place-booth-dropdown");
            _placePlotDropdown = rootVisualElement.Q<DropdownField>("place-plot-dropdown");
            _placeButton = rootVisualElement.Q<Button>("place-button");
            _randomizeButton = rootVisualElement.Q<Button>("randomize-button");
            _confirmOverlay = rootVisualElement.Q("confirm-overlay");
            _confirmSheet = rootVisualElement.Q("confirm-sheet");
            rootVisualElement.Q<Button>("confirm-upload-button").clicked += () => ResolveConfirm(true);
            rootVisualElement.Q<Button>("confirm-cancel-button").clicked += () => ResolveConfirm(false);
            _confirmOverlay.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.target == _confirmOverlay) ResolveConfirm(false);
            });

            _tabButtons = new Dictionary<string, Button>();
            _tabPages = new Dictionary<string, VisualElement>();
            foreach (string id in TabOrder)
            {
                _tabButtons[id] = rootVisualElement.Q<Button>("tab-" + id);
                _tabPages[id] = rootVisualElement.Q("page-" + id);
                string captured = id;
                _tabButtons[id].clicked += () =>
                {
                    if (_currentTab != captured) SelectTab(captured);
                };
            }

            LoadHeaderLogo();

            _loginButton.clicked += StartSignIn;
            _loginCancelButton.clicked += () => _loginCancel?.Cancel();
            _signOutButton.clicked += StartSignOut;
            _refreshButton.clicked += StartRefresh;
            _impostorsButton.clicked += StartMakeImpostors;
            _checkButton.clicked += RunCheck;
            _uploadButton.clicked += StartUpload;
            _staffSyncButton.clicked += StartStaffSync;
            _logoButton.clicked += StartLogoUpload;
            _profileSaveButton.clicked += StartProfileSave;
            _placeButton.clicked += StartManualPlace;
            _randomizeButton.clicked += StartRandomize;
            _communityDescription.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue != null && evt.newValue.Length > 500)
                {
                    _communityDescription.SetValueWithoutNotify(evt.newValue.Substring(0, 500));
                }
                _descriptionCount.text = $"{_communityDescription.value?.Length ?? 0} / 500";
            });
            _eventDropdown.RegisterValueChangedCallback(_ => OnEventPicked());
            _boothDropdown.RegisterValueChangedCallback(_ => RunCheck());

            var apiField = rootVisualElement.Q<TextField>("api-base-field");
            apiField.value = AlleyConfig.ApiBase;
            apiField.RegisterCallback<FocusOutEvent>(_ => AlleyConfig.ApiBase = apiField.value);
            rootVisualElement.Q<Label>("version-label").text = "SDK version " + AlleyConfig.SdkVersion;

            _optimizeTarget = rootVisualElement.Q<ObjectField>("optimize-target");
            _optimizeMaterials = rootVisualElement.Q<SliderInt>("optimize-materials");
            _optimizeAtlasSize = rootVisualElement.Q<DropdownField>("optimize-atlas-size");
            _optimizeTint = rootVisualElement.Q<Toggle>("optimize-tint");
            _optimizeLightmap = rootVisualElement.Q<Toggle>("optimize-lightmap");
            _optimizeList = rootVisualElement.Q("optimize-materials-list");
            _optimizeListHint = rootVisualElement.Q<Label>("optimize-list-hint");
            _optimizeButton = rootVisualElement.Q<Button>("optimize-button");
            _optimizeSummary = rootVisualElement.Q<Label>("optimize-summary");
            _optimizeTarget.objectType = typeof(GameObject);
            _optimizeTarget.allowSceneObjects = true;
            _optimizeAtlasSize.choices = new List<string> { "512", "1024", "2048" };
            _optimizeAtlasSize.value = "2048";
            _optimizeTarget.RegisterValueChangedCallback(_ => RebuildOptimizeList());
            _optimizeButton.clicked += StartOptimize;
            RebuildOptimizeList();

            RefreshUi();
            string savedTab = SessionState.GetString("LegendsAlley.Tab", "");
            if (_tabPages.ContainsKey(savedTab) && _tabButtons[savedTab].style.display.value != DisplayStyle.None)
            {
                SelectTab(savedTab, false);
            }
            _ = TryResume();
        }

        private async Task TryResume()
        {
            SetStatus("Checking your session...");
            SetTicker(true);
            bool resumed = await AlleySession.Resume();
            if (this == null) return;
            SetTicker(false);
            SetStatus(resumed ? "Welcome back!" : "Sign in to get started.");
            RefreshUi();
        }

        private async void StartSignIn()
        {
            if (_busy) return;
            _busy = true;
            _loginCancel = new CancellationTokenSource();
            _loginButton.SetEnabled(false);
            _loginCancelButton.style.display = DisplayStyle.Flex;
            SetStatus("Waiting for the browser sign in...");
            SetTicker(true);
            try
            {
                await AlleySession.SignIn(_loginCancel.Token);
                if (this == null) return;
                SetStatus($"Signed in as {AlleySession.Community?.name}.");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Sign in cancelled.");
            }
            catch (AlleyApiException e)
            {
                SetStatus(e.Message);
            }
            finally
            {
                _busy = false;
                _loginCancel = null;
                if (this != null)
                {
                    SetTicker(false);
                    _loginButton.SetEnabled(true);
                    _loginCancelButton.style.display = DisplayStyle.None;
                    RefreshUi();
                }
            }
        }

        private async void StartSignOut()
        {
            if (_busy) return;
            _busy = true;
            SetStatus("Signing out...");
            SetTicker(true);
            await AlleySession.SignOut();
            _busy = false;
            if (this == null) return;
            SetTicker(false);
            SetStatus("Signed out.");
            RefreshUi();
        }

        /* re-pulls the session, community, events and staff roster without a fresh sign in */
        private async void StartRefresh()
        {
            if (_busy || !AlleySession.IsSignedIn) return;
            _busy = true;
            _refreshButton.SetEnabled(false);
            SetStatus("Refreshing your info...");
            SetTicker(true);
            try
            {
                bool ok = await AlleySession.Resume();
                if (this == null) return;
                if (ok && AlleySession.IsStaff) _ = RefreshStaffData();
                SetStatus(ok ? "Everything is up to date." : "Session expired, sign in again.");
            }
            catch (Exception e)
            {
                if (this != null) SetStatus("Refresh failed: " + e.Message);
            }
            finally
            {
                _busy = false;
                if (this != null)
                {
                    SetTicker(false);
                    _refreshButton.SetEnabled(true);
                    RefreshUi();
                }
            }
        }

        private void RebuildOptimizeList()
        {
            _optimizeEntries.Clear();
            _optimizeList.Clear();
            _optimizeSummary.text = "";

            var target = _optimizeTarget.value as GameObject;
            List<BoothOptimizer.MaterialEntry> entries = BoothOptimizer.ScanMaterials(target);
            _optimizeListHint.style.display = entries.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _optimizeListHint.text = target == null
                ? "Drop your booth above to list its materials."
                : "No materials found on that object.";

            foreach (BoothOptimizer.MaterialEntry entry in entries)
            {
                string shaderName = entry.Material.shader != null ? entry.Material.shader.name : "?";
                int slash = shaderName.LastIndexOf('/');
                if (slash >= 0) shaderName = shaderName.Substring(slash + 1);
                string suffix = entry.IsOpaque ? "" : " (transparent)";
                var toggle = new Toggle($"{entry.Material.name} \u00b7 {shaderName}{suffix}")
                {
                    // transparent mats keep their own shader unless the creator opts in
                    value = entry.IsOpaque,
                };
                _optimizeList.Add(toggle);
                _optimizeEntries.Add((entry, toggle));
            }
        }

        private void StartOptimize()
        {
            if (_busy) return;
            var target = _optimizeTarget.value as GameObject;

            var settings = new BoothOptimizer.Settings
            {
                TargetMaterialCount = _optimizeMaterials.value,
                AtlasSize = int.TryParse(_optimizeAtlasSize.value, out int size) ? size : 2048,
                BakeTint = _optimizeTint.value,
                GenerateLightmapUvs = _optimizeLightmap.value,
            };
            foreach ((BoothOptimizer.MaterialEntry entry, Toggle toggle) in _optimizeEntries)
            {
                if (toggle.value) settings.AtlasMaterials.Add(entry.Material);
            }

            _busy = true;
            _optimizeButton.SetEnabled(false);
            SetTicker(true);
            SetStatus("Optimizing the booth...");
            try
            {
                BoothOptimizer.Result result = BoothOptimizer.Optimize(target, settings);
                if (result.Error != null)
                {
                    _optimizeSummary.text = result.Error;
                    SetStatus(result.Error);
                }
                else
                {
                    _optimizeSummary.text =
                        $"Done! {result.RenderersBefore} renderers / {result.MaterialsBefore} materials became " +
                        $"{result.RenderersAfter} renderer{(result.RenderersAfter == 1 ? "" : "s")} / {result.MaterialsAfter} material{(result.MaterialsAfter == 1 ? "" : "s")}. " +
                        "Your original booth is disabled next to it, delete whichever one you do not want.";
                    SetStatus("Booth optimized.");
                    RefreshBooths();
                }
            }
            finally
            {
                _busy = false;
                SetTicker(false);
                _optimizeButton.SetEnabled(true);
            }
        }

        private async void StartLogoUpload()
        {
            if (_busy || AlleySession.Community == null) return;
            string path = EditorUtility.OpenFilePanel("Pick your community logo", "", "png,jpg,jpeg,webp");
            if (string.IsNullOrEmpty(path)) return;

            byte[] bytes;
            try
            {
                bytes = System.IO.File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                SetStatus("Could not read that file: " + e.Message);
                return;
            }
            if (bytes.Length > 2 * 1024 * 1024)
            {
                SetStatus("Logo must be 2 MB or smaller.");
                return;
            }

            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            string contentType = ext == ".png" ? "image/png" : ext == ".webp" ? "image/webp" : "image/jpeg";

            _busy = true;
            _logoButton.SetEnabled(false);
            SetTicker(true);
            SetStatus("Uploading your logo...");
            try
            {
                LogoResponse response = await AlleyHttp.PutBytes<LogoResponse>("/api/communities/mine/logo", bytes, contentType, AlleySession.Token);
                if (this == null) return;
                AlleySession.SetCommunityLogoUrl(response.logoUrl);
                SetStatus("Logo updated.");
            }
            catch (AlleyApiException e)
            {
                SetStatus(e.Message);
            }
            finally
            {
                _busy = false;
                if (this != null)
                {
                    SetTicker(false);
                    _logoButton.SetEnabled(true);
                }
            }
        }

        private void OnSessionChanged()
        {
            if (this != null) RefreshUi();
        }

        private void RefreshUi()
        {
            bool signedIn = AlleySession.IsSignedIn;
            bool hasCommunity = signedIn && AlleySession.Community != null;
            bool isStaff = signedIn && AlleySession.IsStaff;

            _tabButtons["signin"].style.display = signedIn ? DisplayStyle.None : DisplayStyle.Flex;
            _tabButtons["booth"].style.display = hasCommunity ? DisplayStyle.Flex : DisplayStyle.None;
            _tabButtons["community"].style.display = hasCommunity ? DisplayStyle.Flex : DisplayStyle.None;
            _tabButtons["staff"].style.display = isStaff ? DisplayStyle.Flex : DisplayStyle.None;

            _eventStrip.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _signOutButton.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _refreshButton.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _loginCancelButton.style.display = DisplayStyle.None;
            _uploadProgress.style.display = DisplayStyle.None;

            bool currentValid = _currentTab == "settings"
                || _currentTab == "tools"
                || (_currentTab == "signin" && !signedIn)
                || (_currentTab == "booth" && hasCommunity)
                || (_currentTab == "community" && hasCommunity)
                || (_currentTab == "staff" && isStaff);
            if (!currentValid)
            {
                string fallback = !signedIn ? "signin" : hasCommunity ? "booth" : isStaff ? "staff" : "settings";
                SelectTab(fallback, false);
            }

            if (!signedIn) return;

            if (hasCommunity)
            {
                rootVisualElement.Q<Label>("community-name").text = AlleySession.Community?.name ?? "";
                rootVisualElement.Q<Label>("community-owner").text = "Owner: " + (AlleySession.Community?.ownerUsername ?? "");
                _communityDescription.SetValueWithoutNotify(AlleySession.Community?.description ?? "");
                _communityInvite.SetValueWithoutNotify(AlleySession.Community?.inviteUrl ?? "");
                _descriptionCount.text = $"{(AlleySession.Community?.description ?? "").Length} / 500";
                _ = LoadCommunityLogo();
            }

            if (isStaff) RefreshStaffSummary();

            var names = new List<string>();
            foreach (AlleyEvent alleyEvent in AlleySession.Events) names.Add(alleyEvent.name);
            _eventDropdown.choices = names;
            if (names.Count == 0)
            {
                _eventDropdown.SetEnabled(false);
                _eventInfo.text = "No events are open for uploads right now.";
            }
            else
            {
                _eventDropdown.SetEnabled(true);
                int index = Array.IndexOf(AlleySession.Events, AlleySession.SelectedEvent);
                _eventDropdown.index = Mathf.Max(0, index);
                UpdateEventInfo();
            }

            RefreshBooths();
        }

        // switches the active tab, sliding the page in from the travel direction
        private void SelectTab(string id, bool animate = true)
        {
            int oldIndex = Array.IndexOf(TabOrder, _currentTab);
            int newIndex = Array.IndexOf(TabOrder, id);
            if (newIndex < 0) return;
            bool enterFromRight = oldIndex < 0 || newIndex >= oldIndex;
            _currentTab = id;
            SessionState.SetString("LegendsAlley.Tab", id);

            foreach (string tabId in TabOrder)
            {
                bool active = tabId == id;
                _tabButtons[tabId].EnableInClassList("alley-tab-active", active);
                _tabButtons[tabId].MarkDirtyRepaint();
                _tabPages[tabId].style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (animate && oldIndex != newIndex)
            {
                // slide only, the opacity dip read as a flash on the dark theme
                Animate(_tabPages[id], new Vector2(enterFromRight ? 44f : -44f, 0f), Vector2.zero, 1f, 1f, 0.24f);
            }

            if (id == "booth" && AlleySession.IsSignedIn) RefreshBooths();
            if (id == "staff" && AlleySession.IsSignedIn)
            {
                RefreshStaffSummary();
                _ = RefreshStaffData();
            }
        }

        private static void AnimateRow(VisualElement row, int index)
        {
            Animate(row, new Vector2(-18f, 0f), Vector2.zero, 0f, 1f, 0.2f, index * 0.035f);
        }

        // schedule driven tween, editor uss transitions are too flaky for entrances
        private static void Animate(VisualElement el, Vector2 from, Vector2 to, float fromOpacity, float toOpacity,
            float duration, float delay = 0f, Action onDone = null)
        {
            if (RunningAnims.TryGetValue(el, out IVisualElementScheduledItem previous)) previous.Pause();

            el.style.translate = new Translate(from.x, from.y);
            el.style.opacity = fromOpacity;
            double startAt = EditorApplication.timeSinceStartup + delay;

            IVisualElementScheduledItem item = null;
            item = el.schedule.Execute(() =>
            {
                double now = EditorApplication.timeSinceStartup;
                if (now < startAt) return;
                float t = duration <= 0f ? 1f : Mathf.Clamp01((float)((now - startAt) / duration));
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                el.style.translate = new Translate(Mathf.Lerp(from.x, to.x, eased), Mathf.Lerp(from.y, to.y, eased));
                el.style.opacity = Mathf.Lerp(fromOpacity, toOpacity, eased);
                if (t >= 1f)
                {
                    item.Pause();
                    RunningAnims.Remove(el);
                    onDone?.Invoke();
                }
            }).Every(16);
            RunningAnims[el] = item;
        }

        private static void ClearAnimStyles(VisualElement el)
        {
            el.style.translate = StyleKeyword.Null;
            el.style.opacity = StyleKeyword.Null;
        }

        // bottom sheet confirm, replaces the native editor dialog
        private Task<bool> ShowUploadConfirm()
        {
            _confirmTcs = new TaskCompletionSource<bool>();
            _confirmOverlay.style.display = DisplayStyle.Flex;
            Animate(_confirmOverlay, Vector2.zero, Vector2.zero, 0f, 1f, 0.15f);
            Animate(_confirmSheet, new Vector2(0f, 300f), Vector2.zero, 1f, 1f, 0.26f, 0.03f, () => ClearAnimStyles(_confirmSheet));
            return _confirmTcs.Task;
        }

        private void ResolveConfirm(bool accepted)
        {
            if (_confirmTcs == null) return;
            TaskCompletionSource<bool> tcs = _confirmTcs;
            _confirmTcs = null;
            Animate(_confirmSheet, Vector2.zero, new Vector2(0f, 300f), 1f, 1f, 0.2f);
            Animate(_confirmOverlay, Vector2.zero, Vector2.zero, 1f, 0f, 0.18f, 0.05f, () =>
            {
                _confirmOverlay.style.display = DisplayStyle.None;
                ClearAnimStyles(_confirmOverlay);
                ClearAnimStyles(_confirmSheet);
            });
            tcs.TrySetResult(accepted);
        }

        // pink segment sliding along the status bar while something is going on
        private void SetTicker(bool on)
        {
            if (_ticker == null) return;
            _ticker.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on)
            {
                _tickerAnim?.Pause();
                return;
            }
            if (_tickerAnim == null)
            {
                _tickerAnim = _ticker.schedule.Execute(() =>
                {
                    float track = _statusBar.resolvedStyle.width - 30f;
                    if (track <= 0f) return;
                    _ticker.style.left = Mathf.PingPong((float)(EditorApplication.timeSinceStartup * 260.0), track);
                }).Every(16);
            }
            _tickerAnim.Resume();
        }

        private void RefreshStaffSummary()
        {
            BoothLocation[] locations = BoothImporter.FindLocations();
            int filled = 0;
            int locked = 0;
            foreach (BoothLocation location in locations)
            {
                if (location.HasBooth) filled++;
                if (location.locked) locked++;
            }
            bool hasPlots = locations.Length > 0;
            _staffPlotsSummary.style.display = hasPlots ? DisplayStyle.None : DisplayStyle.Flex;
            _staffPlotStats.style.display = hasPlots ? DisplayStyle.Flex : DisplayStyle.None;
            _staffPlotsSummary.text = hasPlots
                ? ""
                : "No Booth Location plots in the open scene. Add the Booth Location component where booths should go.";
            _staffPlotStats.Clear();
            if (hasPlots)
            {
                AddPlotStat(locations.Length, "PLOTS", "total");
                AddPlotStat(filled, "FILLED", "filled");
                AddPlotStat(locations.Length - filled, "FREE", "free");
                AddPlotStat(locked, "LOCKED", "locked");
            }

            var open = new List<BoothLocation>();
            var plotChoices = new List<string>();
            foreach (BoothLocation location in locations)
            {
                if (location.locked) continue;
                open.Add(location);
                plotChoices.Add(location.HasBooth
                    ? $"{location.PlotLabel} ({location.placedCommunityName})"
                    : $"{location.PlotLabel} (free)");
            }
            _placePlots = open.ToArray();
            _placePlotDropdown.choices = plotChoices;
            if (plotChoices.Count > 0 && (_placePlotDropdown.index < 0 || _placePlotDropdown.index >= plotChoices.Count))
            {
                _placePlotDropdown.index = 0;
            }
            _placePlotDropdown.SetEnabled(plotChoices.Count > 0);

            bool ready = !BoothImporter.IsRunning && AlleySession.SelectedEvent != null;
            _staffSyncButton.SetEnabled(ready && locations.Length > 0);
            _randomizeButton.SetEnabled(ready && open.Count > 0);
            _placeButton.SetEnabled(ready && open.Count > 0 && _staffBooths.Length > 0);
        }

        private void AddPlotStat(int value, string label, string kind)
        {
            var tile = new VisualElement();
            tile.AddToClassList("alley-stat-tile");
            tile.AddToClassList("alley-stat-" + kind);
            var valueLabel = new Label(value.ToString());
            valueLabel.AddToClassList("alley-stat-value");
            tile.Add(valueLabel);
            var nameLabel = new Label(label);
            nameLabel.AddToClassList("alley-stat-label");
            tile.Add(nameLabel);
            _staffPlotStats.Add(tile);
        }

        // pulls the roster and booth list for the staff tab
        private async Task RefreshStaffData()
        {
            if (!AlleySession.IsStaff || AlleySession.SelectedEvent == null) return;
            try
            {
                StaffBooth[] booths = await BoothImporter.FetchBooths(AlleySession.SelectedEvent);
                StaffCommunitiesResponse communities = await AlleyHttp.GetJson<StaffCommunitiesResponse>("/api/admin/communities", AlleySession.Token);
                if (this == null) return;

                _staffBooths = booths;
                var choices = new List<string>();
                foreach (StaffBooth booth in booths) choices.Add($"{booth.communityName} v{booth.version}");
                _placeBoothDropdown.choices = choices;
                if (choices.Count > 0 && (_placeBoothDropdown.index < 0 || _placeBoothDropdown.index >= choices.Count))
                {
                    _placeBoothDropdown.index = 0;
                }
                _placeBoothDropdown.SetEnabled(choices.Count > 0);

                StaffCommunityCache.Store(communities?.communities);
                PopulateStaffCommunities(StaffCommunityCache.Communities);
                RefreshStaffSummary();
            }
            catch (AlleyApiException e)
            {
                SetStatus(e.Message);
            }
            catch (Exception e)
            {
                // fire and forget callers would eat this silently otherwise
                SetStatus("Staff refresh failed: " + e.Message);
                Debug.LogException(e);
            }
        }

        private void PopulateStaffCommunities(StaffCommunity[] list)
        {
            _staffCommunities.Clear();
            int active = 0;
            foreach (StaffCommunity community in list)
            {
                if (!community.active) continue;
                var row = new VisualElement();
                row.AddToClassList("alley-community-item");

                var logo = new VisualElement();
                logo.AddToClassList("alley-community-item-logo");
                row.Add(logo);

                var text = new VisualElement();
                text.AddToClassList("alley-community-item-text");
                var nameLabel = new Label(community.name);
                nameLabel.AddToClassList("alley-community-item-name");
                text.Add(nameLabel);

                string desc = community.description ?? "";
                if (desc.Length > 90) desc = desc.Substring(0, 90) + "...";
                if (desc.Length > 0)
                {
                    var descLabel = new Label(desc);
                    descLabel.AddToClassList("alley-community-item-desc");
                    text.Add(descLabel);
                }
                row.Add(text);

                _staffCommunities.Add(row);
                _ = ApplyLogo(logo, community.logoUrl);
                active++;
            }
            _staffCommunitiesSummary.text = active == 0
                ? "No accepted communities yet."
                : $"{active} accepted communit{(active == 1 ? "y" : "ies")}.";
        }

        private static async Task ApplyLogo(VisualElement element, string url)
        {
            Texture2D texture = await FetchLogo(url);
            if (texture != null && element != null) element.style.backgroundImage = texture;
        }

        private static async Task<Texture2D> FetchLogo(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            bool localDev = url.StartsWith("http://localhost") || url.StartsWith("http://127.0.0.1");
            if (!url.StartsWith("https://") && !localDev) return null;
            if (LogoCache.TryGetValue(url, out Texture2D cached) && cached != null) return cached;
            try
            {
                using var client = new HttpClient();
                byte[] bytes = await client.GetByteArrayAsync(url);
                var texture = new Texture2D(2, 2);
                if (!texture.LoadImage(bytes)) return null;
                LogoCache[url] = texture;
                return texture;
            }
            catch
            {
                return null;
            }
        }

        private async void StartStaffSync()
        {
            if (BoothImporter.IsRunning || AlleySession.SelectedEvent == null) return;
            _staffLog.Clear();
            await RunStaffOp(() => BoothImporter.Sync(AlleySession.SelectedEvent));
        }

        // bakes the four view impostor quads for every placed booth and wires up lod groups
        private void StartMakeImpostors()
        {
            if (_busy || BoothImporter.IsRunning) return;
            _busy = true;
            _impostorsButton.SetEnabled(false);
            SetTicker(true);
            SetStatus("Baking booth impostors...");
            try
            {
                BoothImpostorBaker.Summary summary = BoothImpostorBaker.BakeAllPlacedBooths(OnImporterLog);
                if (summary.Baked == 0 && summary.Skipped == 0)
                {
                    OnImporterLog("No placed booths found. Sync booths onto plots first.");
                    SetStatus("Nothing to bake.");
                }
                else
                {
                    OnImporterLog($"Impostors done: {summary.Baked} baked, {summary.Skipped} skipped. Re-run after syncing new booth versions.");
                    SetStatus($"Impostors baked for {summary.Baked} booth{(summary.Baked == 1 ? "" : "s")}.");
                }
            }
            catch (Exception e)
            {
                SetStatus("Impostor bake failed: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                _busy = false;
                SetTicker(false);
                _impostorsButton.SetEnabled(true);
            }
        }

        private async void StartManualPlace()
        {
            if (BoothImporter.IsRunning || AlleySession.SelectedEvent == null) return;
            int boothIndex = _placeBoothDropdown.index;
            int plotIndex = _placePlotDropdown.index;
            if (boothIndex < 0 || boothIndex >= _staffBooths.Length) return;
            if (plotIndex < 0 || plotIndex >= _placePlots.Length) return;
            StaffBooth booth = _staffBooths[boothIndex];
            BoothLocation plot = _placePlots[plotIndex];
            await RunStaffOp(() => BoothImporter.PlaceSingle(booth, plot));
        }

        private async void StartRandomize()
        {
            if (BoothImporter.IsRunning || AlleySession.SelectedEvent == null) return;
            bool confirmed = EditorUtility.DisplayDialog(
                "Shuffle all booths?",
                "This clears every unlocked plot and re-places all uploaded booths in a random order. " +
                "Locked plots stay put and reserved plots keep their reservations.\n\n" +
                "Everything gets downloaded again, so it can take a while.",
                "Shuffle it", "Cancel");
            if (!confirmed) return;
            _staffLog.Clear();
            await RunStaffOp(() => BoothImporter.Randomize(AlleySession.SelectedEvent));
        }

        private async Task RunStaffOp(Func<Task> operation)
        {
            _staffSyncButton.SetEnabled(false);
            _placeButton.SetEnabled(false);
            _randomizeButton.SetEnabled(false);
            SetTicker(true);
            try
            {
                await operation();
            }
            catch (AlleyApiException e)
            {
                OnImporterLog("Failed: " + e.Message);
            }
            catch (Exception e)
            {
                OnImporterLog("Failed: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                if (this != null)
                {
                    SetTicker(false);
                    RefreshStaffSummary();
                    _ = RefreshStaffData();
                }
            }
        }

        private async void StartProfileSave()
        {
            if (_busy || AlleySession.Community == null) return;
            string description = (_communityDescription.value ?? "").Trim();
            string invite = (_communityInvite.value ?? "").Trim();
            if (description.Length > 500) description = description.Substring(0, 500);

            _busy = true;
            _profileSaveButton.SetEnabled(false);
            SetTicker(true);
            SetStatus("Saving your profile...");
            try
            {
                var body = new CommunityProfileBody { description = description, inviteUrl = invite };
                await AlleyHttp.PatchJson<OkResponse>("/api/communities/mine", body, AlleySession.Token);
                if (this == null) return;
                AlleySession.SetCommunityProfile(description, invite);
                SetStatus("Profile saved.");
            }
            catch (AlleyApiException e)
            {
                SetStatus(e.Message);
            }
            finally
            {
                _busy = false;
                if (this != null)
                {
                    SetTicker(false);
                    _profileSaveButton.SetEnabled(true);
                }
            }
        }

        private void OnImporterLog(string message)
        {
            if (this == null || _staffLog == null) return;
            _staffLogSection.style.display = DisplayStyle.Flex;
            var line = new Label(message);
            line.AddToClassList("alley-staff-log-line");
            AnimateRow(line, 0);
            _staffLog.Add(line);
            _staffLog.schedule.Execute(() => _staffLog.scrollOffset = new Vector2(0, float.MaxValue));
            RefreshStaffSummary();
        }

        private void OnEventPicked()
        {
            if (_eventDropdown.index >= 0 && _eventDropdown.index < AlleySession.Events.Length)
            {
                AlleySession.SelectedEvent = AlleySession.Events[_eventDropdown.index];
                AlleySession.ApplyBoundsToGizmos();
                UpdateEventInfo();
                RunCheck();
                if (AlleySession.IsStaff) _ = RefreshStaffData();
            }
        }

        private void UpdateEventInfo()
        {
            AlleyEvent selected = AlleySession.SelectedEvent;
            if (selected == null)
            {
                _eventInfo.text = "";
                return;
            }
            string deadline = "";
            if (DateTime.TryParse(selected.uploadDeadline, out DateTime parsed))
            {
                // local time in whatever clock format the machine is set to,
                // 12h people get am/pm and 24h people get their 24h
                DateTime local = parsed.ToLocalTime();
                deadline = $"DUE {local:MMM d}, {local.ToString("t", System.Globalization.CultureInfo.CurrentCulture)}".ToUpperInvariant();
            }
            _eventInfo.text = deadline;
        }

        private void RefreshBooths()
        {
            _booths = FindBoothsInScene();
            var names = new List<string>();
            // community name first so its obvious whose booth is selected when a
            // project has several builds lying around
            string communityName = AlleySession.Community?.name;
            foreach (LegendsBooth booth in _booths)
            {
                names.Add(string.IsNullOrEmpty(communityName)
                    ? booth.BoothName
                    : $"{communityName} ({booth.gameObject.name})");
            }

            bool any = names.Count > 0;
            _noBoothMessage.style.display = any ? DisplayStyle.None : DisplayStyle.Flex;
            _boothDropdown.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            _checkWrap.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            _uploadWrap.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            _boothDropdown.choices = names;
            if (any && (_boothDropdown.index < 0 || _boothDropdown.index >= names.Count))
            {
                _boothDropdown.index = 0;
            }

            if (any) RunCheck();
            else
            {
                _checklist.Clear();
                _blockers.Clear();
            }
        }

        private static LegendsBooth[] FindBoothsInScene()
        {
            var found = new List<LegendsBooth>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject rootObject in scene.GetRootGameObjects())
                {
                    // disabled booths are intentionally hidden (eg. swapping between two builds), skip them
                    foreach (LegendsBooth booth in rootObject.GetComponentsInChildren<LegendsBooth>(true))
                    {
                        if (booth.gameObject.activeInHierarchy) found.Add(booth);
                    }
                }
            }
            return found.ToArray();
        }

        private LegendsBooth SelectedBooth =>
            _boothDropdown.index >= 0 && _boothDropdown.index < _booths.Length ? _booths[_boothDropdown.index] : null;

        private static void SelectOffenders(CheckRow check)
        {
            var alive = new List<UnityEngine.Object>();
            foreach (UnityEngine.Object offender in check.Offenders)
            {
                if (offender != null) alive.Add(offender);
            }
            if (alive.Count == 0) return;
            // works for both scene objects (hierarchy) and assets (project window)
            Selection.objects = alive.ToArray();
            EditorGUIUtility.PingObject(alive[0]);
        }

        private void RunCheck()
        {
            LegendsBooth booth = SelectedBooth;
            _checklist.Clear();
            _blockers.Clear();
            _report = null;
            if (booth == null || AlleySession.SelectedEvent == null)
            {
                _uploadButton.SetEnabled(false);
                return;
            }

            _report = BoothAnalyzer.Analyze(booth, AlleySession.SelectedEvent.limits, AlleySession.Community?.limitsBypass ?? false);

            foreach (string blocker in _report.Blockers)
            {
                var row = new Label(blocker);
                row.AddToClassList("alley-blocker-row");
                AnimateRow(row, _blockers.childCount);
                _blockers.Add(row);
            }

            foreach (CheckRow check in _report.Rows)
            {
                var row = new VisualElement();
                row.AddToClassList("alley-check-row");
                row.AddToClassList("alley-check-" + check.Severity.ToString().ToLower());

                // number up top, what it means underneath
                var value = new Label($"{check.Value} / {check.Limit}");
                value.AddToClassList("alley-check-value");
                var label = new Label(check.Label);
                label.AddToClassList("alley-check-label");
                row.Add(value);
                row.Add(label);

                // little magnifier that selects whatever pushed the card over
                if (check.OverLimit && check.Offenders != null && check.Offenders.Length > 0)
                {
                    CheckRow captured = check;
                    var select = new Button(() => SelectOffenders(captured))
                    {
                        tooltip = "Select the objects responsible for this",
                    };
                    select.AddToClassList("alley-check-select");
                    Texture icon = EditorGUIUtility.IconContent("d_ViewToolZoom").image;
                    if (icon != null)
                    {
                        var image = new Image { image = icon };
                        image.AddToClassList("alley-check-select-icon");
                        select.Add(image);
                    }
                    else
                    {
                        select.text = "?";
                    }
                    row.Add(select);
                }

                AnimateRow(row, _checklist.childCount);
                _checklist.Add(row);

                if (!string.IsNullOrEmpty(check.Hint))
                {
                    var hint = new Label(check.Hint);
                    hint.AddToClassList("alley-check-hint");
                    _checklist.Add(hint);
                }
            }

            bool deadlinePassed = AlleySession.SelectedEvent != null
                && DateTime.TryParse(AlleySession.SelectedEvent.uploadDeadline, out DateTime deadline)
                && deadline.ToUniversalTime() < DateTime.UtcNow;

            _uploadButton.SetEnabled(_report.CanUpload && !deadlinePassed && !_busy);
            _uploadButton.text = deadlinePassed ? "UPLOAD DEADLINE PASSED"
                : _report.CanUpload ? "BUILD + UPLOAD"
                : "FIX THE ISSUES ABOVE FIRST";
        }

        private async void StartUpload()
        {
            LegendsBooth booth = SelectedBooth;
            if (_busy || booth == null || _report == null || !_report.CanUpload) return;
            if (AlleySession.SelectedEvent == null || AlleySession.Community == null) return;

            bool confirmed = await ShowUploadConfirm();
            if (!confirmed) return;

            _busy = true;
            _uploadButton.SetEnabled(false);
            _uploadProgress.style.display = DisplayStyle.Flex;
            SetTicker(true);
            string zipPath = null;
            try
            {
                int pbMeshes = ProBuilderBaker.CountMeshes(booth.gameObject);
                SetStatus(pbMeshes > 0
                    ? $"ProBuilder detected on booth, optimizations will be done. Baking {pbMeshes} meshes into one..."
                    : "Building the booth package...");
                _uploadProgress.value = 0;
                zipPath = BoothPackager.CreatePackage(booth, _report.Stats, _report.ShaderNames.ToArray(), AlleySession.SelectedEvent, AlleySession.Community);

                AcceptedBooth accepted = await BoothUploader.Upload(zipPath, AlleySession.SelectedEvent.id, (progress, message) =>
                {
                    if (this == null) return;
                    _uploadProgress.value = progress * 100f;
                    SetStatus(message);
                });
                if (this == null) return;
                SetStatus($"Booth v{accepted.version} uploaded. See you at the event!");
            }
            catch (AlleyApiException e)
            {
                string details = e.Details.Length > 0 ? "\n- " + string.Join("\n- ", e.Details) : "";
                SetStatus(e.Message + details);
            }
            catch (Exception e)
            {
                SetStatus("Something went wrong while building the booth: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                _busy = false;
                if (zipPath != null)
                {
                    try { System.IO.File.Delete(zipPath); } catch { }
                }
                if (this != null)
                {
                    SetTicker(false);
                    _uploadProgress.style.display = DisplayStyle.None;
                    RunCheck();
                }
            }
        }

        private void LoadHeaderLogo()
        {
            var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlleyConfig.PackageRoot + "/Editor/Window/alley-logo.png");
            if (logo != null)
            {
                rootVisualElement.Q("header-logo").style.backgroundImage = logo;
            }
        }

        private async Task LoadCommunityLogo()
        {
            string url = AlleySession.Community?.logoUrl;
            if (string.IsNullOrEmpty(url)) return;
            // https only, except plain http against a local dev server
            bool localDev = url.StartsWith("http://localhost") || url.StartsWith("http://127.0.0.1");
            if (!url.StartsWith("https://") && !localDev) return;
            try
            {
                using var client = new HttpClient();
                byte[] bytes = await client.GetByteArrayAsync(url);
                if (this == null) return;
                var texture = new Texture2D(2, 2);
                if (texture.LoadImage(bytes))
                {
                    rootVisualElement.Q("community-logo").style.backgroundImage = texture;
                }
            }
            catch
            {
                // missing logo is cosmetic, ignore
            }
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null) _statusLabel.text = message;
        }
    }
}
