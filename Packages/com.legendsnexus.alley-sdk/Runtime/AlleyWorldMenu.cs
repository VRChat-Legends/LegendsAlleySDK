using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace LegendsNexus.Alley
{
    // the in world event menu. everything is local: each visitor opens their own
    // copy, the slider only moves their own music volume, nothing syncs.
    // vr opens it by holding the right stick up for a beat (a radial fills so it
    // never fires by accident), desktop just taps M. it closes on another flick,
    // the M key, or by walking away from where it popped in
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley World Menu")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyWorldMenu : UdonSharpBehaviour
    {
        [Tooltip("How long the stick has to be held up before the menu opens")]
        [Range(0.5f, 3f)] public float holdSeconds = 1.5f;

        [Tooltip("Walking this far from the open menu closes it")]
        [Range(2f, 6f)] public float walkAwayMeters = 3f;

        [Header("Wired up by the builder")]
        public GameObject panel;
        public Transform panelScaler;
        public CanvasGroup panelGroup;
        public GameObject radial;
        public Image radialFill;
        public AudioSource sfxSource;
        public AudioClip openClip;
        public AudioClip closeClip;
        public AudioClip clickClip;
        public AudioSource musicSource;
        public Slider musicSlider;
        public TextMeshProUGUI musicPercent;

        [Header("Tabs, wired up by the builder")]
        public GameObject[] tabPanels;
        public Image[] tabPills;
        public TextMeshProUGUI[] tabLabels;
        public Color[] tabAccents;

        private bool _open;
        private bool _stickWasUp;
        private float _holdStart = -1f;
        private float _animStart = -1f;
        private bool _animIn;

        void Start()
        {
            if (panel != null) panel.SetActive(false);
            if (radial != null) radial.SetActive(false);
            if (musicSource != null && musicSlider != null) musicSource.volume = musicSlider.value;
            RefreshPercent();
            SelectTab(0);
        }

        void Update()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;

            bool wantsToggle = false;
            if (local.IsUserInVR()) wantsToggle = VrHold(local);
            else if (Input.GetKeyDown(KeyCode.M)) wantsToggle = true;

            if (wantsToggle)
            {
                if (_open) Close();
                else Open(local);
            }

            Animate();

            if (_open && _animStart < 0f)
            {
                float away = Vector3.Distance(local.GetPosition(), panel.transform.position);
                if (away > walkAwayMeters) Close();
            }
        }

        // hold the stick up to open, radial fills while holding. when the menu is
        // already open a single upward flick is enough to close it
        private bool VrHold(VRCPlayerApi local)
        {
            float stick = Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryThumbstickVertical");
            bool up = stick > 0.7f;

            if (_open)
            {
                bool flicked = up && !_stickWasUp;
                _stickWasUp = up;
                return flicked;
            }

            if (up)
            {
                if (_holdStart < 0f)
                {
                    _holdStart = Time.time;
                    if (radial != null) radial.SetActive(true);
                }
                float progress = Mathf.Clamp01((Time.time - _holdStart) / holdSeconds);
                if (radialFill != null) radialFill.fillAmount = progress;
                PlaceRadial(local);
                if (progress >= 1f)
                {
                    _holdStart = -1f;
                    if (radial != null) radial.SetActive(false);
                    _stickWasUp = true;
                    return true;
                }
            }
            else
            {
                _holdStart = -1f;
                if (radial != null) radial.SetActive(false);
            }
            _stickWasUp = up;
            return false;
        }

        // the radial hangs just below eye line while the hold charges
        private void PlaceRadial(VRCPlayerApi local)
        {
            if (radial == null) return;
            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 forward = head.rotation * Vector3.forward;
            radial.transform.position = head.position + forward * 0.9f + head.rotation * new Vector3(0f, -0.18f, 0f);
            radial.transform.rotation = Quaternion.LookRotation(-forward);
        }

        private void Open(VRCPlayerApi local)
        {
            VRCPlayerApi.TrackingData head = local.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 flat = head.rotation * Vector3.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.001f) flat = Vector3.forward;
            flat = flat.normalized;

            // only the panel moves to the player. the root stays put because it
            // also carries the directory teleport anchors and the music player
            panel.transform.position = head.position + flat * 1.15f + new Vector3(0f, -0.12f, 0f);
            // ui canvases read from their +z side, so aim +z back at the player
            panel.transform.rotation = Quaternion.LookRotation(-flat);

            _open = true;
            _animIn = true;
            _animStart = Time.time;
            if (panel != null) panel.SetActive(true);
            if (panelScaler != null) panelScaler.localScale = Vector3.zero;
            if (panelGroup != null) panelGroup.alpha = 0f;
            if (sfxSource != null && openClip != null) sfxSource.PlayOneShot(openClip);
        }

        private void Close()
        {
            if (!_open) return;
            _open = false;
            _animIn = false;
            _animStart = Time.time;
            if (sfxSource != null && closeClip != null) sfxSource.PlayOneShot(closeClip);
        }

        // scale plus fade, a small overshoot on the way in so it pops
        private void Animate()
        {
            if (_animStart < 0f || panelScaler == null) return;

            if (_animIn)
            {
                float t = Mathf.Clamp01((Time.time - _animStart) / 0.28f);
                float eased = Mathf.SmoothStep(0f, 1f, t) + 0.08f * Mathf.Sin(t * Mathf.PI);
                panelScaler.localScale = Vector3.one * eased;
                if (panelGroup != null) panelGroup.alpha = Mathf.Clamp01(t * 1.6f);
                if (t >= 1f)
                {
                    panelScaler.localScale = Vector3.one;
                    _animStart = -1f;
                }
            }
            else
            {
                float t = Mathf.Clamp01((Time.time - _animStart) / 0.16f);
                panelScaler.localScale = Vector3.one * (1f - Mathf.SmoothStep(0f, 1f, t));
                if (panelGroup != null) panelGroup.alpha = 1f - t;
                if (t >= 1f)
                {
                    if (panel != null) panel.SetActive(false);
                    _animStart = -1f;
                }
            }
        }

        // wired to the sliders OnValueChanged by the builder
        public void OnMusicVolume()
        {
            if (musicSource != null && musicSlider != null) musicSource.volume = musicSlider.value;
            RefreshPercent();
        }

        // one event per pill, udon ui events cannot carry arguments
        public void OnTab0() { ClickTab(0); }
        public void OnTab1() { ClickTab(1); }
        public void OnTab2() { ClickTab(2); }
        public void OnTab3() { ClickTab(3); }

        private void ClickTab(int index)
        {
            if (sfxSource != null && clickClip != null) sfxSource.PlayOneShot(clickClip, 0.7f);
            SelectTab(index);
        }

        // active pill lights up in its accent with ink text, the rest go dark
        private void SelectTab(int index)
        {
            if (tabPanels == null) return;
            for (int i = 0; i < tabPanels.Length; i++)
            {
                bool active = i == index;
                if (tabPanels[i] != null) tabPanels[i].SetActive(active);
                if (tabPills != null && i < tabPills.Length && tabPills[i] != null && tabAccents != null && i < tabAccents.Length)
                    tabPills[i].color = active ? tabAccents[i] : new Color(0.078f, 0.086f, 0.102f, 1f);
                if (tabLabels != null && i < tabLabels.Length && tabLabels[i] != null)
                    tabLabels[i].color = active ? new Color(0.04f, 0.04f, 0.04f, 1f) : new Color(0.84f, 0.85f, 0.87f, 1f);
            }
        }

        private void RefreshPercent()
        {
            if (musicPercent == null || musicSlider == null) return;
            musicPercent.text = Mathf.RoundToInt(musicSlider.value * 100f) + "%";
        }
    }
}
