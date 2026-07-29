using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace LegendsNexus.Alley
{
    // the performance switch on the world menu's settings tab. the impostor
    // bake parks each booth's LODGroup on a little "Impostor LOD" child and
    // hands us direct references, because udon cannot touch LODGroup itself.
    // balanced leaves the baked group in charge, quality turns the group off
    // and shows only the real booths, performance turns the group off and runs
    // its own shorter swaps plus a hard cull for far booths. all local, every
    // visitor picks for themselves
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Performance Mode")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyPerformanceMode : UdonSharpBehaviour
    {
        [Tooltip("Performance mode swaps to the impostor past this, meters")]
        public float perfFullMeters = 12f;

        [Tooltip("Performance mode swaps to the cheap far impostor past this, meters")]
        public float perfNearMeters = 18f;

        [Tooltip("Performance mode hides booths entirely past this, meters")]
        public float perfCullMeters = 30f;

        [Header("Wired by the impostor bake")]
        public GameObject[] boothRoots;
        public GameObject[] lodControls;
        public GameObject[] nearImpostors;
        public GameObject[] farImpostors;
        public Renderer[] realRenderers;
        public int[] realStarts;
        public int[] realCounts;

        [Header("Wired by the builder")]
        public Image[] modePills;
        public TextMeshProUGUI[] modeLabels;
        public TextMeshProUGUI modeHint;
        public AudioSource sfxSource;
        public AudioClip clickClip;

        private int _mode = 1;
        private float _nextCheck;
        private int[] _tiers;

        void Start()
        {
            PaintPills();
        }

        void Update()
        {
            if (_mode != 0 || boothRoots == null) return;
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.3f;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;
            Vector3 here = local.GetPosition();

            if (_tiers == null || _tiers.Length != boothRoots.Length) ResetTiers();
            for (int i = 0; i < boothRoots.Length; i++)
            {
                GameObject booth = boothRoots[i];
                if (booth == null) continue;

                float distance = Vector3.Distance(here, booth.transform.position);
                int tier = distance < perfFullMeters ? 0 : distance < perfNearMeters ? 1 : distance < perfCullMeters ? 2 : 3;
                if (tier == _tiers[i]) continue;
                _tiers[i] = tier;

                if (tier == 3)
                {
                    booth.SetActive(false);
                    continue;
                }
                if (!booth.activeSelf) booth.SetActive(true);
                SetImpostor(nearImpostors, i, tier == 1);
                SetImpostor(farImpostors, i, tier == 2);
                SetRealRenderers(i, tier == 0);
            }
        }

        public void OnPerformance() { SetMode(0); }
        public void OnBalanced() { SetMode(1); }
        public void OnQuality() { SetMode(2); }

        private void SetMode(int mode)
        {
            if (sfxSource != null && clickClip != null) sfxSource.PlayOneShot(clickClip, 0.7f);
            if (mode == _mode) return;
            _mode = mode;
            ResetTiers();

            if (boothRoots != null)
            {
                // balanced hands control back to the baked group, the other two
                // switch the group off and drive visibility themselves
                bool groupOn = mode == 1;
                bool impostorsOn = mode == 1;
                for (int i = 0; i < boothRoots.Length; i++)
                {
                    GameObject booth = boothRoots[i];
                    if (booth != null && !booth.activeSelf) booth.SetActive(true);
                    SetImpostor(lodControls, i, groupOn);
                    SetImpostor(nearImpostors, i, impostorsOn);
                    SetImpostor(farImpostors, i, impostorsOn);
                    SetRealRenderers(i, true);
                }
            }
            PaintPills();
        }

        private void SetImpostor(GameObject[] list, int index, bool active)
        {
            if (list == null || index >= list.Length) return;
            GameObject target = list[index];
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private void SetRealRenderers(int index, bool enabled)
        {
            if (realRenderers == null || realStarts == null || realCounts == null) return;
            if (index >= realStarts.Length || index >= realCounts.Length) return;
            int start = realStarts[index];
            int end = start + realCounts[index];
            if (start < 0 || end > realRenderers.Length) return;
            for (int i = start; i < end; i++)
            {
                Renderer renderer = realRenderers[i];
                if (renderer != null && renderer.enabled != enabled) renderer.enabled = enabled;
            }
        }

        private void ResetTiers()
        {
            int count = boothRoots == null ? 0 : boothRoots.Length;
            _tiers = new int[count];
            for (int i = 0; i < count; i++) _tiers[i] = -1;
        }

        private void PaintPills()
        {
            if (modePills == null) return;
            for (int i = 0; i < modePills.Length; i++)
            {
                bool active = i == _mode;
                if (modePills[i] != null)
                    modePills[i].color = active ? new Color(0.12f, 0.82f, 0.93f, 1f) : new Color(0.078f, 0.086f, 0.102f, 1f);
                if (modeLabels != null && i < modeLabels.Length && modeLabels[i] != null)
                    modeLabels[i].color = active ? new Color(0.04f, 0.04f, 0.04f, 1f) : new Color(0.84f, 0.85f, 0.87f, 1f);
            }
            if (modeHint != null)
            {
                if (_mode == 0) modeHint.text = "Performance: booths swap to flat stand ins sooner and far plots hide completely. Best for weaker headsets and busy instances.";
                else if (_mode == 2) modeHint.text = "Quality: every booth stays fully detailed no matter how far away it is. Heaviest option, best for screenshots and beefy PCs.";
                else modeHint.text = "Balanced: booths swap to flat stand ins at a comfortable distance. The default, good on most setups.";
            }
        }
    }
}
