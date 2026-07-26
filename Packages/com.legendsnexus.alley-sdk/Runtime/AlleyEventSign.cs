using System;
using UnityEngine;

namespace LegendsNexus.Alley
{
    [Serializable]
    public class AlleySignPanel
    {
        public string heading = "";

        [TextArea(3, 14)]
        public string body = "";
    }

    // marker for the event info wall. holds all the wording in one place and
    // pushes it into the panels, so staff never go hunting through child
    // objects. editor only sugar, vrchat strips it at build time
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Event Sign")]
    public class AlleyEventSign : MonoBehaviour
    {
        [Tooltip("Big line across the top of the wall.")]
        public string title = "LEGENDS ALLEY";

        [Tooltip("Small line in the chip beside it, usually the event name.")]
        public string subtitle = "BOOTH EVENT";

        [Tooltip("One entry per card, in the order the builder laid them out.")]
        public AlleySignPanel[] panels = new AlleySignPanel[0];

        [Header("Wired by the sign builder")]
        public TMPro.TMP_Text[] titleLabels;
        public TMPro.TMP_Text[] subtitleLabels;
        public TMPro.TMP_Text[] panelHeadings;
        public TMPro.TMP_Text[] panelBodies;

#if UNITY_EDITOR
        private void OnValidate()
        {
            Apply();
        }

        public void Apply()
        {
            Fill(titleLabels, string.IsNullOrEmpty(title) ? "" : title.ToUpperInvariant());
            Fill(subtitleLabels, string.IsNullOrEmpty(subtitle) ? "" : subtitle.ToUpperInvariant());

            if (panels == null) return;
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;
                if (panelHeadings != null && i < panelHeadings.Length && panelHeadings[i] != null)
                {
                    panelHeadings[i].text = panels[i].heading.ToUpperInvariant();
                }
                if (panelBodies != null && i < panelBodies.Length && panelBodies[i] != null)
                {
                    panelBodies[i].text = panels[i].body;
                }
            }
        }

        private static void Fill(TMPro.TMP_Text[] labels, string text)
        {
            if (labels == null) return;
            foreach (TMPro.TMP_Text label in labels)
            {
                if (label != null) label.text = text;
            }
        }
#endif
    }
}
