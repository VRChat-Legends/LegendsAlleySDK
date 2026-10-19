using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // carries the staff world between projects: the world menu, the event info
    // wall, the booth directory, name tags, the warp effect, the join intro and
    // the Tiltify fundraiser banner with its fireworks, with all their code,
    // icons and volumetric ui. one click unpacks the kit, waits for unity to
    // compile it, drops in whichever pieces the open scene is missing and runs
    // the world setup to wire everything up.
    //
    // the kit lives in StaffKit~ inside this package, which unity never imports
    // on its own. staff refresh it from a working world with
    // Tools > Legends Alley > World > Export Staff Kit
    internal static class AlleyWorldKit
    {
        public const string KitFolder = AlleyConfig.PackageRoot + "/StaffKit~";
        public const string KitFile = KitFolder + "/LegendsAlleyWorldKit.unitypackage";
        private const string PrefabFolder = "Assets/AlleyWorld/Prefabs";
        private const string SetupMenu = "Tools/Legends Alley/World/Run Full Setup";
        private const string PendingKey = "LegendsAlley.WorldKitPending";

        // order matters: the menu has to exist before setup wires the rest to it
        private static readonly string[] Pieces =
        {
            "Alley World Menu",
            "Event Info Wall",
            "Booth Directory Board",
            "Alley World Systems",
            "Alley Join Intro",
            "Fundraiser Board",
            "Fundraiser Fireworks",
        };

        public static bool Available => File.Exists(Path.GetFullPath(KitFile));

        [MenuItem("Legends Alley/Staff/Import World Kit", priority = 30)]
        public static void Import()
        {
            if (AlleyStaffOnly.Blocked("The world kit")) return;
            if (!Available)
            {
                EditorUtility.DisplayDialog("World kit missing",
                    "This copy of the SDK has no staff world kit in it. Update the SDK, or export one from a working world with Tools > Legends Alley > World > Export Staff Kit.",
                    "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Import the Legends Alley world kit?",
                    "This adds the world menu, event info wall, booth directory, name tags, warp effect, join intro and fundraiser banner to this project, then places any of them the open scene is missing and wires them up.\n\nPieces already in the scene are kept and repaired, not replaced.",
                    "Import", "Cancel"))
            {
                return;
            }

            AssetDatabase.importPackageCompleted -= OnImported;
            AssetDatabase.importPackageCompleted += OnImported;
            AssetDatabase.importPackageFailed -= OnFailed;
            AssetDatabase.importPackageFailed += OnFailed;
            AssetDatabase.ImportPackage(Path.GetFullPath(KitFile), false);
        }

        private static void OnImported(string packageName)
        {
            AssetDatabase.importPackageCompleted -= OnImported;
            SessionState.SetBool(PendingKey, true);
            Debug.Log("[Legends Alley] World kit unpacked, it finishes setting up once the scripts have compiled.");
            // with nothing new to compile there is no reload, so try straight away
            EditorApplication.delayCall += Resume;
        }

        private static void OnFailed(string packageName, string error)
        {
            AssetDatabase.importPackageFailed -= OnFailed;
            SessionState.SetBool(PendingKey, false);
            Debug.LogError("[Legends Alley] World kit import failed: " + error);
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            if (SessionState.GetBool(PendingKey, false)) EditorApplication.delayCall += Resume;
        }

        private static void Resume()
        {
            if (!SessionState.GetBool(PendingKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Resume;
                return;
            }
            SessionState.SetBool(PendingKey, false);

            int placed = PlaceMissingPieces();
            if (!EditorApplication.ExecuteMenuItem(SetupMenu))
            {
                Debug.LogWarning("[Legends Alley] World setup is not available yet. Run " + SetupMenu + " once the scripts have compiled.");
                return;
            }
            AlleyDirectoryBuilder.RebuildAll(msg => Debug.Log("[Legends Alley] " + msg));
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[Legends Alley] World kit ready, placed {placed} missing piece(s). Move the boards where you want them and save the scene.");
        }

        // placed unpacked, so the scene owns them outright and later setup runs
        // do not pile up prefab overrides
        private static int PlaceMissingPieces()
        {
            int placed = 0;
            foreach (string piece in Pieces)
            {
                if (FindRoot(piece) != null) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + piece + ".prefab");
                if (prefab == null) continue;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = piece;
                Undo.RegisterCreatedObjectUndo(instance, "Place " + piece);
                placed++;
            }
            return placed;
        }

        private static GameObject FindRoot(string name)
        {
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == name) return root;
            }
            return null;
        }
    }
}
