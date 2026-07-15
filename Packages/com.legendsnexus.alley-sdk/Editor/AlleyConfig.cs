using UnityEditor;

namespace LegendsNexus.Alley.Editor
{
    internal static class AlleyConfig
    {
        public const string SdkVersion = "1.0.0";
        public const string DefaultApiBase = "https://alley.vrchatlegends.com";
        public const string PackageRoot = "Packages/com.legendsnexus.alley-sdk";

        private const string ApiBaseKey = "LegendsAlley.ApiBase";

        public static string ApiBase
        {
            get
            {
                string stored = EditorPrefs.GetString(ApiBaseKey, DefaultApiBase).TrimEnd('/');
                return string.IsNullOrEmpty(stored) ? DefaultApiBase : stored;
            }
            set => EditorPrefs.SetString(ApiBaseKey, string.IsNullOrEmpty(value) ? DefaultApiBase : value.TrimEnd('/'));
        }
    }
}
