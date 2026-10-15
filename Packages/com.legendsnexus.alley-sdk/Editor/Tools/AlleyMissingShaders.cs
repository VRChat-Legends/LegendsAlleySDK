using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LegendsNexus.Alley.Editor
{
    // anything rendering with no shader gets an obvious "shader not found" card
    // instead of the anonymous pink, so staff and creators know exactly what to fix.
    // only the scene instance is touched, the booth's own materials stay as
    // uploaded, so installing the real shader and running this again restores them
    internal static class AlleyMissingShaders
    {
        private const string TexturePath = AlleyConfig.PackageRoot + "/Runtime/Textures/AlleyShaderMissing.png";
        private const string MaterialPath = AlleyConfig.PackageRoot + "/Runtime/Materials/AlleyShaderMissing.mat";

        private static Material _fallback;

        public static Material Fallback
        {
            get
            {
                if (_fallback != null) return _fallback;
                _fallback = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (_fallback != null) return _fallback;

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
                _fallback = new Material(Shader.Find("Unlit/Texture")) { name = "AlleyShaderMissing", mainTexture = texture };
                AssetDatabase.CreateAsset(_fallback, MaterialPath);
                AssetDatabase.SaveAssets();
                return _fallback;
            }
        }

        public static bool IsBroken(Material material)
        {
            if (material == null) return true;
            Shader shader = material.shader;
            return shader == null || shader.name == "Hidden/InternalErrorShader" || !shader.isSupported;
        }

        [MenuItem("Tools/Legends Alley/Mark Missing Shaders In Scene")]
        private static void MarkSceneMenu()
        {
            int restored = 0;
            int marked = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    restored += Restore(root);
                    marked += Apply(root);
                }
            }
            if (marked > 0 || restored > 0) EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Legends Alley] Marked {marked} material slot(s) with no shader, restored {restored} whose shader is back.");
        }

        // returns how many material slots were swapped for the fallback
        public static int Apply(GameObject root)
        {
            if (root == null) return 0;
            Material fallback = Fallback;
            int swapped = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer particles && particles.renderMode == ParticleSystemRenderMode.None) continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials.Length == 0) continue;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == fallback || !IsBroken(materials[i])) continue;
                    materials[i] = fallback;
                    changed = true;
                    swapped++;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "Mark missing shaders");
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            return swapped;
        }

        // puts back the uploaded material wherever its shader now exists
        public static int Restore(GameObject root)
        {
            if (root == null) return 0;
            Material fallback = Fallback;
            int restored = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (Array.IndexOf(renderer.sharedMaterials, fallback) < 0) continue;
                var source = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                if (source == null) continue;

                Material[] original = source.sharedMaterials;
                Material[] current = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < current.Length && i < original.Length; i++)
                {
                    if (current[i] != fallback || IsBroken(original[i])) continue;
                    current[i] = original[i];
                    changed = true;
                    restored++;
                }
                if (!changed) continue;
                Undo.RecordObject(renderer, "Restore shaders");
                renderer.sharedMaterials = current;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            return restored;
        }
    }
}
