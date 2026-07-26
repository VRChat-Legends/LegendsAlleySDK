using UnityEngine;

namespace LegendsNexus.Alley
{
    // marker for the booth directory kiosk in event maps. the editor side
    // builder finds boards through this and refills their exhibitor list after
    // every sync. carries no runtime logic, vrchat strips it at build time
    // the same way it does BoothLocation
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Booth Directory Board")]
    public class AlleyDirectoryBoard : MonoBehaviour
    {
        [Header("Wired by the directory builder")]
        public RectTransform listContent;
        public GameObject emptyState;
        public Transform anchorsRoot;
        public AlleyDirectoryKiosk kiosk;
        public TMPro.TMP_Text countLabel;

        [Tooltip("How far in front of each plot the teleport drops people, meters.")]
        public float teleportDistance = 4.2f;
    }
}
