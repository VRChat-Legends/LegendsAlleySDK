using System;
using System.Collections.Generic;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    [Serializable]
    internal class ImportItem
    {
        public string boothId;
        public string communityId;
        public string communityName;
        public string prefabName;
        public string sha256;
        public int version;
        public string locationPath;
        public string packagePath;
        public string stage; // pending -> importing -> place
    }

    [Serializable]
    internal class ImportQueueData
    {
        public List<ImportItem> items = new List<ImportItem>();
        public int importedCount;
        public int updatedCount;
        public int skippedCount;
    }

    // the queue lives in SessionState so a script import triggering a domain
    // reload mid sync does not lose our place
    internal static class ImportQueue
    {
        private const string Key = "LegendsAlley.ImportQueue";

        public static ImportQueueData Load()
        {
            string json = UnityEditor.SessionState.GetString(Key, "");
            if (string.IsNullOrEmpty(json)) return new ImportQueueData();
            try
            {
                return JsonUtility.FromJson<ImportQueueData>(json) ?? new ImportQueueData();
            }
            catch
            {
                return new ImportQueueData();
            }
        }

        public static void Save(ImportQueueData data)
        {
            UnityEditor.SessionState.SetString(Key, JsonUtility.ToJson(data));
        }

        public static void Clear()
        {
            UnityEditor.SessionState.EraseString(Key);
        }

        public static bool HasWork()
        {
            return Load().items.Count > 0;
        }
    }
}
