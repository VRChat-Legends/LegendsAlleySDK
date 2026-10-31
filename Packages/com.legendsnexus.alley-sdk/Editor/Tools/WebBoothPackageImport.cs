using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // a booth made on the website can be downloaded from the community dashboard
    // as a Unity package: booth.json and its pictures under
    // Assets/LegendsAlley/WebBooths/<name>. once the package is imported this
    // builds the booth prefab next to them, on the Alley Booth v2 in the SDK, so
    // it can be dragged into any scene. the Assets menu rebuilds it, in the
    // standard or the Christmas look
    internal class WebBoothPackageImport : AssetPostprocessor
    {
        private const string Root = "Assets/LegendsAlley/WebBooths/";
        private static readonly HashSet<string> Pending = new HashSet<string>();

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
            {
                if (!IsBoothJson(path) || !Pending.Add(path)) continue;
                // after the import finishes, the build imports assets of its own
                EditorApplication.delayCall += () => Build(path, false);
            }
        }

        private static bool IsBoothJson(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(Root) || !path.EndsWith("/booth.json")) return false;
            try
            {
                return File.ReadAllText(path).Contains("\"legendsAlleyWebBooth\"");
            }
            catch
            {
                return false;
            }
        }

        private static async void Build(string path, bool christmas)
        {
            try
            {
                string prefab = await WebBooths.BuildFromPackage(path, christmas);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
                Debug.Log($"[LegendsAlley] Built the website booth at {prefab}{(christmas ? " in the Christmas look" : "")}. Drag it into your scene.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LegendsAlley] Could not build the website booth from {path}: {e.Message}");
                EditorUtility.DisplayDialog("Legends Alley", $"Could not build the website booth: {e.Message}", "OK");
            }
            finally
            {
                Pending.Remove(path);
            }
        }

        private static string SelectedJson()
        {
            foreach (UnityEngine.Object picked in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(picked);
                if (AssetDatabase.IsValidFolder(path)) path += "/booth.json";
                if (IsBoothJson(path)) return path;
            }
            return null;
        }

        [MenuItem("Assets/Legends Alley/Rebuild Website Booth", false, 40)]
        private static void Rebuild() => Build(SelectedJson(), false);

        [MenuItem("Assets/Legends Alley/Rebuild Website Booth (Christmas)", false, 41)]
        private static void RebuildChristmas() => Build(SelectedJson(), true);

        [MenuItem("Assets/Legends Alley/Rebuild Website Booth", true)]
        [MenuItem("Assets/Legends Alley/Rebuild Website Booth (Christmas)", true)]
        private static bool CanRebuild() => SelectedJson() != null;
    }
}
