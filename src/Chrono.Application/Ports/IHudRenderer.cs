using System.Collections.Generic;
using Chrono.Domain;

namespace Chrono.Application.Ports;

/// <summary>
/// S21 v3 — persistent HUD widget, segmented Material card (user UAT: "make it
/// readable, a proper widget with segment/partition — beautiful, practical,
/// usable"). Game-neutral snapshot; the boundary renders it. Feed carries the
/// live message tail (notifier + WEBNET).
/// </summary>
public sealed record JusticeHudState(
    bool Visible,             // widget on (config + menu toggle)
    int Stars,                // current wanted level
    string StatusLine,        // e.g. "WANTED 3*" / "ON BAIL" / "PAROLE 2D" / "FREE"
    string CountdownLine,     // "COURT IN 0:34" / "PRISON DAY 3/14 · 1:12 LEFT" / ""
    string SecondLine,        // e.g. "WARRANT ACTIVE" / "CLEAN" / ""
    bool CourtCountdown,      // show the countdown in amber (urgent)
    bool PrisonCountdown,     // show the countdown in blue
    IReadOnlyList<HudFeedItem>? Feed = null,      // S21 v2: live message feed (oldest first)
    JusticeStatusKind Kind = JusticeStatusKind.Free,   // S21 v3: status color coding
    float Progress = 0f);     // S21 v3: countdown progress 0..1 (court/prison bar)

/// <summary>Widget status color coding (S21 v3 — Material semantic colors).</summary>
public enum JusticeStatusKind
{
    Free,       // green — clean
    Wanted,     // amber — active chase
    Captured,   // red — in custody
    Prison,     // blue — serving time
    OnBail,     // violet — conditional release
    Manhunt,    // S21 v3: crimson — escaped prisoner, the whole state is looking
}

/// <summary>Renders the persistent justice widget (implemented by the boundary).</summary>
public interface IHudRenderer
{
    void DrawJusticeHud(JusticeHudState state);
}
