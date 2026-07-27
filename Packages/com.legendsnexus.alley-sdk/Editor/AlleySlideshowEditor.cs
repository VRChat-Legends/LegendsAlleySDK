using UdonSharpEditor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleySlideshow))]
    public class AlleySlideshowEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "teal", "SLIDESHOW BOARD");

            // keeps the usharp compile state and conversion handling working
            card.Add(new IMGUIContainer(() => UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)));

            var auto = new Toggle("Auto advance");
            auto.BindProperty(serializedObject.FindProperty(nameof(AlleySlideshow.autoAdvance)));
            card.Add(auto);

            var dwell = new Slider("Seconds per slide", 1f, 30f) { showInputField = true };
            dwell.BindProperty(serializedObject.FindProperty(nameof(AlleySlideshow.secondsPerSlide)));
            card.Add(dwell);

            var boardField = new ObjectField("Board renderer")
            {
                objectType = typeof(Renderer),
                allowSceneObjects = true,
            };
            boardField.BindProperty(serializedObject.FindProperty(nameof(AlleySlideshow.target)));
            card.Add(boardField);

            var counterField = new ObjectField("Counter label (optional)")
            {
                objectType = typeof(TMPro.TextMeshProUGUI),
                allowSceneObjects = true,
            };
            counterField.BindProperty(serializedObject.FindProperty(nameof(AlleySlideshow.counterLabel)));
            card.Add(counterField);

            Label status = AlleyInspectorKit.MakeStatus(card);
            UpdateStatus(status);
            root.TrackSerializedObjectValue(serializedObject, _ => UpdateStatus(status));
            // slide edits happen on the sibling source, watch it too
            var show = (AlleySlideshow)target;
            var source = show != null ? show.GetComponent<AlleySlideshowSource>() : null;
            if (source != null) root.TrackSerializedObjectValue(new SerializedObject(source), _ => UpdateStatus(status));

            return root;
        }

        private void UpdateStatus(Label status)
        {
            var show = (AlleySlideshow)target;
            var source = show.GetComponent<AlleySlideshowSource>();

            if (source == null)
            {
                AlleyInspectorKit.SetStatus(status,
                    "No Alley Slideshow Source on this object, so there is nothing to bake from. Use the bundled prefab (GameObject > Legends Alley > Slideshow) or add the component.",
                    "warn");
                return;
            }

            if (show.slideCount < 1)
            {
                AlleyInspectorKit.SetStatus(status,
                    "Nothing baked yet. Drop images into the Alley Slideshow Source below and press BAKE SLIDES.",
                    "empty");
                return;
            }

            int usable = AlleySlideshowBaker.CountUsable(source);
            if (usable != show.slideCount)
            {
                AlleyInspectorKit.SetStatus(status,
                    $"Showing {show.slideCount} slide{(show.slideCount == 1 ? "" : "s")} from the last bake, but the list below has {usable}. Press BAKE SLIDES to update the board.",
                    "warn");
                return;
            }

            AlleyInspectorKit.SetStatus(status,
                $"{show.slideCount} slide{(show.slideCount == 1 ? "" : "s")} baked into a {show.columns} x {show.rows} atlas.", null);
        }
    }
}
