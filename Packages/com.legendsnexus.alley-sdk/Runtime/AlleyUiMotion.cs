using UdonSharp;
using UnityEngine;

namespace LegendsNexus.Alley
{
    // the motion every legends alley booth panel shares. it springs in with a
    // short fade and overshoot whenever it is switched on, and any button can
    // call Pulse for a little press bounce. purely local and cheap: it only runs
    // while something is actually moving
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley UI Motion")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyUiMotion : UdonSharpBehaviour
    {
        [Tooltip("What springs in. Leave empty to animate this object.")]
        public Transform target;

        [Tooltip("Optional, faded in with the spring.")]
        public CanvasGroup group;

        [Tooltip("Seconds the entrance takes.")]
        public float enterSeconds = 0.4f;

        [Tooltip("Scale the panel starts from.")]
        public float fromScale = 0.9f;

        [Tooltip("How much a press squashes it.")]
        public float pulseAmount = 0.05f;

        private Vector3 _home = Vector3.one;
        private bool _homeSet;
        private float _enter = -1f;
        private float _pulse = -1f;

        private void OnEnable()
        {
            Transform t = target != null ? target : transform;
            if (!_homeSet)
            {
                _home = t.localScale;
                _homeSet = true;
            }
            _enter = 0f;
            Pose();
        }

        // ui events cannot start with an underscore, buttons call this by name
        public void Pulse()
        {
            _pulse = 0f;
        }

        private void Update()
        {
            if (_enter < 0f && _pulse < 0f) return;
            if (_enter >= 0f)
            {
                _enter += Time.deltaTime;
                if (_enter >= enterSeconds) _enter = -1f;
            }
            if (_pulse >= 0f)
            {
                _pulse += Time.deltaTime;
                if (_pulse >= 0.25f) _pulse = -1f;
            }
            Pose();
        }

        private void Pose()
        {
            Transform t = target != null ? target : transform;
            float scale = 1f;
            float alpha = 1f;
            if (_enter >= 0f)
            {
                float k = Mathf.Clamp01(_enter / Mathf.Max(enterSeconds, 0.01f));
                float u = k - 1f;
                float back = 1f + 2.4f * u * u * u + 1.4f * u * u;
                scale = Mathf.LerpUnclamped(fromScale, 1f, back);
                alpha = k * k * (3f - 2f * k);
            }
            if (_pulse >= 0f)
            {
                float p = Mathf.Clamp01(_pulse / 0.25f);
                scale *= 1f - Mathf.Sin(p * Mathf.PI) * pulseAmount;
            }
            t.localScale = _home * scale;
            if (group != null) group.alpha = alpha;
        }
    }
}
