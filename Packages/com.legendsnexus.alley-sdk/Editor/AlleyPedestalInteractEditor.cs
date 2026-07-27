using UdonSharpEditor;
using UnityEditor;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleyPedestalInteract))]
    public class AlleyPedestalInteractEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "purple", "PEDESTAL INTERACT");

            // keeps the usharp compile state and conversion handling working
            card.Add(new IMGUIContainer(() => UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)));

            Label status = AlleyInspectorKit.MakeStatus(card);
            AlleyInspectorKit.SetStatus(status,
                "This is what makes the pedestal pressable, nothing to set up here. The press text and reach live in the header above.", null);

            return root;
        }
    }
}
