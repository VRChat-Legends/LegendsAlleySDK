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
        // the avatar picture only renders ingame, so paint a filled stand in
        // where it will be. faint always, loud with a cross when selected
        private void OnDrawGizmos()
        {
            if (displayAnchor == null) return;
            Gizmos.matrix = displayAnchor.localToWorldMatrix;
            Vector3 center = new Vector3(0f, 1.35f, 0f);
            Vector3 size = new Vector3(1.68f, 1.68f, 0.02f);
            Gizmos.color = new Color(0.61f, 0.48f, 0.83f, 0.22f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(0.61f, 0.48f, 0.83f, 1f);
            Gizmos.DrawWireCube(center, size);
        }

        private void OnDrawGizmosSelected()
        {
            if (displayAnchor == null) return;
            Gizmos.matrix = displayAnchor.localToWorldMatrix;
            Vector3 center = new Vector3(0f, 1.35f, 0f);
            const float e = 0.84f;
            Gizmos.color = new Color(1f, 0f, 0.48f, 0.9f);
            Gizmos.DrawLine(center + new Vector3(-e, -e, 0f), center + new Vector3(e, e, 0f));
            Gizmos.DrawLine(center + new Vector3(-e, e, 0f), center + new Vector3(e, -e, 0f));

            var style = new GUIStyle();
            style.normal.textColor = new Color(0.75f, 0.62f, 0.95f);
            style.fontStyle = FontStyle.Bold;
            UnityEditor.Handles.Label(displayAnchor.TransformPoint(center + new Vector3(0f, e + 0.12f, 0f)), "AVATAR PICTURE", style);
        }
#endif
    }
}
