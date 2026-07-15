using System;
using System.Threading.Tasks;

namespace LegendsNexus.Alley.Editor
{
    // shared community roster so inspectors can offer pickers without running their own fetches
    internal static class StaffCommunityCache
    {
        public static StaffCommunity[] Communities { get; private set; } = Array.Empty<StaffCommunity>();
        public static event Action Changed;

        private static bool _fetching;

        public static void Store(StaffCommunity[] list)
        {
            Communities = list ?? Array.Empty<StaffCommunity>();
            Changed?.Invoke();
        }

        // fire and forget refresh for inspectors, only does anything for signed in staff
        public static async void EnsureLoaded()
        {
            AlleySession.LoadIfNeeded();
            if (_fetching || Communities.Length > 0) return;
            if (!AlleySession.IsSignedIn || !AlleySession.IsStaff) return;
            _fetching = true;
            try
            {
                StaffCommunitiesResponse response = await AlleyHttp.GetJson<StaffCommunitiesResponse>("/api/admin/communities", AlleySession.Token);
                Store(response?.communities);
            }
            catch
            {
                // roster is a nicety, the inspector falls back to a text field
            }
            finally
            {
                _fetching = false;
            }
        }
    }
}
