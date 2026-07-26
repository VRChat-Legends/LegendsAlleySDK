using UdonSharp;
using UnityEngine;

namespace LegendsNexus.Alley
{
    // one of these sits on every row of the legend list. clicking the row
    // hands its index to the kiosk, which fills the detail panel next to it.
    // everything is local, no sync, so any number of people can browse at once
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    public class AlleyDirectoryEntry : UdonSharpBehaviour
    {
        [Tooltip("The board this row belongs to, set by the directory builder.")]
        public AlleyDirectoryKiosk kiosk;

        [Tooltip("Which booth this row stands for, set by the directory builder.")]
        public int index;

        // ui events cannot start with an underscore, wired by the editor builder
        public void OnSelect()
        {
            if (kiosk == null) return;
            kiosk.Show(index);
        }
    }
}
