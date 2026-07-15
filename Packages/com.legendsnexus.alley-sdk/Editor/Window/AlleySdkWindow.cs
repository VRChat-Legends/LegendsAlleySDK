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

        private VisualElement _loginCard;
        private VisualElement _communityCard;
        private VisualElement _eventCard;
        private VisualElement _boothCard;
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
        private VisualElement _staffCard;
        private Button _staffSyncButton;
        private Label _staffPlotsSummary;
        private ScrollView _staffLog;
        private VisualElement _checkWrap;
        private VisualElement _uploadWrap;
        private VisualElement _ticker;
        private VisualElement _statusBar;
        private Button _logoButton;
        private IVisualElementScheduledItem _tickerAnim;
        private readonly HashSet<VisualElement> _shownCards = new HashSet<VisualElement>();

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

            _loginCard = rootVisualElement.Q("login-card");
            _communityCard = rootVisualElement.Q("community-card");
            _eventCard = rootVisualElement.Q("event-card");
            _boothCard = rootVisualElement.Q("booth-card");
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
            _staffCard = rootVisualElement.Q("staff-card");
            _staffSyncButton = rootVisualElement.Q<Button>("staff-sync-button");
            _staffPlotsSummary = rootVisualElement.Q<Label>("staff-plots-summary");
            _staffLog = rootVisualElement.Q<ScrollView>("staff-log");
            _checkWrap = rootVisualElement.Q("check-wrap");
            _uploadWrap = rootVisualElement.Q("upload-wrap");
            _ticker = rootVisualElement.Q("status-ticker");
            _statusBar = rootVisualElement.Q("status-bar");
            _logoButton = rootVisualElement.Q<Button>("logo-button");

            LoadHeaderLogo();

            _loginButton.clicked += StartSignIn;
            _loginCancelButton.clicked += () => _loginCancel?.Cancel();
            _signOutButton.clicked += StartSignOut;
            _checkButton.clicked += RunCheck;
            _uploadButton.clicked += StartUpload;
            _staffSyncButton.clicked += StartStaffSync;
            _logoButton.clicked += StartLogoUpload;
            _eventDropdown.RegisterValueChangedCallback(_ => OnEventPicked());
            _boothDropdown.RegisterValueChangedCallback(_ => RunCheck());

            var apiField = rootVisualElement.Q<TextField>("api-base-field");
            apiField.value = AlleyConfig.ApiBase;
            apiField.RegisterCallback<FocusOutEvent>(_ => AlleyConfig.ApiBase = apiField.value);
            rootVisualElement.Q<Label>("version-label").text = "SDK version " + AlleyConfig.SdkVersion;

            RefreshUi();
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
            int stagger = 0;
            ShowCard(_loginCard, !signedIn, ref stagger);
            ShowCard(_communityCard, hasCommunity, ref stagger);
            ShowCard(_eventCard, signedIn, ref stagger);
            ShowCard(_boothCard, hasCommunity, ref stagger);
            ShowCard(_staffCard, isStaff, ref stagger);
            _signOutButton.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _loginCancelButton.style.display = DisplayStyle.None;
            _uploadProgress.style.display = DisplayStyle.None;

            if (!signedIn) return;

            if (hasCommunity)
            {
                rootVisualElement.Q<Label>("community-name").text = AlleySession.Community?.name ?? "";
                rootVisualElement.Q<Label>("community-owner").text = "Owner: " + (AlleySession.Community?.ownerUsername ?? "");
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

        // cards drop in with a slight stagger instead of just popping into existence
        private void ShowCard(VisualElement card, bool visible, ref int stagger)
        {
            bool wasShown = _shownCards.Contains(card);
            card.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
            {
                _shownCards.Remove(card);
                return;
            }
            if (wasShown) return;
            _shownCards.Add(card);
            card.AddToClassList("alley-enter");
            int delay = 30 + stagger;
            stagger += 60;
            card.schedule.Execute(() => card.RemoveFromClassList("alley-enter")).StartingIn(delay);
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
            _staffSyncButton.SetEnabled(!BoothImporter.IsRunning && AlleySession.SelectedEvent != null && locations.Length > 0);
        }

        private async void StartStaffSync()
        {
            if (BoothImporter.IsRunning || AlleySession.SelectedEvent == null) return;
            _staffSyncButton.SetEnabled(false);
            _staffLog.Clear();
            SetTicker(true);
            try
            {
                await BoothImporter.Sync(AlleySession.SelectedEvent);
            }
            catch (AlleyApiException e)
            {
                OnImporterLog("Sync failed: " + e.Message);
            }
            catch (Exception e)
            {
                OnImporterLog("Sync failed: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                if (this != null)
                {
                    SetTicker(false);
                    RefreshStaffSummary();
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
                deadline = $"Upload deadline: {parsed.ToLocalTime():MMM d, yyyy HH:mm}";
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
