using UnityEngine;

namespace LegendsNexus.Alley
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Legends Booth")]
    [HelpURL("https://vrchatlegends.com")]
    public class LegendsBooth : MonoBehaviour
    {
        [Tooltip("Which community this booth belongs to. Pick it from the dropdown in the inspector.")]
        public string displayName = "";

        [Tooltip("Draw the size limit box in the scene view.")]
        public bool showBounds = true;

        // the sdk window pushes the real per event limit in here, this is just the fallback
        public static Vector3 BoundsLimit = new Vector3(5f, 5f, 5f);

        public string BoothName => string.IsNullOrEmpty(displayName) ? gameObject.name : displayName;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showBounds) return;

            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            // floor footprint plus a marker for which way the booth faces
            Gizmos.color = new Color(0.42f, 0.27f, 0.76f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(0f, BoundsLimit.y * 0.5f, 0f), BoundsLimit);
            Gizmos.color = new Color(1f, 0f, 0.48f, 0.8f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, BoundsLimit.z * 0.5f));
            Gizmos.DrawWireSphere(new Vector3(0f, 0f, BoundsLimit.z * 0.5f), 0.12f);

            Gizmos.matrix = previous;
        }

        private void OnDrawGizmosSelected()
        {
            if (!showBounds) return;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            Vector3 min = renderers[0].bounds.min;
            Vector3 max = renderers[0].bounds.max;
            foreach (Renderer child in renderers)
            {
                min = Vector3.Min(min, child.bounds.min);
                max = Vector3.Max(max, child.bounds.max);
            }

            Vector3 size = max - min;
            bool tooBig = size.x > BoundsLimit.x || size.y > BoundsLimit.y || size.z > BoundsLimit.z;

            Gizmos.color = tooBig ? Color.red : new Color(0.12f, 0.82f, 0.93f, 0.9f);
            Gizmos.DrawWireCube((min + max) * 0.5f, size);
        }
#endif
    }
}
