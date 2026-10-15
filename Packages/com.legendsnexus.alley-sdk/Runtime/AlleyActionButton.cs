using UdonSharp;
using UnityEngine;
using VRC.Economy;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace LegendsNexus.Alley
{
    public enum AlleyButtonAction
    {
        ToggleObjects,
        ShowOnly,
        TeleportTo,
        AnimatorTrigger,
        PlaySound,
        OpenGroup,
        CallEvent,
    }

    // the booth button builder. drop it on anything with a collider (press it
    // in the world) or wire a ui button to Press, pick what it does, done.
    //   ToggleObjects    flips every object in targets on or off
    //   ShowOnly         turns targets[showIndex] on and the rest off, cycling each press
    //   TeleportTo       sends the presser to teleportTarget
    //   AnimatorTrigger  fires a trigger on the animator
    //   PlaySound        plays the clip through the audio source
    //   OpenGroup        opens the vrchat group page for groupId
    //   CallEvent        sends eventName to another udon behaviour
    // everyone sees the result when Everyone is ticked, otherwise only the presser
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [AddComponentMenu("Legends Alley/Alley Action Button")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyActionButton : UdonSharpBehaviour
    {
        public AlleyButtonAction action = AlleyButtonAction.ToggleObjects;

        [Tooltip("Run the action for everyone in the instance, not just the person pressing.")]
        public bool everyone;

        [Tooltip("Text on the hover prompt when it is pressed in the world.")]
        public string prompt = "Press";

        [Header("Toggle / show only")]
        public GameObject[] targets;

        [Header("Teleport")]
        public Transform teleportTarget;

        [Header("Animator")]
        public Animator animator;
        public string trigger = "Play";

        [Header("Sound")]
        public AudioSource audioSource;
        public AudioClip clip;

        [Header("Group")]
        [Tooltip("The grp_ id from the group's vrchat page.")]
        public string groupId = "";

        [Header("Call event")]
        public UdonSharpBehaviour eventTarget;
        public string eventName = "";

        [Header("Feel")]
        [Tooltip("Optional, bounces when pressed.")]
        public AlleyUiMotion motion;

        [Tooltip("Seconds before it can be pressed again. Keeps everyone mode from being spammed.")]
        public float cooldown = 0.5f;

        private int _showIndex;
        private float _readyAt;

        private void Start()
        {
            InteractionText = prompt;
        }

        public override void Interact()
        {
            Press();
        }

        // ui events cannot start with an underscore, ui buttons call this
        public void Press()
        {
            if (Time.time < _readyAt) return;
            _readyAt = Time.time + Mathf.Max(cooldown, 0.1f);
            if (motion != null) motion.Pulse();

            // teleport and group pages only ever make sense for the presser
            bool personal = action == AlleyButtonAction.TeleportTo || action == AlleyButtonAction.OpenGroup;
            if (everyone && !personal) SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Run));
            else Run();
        }

        public void Run()
        {
            switch (action)
            {
                case AlleyButtonAction.ToggleObjects:
                    if (targets == null) return;
                    for (int i = 0; i < targets.Length; i++)
                    {
                        if (targets[i] != null) targets[i].SetActive(!targets[i].activeSelf);
                    }
                    break;
                case AlleyButtonAction.ShowOnly:
                    if (targets == null || targets.Length == 0) return;
                    _showIndex = (_showIndex + 1) % targets.Length;
                    for (int i = 0; i < targets.Length; i++)
                    {
                        if (targets[i] != null) targets[i].SetActive(i == _showIndex);
                    }
                    break;
                case AlleyButtonAction.TeleportTo:
                    VRCPlayerApi local = Networking.LocalPlayer;
                    if (local != null && teleportTarget != null) local.TeleportTo(teleportTarget.position, teleportTarget.rotation);
                    break;
                case AlleyButtonAction.AnimatorTrigger:
                    if (animator != null && !string.IsNullOrEmpty(trigger)) animator.SetTrigger(trigger);
                    break;
                case AlleyButtonAction.PlaySound:
                    if (audioSource != null)
                    {
                        if (clip != null) audioSource.PlayOneShot(clip);
                        else audioSource.Play();
                    }
                    break;
                case AlleyButtonAction.OpenGroup:
                    if (!string.IsNullOrEmpty(groupId)) Store.OpenGroupPage(groupId);
                    break;
                case AlleyButtonAction.CallEvent:
                    if (eventTarget != null && !string.IsNullOrEmpty(eventName)) eventTarget.SendCustomEvent(eventName);
                    break;
            }
        }
    }
}
