using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.StringLoading;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace LegendsNexus.Alley
{
    // pulls the schedule and crew list off the alley site so staff can retime a
    // day without anyone rebuilding the world. first line of the schedule file is
    // the event name
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    public class AlleySignFeed : UdonSharpBehaviour
    {
        [Header("Where to read from")]
        [Tooltip("Plain text. First line is the event name, the rest is the schedule.")]
        public VRCUrl scheduleUrl;

        [Tooltip("Plain text list of the people running the event.")]
        public VRCUrl crewUrl;

        [Header("Where it lands")]
        public TextMeshProUGUI eventNameLabel;
        public TextMeshProUGUI scheduleLabel;
        public TextMeshProUGUI crewLabel;

        [Header("Timing")]
        [Tooltip("How often to check for new wording, seconds. Keep this well above a minute.")]
        public float refreshSeconds = 300f;

        [Tooltip("How long to wait before trying again after a failed download, seconds.")]
        public float retrySeconds = 60f;

        // vrchat rate limits string downloads, so the two never go out together
        private bool _scheduleNext = true;

        private void Start()
        {
            SendCustomEventDelayedSeconds(nameof(FetchSchedule), 1f);
        }

        public void FetchSchedule()
        {
            _scheduleNext = true;
            if (!Load(scheduleUrl)) SendCustomEventDelayedSeconds(nameof(FetchCrew), 5f);
        }

        public void FetchCrew()
        {
            _scheduleNext = false;
            if (!Load(crewUrl)) QueueRefresh();
        }

        private bool Load(VRCUrl url)
        {
            if (url == null || string.IsNullOrEmpty(url.Get())) return false;
            VRCStringDownloader.LoadUrl(url, (IUdonEventReceiver)this);
            return true;
        }

        public override void OnStringLoadSuccess(IVRCStringDownload result)
        {
            string text = result.Result;
            if (_scheduleNext)
            {
                ApplySchedule(text);
                // spaced out on purpose, back to back downloads get throttled
                SendCustomEventDelayedSeconds(nameof(FetchCrew), 5f);
            }
            else
            {
                if (crewLabel != null && !string.IsNullOrEmpty(text)) crewLabel.text = text.Trim();
                QueueRefresh();
            }
        }

        public override void OnStringLoadError(IVRCStringDownload result)
        {
            Debug.LogWarning("[LegendsAlley] Sign download failed: " + result.Error);
            if (_scheduleNext) SendCustomEventDelayedSeconds(nameof(FetchCrew), 5f);
            else SendCustomEventDelayedSeconds(nameof(FetchSchedule), Mathf.Max(retrySeconds, 15f));
        }

        // first line is the event name, everything after it is the schedule
        private void ApplySchedule(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            string body = text.Replace("\r\n", "\n").Replace("\r", "\n");
            int split = body.IndexOf('\n');
            if (split < 0)
            {
                if (eventNameLabel != null) eventNameLabel.text = body.Trim().ToUpper();
                return;
            }
            string name = body.Substring(0, split).Trim();
            string rest = body.Substring(split + 1).Trim();
            if (eventNameLabel != null && name.Length > 0) eventNameLabel.text = name.ToUpper();
            if (scheduleLabel != null && rest.Length > 0) scheduleLabel.text = rest;
        }

        private void QueueRefresh()
        {
            SendCustomEventDelayedSeconds(nameof(FetchSchedule), Mathf.Max(refreshSeconds, 60f));
        }
    }
}
