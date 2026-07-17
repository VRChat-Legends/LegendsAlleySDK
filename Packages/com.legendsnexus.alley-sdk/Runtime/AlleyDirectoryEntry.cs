using UdonSharp;
using UnityEngine;
using VRC.Economy;
using VRC.SDKBase;

namespace LegendsNexus.Alley
{
    // one of these sits on every row of the booth directory board. the row's
    // buttons call the two events below. everything is local, no sync, so any
    // number of people can use the board at once without stepping on each other
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    public class AlleyDirectoryEntry : UdonSharpBehaviour
    {
        [Tooltip("Where the teleport button drops you, set by the directory builder.")]
        public Transform teleportTarget;

        [Tooltip("VRChat group id for the join button, set by the directory builder.")]
        public string groupId = "";

        // ui events cannot start with an underscore, wired by the editor builder
        public void OnTeleport()
        {
            VRCPlayerApi player = Networking.LocalPlayer;
            if (player == null || teleportTarget == null) return;
            player.TeleportTo(teleportTarget.position, teleportTarget.rotation);
        }

        public void OnJoin()
        {
            if (string.IsNullOrEmpty(groupId)) return;
            Store.OpenGroupPage(groupId);
        }
    }
}
