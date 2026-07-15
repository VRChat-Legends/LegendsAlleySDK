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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
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
    }
}
