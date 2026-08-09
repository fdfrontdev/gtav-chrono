using System;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// S21 — persistent justice HUD widget (user UAT r15: "there's a lack of feedback
/// on the screen. I expect a custom widget for our mods — e.g. how many days
/// before I have to go to court"). Builds the game-neutral snapshot from the
/// JusticeService probes and pushes it to the boundary renderer every tick.
/// Toggleable via config + the menu (Settings → Show HUD).
/// </summary>
public sealed class JusticeHudWidget
{
    private readonly JusticeService _justice;
    private readonly IHudRenderer _renderer;
    private readonly JusticeConfig _config;

    public bool Enabled { get; set; }   // menu toggle — Settings → Show HUD

    public JusticeHudWidget(JusticeService justice, IHudRenderer renderer, JusticeConfig config)
    {
        _justice = justice;
        _renderer = renderer;
        _config = config;
        Enabled = config.HudEnabled;
    }

    /// <summary>Per-tick: rebuild + draw the widget (cheap — a few text draws).</summary>
    public void Tick()
    {
        var j = _justice;
        var state = j.State;

        int stars = j.CurrentStars;
        string status;
        switch (state)
        {
            case JusticeState.Captured: status = "IN CUSTODY — COURT AWAITS"; break;
            case JusticeState.Prison:   status = $"PRISON — DAY {j.ServedDays + 1}/{j.SentenceDays}"; break;
            case JusticeState.Wanted:   status = stars > 0 ? $"WANTED {stars}*" : "WANTED"; break;
            default:                    status = "FREE"; break;
        }

        string countdown = "";
        bool court = false, prison = false;
        if (state == JusticeState.Captured)
        {
            double s = j.TrialSecondsLeft;
            countdown = $"COURT IN {FormatClock(s)}";
            court = true;
        }
        else if (state == JusticeState.Prison)
        {
            countdown = $"NEXT DAY IN {FormatClock(j.PrisonDaySecondsLeft)}";
            prison = true;
        }
        else if (j.IsOnBail)
        {
            countdown = "ON BAIL — CHARGES PENDING";
        }
        else if (j.ParoleDaysLeft > 0)
        {
            countdown = $"PAROLE {j.ParoleDaysLeft}D LEFT";
        }

        string second = "";
        if (j.Warrant.IsActive && j.Identity.IsBurned)
            second = "WARRANT ACTIVE — FACE ON FILE";
        else if (j.Identity.IsBurned)
            second = "FACE ON FILE (BURNED)";
        else
            second = "CLEAN IDENTITY";

        _renderer.DrawJusticeHud(new JusticeHudState(
            Visible: Enabled,
            Stars: stars,
            StatusLine: status,
            CountdownLine: countdown,
            SecondLine: second,
            CourtCountdown: court,
            PrisonCountdown: prison));
    }

    private static string FormatClock(double seconds)
    {
        int s = Math.Max(0, (int)Math.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
