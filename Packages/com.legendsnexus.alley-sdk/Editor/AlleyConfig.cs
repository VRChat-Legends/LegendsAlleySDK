using UnityEditor;

namespace LegendsNexus.Alley.Editor
{
    internal static class AlleyConfig
    {
        public const string DefaultApiBase = "https://alley.vrchatlegends.com";
        public const string PackageRoot = "Packages/com.legendsnexus.alley-sdk";

        private static string _sdkVersion;

        // read from package.json so the settings page, user agent and upload
        // payload always match the shipped package version
        public static string SdkVersion
        {
            get
            {
                if (_sdkVersion == null)
                {
                    var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(PackageRoot + "/package.json");
                    _sdkVersion = string.IsNullOrEmpty(info?.version) ? "0.0.0" : info.version;
                }
                return _sdkVersion;
            }
        }

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
