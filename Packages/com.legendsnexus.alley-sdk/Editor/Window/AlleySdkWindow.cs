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

        private void OnEnable()
        {
            AlleySession.Changed += OnSessionChanged;
        }

        private void OnDisable()
        {
            AlleySession.Changed -= OnSessionChanged;
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

            LoadHeaderLogo();

            _loginButton.clicked += StartSignIn;
            _loginCancelButton.clicked += () => _loginCancel?.Cancel();
            _signOutButton.clicked += StartSignOut;
            _checkButton.clicked += RunCheck;
            _uploadButton.clicked += StartUpload;
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
            bool resumed = await AlleySession.Resume();
            if (this == null) return;
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
            await AlleySession.SignOut();
            _busy = false;
            if (this == null) return;
            SetStatus("Signed out.");
            RefreshUi();
        }

        private void OnSessionChanged()
        {
            if (this != null) RefreshUi();
        }

        private void RefreshUi()
        {
            bool signedIn = AlleySession.IsSignedIn;
            _loginCard.style.display = signedIn ? DisplayStyle.None : DisplayStyle.Flex;
            _communityCard.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _eventCard.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _boothCard.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _signOutButton.style.display = signedIn ? DisplayStyle.Flex : DisplayStyle.None;
            _loginCancelButton.style.display = DisplayStyle.None;
            _uploadProgress.style.display = DisplayStyle.None;

            if (!signedIn) return;

            rootVisualElement.Q<Label>("community-name").text = AlleySession.Community?.name ?? "";
            rootVisualElement.Q<Label>("community-owner").text = "Owner: " + (AlleySession.Community?.ownerUsername ?? "");
            _ = LoadCommunityLogo();

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
            _checkButton.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
            _uploadButton.style.display = any ? DisplayStyle.Flex : DisplayStyle.None;
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
            _uploadButton.text = deadlinePassed ? "Upload deadline passed"
                : _report.CanUpload ? "Build and upload"
                : "Fix the issues above first";
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
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://")) return;
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
