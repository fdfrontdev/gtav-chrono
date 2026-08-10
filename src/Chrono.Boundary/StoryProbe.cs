using System;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// S22 v5 — story-progress probe (user UAT: "this is a new game, why is the
/// public image City Menace / identity burned / warrant active?").
/// <para>
/// The criminal record used to live in ONE global chrono.db — a brand-new
/// GTA game loaded the previous playthrough's record (108 crimes, 17
/// convictions, burned face, active warrant). This probe reads the story
/// progress stat SPM_MIS (missions passed) so the entry point can detect a
/// fresh start (≈0) vs an old save (20+) and reset the record.
/// </para>
/// </summary>
public sealed class StoryProbe
{
    /// <summary>
    /// Missions passed (stat SPM_MIS). Returns -1 when the stat is unavailable
    /// (e.g. loading) — callers must NOT treat -1 as "fresh game".
    /// </summary>
    public int GetMissionsPassed()
    {
        try
        {
            int value = 0;
            unsafe
            {
                // STAT_GET_INT(statName, out int) — SHVDN3 marshals the pointer
                Function.Call(Hash.STAT_GET_INT, StringHash.AtStringHash("SPM_MIS", 0), &value);
            }
            return value;
        }
        catch (Exception)
        {
            return -1;   // unavailable — not a fresh-game signal
        }
    }
}
