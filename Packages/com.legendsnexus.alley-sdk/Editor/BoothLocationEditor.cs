using UnityEditor;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    [CustomEditor(typeof(BoothLocation))]
    [CanEditMultipleObjects]
    public class BoothLocationEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("plotName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("reservedFor"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("locked"));
            serializedObject.ApplyModifiedProperties();

            var location = (BoothLocation)target;
            EditorGUILayout.Space(6);

            if (location.HasBooth)
            {
                EditorGUILayout.HelpBox(
                    $"Holding {location.placedCommunityName} v{location.placedVersion}.",
                    MessageType.Info);
                if (GUILayout.Button("Clear plot"))
                {
                    for (int i = location.transform.childCount - 1; i >= 0; i--)
                    {
                        Undo.DestroyObjectImmediate(location.transform.GetChild(i).gameObject);
                    }
                    Undo.RecordObject(location, "Clear plot");
                    location.ClearPlacement();
                    EditorUtility.SetDirty(location);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Empty plot. The blue arrow is the booth front. Run Sync booths in the Legends Alley SDK window to fill it.",
                    MessageType.None);
                if (!string.IsNullOrEmpty(location.placedCommunityId))
                {
                    location.ClearPlacement();
                    EditorUtility.SetDirty(location);
                }
            }
        }
    }
}
