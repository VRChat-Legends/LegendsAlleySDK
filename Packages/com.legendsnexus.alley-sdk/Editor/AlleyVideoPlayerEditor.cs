using UdonSharpEditor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Components;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(AlleyVideoPlayer))]
    public class AlleyVideoPlayerEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            var card = AlleyInspectorKit.BuildCard(root, "teal", "VIDEO PLAYER");

            // keeps the usharp compile state and conversion handling working
            card.Add(new IMGUIContainer(() => UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target, false, false)));

            var urlField = new TextField("Video link");
            urlField.BindProperty(serializedObject.FindProperty(nameof(AlleyVideoPlayer.videoUrl)).FindPropertyRelative("url"));
            card.Add(urlField);

            var playRange = new Slider("Play range (m)", 3f, 5f) { showInputField = true };
            playRange.BindProperty(serializedObject.FindProperty(nameof(AlleyVideoPlayer.playbackRange)));
            card.Add(playRange);

            var audioReach = new Slider("Audio range (m)", 1f, 5f) { showInputField = true };
            audioReach.BindProperty(serializedObject.FindProperty(nameof(AlleyVideoPlayer.audioRange)));
            card.Add(audioReach);

            var loopToggle = new Toggle("Loop when it ends");
            loopToggle.BindProperty(serializedObject.FindProperty(nameof(AlleyVideoPlayer.loop)));
            card.Add(loopToggle);

            Label status = AlleyInspectorKit.MakeStatus(card);
            urlField.RegisterValueChangedCallback(_ => UpdateStatus(status));
            playRange.RegisterValueChangedCallback(_ => UpdateStatus(status));
            audioReach.RegisterValueChangedCallback(_ => { SyncAudio(); UpdateStatus(status); });
            SyncAudio();
            UpdateStatus(status);

            var test = new Button(OpenInBrowser) { text = "TEST LINK IN BROWSER" };
            test.AddToClassList("alley-insp-button-ghost");
            card.Add(test);

            return root;
        }

        private AlleyVideoPlayer Player => (AlleyVideoPlayer)target;

        private string CurrentUrl => Player.videoUrl != null ? (Player.videoUrl.Get() ?? "").Trim() : "";

        // the audio child carries the real falloff settings, keep them matched
        // to the slider so what you see in the scene is what ships
        private void SyncAudio()
        {
            AudioSource source = Player.audioSource;
            if (source == null) return;
            float range = Mathf.Clamp(Player.audioRange, 1f, 5f);
            if (!Mathf.Approximately(source.maxDistance, range))
            {
                Undo.RecordObject(source, "Set Video Audio Range");
                source.maxDistance = range;
                PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            }
            var spatial = source.GetComponent<VRCSpatialAudioSource>();
            if (spatial != null && !Mathf.Approximately(spatial.Far, range))
            {
                Undo.RecordObject(spatial, "Set Video Audio Range");
                spatial.Far = range;
                PrefabUtility.RecordPrefabInstancePropertyModifications(spatial);
            }
        }

        private void UpdateStatus(Label status)
        {
            string url = CurrentUrl;
            if (string.IsNullOrEmpty(url))
            {
                AlleyInspectorKit.SetStatus(status,
                    "Paste a link to what should play: a YouTube video, a direct .mp4 link, or a live stream (VRCDN and friends).",
                    "empty");
            }
            else if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                AlleyInspectorKit.SetStatus(status,
                    "The link needs to start with https:// so VRChat will load it.",
                    "warn");
            }
            else
            {
                AlleyInspectorKit.SetStatus(status,
                    $"People within {Player.playbackRange:0.#}m see it start on its own and get a play, pause, and volume bar. Sound never carries past {Mathf.Clamp(Player.audioRange, 1f, 5f):0.#}m.",
                    null);
            }
        }

        private void OpenInBrowser()
        {
            string url = CurrentUrl;
            if (url.StartsWith("http://") || url.StartsWith("https://")) Application.OpenURL(url);
        }
    }
}
