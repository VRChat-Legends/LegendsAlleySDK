using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(LegendsBooth))]
    public class LegendsBoothEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawCommunityPicker();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("showBounds"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6);
            Vector3 limit = LegendsBooth.BoundsLimit;
            EditorGUILayout.HelpBox(
                $"Keep the booth inside {limit.x} x {limit.y} x {limit.z} meters. " +
                "Open the Legends Alley SDK window to check everything and upload.",
                MessageType.Info);

            if (GUILayout.Button("Open Legends Alley SDK"))
            {
                AlleySdkWindow.ShowWindow();
            }
        }

        // the booth belongs to a community, pick it from the signed in account
        private void DrawCommunityPicker()
        {
            AlleySession.LoadIfNeeded();
            SerializedProperty nameProp = serializedObject.FindProperty("displayName");
            CommunityInfo mine = AlleySession.IsSignedIn ? AlleySession.Community : null;

            if (mine == null || string.IsNullOrEmpty(mine.name))
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Community",
                        string.IsNullOrEmpty(nameProp.stringValue) ? "(not set)" : nameProp.stringValue);
                }
                EditorGUILayout.HelpBox(
                    "Sign in on the Legends Alley SDK window to pick which community this booth belongs to.",
                    MessageType.Info);
                return;
            }

            var options = new List<string>();
            string stored = nameProp.stringValue;
            if (!string.IsNullOrEmpty(stored) && stored != mine.name) options.Add(stored);
            options.Add(mine.name);

            int current = string.IsNullOrEmpty(stored) ? options.Count - 1 : options.IndexOf(stored);
            if (current < 0) current = options.Count - 1;

            int picked = EditorGUILayout.Popup("Community", current, options.ToArray());
            string pickedName = options[picked];
            if (pickedName != stored) nameProp.stringValue = pickedName;

            if (options.Count > 1)
            {
                EditorGUILayout.HelpBox(
                    $"This booth is labeled \"{stored}\" which is not your community. Pick {mine.name} from the dropdown to claim it.",
                    MessageType.Warning);
            }
        }
    }
}
