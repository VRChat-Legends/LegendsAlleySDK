using System.Collections.Generic;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.Udon.UAssembly.Disassembler;

namespace LegendsNexus.Alley.Editor
{
    // event whitelist for what booth udon is allowed to call. every compiled
    // program gets disassembled and each extern call checked against this list,
    // so a reworked kit script or a hand written graph cant reach things booths
    // have no business touching: downloaders, cameras, the scene outside their
    // own objects, other players movement, and so on. default deny, the backend
    // and staff import rely on the same rules
    internal static class AlleyUdonRules
    {
        // extern type names are the full type with namespace dots stripped,
        // like UnityEngineTransform or VRCSDKBaseNetworking
        private static readonly HashSet<string> AllowedTypes = new HashSet<string>
        {
            // plain values and strings
            "SystemString", "SystemBoolean", "SystemByte", "SystemSByte",
            "SystemInt16", "SystemUInt16", "SystemInt32", "SystemUInt32",
            "SystemInt64", "SystemUInt64", "SystemSingle", "SystemDouble",
            "SystemChar", "SystemObject", "SystemArray", "SystemMath",
            "SystemConvert", "SystemDateTime", "SystemTimeSpan", "SystemType",
            "SystemTextStringBuilder",

            // local data wrangling
            "VRCSDK3DataDataList", "VRCSDK3DataDataDictionary", "VRCSDK3DataDataToken",
            "VRCSDK3DataVRCJson",

            // unity core
            "UnityEngineObject", "UnityEngineGameObject", "UnityEngineComponent",
            "UnityEngineBehaviour", "UnityEngineMonoBehaviour", "UnityEngineTransform",
            "UnityEngineRectTransform", "UnityEngineMathf", "UnityEngineVector2",
            "UnityEngineVector3", "UnityEngineVector4", "UnityEngineQuaternion",
            "UnityEngineColor", "UnityEngineColor32", "UnityEngineRandom", "UnityEngineTime",
            "UnityEngineDebug", "UnityEngineAnimationCurve", "UnityEngineRect",
            "UnityEngineMatrix4x4",

            // visuals on their own objects
            "UnityEngineRenderer", "UnityEngineMeshRenderer", "UnityEngineSkinnedMeshRenderer",
            "UnityEngineMaterial", "UnityEngineMaterialPropertyBlock", "UnityEngineShader",
            "UnityEngineLight", "UnityEngineSpriteRenderer", "UnityEngineSprite",
            "UnityEngineTrailRenderer", "UnityEngineLineRenderer",
            "UnityEngineAnimator", "UnityEngineAnimation",

            // sound, with the range members banned below
            "UnityEngineAudioSource", "UnityEngineAudioClip",

            // physics props inside the booth
            "UnityEngineRigidbody", "UnityEngineCollider", "UnityEngineBoxCollider",

            // ui
            "UnityEngineCanvas", "UnityEngineCanvasGroup",

            // vrchat
            "VRCSDKBaseNetworking", "VRCSDKBaseVRCPlayerApi", "VRCSDKBaseUtilities",
            "VRCSDKBaseVRCUrl", "VRCUdonUdonBehaviour", "VRCUdonCommonInterfacesIUdonEventReceiver",
            "VRCSDK3ComponentsVRCPickup", "VRCSDK3ComponentsVRCAvatarPedestal",
            "VRCSDK3VideoComponentsBaseBaseVRCVideoPlayer",
            "VRCSDK3VideoComponentsAVProVRCAVProVideoPlayer",
            "VRCSDK3VideoComponentsVRCUnityVideoPlayer",
        };

        // whole families where every subtype is fine
        private static readonly string[] AllowedTypePrefixes =
        {
            "UnityEngineParticleSystem",
            "UnityEngineUI",
            "TMPro",
        };

        // allowed types that still have members booths must not touch
        private static readonly HashSet<string> BannedMembers = new HashSet<string>
        {
            // scene wide reach, a booth only gets to know its own objects
            "UnityEngineGameObject.Find",
            "UnityEngineGameObject.FindWithTag",
            "UnityEngineGameObject.FindGameObjectWithTag",
            "UnityEngineGameObject.FindGameObjectsWithTag",
            "UnityEngineGameObject.GetComponentInParent",
            "UnityEngineGameObject.GetComponentsInParent",
            "UnityEngineComponent.GetComponentInParent",
            "UnityEngineComponent.GetComponentsInParent",
            "UnityEngineObject.FindObjectOfType",
            "UnityEngineObject.FindObjectsOfType",
            "UnityEngineObject.FindObjectsByType",
            "UnityEngineObject.FindAnyObjectByType",
            "UnityEngineObject.FindFirstObjectByType",
            "UnityEngineObject.Instantiate",
            "UnityEngineObject.DontDestroyOnLoad",

            // climbing out of the booth hierarchy
            "UnityEngineTransform.get_parent",
            "UnityEngineTransform.set_parent",
            "UnityEngineTransform.SetParent",
            "UnityEngineTransform.get_root",
            "UnityEngineTransform.GetComponentInParent",
            "UnityEngineTransform.GetComponentsInParent",

            // the audio range checks happen at upload, no re-cranking them at runtime
            "UnityEngineAudioSource.set_maxDistance",
            "UnityEngineAudioSource.set_minDistance",
            "UnityEngineAudioSource.set_spatialBlend",
            "UnityEngineAudioSource.set_rolloffMode",
            "UnityEngineAudioSource.SetCustomCurve",

            // other people are not toys
            "VRCSDKBaseVRCPlayerApi.TeleportTo",
            "VRCSDKBaseVRCPlayerApi.SetVelocity",
            "VRCSDKBaseVRCPlayerApi.Immobilize",
            "VRCSDKBaseVRCPlayerApi.SetWalkSpeed",
            "VRCSDKBaseVRCPlayerApi.SetRunSpeed",
            "VRCSDKBaseVRCPlayerApi.SetStrafeSpeed",
            "VRCSDKBaseVRCPlayerApi.SetJumpImpulse",
            "VRCSDKBaseVRCPlayerApi.SetGravityStrength",
            "VRCSDKBaseVRCPlayerApi.UseAttachedStation",
            "VRCSDKBaseVRCPlayerApi.PlayHapticEventInHand",

            // pedestal content is reviewed at upload, no swapping it afterwards
            "VRCSDK3ComponentsVRCAvatarPedestal.SwitchAvatar",
        };

