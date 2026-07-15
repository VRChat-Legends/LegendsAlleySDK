using System.Collections.Generic;

namespace LegendsNexus.Alley.Editor
{
    // event shader whitelist, the backend re-checks the same list server side
    internal static class AlleyShaderRules
    {
        public const string Description = "Standard, z3y, Filamented, unlit, TMP, UI, or particle shaders";

        private static readonly HashSet<string> AllowedExact = new HashSet<string>
        {
            "Standard",
            "Standard (Specular setup)",
        };

        private static readonly string[] AllowedPrefixes =
        {
            "Unlit/",
            "UI/",
            "Sprites/",
            "TextMeshPro/",
            "TMP/",
            "Particles/",
            "Legacy Shaders/Particles/",
            "VRChat/Mobile/",
            "z3y/",
            "Filamented/",
        };

        public static bool IsAllowed(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return false;
            if (AllowedExact.Contains(shaderName)) return true;
            foreach (string prefix in AllowedPrefixes)
            {
                if (shaderName.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
