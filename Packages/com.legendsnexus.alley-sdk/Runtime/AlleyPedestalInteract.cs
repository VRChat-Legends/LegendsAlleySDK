using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace LegendsNexus.Alley
{
    // the pedestal component alone isnt interactive in udon worlds, something
    // has to press it. this is the same job the AvatarPedestal udon program
    // does on vrchats own sample prefab, local only so booths never clash.
    // grabbed via GetComponent because usharp cant serialize pedestal fields
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Pedestal Interact")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyPedestalInteract : UdonSharpBehaviour
    {
        public override void Interact()
        {
            var pedestal = GetComponent<VRCAvatarPedestal>();
            if (pedestal == null) return;
            pedestal.SetAvatarUse(Networking.LocalPlayer);
        }
    }
}
