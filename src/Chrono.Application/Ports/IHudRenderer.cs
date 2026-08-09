using Chrono.Domain;

namespace Chrono.Application.Ports;

/// <summary>
/// S21 — persistent HUD widget (user UAT r15: "lack of feedback on the screen —
/// I expect a custom widget... how many days before I have to go to court").
/// Game-neutral snapshot; the boundary renders it as a Material card.
/// </summary>
public sealed record JusticeHudState(
    bool Visible,             // widget on (config + menu toggle)
    int Stars,                // current wanted level
    string StatusLine,        // e.g. "WANTED 3*" / "ON BAIL" / "PAROLE 2D" / "FREE"
    string CountdownLine,     // "COURT IN 0:34" / "PRISON DAY 3/14 · 1:12 LEFT" / ""
    string SecondLine,        // e.g. "WARRANT ACTIVE" / "CLEAN" / ""
    bool CourtCountdown,      // show the countdown in amber (urgent)
    bool PrisonCountdown);    // show the countdown in blue

/// <summary>Renders the persistent justice widget (implemented by the boundary).</summary>
public interface IHudRenderer
{
    void DrawJusticeHud(JusticeHudState state);
}
