using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
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
        private static readonly string[] TabOrder = { "signin", "booth", "community", "staff", "settings" };
        private Button _loginButton;
        private Button _loginCancelButton;
        private Button _signOutButton;
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
        private Label _staffPlotsSummary;
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
            _staffPlotsSummary = rootVisualElement.Q<Label>("staff-plots-summary");
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
            _loginCancelButton.style.display = DisplayStyle.None;
            _uploadProgress.style.display = DisplayStyle.None;

            bool currentValid = _currentTab == "settings"
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
                _tabPages[tabId].style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (animate && oldIndex != newIndex)
            {
                VisualElement page = _tabPages[id];
                string enterClass = enterFromRight ? "alley-page-enter-right" : "alley-page-enter-left";
                page.AddToClassList(enterClass);
                page.schedule.Execute(() => page.RemoveFromClassList(enterClass)).StartingIn(20);
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
            row.AddToClassList("alley-row-enter");
            row.schedule.Execute(() => row.RemoveFromClassList("alley-row-enter")).StartingIn(30 + index * 25);
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
            _staffPlotsSummary.text = locations.Length == 0
                ? "No Booth Location plots in the open scene. Add the Booth Location component where booths should go."
                : $"{locations.Length} plot(s): {filled} filled, {locations.Length - filled} free, {locked} locked.";

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

                PopulateStaffCommunities(communities?.communities ?? Array.Empty<StaffCommunity>());
                RefreshStaffSummary();
            }
            catch (AlleyApiException e)
            {
                SetStatus(e.Message);
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

                AnimateRow(row, active);
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
                deadline = $"DUE {parsed.ToLocalTime():MMM d, HH:mm}".ToUpperInvariant();
            }
            _eventInfo.text = deadline;
        }

        private void RefreshBooths()
        {
            _booths = FindBoothsInScene();
            var names = new List<string>();
            foreach (LegendsBooth booth in _booths) names.Add(booth.BoothName);

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
                    found.AddRange(rootObject.GetComponentsInChildren<LegendsBooth>(true));
                }
            }
            return found.ToArray();
        }

        private LegendsBooth SelectedBooth =>
            _boothDropdown.index >= 0 && _boothDropdown.index < _booths.Length ? _booths[_boothDropdown.index] : null;

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

            _report = BoothAnalyzer.Analyze(booth, AlleySession.SelectedEvent.limits);

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

                var label = new Label(check.Label);
                label.AddToClassList("alley-check-label");
                var value = new Label($"{check.Value} / {check.Limit}");
                value.AddToClassList("alley-check-value");
                row.Add(label);
                row.Add(value);
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

            bool confirmed = EditorUtility.DisplayDialog(
                "Ready to upload?",
                "By uploading you confirm you have the rights to everything in this booth and that " +
                "VRChat Legends may include it in the event world and related media.\n\n" +
                "The export copy gets event safe tweaks (baked light settings, 3D audio, no reflection " +
                "probes or directional lights). Your scene objects are not changed.",
                "Upload my booth", "Not yet");
            if (!confirmed) return;

            _busy = true;
            _uploadButton.SetEnabled(false);
            _uploadProgress.style.display = DisplayStyle.Flex;
            SetTicker(true);
            string zipPath = null;
            try
            {
                SetStatus("Building the booth package...");
                _uploadProgress.value = 0;
                zipPath = BoothPackager.CreatePackage(booth, _report.Stats, AlleySession.SelectedEvent, AlleySession.Community);

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

        private void OnFocus()
        {
            if (AlleySession.IsSignedIn && _boothDropdown != null) RefreshBooths();
        }
    }
}
