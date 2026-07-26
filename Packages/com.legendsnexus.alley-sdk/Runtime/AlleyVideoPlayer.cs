using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components.Video;
using VRC.SDK3.Video.Components.Base;
using VRC.SDKBase;

namespace LegendsNexus.Alley
{
    // booth video player. everything runs local: the video only loads and plays
    // for people standing near this booth, so any number of booths can have one
    // without fighting over bandwidth or talking over each other. audio range is
    // hard capped so sound stays inside the booth
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Video Player")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyVideoPlayer : UdonSharpBehaviour
    {
        [Tooltip("Link to what should play, like a youtube video or a vrcdn stream")]
        public VRCUrl videoUrl;

        [Tooltip("People closer than this start the video, walking away stops it")]
        [Range(3f, 5f)] public float playbackRange = 4f;

        [Tooltip("How far the sound carries, capped at 5m so it stays inside the booth")]
        [Range(1f, 5f)] public float audioRange = 4f;

        [Tooltip("Start the video over when it ends. Streams ignore this and just reconnect")]
        public bool loop = true;

        [Header("Wired up by the prefab")]
        public BaseVRCVideoPlayer videoPlayer;
        public AudioSource audioSource;
        public Slider volumeSlider;
        public TextMeshProUGUI statusText;
        public GameObject playIcon;
        public GameObject pauseIcon;

        private bool _inRange;
        private bool _loading;
        private bool _playing;
        private bool _paused;
        private bool _ended;
        private bool _resuming;
        private float _resumeDeadline;
        private float _nextCheck;
        private float _nextAttempt;
        private float _loadStarted;

        void Start()
        {
            playbackRange = Mathf.Clamp(playbackRange, 3f, 5f);
            // the audio falloff itself is wired at edit time by the inspector and
            // clamped again at upload, runtime never touches the range (the event
            // whitelist bans range writes for every booth, ours included)
            if (audioSource != null && volumeSlider != null) audioSource.volume = volumeSlider.value;
            if (videoPlayer != null) videoPlayer.Loop = loop;
            SetStatus(HasUrl() ? "WALK UP TO PLAY" : "NO VIDEO SET");
            RefreshIcons();
        }

        void Update()
        {
            // just a distance check a few times a second, not real per frame work
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.25f;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null || videoPlayer == null) return;

            float distance = Vector3.Distance(local.GetPosition(), transform.position);
            // a little hysteresis so pacing on the range line doesnt thrash the stream
            bool nowInRange = distance <= (_inRange ? playbackRange + 1f : playbackRange);
            if (nowInRange != _inRange)
            {
                _inRange = nowInRange;
                if (!_inRange)
                {
                    StopVideo("WALK UP TO PLAY");
                    return;
                }
                // tiny random stagger so booths entering view together dont all
                // hit the clients video rate limit in the same instant
                _nextAttempt = Time.time + Random.Range(0.1f, 0.6f);
            }

            if (!_inRange) return;

            // avpro often skips OnVideoPlay on a resume, so watch IsPlaying
            // instead of waiting for a callback that may never arrive
            if (_resuming)
            {
                if (videoPlayer.IsPlaying)
                {
                    _resuming = false;
                    _paused = false;
                    _playing = true;
                    SetStatus("NOW PLAYING");
                    RefreshIcons();
                }
                else if (Time.time > _resumeDeadline)
                {
                    // resume never took, load it again from the top
                    _resuming = false;
                    _paused = false;
                    _ended = false;
                    StartVideo();
                }
                return;
            }

            // a load that never comes back counts as failed
            if (_loading && Time.time > _loadStarted + 20f)
            {
                _loading = false;
                ScheduleRetry(8f, "TAKING TOO LONG, RETRYING");
                return;
            }

            if (!_loading && !_playing && !_paused && !_ended && HasUrl() && Time.time >= _nextAttempt)
            {
                StartVideo();
            }
        }

        private void StartVideo()
        {
            _loading = true;
            _loadStarted = Time.time;
            SetStatus("LOADING");
            videoPlayer.PlayURL(videoUrl);
        }

        private void StopVideo(string status)
        {
            videoPlayer.Stop();
            _loading = false;
            _playing = false;
            _paused = false;
            _ended = false;
            _resuming = false;
            SetStatus(status);
            RefreshIcons();
        }

