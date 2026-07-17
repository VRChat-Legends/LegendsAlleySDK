using System;

namespace LegendsNexus.Alley.Editor
{
    [Serializable]
    public class BoundsLimit
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public class EventLimits
    {
        public BoundsLimit maxBoundsMeters;
        public int maxTriangles;
        public int maxBuildSizeMB;
        public int maxVramMB;
        public int maxMaterialSlots;
        public int maxUniqueTextures;
        public int maxTextureResolution;
        public int maxAndroidTextureResolution;
        public int maxStaticMeshes;
        public int maxSkinnedMeshes;
        public int maxParticleSystems;
        public int maxTotalParticles;
        public int maxAnimators;
        public int maxAnimationClips;
        public int maxUdonScripts;
        public int maxPickups;
        public int maxAvatarPedestals;
        public int maxPortals;
        public int maxTextComponents;
        public int maxAudioSources;
        public float maxAudioRangeMeters;
        public int maxEstimatedDrawCalls;
        public int maxEstimatedSetPasses;
        public int maxNonBoxColliders;
        public bool allowUdon;
        public bool allowPickups;
        public bool allowPedestals;
        public bool allowPortals;
    }

    [Serializable]
    public class AlleyEvent
    {
        public string id;
        public string name;
        public string slug;
        public string startsAt;
        public string endsAt;
        public string uploadDeadline;
        public bool acceptingBooths;
        public string minSdkVersion;
        public EventLimits limits;
    }

    [Serializable]
    public class EventsResponse
    {
        public AlleyEvent[] events;
    }

    [Serializable]
    public class TeamMember
    {
        public string name;
        public string discordId;
    }

    [Serializable]
    public class CommunityInfo
    {
        public string id;
        public string name;
        public string slug;
        public string description;
        public string inviteUrl;
        public string logoUrl;
        public string groupId;
        public string ownerDiscordId;
        public string ownerUsername;
        public string managerDiscordId;
        public string managerUsername;
        public TeamMember[] teamMembers;
        public bool limitsBypass;
    }

    [Serializable]
    public class ExchangeResponse
    {
        public string token;
        public CommunityInfo community;
        public bool staff;
        public string role;
    }

    [Serializable]
    public class MeResponse
    {
        public CommunityInfo community;
        public bool staff;
        public string role;
    }

    [Serializable]
    public class LogoResponse
    {
        public bool ok;
        public string logoUrl;
    }

    [Serializable]
    public class CommunityProfileBody
    {
        public string description;
        public string inviteUrl;
    }

    [Serializable]
    public class ManagerBody
    {
        public string discordId;
    }

    [Serializable]
    public class OkResponse
    {
        public bool ok;
    }

    [Serializable]
    public class StaffCommunity
    {
        public string id;
        public string name;
        public string slug;
        public string description;
        public string logoUrl;
        public string ownerUsername;
        public bool active;
    }

    [Serializable]
    public class StaffCommunitiesResponse
    {
        public StaffCommunity[] communities;
    }

    [Serializable]
    public class StaffBooth
    {
        public string id;
        public string eventId;
        public string communityId;
        public string communityName;
        public string communitySlug;
        public string groupId;
        public string logoUrl;
        public int version;
        public string status;
        public long fileSize;
        public string sha256;
        public string prefabName;
        public string[] shaders;
        public string downloadUrl;
        public string uploadedAt;
    }

    [Serializable]
    public class StaffBoothsResponse
    {
        public StaffBooth[] booths;
    }

    [Serializable]
    public class UploadInitResponse
    {
        public string uploadId;
        public int chunkSize;
        public int chunkCount;
    }

    [Serializable]
    public class ChunkResponse
    {
        public int received;
        public int total;
    }

    [Serializable]
    public class AcceptedBooth
    {
        public string id;
        public int version;
        public string eventId;
        public string uploadedAt;
    }

    [Serializable]
    public class CompleteResponse
    {
        public AcceptedBooth booth;
    }

    [Serializable]
    public class ApiError
    {
        public string error;
        public string code;
        public string[] details;
    }

    [Serializable]
    public class BoothStatsPayload
    {
        public BoundsLimit boundsMeters;
        public int triangles;
        public float buildSizeMB;
        public float vramMB;
        public int materialSlots;
        public int uniqueTextures;
        public int maxTextureResolution;
        public int staticMeshes;
        public int skinnedMeshes;
        public int particleSystems;
        public int totalParticles;
        public int animators;
        public int animationClips;
        public int udonScripts;
        public int pickups;
        public int avatarPedestals;
        public int portals;
        public int textComponents;
        public int audioSources;
        public float audioRangeMeters;
        public int estimatedDrawCalls;
        public int estimatedSetPasses;
        public int nonBoxColliders;
    }

    [Serializable]
    public class BoothMetadataPayload
    {
        public string sdkVersion;
        public string eventId;
        public string communityId;
        public string prefabName;
        public string[] shaders;
        public BoothStatsPayload stats;
    }

    [Serializable]
    public class SessionFile
    {
        public string token;
        public CommunityInfo community;
        public string apiBase;
        public bool staff;
        public string role;
    }
}
