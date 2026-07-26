using System;
using UnityEditor;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    // boards and info walls are event furniture, not booth props, so the menu
    // and the booth check both turn them down for non staff.
    //
    // reading this and want the boards anyway? go for it, honestly. this is a
    // convenience gate, not a lock, it just stops people wiring one into their
    // booth by accident and wondering why the upload fails. if you tinker your
    // way past it and build something cool with them, fair play. the only hard
    // rule is booths we ship to the event still have to pass the checker
    internal static class AlleyStaffOnly
    {
        private static readonly Type[] Components =
        {
            typeof(AlleyDirectoryBoard),
            typeof(AlleyDirectoryKiosk),
            typeof(AlleyDirectoryEntry),
            typeof(AlleyEventSign),
            typeof(AlleySignFeed),
        };

        public static bool Allowed
        {
            get
            {
                AlleySession.LoadIfNeeded();
                return AlleySession.IsStaff;
            }
        }

        // second gate for anything that reaches a builder without the menu
        public static bool Blocked(string what)
        {
            if (Allowed) return false;
            EditorUtility.DisplayDialog(
                "Staff only",
                what + " is part of the event world, not a booth prop. Sign in with a staff account in the Legends Alley window if you need to place one.",
                "Got it");
            return true;
        }

        public static string Find(GameObject root)
        {
            foreach (Type type in Components)
            {
                if (root.GetComponentInChildren(type, true) != null) return type.Name;
            }
            return null;
        }
    }
}
