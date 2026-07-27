using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleySlideshowSource))]
    public class AlleySlideshowSourceEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "teal", "SLIDESHOW");

            var slides = new PropertyField(serializedObject.FindProperty(nameof(AlleySlideshowSource.slides)), "Slides");
            slides.Bind(serializedObject);
            card.Add(slides);

            var size = new PropertyField(serializedObject.FindProperty(nameof(AlleySlideshowSource.atlasSize)), "Atlas size");
            size.Bind(serializedObject);
            card.Add(size);

            Label status = AlleyInspectorKit.MakeStatus(card);
            UpdateStatus(status);
            root.TrackSerializedObjectValue(serializedObject, _ => UpdateStatus(status));

            var bake = new Button(() => Bake(status)) { text = "BAKE SLIDES" };
            bake.AddToClassList("alley-insp-button-ghost");
            card.Add(bake);

            return root;
        }

        private void Bake(Label status)
        {
            var source = (AlleySlideshowSource)target;
            var show = source.GetComponent<AlleySlideshow>();
            bool ok = AlleySlideshowBaker.Bake(source, show, out string message);
            AlleyInspectorKit.SetStatus(status, message, ok ? null : "warn");
        }

        private void UpdateStatus(Label status)
        {
            var source = (AlleySlideshowSource)target;
            int max = AlleySlideshowBaker.MaxSlides();

            if (string.IsNullOrEmpty(source.bakedAt))
            {
                AlleyInspectorKit.SetStatus(status,
                    $"Drop your images in, then press bake. This event allows up to {max} slides.", "empty");
                return;
            }

            // the board only knows what the last bake wrote into it
            var show = source.GetComponent<AlleySlideshow>();
            int usable = AlleySlideshowBaker.CountUsable(source);
            if (show != null && usable != show.slideCount)
            {
                AlleyInspectorKit.SetStatus(status,
                    $"The list has {usable} image{(usable == 1 ? "" : "s")} but the board still shows {show.slideCount}. Press BAKE SLIDES to update it.",
                    "warn");
                return;
            }

            AlleyInspectorKit.SetStatus(status,
                $"Last baked {source.bakedAt}. Bake again after you change the list, up to {max} slides.", null);
        }
    }
}
