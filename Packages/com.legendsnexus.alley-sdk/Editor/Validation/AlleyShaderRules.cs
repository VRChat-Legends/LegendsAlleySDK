using System.Collections.Generic;

namespace LegendsNexus.Alley.Editor
{
    // event shader whitelist, the backend re-checks the same list server side
    internal static class AlleyShaderRules
    {
        public const string Description = "Standard, z3y, Filamented, lilToon, unlit, legacy, TMP, UI, or particle shaders";

        private static readonly HashSet<string> AllowedExact = new HashSet<string>
        {
            "Standard",
            "Standard (Specular setup)",
            // vrchat sdk's own avpro screen shader, the bundled video player uses it
            "Video/RealtimeEmissiveGamma",
            // the one mobile shader that lightmaps properly, the rest stay banned
            "VRChat/Mobile/Toon Standard",
            "VRChat/Mobile/Toon Standard (Outline)",
        };

        private static readonly string[] AllowedPrefixes =
        {
            "Unlit/",
            "UI/",
            "Sprites/",
            "TextMeshPro/",
            "TMP/",
            "Particles/",
            "Legacy Shaders/",
            "Mobile/",
            "z3y/",
            "Filamented/",
        };

        // no lightmap support, booths using them turn black once the event world bakes
        public static bool IsMobileTrap(string shaderName)
        {
            return !string.IsNullOrEmpty(shaderName) && shaderName.StartsWith("VRChat/Mobile/", System.StringComparison.Ordinal);
        }

        public static bool IsAllowed(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return false;
            if (AllowedExact.Contains(shaderName)) return true;
            // limited custom shaders: liltoon as shipped
            if (shaderName == "lilToon" || shaderName.StartsWith("lilToon/", System.StringComparison.Ordinal)) return true;
            foreach (string prefix in AllowedPrefixes)
            {
                if (shaderName.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
