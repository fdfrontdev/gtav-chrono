using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly PowerEnergyService? _energy;   // v0.10: combat energy bar
    private readonly NeedsService? _needs;          // v0.10: survivor need bars

    public bool Enabled { get; set; }   // menu toggle — Settings → Show HUD

    /// <summary>S22 v2: mod master toggle OFF — the widget draws nothing.</summary>
    public bool ModOff { get; set; }

    /// <summary>S22 v2: justice toggle OFF — the widget shows a suspended status.</summary>
    public bool JusticeOff { get; set; }

    public JusticeHudWidget(JusticeService justice, IHudRenderer renderer, JusticeConfig config,
        HudFeedBuffer? feed = null, PowerEnergyService? energy = null, NeedsService? needs = null)
    {
        _justice = justice;
        _renderer = renderer;
        _config = config;
        _feed = feed ?? new HudFeedBuffer();
        _energy = energy;
        _needs = needs;
        Enabled = config.HudEnabled;
    }

    /// <summary>The live feed (shared with the notifier + media service).</summary>
    public HudFeedBuffer Feed => _feed;

    /// <summary>Per-tick: rebuild + draw the widget (cheap — a few text draws).</summary>
    public void Tick()
    {
        // S22 v4 (user UAT: "the toggle on/off didn't work"): the toggles
        // short-circuit FIRST — Mod OFF draws nothing, Justice OFF draws the
        // suspended card — without touching the (possibly suspended) service.
        if (ModOff) return;
        if (JusticeOff)
        {
            _renderer.DrawJusticeHud(new JusticeHudState(
                Visible: Enabled,
                Stars: 0,
                StatusLine: "JUSTICE OFF — LAWS SUSPENDED",
                CountdownLine: "Toggle ON in Settings",
                SecondLine: "",
                CourtCountdown: false,
                PrisonCountdown: false,
                Kind: JusticeStatusKind.Free,
                Progress: 0f,
                Feed: _feed.Snapshot()));
            return;
        }

        var j = _justice;
        var state = j.State;

        // S22 (user UAT: "mod makes a mess on main story events"): during a
        // scripted mission the widget says so — the pipeline is frozen.
        if (j.MissionStandby)
        {
            _renderer.DrawJusticeHud(new JusticeHudState(
                Visible: Enabled,
                Stars: 0,
                StatusLine: "MISSION — JUSTICE ON STANDBY",
                CountdownLine: "STORY MODE: THE LAW WAITS",
                SecondLine: "",
                CourtCountdown: false,
                PrisonCountdown: false,
                Kind: JusticeStatusKind.Free,
                Progress: 0f,
                Feed: _feed.Snapshot()));
            return;
        }

        int stars = j.CurrentStars;
        string status;
        switch (state)
        {
            case JusticeState.Captured: status = "IN CUSTODY — COURT AWAITS"; break;
            case JusticeState.Prison:   status = $"PRISON — DAY {j.ServedDays + 1}/{j.SentenceDays}"; break;
            case JusticeState.Wanted:   status = stars > 0 ? $"WANTED {stars}★" : "WANTED"; break;
            default:                    status = "FREE"; break;
        }

        string countdown = "";
        float progress = 0f;
        bool court = false, prison = false;
        if (state == JusticeState.Captured)
        {
            // S22 v8: during the police escort ride the court clock is FROZEN
            // (verdict waits for arrival) — show the transport status instead
            // of a stuck "COURT IN 0:45" (user UAT: "the court timer seems stuck").
            if (j.IsEscortRiding)
            {
                countdown = $"TRANSPORT — BOLINGBROKE ({_config.InteractKey} TO SKIP)";
                progress = 1f;   // bar full = the ride is live; skip hint on the line
            }
            else
            {
                double s = j.TrialSecondsLeft;
                countdown = $"COURT IN {FormatClock(s)}";
                court = true;
                progress = (float)(1 - s / Math.Max(1.0, _config.TrialDelaySeconds));
            }
        }
        else if (state == JusticeState.Prison)
        {
            // S21 v3 (user UAT: "how do I escape?"): during yard time the
            // countdown line becomes the escape prompt — the player must SEE it.
            if (j.IsYardPhase)
            {
                countdown = "YARD OPEN — PRESS G TO ESCAPE";
                progress = 1f;   // bar full = the escape window is live
            }
            else
            {
                countdown = $"NEXT DAY IN {FormatClock(j.PrisonDaySecondsLeft)}";
                progress = (float)j.PrisonDayProgress;
            }
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

        // S21 v3: status kind drives the card's color coding
        var kind = state switch
        {
            JusticeState.Prison   => JusticeStatusKind.Prison,
            JusticeState.Captured => JusticeStatusKind.Captured,
            JusticeState.Wanted   => JusticeStatusKind.Wanted,
            _ when j.IsOnBail     => JusticeStatusKind.OnBail,
            _ when j.IsManhunt    => JusticeStatusKind.Manhunt,   // S21 v3: prison-break heat
            _                     => JusticeStatusKind.Free,
        };

        // S21 v3 (prison-break vibe, user UAT): an active manhunt overrides the
        // status line — "MANHUNT — PRISON BREAK" reads like a TV-series manhunt,
        // with the heat countdown on the countdown line.
        if (j.IsManhunt)
        {
            status = $"MANHUNT — PRISON BREAK {stars}★";
            countdown = $"HEAT UNTIL DAY {j.ManhuntUntilDay}";
            progress = 1f;
            prison = true;   // reuse the countdown bar + urgent color for the heat timer
        }

        string second = "";
        if (j.Warrant.IsActive && j.Identity.IsBurned)
            second = "WARRANT ACTIVE — FACE ON FILE";
        else if (j.Identity.IsBurned)
            second = "FACE ON FILE (BURNED)";
        else
            second = "CLEAN IDENTITY";

        // S22 v8 (user: "HUD can be improved — check dashboard design"): KPI
        // tiles — dashboard BANs. Up to 3: stars, day/heat, and the state's
        // third metric. Big numerals in enclosed tiles (Big Book of Dashboards).
        var kpis = new List<(string Label, string Value)>
        {
            // S23 (user UAT 2026-08-13): in custody/prison the street chase is
            // over — the WANTED tile shows a dash, NEVER a star (the game's
            // level is forced to 0 anyway; the tile must not contradict it).
            ("WANTED", state is JusticeState.Captured or JusticeState.Prison || stars == 0 ? "—" : $"{stars}★"),
        };
        if (state == JusticeState.Prison)
            kpis.Add(("DAY", $"{j.ServedDays + 1}/{j.SentenceDays}"));
        else if (j.IsManhunt)
            kpis.Add(("HEAT", $"DAY {j.ManhuntUntilDay}"));
        else if (state == JusticeState.Captured)
            kpis.Add(("COURT", FormatClock(j.TrialSecondsLeft)));
        else if (j.IsOnBail)
            kpis.Add(("BAIL", "ON"));
        else if (j.ParoleDaysLeft > 0)
            kpis.Add(("PAROLE", $"{j.ParoleDaysLeft}D"));
        else
            kpis.Add(("NOTORIETY", $"{j.Notoriety}"));
        if (j.Warrant.IsActive)
            kpis.Add(("WARRANT", "ACTIVE"));
        else if (j.Identity.IsBurned)
            kpis.Add(("FACE", "ON FILE"));
        else
            kpis.Add(("FAME", $"{j.Fame}"));

        // S22 v8 r3 (user: "feed seems too quiet"): priority tiers — viral and
        // WEBNET headlines float to the TOP (a manhunt must never be buried
        // under a parking-dispute blotter), ambient city lines sink to the
        // bottom. 4 rows (duplicates killed, ambient added).
        var tiered = _feed.Items
            .OrderByDescending(i => i.Kind == FeedKind.Viral ? 2 : i.Kind == FeedKind.Webnet ? 1 : 0)
            .Take(HudLayoutEngine.MaxFeedRows)
            .ToList();

        _renderer.DrawJusticeHud(new JusticeHudState(
            Visible: Enabled,
            Stars: stars,
            StatusLine: status,
            CountdownLine: countdown,
            SecondLine: second,
            CourtCountdown: court,
            PrisonCountdown: prison,
            Feed: tiered,
            Kind: kind,
            Progress: progress,
            Kpis: kpis,
            Energy: _energy?.Current ?? 0,          // v0.10: combat energy (FR-B3)
            EnergyMax: _energy?.Max ?? 0,
            Needs: _needs?.Enabled == true ? NeedsBars() : null));   // v0.10: survivor bars (FR-C14)
    }

    /// <summary>v0.10 — the four survivor bars (label, value, tier).</summary>
    private IReadOnlyList<NeedBar> NeedsBars()
    {
        var s = _needs!.State;
        return new[]
        {
            new NeedBar("HUN", s.Hunger, s.Tier(NeedKind.Hunger)),
            new NeedBar("THR", s.Thirst, s.Tier(NeedKind.Thirst)),
            new NeedBar("ENG", s.Energy, s.Tier(NeedKind.Energy)),
            new NeedBar("MOD", s.Mood, s.Tier(NeedKind.Mood)),
        };
    }

    private static string FormatClock(double seconds)
    {
        int s = Math.Max(0, (int)Math.Ceiling(seconds));
        return $"{s / 60}:{s % 60:00}";
    }
}
