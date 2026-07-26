using UdonSharp;
using UnityEngine;

namespace LegendsNexus.Alley
{
    // press to run an animation, local so one person cant yank it for everyone
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Animation Button")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyAnimationButton : UdonSharpBehaviour
    {
        [Tooltip("The Animator holding your clip.")]
        public Animator target;

        [Tooltip("Name of the trigger parameter on the Animator, or leave blank to use the state name below.")]
        public string triggerName = "";

        [Tooltip("Used when no trigger is set. Name of the state to play, like Base Layer.Open.")]
        public string stateName = "";

        [Tooltip("Wait this long before it can be pressed again, seconds.")]
        [Range(0f, 30f)] public float cooldownSeconds = 1f;

        [Tooltip("Optional label that shows while the button is on cooldown.")]
        public GameObject busyIndicator;

        private float _readyAt;

        private void Start()
        {
            if (busyIndicator != null) busyIndicator.SetActive(false);
        }

        public override void Interact()
        {
            Play();
        }

        // public so creators can point their own ui buttons at it too
        public void Play()
        {
            if (target == null || Time.time < _readyAt) return;

            if (!string.IsNullOrEmpty(triggerName)) target.SetTrigger(triggerName);
            else if (!string.IsNullOrEmpty(stateName)) target.Play(stateName, -1, 0f);
            else return;

            if (cooldownSeconds <= 0f) return;
            _readyAt = Time.time + cooldownSeconds;
            if (busyIndicator == null) return;
            busyIndicator.SetActive(true);
            SendCustomEventDelayedSeconds(nameof(ClearBusy), cooldownSeconds);
        }

        public void ClearBusy()
        {
            if (busyIndicator != null) busyIndicator.SetActive(false);
        }
    }
}
