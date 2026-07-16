using UnityEngine;

namespace LegendsNexus.Alley
{
    // editor side config for the avatar pedestal prefab. the inspector writes the
    // id into the VRCAvatarPedestal underneath and the packager strips this helper
    // on export, so only whitelisted components ride into the event world
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Avatar Pedestal")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyAvatarPedestal : MonoBehaviour
    {
        [Tooltip("The avatar people switch into, looks like avtr_12345678-1234-1234-1234-123456789abc")]
        public string avatarId = "";

        public Transform displayAnchor;

#if UNITY_EDITOR
        // the avatar picture only renders ingame, show creators where it will be
        private void OnDrawGizmosSelected()
        {
            if (displayAnchor == null) return;
            Gizmos.color = new Color(0.42f, 0.27f, 0.76f, 0.9f);
            Gizmos.matrix = displayAnchor.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.35f, 0.35f, 0.02f));
        }
#endif
    }
}