        // types where only specific members are allowed instead of the whole type
        private static readonly Dictionary<string, HashSet<string>> AllowedMembersByType =
            new Dictionary<string, HashSet<string>>
            {
                // group page is fine, purchase prompts and listings are not
                ["VRCEconomyStore"] = new HashSet<string> { "OpenGroupPage" },
            };

        // scans one behaviour, returns the extern calls that are off the list.
        // unreadable means there is a program but it could not be inspected,
        // which gets treated as a failure by callers (fail closed)
        public static List<string> ScanBehaviour(UdonBehaviour udon, out bool unreadable)
        {
            unreadable = false;
            var flagged = new List<string>();
            if (udon == null || udon.programSource == null)
            {
                unreadable = udon != null;
                return flagged;
            }

            IUdonProgram program = null;
            try
            {
                var serialized = udon.programSource.SerializedProgramAsset;
                if (serialized != null) program = serialized.RetrieveProgram();
            }
            catch
            {
                // fall through to the unreadable flag
            }
            if (program == null)
            {
                unreadable = true;
                return flagged;
            }

            foreach (string signature in ExternSignatures(program))
            {
                if (!IsAllowed(signature))
                {
                    string friendly = FriendlyName(signature);
                    if (!flagged.Contains(friendly)) flagged.Add(friendly);
                }
            }
            return flagged;
        }

        // pulls the extern call signatures out of the compiled bytecode using
        // the sdks own disassembler, no string heap guessing
        private static IEnumerable<string> ExternSignatures(IUdonProgram program)
        {
            string[] lines;
            try
            {
                lines = new UAssemblyDisassembler().DisassembleProgram(program);
            }
            catch
            {
                yield break;
            }
            foreach (string line in lines)
            {
                int marker = line.IndexOf("EXTERN, \"", System.StringComparison.Ordinal);
                if (marker < 0) continue;
                int start = marker + 9;
                int end = line.LastIndexOf('"');
                if (end <= start) continue;
                yield return line.Substring(start, end - start);
            }
        }

        public static bool IsAllowed(string signature)
        {
            string type = TypePart(signature);
            string member = MemberPart(signature);
            if (type.Length == 0 || member.Length == 0) return false;

            if (AllowedMembersByType.TryGetValue(type, out HashSet<string> members))
            {
                return members.Contains(member);
            }
            if (BannedMembers.Contains(type + "." + member)) return false;

            if (AllowedTypes.Contains(type)) return true;
            foreach (string prefix in AllowedTypePrefixes)
            {
                if (type.StartsWith(prefix, System.StringComparison.Ordinal)) return true;
            }
            // typed arrays of allowed element types, like UnityEngineVector3Array
            if (type.EndsWith("Array", System.StringComparison.Ordinal))
            {
                return IsAllowed(type.Substring(0, type.Length - 5) + ".__Get__");
            }
            return false;
        }

        // "UnityEngineTransform.__get_position__UnityEngineVector3" -> readable
        public static string FriendlyName(string signature)
        {
            string type = TypePart(signature);
            string member = MemberPart(signature);
            if (type.Length == 0 || member.Length == 0) return signature;
            if (member.StartsWith("get_", System.StringComparison.Ordinal) ||
                member.StartsWith("set_", System.StringComparison.Ordinal))
            {
                member = member.Substring(4);
            }
            foreach (string prefix in NamespacePrefixes)
            {
                if (type.StartsWith(prefix, System.StringComparison.Ordinal) && type.Length > prefix.Length)
                {
                    type = type.Substring(prefix.Length);
                    break;
                }
            }
            return type + "." + member;
        }

        // longest first so the specific ones win
        private static readonly string[] NamespacePrefixes =
        {
            "VRCSDK3VideoComponentsAVPro", "VRCSDK3VideoComponentsBase", "VRCSDK3VideoComponents",
            "VRCSDK3ComponentsVideo", "VRCSDK3Components", "VRCSDK3StringLoading", "VRCSDK3ImageLoading",
            "VRCSDK3Rendering", "VRCSDK3Data", "VRCSDK3", "VRCSDKBase", "VRCEconomy",
            "VRCUdonCommonInterfaces", "VRCUdonCommon", "VRCUdon", "UnityEngineUI", "UnityEngine", "System",
        };

        private static string TypePart(string signature)
        {
            int split = signature.IndexOf(".__", System.StringComparison.Ordinal);
            return split <= 0 ? "" : signature.Substring(0, split);
        }

        private static string MemberPart(string signature)
        {
            int split = signature.IndexOf(".__", System.StringComparison.Ordinal);
            if (split < 0) return "";
            int start = split + 3;
            int end = signature.IndexOf("__", start, System.StringComparison.Ordinal);
            if (end < 0) end = signature.Length;
            return end <= start ? "" : signature.Substring(start, end - start);
        }
    }
}
