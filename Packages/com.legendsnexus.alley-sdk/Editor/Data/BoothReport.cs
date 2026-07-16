using System.Collections.Generic;
using UnityEngine;

namespace LegendsNexus.Alley.Editor
{
    public enum CheckSeverity
    {
        Pass,
        Warn,
        Fail,
    }

    public class CheckRow
    {
        public string Label;
        public string Value;
        public string Limit;
        public CheckSeverity Severity;
        public string Hint;
        // true when the value is actually past the limit, not just close to it
        public bool OverLimit;
        // scene objects or assets responsible for the number, for the select button
        public Object[] Offenders;
    }

    public class BoothReport
    {
        public BoothStatsPayload Stats = new BoothStatsPayload();
        public readonly List<CheckRow> Rows = new List<CheckRow>();
        public readonly List<string> Blockers = new List<string>();
        // shader names the packaged booth will actually ship with
        public readonly List<string> ShaderNames = new List<string>();

        public bool CanUpload
        {
            get
            {
                if (Blockers.Count > 0) return false;
                foreach (CheckRow row in Rows)
                {
                    if (row.Severity == CheckSeverity.Fail) return false;
                }
                return true;
            }
        }
    }
}