        private void ScheduleRetry(float baseDelay, string status)
        {
            // jitter keeps multiple players from retrying in lockstep
            _nextAttempt = Time.time + baseDelay + Random.Range(0f, 3f);
            _resuming = false;
            SetStatus(status);
            RefreshIcons();
        }

        /* ─── ui events, wired to the buttons on the prefab ─── */

        public void OnPlayPauseClick()
        {
            if (videoPlayer == null) return;
            if (!HasUrl())
            {
                SetStatus("NO VIDEO SET");
                return;
            }
            if (_playing)
            {
                _paused = true;
                _playing = false;
                _resuming = false;
                videoPlayer.Pause();
                SetStatus("PAUSED");
                RefreshIcons();
            }
            else if (_paused)
            {
                // _paused stays set until the tick sees the video actually rolling,
                // clearing it here would let the state tick call PlayURL again and
                // reload the whole video instead of resuming
                _resuming = true;
                _resumeDeadline = Time.time + 3f;
                SetStatus("RESUMING");
                RefreshIcons();
                videoPlayer.Play();
            }
            else if (!_loading)
            {
                // the ui laser reaches further than the video range, dont let
                // someone across the hall spin up a stream they cant even hear
                if (!_inRange)
                {
                    SetStatus("MOVE CLOSER TO PLAY");
                    return;
                }
                _ended = false;
                StartVideo();
            }
        }

        public void OnVolumeChanged()
        {
            if (audioSource != null && volumeSlider != null) audioSource.volume = volumeSlider.value;
        }

        /* ─── video player callbacks ─── */

        public override void OnVideoReady()
        {
            if (_loading) SetStatus("STARTING");
        }

        public override void OnVideoStart()
        {
            // stop can race a load that was already in flight when they walked off
            if (!_inRange)
            {
                StopVideo("WALK UP TO PLAY");
                return;
            }
            _loading = false;
            _playing = true;
            _paused = false;
            _resuming = false;
            SetStatus("NOW PLAYING");
            RefreshIcons();
        }

        public override void OnVideoPlay()
        {
            _loading = false;
            _playing = true;
            _paused = false;
            _resuming = false;
            SetStatus("NOW PLAYING");
            RefreshIcons();
        }

        public override void OnVideoPause()
        {
            _playing = false;
            _paused = true;
            _resuming = false;
            SetStatus("PAUSED");
            RefreshIcons();
        }

        public override void OnVideoEnd()
        {
            // a paused video cant genuinely end, some player backends emit
            // spurious end events around pause
            if (!_inRange || _paused) return;
            _playing = false;
            float duration = videoPlayer.GetDuration();
            if (duration <= 0f || float.IsInfinity(duration))
            {
                // live stream dropped, try to pick it back up
                ScheduleRetry(5f, "STREAM ENDED, RETRYING");
            }
            else if (loop)
            {
                // loop is handled by the player itself, this is just a fallback
                ScheduleRetry(1f, "REPLAYING");
            }
            else
            {
                _ended = true;
                SetStatus("ENDED");
                RefreshIcons();
            }
        }

        public override void OnVideoError(VideoError videoError)
        {
            _loading = false;
            _playing = false;
            _paused = false;
            if (videoError == VideoError.RateLimited)
            {
                // another player just loaded something, wait our turn
                ScheduleRetry(5.5f, "BUSY, RETRYING");
            }
            else if (videoError == VideoError.InvalidURL)
            {
                ScheduleRetry(30f, "LINK LOOKS BROKEN");
            }
            else if (videoError == VideoError.AccessDenied)
            {
                ScheduleRetry(15f, "ALLOW UNTRUSTED URLS");
            }
            else
            {
                ScheduleRetry(12f, "VIDEO FAILED, RETRYING");
            }
        }

        /* ─── helpers ─── */

        private bool HasUrl()
        {
            return videoUrl != null && videoUrl.Get() != "";
        }

        private void SetStatus(string text)
        {
            if (statusText != null) statusText.text = text;
        }

        private void RefreshIcons()
        {
            // flip the icon the moment someone hits resume, waiting for the video
            // to actually roll makes the button feel dead
            bool showPause = _playing || _resuming;
            if (playIcon != null) playIcon.SetActive(!showPause);
            if (pauseIcon != null) pauseIcon.SetActive(showPause);
        }
    }
}
