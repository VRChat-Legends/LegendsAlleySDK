using System.Collections.Generic;
using System.Linq;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace LegendsNexus.Alley.Editor
{
    // "Update to latest" for the SDK prefabs (video player, slideshow, group
    // button, portal, avatar pedestal). an update can rebuild a prefab, and a
    // copy placed in a scene, or one that was unpacked, then keeps the old
    // insides. this swaps in a fresh copy of the current prefab and carries over
    // what the creator set: where it stands, its name, the settings on its
    // Legends Alley script (links, ids, images, ranges), pictures and materials
    // they picked, and anything elsewhere in the scene that pointed at it.
    // booths on staff plots are left alone, the booth sync rebuilds those
    [InitializeOnLoad]
    internal static class AlleyPrefabUpdater
    {
        private const string PrefabFolder = AlleyConfig.PackageRoot + "/Runtime/Prefabs";

        static AlleyPrefabUpdater()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DrawHeader;
        }

        // ------------------------------------------------------------ ui

        private static void DrawHeader(UnityEditor.Editor editor)
        {
            if (!(editor.target is GameObject)) return;
            GameObject[] picked = editor.targets.OfType<GameObject>().Where(g => g != null && g.scene.IsValid()).ToArray();
            if (picked.Length == 0) return;
            GameObject[] ready = picked.Where(g => SourceFor(g, out _) != null).ToArray();
            if (ready.Length == 0)
            {
                if (picked.Any(IsNestedSdkPrefab))
                {
                    EditorGUILayout.HelpBox("This Legends Alley prefab sits inside another prefab. Open that prefab to update it.", MessageType.None);
                }
                return;
            }
            var content = new GUIContent(
                ready.Length == 1 ? "Update to latest Legends Alley prefab" : $"Update {ready.Length} to latest Legends Alley prefab",
                "Swaps in a fresh copy of the prefab from the installed SDK, keeping its place, name and settings.");
            if (GUILayout.Button(content)) UpdateAll(ready);
        }

        [MenuItem("Tools/Legends Alley/Update SDK Prefabs In Scene", false, 40)]
        private static void UpdateScene()
        {
            var found = new List<GameObject>();
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var scene = EditorSceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (SourceFor(t.gameObject, out _) != null) found.Add(t.gameObject);
                    }
                }
            }
            // only the outermost ones, an update replaces everything below it
            found = found.Where(g => !found.Any(o => o != g && g.transform.IsChildOf(o.transform))).ToList();
            if (found.Count == 0)
            {
                EditorUtility.DisplayDialog("Legends Alley", "No Legends Alley prefabs to update in the open scenes.", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Legends Alley", $"Update {found.Count} Legends Alley prefab(s) to the latest SDK version? Their place, names and settings are kept. You can undo this.", "Update", "Cancel")) return;
            UpdateAll(found.ToArray());
        }

        [MenuItem("GameObject/Legends Alley/Update to Latest", false, 30)]
        private static void UpdateSelected(MenuCommand command)
        {
            // runs once per selected object from the hierarchy menu, do them all on the first
            if (command.context != null && command.context != Selection.activeGameObject) return;
            GameObject[] ready = Selection.gameObjects.Where(g => SourceFor(g, out _) != null).ToArray();
            if (ready.Length == 0)
            {
                EditorUtility.DisplayDialog("Legends Alley", "Pick a Legends Alley prefab (video player, slideshow, group button, portal or avatar pedestal) to update.", "OK");
                return;
            }
            UpdateAll(ready);
        }

        // ------------------------------------------------------------ which prefab

        private static Dictionary<System.Type, string> _byType;

        // the SDK prefab each script lives on, for copies that were unpacked
        private static Dictionary<System.Type, string> ByType()
        {
            if (_byType != null) return _byType;
            _byType = new Dictionary<System.Type, string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                foreach (UdonSharpBehaviour behaviour in asset.GetComponents<UdonSharpBehaviour>())
                {
                    if (behaviour != null && !_byType.ContainsKey(behaviour.GetType())) _byType[behaviour.GetType()] = path;
                }
            }
            return _byType;
        }

        private static GameObject SourceFor(GameObject go, out string path)
        {
            path = null;
            if (go == null || !go.scene.IsValid()) return null;
            // plots belong to the booth sync
            if (go.GetComponentInParent<BoothLocation>(true) != null) return null;

            if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go)) return null;
                path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                if (string.IsNullOrEmpty(path) || !path.StartsWith(PrefabFolder + "/")) return null;
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            foreach (UdonSharpBehaviour behaviour in go.GetComponents<UdonSharpBehaviour>())
            {
                if (behaviour != null && ByType().TryGetValue(behaviour.GetType(), out path))
                {
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
                }
            }
            return null;
        }

        private static bool IsNestedSdkPrefab(GameObject go)
        {
            if (!PrefabUtility.IsPartOfPrefabInstance(go) || PrefabUtility.IsOutermostPrefabInstanceRoot(go)) return false;
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(go)) return false;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            return !string.IsNullOrEmpty(path) && path.StartsWith(PrefabFolder + "/") && go.GetComponentInParent<BoothLocation>(true) == null;
        }

        // ------------------------------------------------------------ update

        private static void UpdateAll(GameObject[] targets)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Update Legends Alley prefab");
            int group = Undo.GetCurrentGroup();
            var fresh = new List<GameObject>();
            int done = 0;
            foreach (GameObject go in targets)
            {
                if (go == null) continue;
                GameObject asset = SourceFor(go, out string path);
                if (asset == null) continue;
                GameObject made = Replace(go, asset);
                if (made == null) continue;
                fresh.Add(made);
                done++;
            }
            Undo.CollapseUndoOperations(group);
            if (fresh.Count > 0) Selection.objects = fresh.ToArray();
            Debug.Log($"[LegendsAlley] Updated {done} prefab(s) to SDK {AlleyConfig.SdkVersion}.");
        }

        private static GameObject Replace(GameObject old, GameObject asset)
        {
            Transform oldRoot = old.transform;
            var made = (GameObject)PrefabUtility.InstantiatePrefab(asset, old.scene);
            if (made == null) return null;
            Undo.RegisterCreatedObjectUndo(made, "Update Legends Alley prefab");
            Transform root = made.transform;
            root.SetParent(oldRoot.parent, false);
            root.SetSiblingIndex(oldRoot.GetSiblingIndex());
            root.localPosition = oldRoot.localPosition;
            root.localRotation = oldRoot.localRotation;
            root.localScale = oldRoot.localScale;
            made.name = old.name;
            made.layer = old.layer;
            made.tag = old.tag;
            GameObjectUtility.SetStaticEditorFlags(made, GameObjectUtility.GetStaticEditorFlags(old));
            made.SetActive(old.activeSelf);

            // the script settings, and the labels they point at
            var labels = new List<(TMP_Text from, TMP_Text to)>();
            foreach (UdonSharpBehaviour from in old.GetComponents<UdonSharpBehaviour>())
            {
                if (from == null) continue;
                var to = made.GetComponent(from.GetType()) as UdonSharpBehaviour;
                if (to == null) continue;
                CopySettings(from, to, oldRoot, root, labels);
            }
            foreach ((TMP_Text from, TMP_Text to) in labels)
            {
                if (from == null || to == null || from.text == to.text) continue;
                Undo.RecordObject(to, "Update Legends Alley prefab");
                to.text = from.text;
                EditorUtility.SetDirty(to);
            }
            CopyPictures(oldRoot, root);

            // whatever else in the scene pointed into the old copy
            Repoint(old, made);

            foreach (UdonSharpBehaviour behaviour in made.GetComponentsInChildren<UdonSharpBehaviour>(true))
            {
                UdonSharpEditorUtility.CopyProxyToUdon(behaviour);
            }
            Undo.DestroyObjectImmediate(old);
            EditorSceneManager.MarkSceneDirty(made.scene);
            return made;
        }

        // settings are copied as they are; references into the prefab itself are
        // its own wiring and stay as the new prefab has them
        private static void CopySettings(Object from, Object to, Transform oldRoot, Transform newRoot, List<(TMP_Text, TMP_Text)> labels)
        {
            var source = new SerializedObject(from);
            var target = new SerializedObject(to);
            SerializedProperty it = source.GetIterator();
            if (!it.NextVisible(true)) return;
            do
            {
                if (it.name == "m_Script") continue;
                if (PointsInside(it, oldRoot))
                {
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue is TMP_Text oldLabel)
                    {
                        SerializedProperty newProp = target.FindProperty(it.propertyPath);
                        if (newProp != null && newProp.objectReferenceValue is TMP_Text newLabel) labels.Add((oldLabel, newLabel));
                    }
                    continue;
                }
                target.CopyFromSerializedPropertyIfDifferent(it);
            }
            while (it.NextVisible(false));
            target.ApplyModifiedProperties();
        }

        private static bool PointsInside(SerializedProperty property, Transform root)
        {
            SerializedProperty walk = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enter = true;
            while (walk.Next(enter) && !SerializedProperty.EqualContents(walk, end))
            {
                enter = true;
                if (walk.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (Inside(walk.objectReferenceValue, root)) return true;
            }
            return property.propertyType == SerializedPropertyType.ObjectReference && Inside(property.objectReferenceValue, root);
        }

        private static bool Inside(Object value, Transform root)
        {
            if (value is Component c) return c != null && c.transform.IsChildOf(root);
            if (value is GameObject g) return g != null && g.transform.IsChildOf(root);
            return false;
        }

        // pictures and materials the creator picked (anything not from the SDK)
        // carry over onto the same part of the new prefab
        private static void CopyPictures(Transform oldRoot, Transform newRoot)
        {
            foreach (Image image in oldRoot.GetComponentsInChildren<Image>(true))
            {
                if (!Picked(image.sprite)) continue;
                var to = Same<Image>(image, oldRoot, newRoot);
                if (to == null || to.sprite == image.sprite) continue;
                Undo.RecordObject(to, "Update Legends Alley prefab");
                to.sprite = image.sprite;
                to.enabled = image.enabled;
            }
            foreach (RawImage image in oldRoot.GetComponentsInChildren<RawImage>(true))
            {
                if (!Picked(image.texture)) continue;
                var to = Same<RawImage>(image, oldRoot, newRoot);
                if (to == null) continue;
                Undo.RecordObject(to, "Update Legends Alley prefab");
                to.texture = image.texture;
            }
            foreach (Renderer renderer in oldRoot.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                if (!mats.Any(Picked)) continue;
                var to = Same<Renderer>(renderer, oldRoot, newRoot);
                if (to == null) continue;
                Material[] next = to.sharedMaterials;
                for (int i = 0; i < next.Length && i < mats.Length; i++)
                {
                    if (Picked(mats[i])) next[i] = mats[i];
                }
                Undo.RecordObject(to, "Update Legends Alley prefab");
                to.sharedMaterials = next;
            }
        }

        private static bool Picked(Object asset)
        {
            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && !path.StartsWith(AlleyConfig.PackageRoot + "/");
        }

        // the matching part of the new prefab: same path from the root, same
        // kind of component at the same index
        private static T Same<T>(T component, Transform oldRoot, Transform newRoot) where T : Component
        {
            Object found = Map(component, oldRoot, newRoot);
            return found as T;
        }

        private static Object Map(Object value, Transform oldRoot, Transform newRoot)
        {
            Transform from = value is Component c ? c.transform : value is GameObject g ? g.transform : null;
            if (from == null || !from.IsChildOf(oldRoot)) return null;
            string path = from == oldRoot ? "" : AnimationUtility.CalculateTransformPath(from, oldRoot);
            Transform to = path.Length == 0 ? newRoot : newRoot.Find(path);
            if (to == null) return null;
            if (value is GameObject) return to.gameObject;
            System.Type type = value.GetType();
            Component[] olds = from.GetComponents(type);
            Component[] news = to.GetComponents(type);
            int index = System.Array.IndexOf(olds, (Component)value);
            return index >= 0 && index < news.Length ? news[index] : null;
        }

        // buttons, scripts and events elsewhere in the scene that pointed into the
        // old copy now point at the same part of the new one
        private static void Repoint(GameObject old, GameObject made)
        {
            Transform oldRoot = old.transform, newRoot = made.transform;
            foreach (GameObject sceneRoot in old.scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in sceneRoot.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null) continue;
                    if (behaviour.transform.IsChildOf(oldRoot) || behaviour.transform.IsChildOf(newRoot)) continue;
                    var so = new SerializedObject(behaviour);
                    SerializedProperty it = so.GetIterator();
                    bool changed = false;
                    while (it.Next(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        Object value = it.objectReferenceValue;
                        if (!Inside(value, oldRoot)) continue;
                        Object mapped = Map(value, oldRoot, newRoot);
                        if (mapped == null) continue;
                        it.objectReferenceValue = mapped;
                        changed = true;
                    }
                    if (!changed) continue;
                    Undo.RecordObject(behaviour, "Update Legends Alley prefab");
                    so.ApplyModifiedProperties();
                    if (behaviour is UdonSharpBehaviour usharp) UdonSharpEditorUtility.CopyProxyToUdon(usharp);
                }
            }
        }
    }
}
