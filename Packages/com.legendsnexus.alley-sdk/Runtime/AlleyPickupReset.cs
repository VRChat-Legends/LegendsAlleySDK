using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace LegendsNexus.Alley
{
    // sends pickups home. the respawn is networked, the button just gates who can
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Pickup Reset")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyPickupReset : UdonSharpBehaviour
    {
        [Tooltip("Objects to send home. Each one needs a VRC Object Sync component.")]
        public VRCObjectSync[] pickups;

        [Tooltip("Leave empty to let anyone reset. Otherwise only these exact VRChat usernames can.")]
        public string[] allowedUsers;

        [Tooltip("Optional label that shows when someone who is not on the list presses it.")]
        public GameObject deniedIndicator;

        [Tooltip("How long that label stays up, seconds.")]
        [Range(1f, 10f)] public float deniedSeconds = 2f;

        private void Start()
        {
            if (deniedIndicator != null) deniedIndicator.SetActive(false);
        }

        public override void Interact()
        {
            ResetPickups();
        }

        public void ResetPickups()
        {
            if (!CanReset())
            {
                if (deniedIndicator == null) return;
                deniedIndicator.SetActive(true);
                SendCustomEventDelayedSeconds(nameof(ClearDenied), deniedSeconds);
                return;
            }

            if (pickups == null) return;
            VRCPlayerApi local = Networking.LocalPlayer;
            for (int i = 0; i < pickups.Length; i++)
            {
                VRCObjectSync sync = pickups[i];
                if (sync == null) continue;
                // respawn only works for the owner, so take it first
                if (local != null && !Networking.IsOwner(local, sync.gameObject))
                {
                    Networking.SetOwner(local, sync.gameObject);
                }
                sync.Respawn();
            }
        }

        public void ClearDenied()
        {
            if (deniedIndicator != null) deniedIndicator.SetActive(false);
        }

        private bool CanReset()
        {
            if (allowedUsers == null || allowedUsers.Length == 0) return true;
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local == null) return true;

            string name = local.displayName;
            for (int i = 0; i < allowedUsers.Length; i++)
            {
                if (allowedUsers[i] == name) return true;
            }
            return false;
        }
    }
}
