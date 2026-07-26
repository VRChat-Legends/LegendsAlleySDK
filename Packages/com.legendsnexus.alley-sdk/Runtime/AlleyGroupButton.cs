using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.Economy;

namespace LegendsNexus.Alley
{
    // opens the community's vrchat group page for whoever presses the button.
    // everything runs local with no sync, so any number of booths can have one
    // with different group ids and they never step on each other
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Group Button")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyGroupButton : UdonSharpBehaviour
    {
        [Tooltip("Your VRChat group ID, looks like grp_12345678-1234-1234-1234-123456789abc")]
        public string groupId = "";

        // wired by the prefab so the inspector can edit the card art
        [HideInInspector] public TextMeshProUGUI nameLabel;
        [HideInInspector] public Image logoTarget;

        public override void Interact()
        {
            OpenGroup();
        }

        // public so creators can also point their own ui buttons at it
        public void OpenGroup()
        {
            if (string.IsNullOrEmpty(groupId)) return;
            Store.OpenGroupPage(groupId);
        }
    }
}
