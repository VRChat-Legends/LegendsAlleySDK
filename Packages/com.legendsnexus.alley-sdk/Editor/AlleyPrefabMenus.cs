using UnityEditor;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // puts the bundled prefabs in the GameObject menu next to the builtins
    internal static class AlleyPrefabMenus
    {
        private const string GroupButton = AlleyConfig.PackageRoot + "/Runtime/Prefabs/Alley Group Button.prefab";
        private const string AvatarPedestal = AlleyConfig.PackageRoot + "/Runtime/Prefabs/Alley Avatar Pedestal.prefab";
        private const string VideoPlayer = AlleyConfig.PackageRoot + "/Runtime/Prefabs/Alley Video Player.prefab";

        [MenuItem("GameObject/Legends Alley/Group Button", false, 10)]
        private static void SpawnGroupButton(MenuCommand command)
        {
            // float it at hand height instead of half sunk in the floor
            Spawn(GroupButton, command, new Vector3(0f, 1.2f, 0f));
        }

        [MenuItem("GameObject/Legends Alley/Avatar Pedestal", false, 11)]
        private static void SpawnAvatarPedestal(MenuCommand command)
        {
            // pivot is the center of the avatar picture, float it at eye height
            Spawn(AvatarPedestal, command, new Vector3(0f, 1.2f, 0f));
        }

        [MenuItem("GameObject/Legends Alley/Video Player", false, 12)]
        private static void SpawnVideoPlayer(MenuCommand command)
        {
            // pivot is the center of the screen, put it at head height
            Spawn(VideoPlayer, command, new Vector3(0f, 1.5f, 0f));
        }

        private static void Spawn(string path, MenuCommand command, Vector3 offset)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError("[LegendsAlley] Bundled prefab is missing at " + path + ". Reinstall the SDK package.");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            GameObjectUtility.SetParentAndAlign(instance, command.context as GameObject);
            instance.transform.localPosition += offset;
            Undo.RegisterCreatedObjectUndo(instance, "Create " + instance.name);
            Selection.activeGameObject = instance;
        }
    }
}
