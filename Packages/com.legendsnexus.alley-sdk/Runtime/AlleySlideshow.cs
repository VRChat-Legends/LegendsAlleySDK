using TMPro;
using UdonSharp;
using UnityEngine;

namespace LegendsNexus.Alley
{
    // flips through slides baked into one atlas, so a whole deck costs the booth
    // a single texture and a single material
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [DisallowMultipleComponent]
    [AddComponentMenu("Legends Alley/Alley Slideshow")]
    [HelpURL("https://vrchatlegends.com")]
    public class AlleySlideshow : UdonSharpBehaviour
    {
        [Tooltip("Move to the next slide on its own.")]
        public bool autoAdvance = true;

        [Tooltip("How long each slide stays up, seconds.")]
        [Range(1f, 30f)] public float secondsPerSlide = 6f;

        [Header("Filled in by the Bake button")]
        public Renderer target;
        public int slideCount;
        public int columns = 1;
        public int rows = 1;

        [Header("Optional")]
        public TextMeshProUGUI counterLabel;

        private int _index;
        private float _nextFlip;
        private Material _material;

        private void Start()
        {
            if (target != null) _material = target.material;
            _nextFlip = Time.time + secondsPerSlide;
            Paint();
        }

        private void Update()
        {
            if (!autoAdvance || slideCount < 2 || Time.time < _nextFlip) return;
            _nextFlip = Time.time + secondsPerSlide;
            Step(1);
        }

        public void NextSlide()
        {
            Step(1);
        }

        public void PreviousSlide()
        {
            Step(-1);
        }

        private void Step(int direction)
        {
            if (slideCount < 1) return;
            _index = (_index + direction + slideCount) % slideCount;
            // pressing a button restarts the dwell so it does not flip instantly
            _nextFlip = Time.time + secondsPerSlide;
            Paint();
        }

        private void Paint()
        {
            if (_material == null || slideCount < 1 || columns < 1 || rows < 1) return;

            float w = 1f / columns;
            float h = 1f / rows;
            int column = _index % columns;
            int row = _index / columns;

            _material.SetTextureScale("_MainTex", new Vector2(w, h));
            // uv starts bottom left, the atlas fills top left first
            _material.SetTextureOffset("_MainTex", new Vector2(column * w, 1f - (row + 1) * h));

            if (counterLabel != null) counterLabel.text = (_index + 1) + " / " + slideCount;
        }
    }
}
