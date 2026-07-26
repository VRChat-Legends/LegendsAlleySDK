using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace LegendsNexus.Alley
{
    // hops you between two spots in the booth, always local. the booth check
    // keeps both markers inside the bounds
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Teleport Button")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyTeleportButton : UdonSharpBehaviour
    {
        [Tooltip("Where this button sends you. Must sit inside your booth.")]
        public Transform destination;

        [Tooltip("Optional second spot. Pressing again sends you back and forth.")]
        public Transform returnPoint;

        [Tooltip("Keep the direction you were already facing instead of facing the way the marker points.")]
        public bool keepPlayerRotation;

        private bool _atDestination;

        public override void Interact()
        {
            Teleport();
        }

        public void Teleport()
        {
            Transform target = _atDestination && returnPoint != null ? returnPoint : destination;
            if (target == null) return;

            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return;

            Quaternion rotation = keepPlayerRotation ? local.GetRotation() : target.rotation;
            local.TeleportTo(target.position, rotation);
            _atDestination = !_atDestination;
        }
    }
}
