using UnityEngine;

namespace LegendsNexus.Alley
{
    // editor side config for the portal prefab. the inspector writes the id into
    // the VRCPortalMarker underneath and the packager strips this helper on
    // export, so only whitelisted components ride into the event world
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Portal")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyPortal : MonoBehaviour
    {
        [Tooltip("The world the portal leads to, looks like wrld_12345678-1234-1234-1234-123456789abc")]
        public string worldId = "";

#if UNITY_EDITOR
        // the portal graphic only spawns ingame, so sketch in where it will stand
        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Vector3 center = new Vector3(0f, 1.15f, 0f);
            Vector3 size = new Vector3(1.1f, 2.3f, 0.3f);
            Gizmos.color = new Color(0.12f, 0.82f, 0.93f, 0.22f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = new Color(0.12f, 0.82f, 0.93f, 1f);
            Gizmos.DrawWireCube(center, size);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            // people walk in through the face the arrow points out of
            Gizmos.color = new Color(1f, 0f, 0.48f, 0.9f);
            Vector3 chest = new Vector3(0f, 1.15f, 0f);
            Gizmos.DrawLine(chest, chest + new Vector3(0f, 0f, 0.8f));
            Gizmos.DrawLine(chest + new Vector3(0f, 0f, 0.8f), chest + new Vector3(0.12f, 0f, 0.6f));
            Gizmos.DrawLine(chest + new Vector3(0f, 0f, 0.8f), chest + new Vector3(-0.12f, 0f, 0.6f));
        }
#endif
    }
}
