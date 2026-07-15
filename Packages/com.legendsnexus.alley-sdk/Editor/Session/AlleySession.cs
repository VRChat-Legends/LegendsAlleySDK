using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // holds the signed in state, persisted per user so it survives editor restarts
    internal static class AlleySession
    {
        public static CommunityInfo Community { get; private set; }
        public static bool IsStaff { get; private set; }
        public static AlleyEvent[] Events { get; private set; } = Array.Empty<AlleyEvent>();
        public static AlleyEvent SelectedEvent { get; set; }
        public static bool IsSignedIn => !string.IsNullOrEmpty(_token);

        public static event Action Changed;

        private static string _token;
        private static bool _loaded;

        private static string SessionPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "LegendsAlley", "session.json");

        public static string Token
        {
            get
            {
                LoadIfNeeded();
                return _token;
            }
        }

        public static void LoadIfNeeded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(SessionPath)) return;
                SessionFile saved = JsonUtility.FromJson<SessionFile>(File.ReadAllText(SessionPath));
                if (saved == null || string.IsNullOrEmpty(saved.token)) return;
                // sessions are tied to the server they came from
                if (!string.Equals(saved.apiBase, AlleyConfig.ApiBase, StringComparison.OrdinalIgnoreCase)) return;
                _token = saved.token;
                Community = saved.community;
                IsStaff = saved.staff;
            }
            catch
            {
                _token = null;
                Community = null;
            }
        }

        public static async Task<bool> Resume()
        {
            LoadIfNeeded();
            if (!IsSignedIn) return false;
            try
            {
                MeResponse me = await AlleyHttp.GetJson<MeResponse>("/api/auth/me", _token);
                Community = me.community != null && !string.IsNullOrEmpty(me.community.id) ? me.community : null;
                IsStaff = me.staff;
                Save();
                await RefreshEvents();
                Changed?.Invoke();
                return true;
            }
            catch (AlleyApiException e)
            {
                if (e.Status == 401 || e.Status == 403) ClearLocal();
                return false;
            }
        }

        public static async Task SignIn(CancellationToken cancel)
        {
            ExchangeResponse result = await AlleyAuth.SignIn(cancel);
            _token = result.token;
            Community = result.community != null && !string.IsNullOrEmpty(result.community.id) ? result.community : null;
            IsStaff = result.staff;
            _loaded = true;
            Save();
            await RefreshEvents();
            Changed?.Invoke();
        }

        public static async Task SignOut()
        {
            try
            {
                // staff only tokens have nothing to revoke server side
                if (IsSignedIn && Community != null) await AlleyHttp.PostJson<object>("/api/auth/revoke", null, _token);
            }
            catch
            {
                // server side revoke is best effort, local wipe matters more
            }
            ClearLocal();
        }

        public static async Task RefreshEvents()
        {
            if (!IsSignedIn) return;
            EventsResponse response = await AlleyHttp.GetJson<EventsResponse>("/api/events", _token);
            Events = response?.events ?? Array.Empty<AlleyEvent>();

            string previousId = SelectedEvent?.id;
            SelectedEvent = null;
            foreach (AlleyEvent candidate in Events)
            {
                if (candidate.id == previousId) SelectedEvent = candidate;
            }
            if (SelectedEvent == null && Events.Length > 0) SelectedEvent = Events[0];
            ApplyBoundsToGizmos();
        }

        public static void ApplyBoundsToGizmos()
        {
            if (SelectedEvent?.limits?.maxBoundsMeters == null) return;
            BoundsLimit bounds = SelectedEvent.limits.maxBoundsMeters;
            LegendsBooth.BoundsLimit = new Vector3(bounds.x, bounds.y, bounds.z);
        }

        public static void SetCommunityLogoUrl(string url)
        {
            if (Community == null || string.IsNullOrEmpty(url)) return;
            Community.logoUrl = url;
            Save();
            Changed?.Invoke();
        }

        private static void ClearLocal()
        {
            _token = null;
            Community = null;
            IsStaff = false;
            Events = Array.Empty<AlleyEvent>();
            SelectedEvent = null;
            try
            {
                if (File.Exists(SessionPath)) File.Delete(SessionPath);
            }
            catch
            {
                // locked file just means a stale session next launch, resume will reject it
            }
            Changed?.Invoke();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SessionPath));
                var file = new SessionFile { token = _token, community = Community, apiBase = AlleyConfig.ApiBase, staff = IsStaff };
                File.WriteAllText(SessionPath, JsonUtility.ToJson(file));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Legends Alley] Could not save the session file: " + e.Message);
            }
        }
    }
}
