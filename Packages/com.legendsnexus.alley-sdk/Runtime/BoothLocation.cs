using UnityEngine;

namespace LegendsNexus.Alley
{
    // staff drop these on the event map where booths should land.
    // the plot's forward axis is the booth's front
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Booth Location")]
    public class BoothLocation : MonoBehaviour
    {
        [Tooltip("Plot label used for sorting and reports, e.g. A-01.")]
        public string plotName = "";

        [Tooltip("Pin a specific community to this plot by its slug. Leave empty for automatic assignment.")]
        public string reservedFor = "";

        [Tooltip("Locked plots are skipped by the importer.")]
        public bool locked = false;

        [Header("Placed booth (managed by the importer)")]
        public string placedCommunityId = "";
        public string placedCommunityName = "";
        public string placedGroupId = "";
        public int placedVersion;
        public string placedSha256 = "";

        public string PlotLabel => string.IsNullOrEmpty(plotName) ? gameObject.name : plotName;
        public bool HasBooth => !string.IsNullOrEmpty(placedCommunityId) && transform.childCount > 0;

        public void ClearPlacement()
        {
            placedCommunityId = "";
            placedCommunityName = "";
            placedGroupId = "";
            placedVersion = 0;
            placedSha256 = "";
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Vector3 limit = LegendsBooth.BoundsLimit;
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

            Gizmos.color = locked ? new Color(1f, 0.84f, 0f, 0.5f)
                : HasBooth ? new Color(0.12f, 0.82f, 0.93f, 0.5f)
                : new Color(0.42f, 0.27f, 0.76f, 0.6f);
            Gizmos.DrawWireCube(new Vector3(0f, limit.y * 0.5f, 0f), limit);

            // front arrow
            Gizmos.color = new Color(1f, 0f, 0.48f, 0.9f);
            Vector3 tip = new Vector3(0f, 0f, limit.z * 0.5f);
            Gizmos.DrawLine(Vector3.zero, tip);
            Gizmos.DrawLine(tip, tip + new Vector3(0.25f, 0f, -0.4f));
            Gizmos.DrawLine(tip, tip + new Vector3(-0.25f, 0f, -0.4f));

            Gizmos.matrix = previous;
        }
#endif
    }
}
