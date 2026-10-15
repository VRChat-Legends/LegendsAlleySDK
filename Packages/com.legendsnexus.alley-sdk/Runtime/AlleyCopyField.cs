using TMPro;
using UdonSharp;
using UnityEngine;

namespace LegendsNexus.Alley
{
    // a promo code or link people can copy. vrchat has no clipboard api, but it
    // does open its own keyboard when you tap a text field, already filled in,
    // and that keyboard can copy. so this is a field holding the text; tap it,
    // the keyboard opens with it ready, copy. every keystroke is undone on the
    // spot, so nobody can change what it says
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [AddComponentMenu("Legends Alley/Alley Copy Field")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleyCopyField : UdonSharpBehaviour
    {
        public TMP_InputField field;

        [Tooltip("What gets copied.")]
        public string text = "";

        [Tooltip("Optional, bounces when tapped.")]
        public AlleyUiMotion motion;

        private void Start()
        {
            Restore();
        }

        // the world's own code can change what is shown, the info wall does
        public void SetText(string value)
        {
            text = value == null ? "" : value;
            Restore();
        }

        // ui events cannot start with an underscore, wired to the field's value changed and end edit
        public void Restore()
        {
            if (field != null && field.text != text) field.text = text;
        }

        public void Tapped()
        {
            if (motion != null) motion.Pulse();
            Restore();
        }
    }
}
