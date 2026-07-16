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
            if (Changed == null) return;
            // dead inspectors linger on this event with disposed serialized objects,
            // one of them throwing must not stop the rest (or the caller) from updating
            foreach (Delegate handler in Changed.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch
                {
                    Changed -= (Action)handler;
                }
            }
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
