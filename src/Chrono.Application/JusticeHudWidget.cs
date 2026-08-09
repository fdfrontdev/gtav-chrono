using System;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// S21 v2 — persistent justice HUD widget (user UAT r15 + screenshot follow-up):
/// on-screen feedback bottom-right. Shows status, court/prison countdown,
/// identity/warrant line, AND the live message feed (notifier messages + WEBNET
/// headlines — user: "all the messages on the left should move inside the widget;
/// WEBNET should live-stream into it"). Toggleable via Settings → Show HUD.
/// </summary>
public sealed class JusticeHudWidget
{
    private readonly JusticeService _justice;
    private readonly IHudRenderer _renderer;
    private readonly JusticeConfig _config;
    private readonly HudFeedBuffer _feed;

    public bool Enabled { get; set; }   // menu toggle — Settings → Show HUD

    public JusticeHudWidget(JusticeService justice, IHudRenderer renderer, JusticeConfig config,
        HudFeedBuffer? feed = null)
    {
        _justice = justice;
        _renderer = renderer;
        _config = config;
        _feed = feed ?? new HudFeedBuffer();
        Enabled = config.HudEnabled;
    }

    /// <summary>The live feed (shared with the notifier + media service).</summary>
    public HudFeedBuffer Feed => _feed;

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
            PrisonCountdown: prison,
            Feed: _feed.Items));
    }

    private static string FormatClock(double seconds)
    {
        int s = Math.Max(0, (int)Math.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
