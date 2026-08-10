using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S21 — persistent justice HUD widget (user UAT r15 #5): on-screen feedback for
/// court countdown, prison days, wanted stars, bail/parole, warrant. Game-neutral
/// snapshot → boundary renderer.
/// </summary>
public class JusticeHudWidgetTests
{
    private sealed class FakeHudRenderer : IHudRenderer
    {
        public JusticeHudState? Last { get; private set; }
        public void DrawJusticeHud(JusticeHudState state) => Last = state;
    }

    private static (JusticeHudWidget widget, FakeHudRenderer renderer, JusticeService justice, FakeWantedMonitor wanted, FakeCrimeProbe probe, FakeNotifier notifier) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var clock = new FakeClock();
        var probe = new FakeCrimeProbe();
        var config = new JusticeConfig { TrialDelaySeconds = 60, PrisonDayRealSeconds = 30 };
        var notifier = new FakeNotifier();
        var justice = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(), config, clock,
            input: new FakeInput(), crimeProbe: probe);
        var renderer = new FakeHudRenderer();
        var widget = new JusticeHudWidget(justice, renderer, config);
        return (widget, renderer, justice, wanted, probe, notifier);
    }

    [Fact]
    public void FreeState_ShowsCleanIdentity()
    {
        var (widget, renderer, _, _, _, _) = Build();
        widget.Tick();

        Assert.NotNull(renderer.Last);
        Assert.True(renderer.Last.Visible);
        Assert.Equal("FREE", renderer.Last.StatusLine);
        Assert.Equal("CLEAN IDENTITY", renderer.Last.SecondLine);
        Assert.Equal(0, renderer.Last.Stars);
    }

    [Fact]
    public void WantedState_ShowsStars()
    {
        var (widget, renderer, justice, wanted, _, _) = Build();
        wanted.CurrentStars = 3;
        justice.Tick();                  // enter Wanted state
        widget.Tick();

        Assert.Equal("WANTED 3*", renderer.Last!.StatusLine);
        Assert.Equal(3, renderer.Last.Stars);
    }

    [Fact]
    public void CapturedState_ShowsCourtCountdown()
    {
        var (widget, renderer, justice, wanted, probe, _) = Build();
        wanted.CurrentStars = 4;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();                  // cuffed
        widget.Tick();

        Assert.Equal("IN CUSTODY — COURT AWAITS", renderer.Last!.StatusLine);
        Assert.Contains("COURT IN", renderer.Last.CountdownLine);
        Assert.True(renderer.Last.CourtCountdown);
        Assert.True(justice.TrialSecondsLeft > 0);
    }

    [Fact]
    public void PrisonState_ShowsDayCounter()
    {
        var (widget, renderer, justice, wanted, probe, _) = Build();
        wanted.CurrentStars = 5;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();
        justice.AdvanceTrialTime(60.0);
        justice.Tick();                  // verdict → prison
        justice.Tick();                  // intake done → confinement starts
        widget.Tick();

        Assert.StartsWith("PRISON — DAY 1/", renderer.Last!.StatusLine);
        Assert.Contains("NEXT DAY IN", renderer.Last.CountdownLine);
        Assert.True(renderer.Last.PrisonCountdown);
    }

    [Fact]
    public void YardPhase_ShowsEscapePrompt()   // S21 v3: "how do I escape?" — the widget must SAY it
    {
        var (widget, renderer, justice, wanted, probe, _) = Build();
        wanted.CurrentStars = 5;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();
        justice.AdvanceTrialTime(60.0);
        justice.Tick();                  // verdict → prison
        justice.Tick();                  // intake done → confinement starts

        // serve most of a day → yard opens (10s before day end; day = 30s in tests)
        justice.AdvancePrisonTime(20.0);
        justice.Tick();                  // yard phase notified
        widget.Tick();

        Assert.True(justice.IsYardPhase);
        Assert.Contains("YARD OPEN — PRESS G TO ESCAPE", renderer.Last!.CountdownLine);
        Assert.True(renderer.Last.PrisonCountdown);
    }

    [Fact]
    public void Manhunt_ShowsPrisonBreakStatus()   // S21 v3: prison-break vibe (user UAT)
    {
        var (widget, renderer, justice, wanted, probe, _) = Build();
        wanted.CurrentStars = 5;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();
        justice.AdvanceTrialTime(60.0);
        justice.Tick();                  // verdict → prison
        justice.Tick();                  // confinement starts

        // escape via powers (yard open → choose)
        justice.AdvancePrisonTime(20.0);
        justice.Tick();                  // yard phase
        justice.TryOpenEscapeChoice();
        justice.ChooseEscape(EscapeKind.Dash);

        Assert.True(justice.IsManhunt);
        widget.Tick();

        Assert.Contains("MANHUNT — PRISON BREAK", renderer.Last!.StatusLine);
        Assert.Contains("HEAT UNTIL DAY", renderer.Last.CountdownLine);
        Assert.Equal(JusticeStatusKind.Manhunt, renderer.Last.Kind);
        Assert.True(renderer.Last.PrisonCountdown);   // countdown line active (heat timer)
    }

    [Fact]
    public void Recapture_EndsManhunt_ShowsRecaptured()   // S21 v3
    {
        var (widget, renderer, justice, wanted, probe, notifier) = Build();
        wanted.CurrentStars = 5;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();
        justice.AdvanceTrialTime(60.0);
        justice.Tick();
        justice.Tick();                  // prison

        justice.AdvancePrisonTime(20.0);
        justice.Tick();
        justice.TryOpenEscapeChoice();
        justice.ChooseEscape(EscapeKind.Dash);   // escape → manhunt
        Assert.True(justice.IsManhunt);

        // caught during the manhunt → recapture
        wanted.CurrentStars = 4;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();                  // physical capture

        Assert.Equal(JusticeState.Captured, justice.State);
        Assert.False(justice.IsManhunt, "recapture must END the manhunt");
        widget.Tick();   // renderer.Last reflects the recaptured state
        Assert.Contains(notifier.Messages, m => m.Contains("RECAPTURED"));
    }

    [Fact]
    public void ToggleOff_HidesWidget()
    {
        var (widget, renderer, _, _, _, _) = Build();
        widget.Enabled = false;
        widget.Tick();

        Assert.NotNull(renderer.Last);
        Assert.False(renderer.Last.Visible);
    }

    // S22 v7 (user UAT: "I'm in the manhunt, but HUD still shows WANTED —
    // the glitch happens on the 2ND ESCAPEE"): escape #1 → recapture → re-trial
    // → re-prison → escape #2 must re-arm the manhunt. The widget must show
    // MANHUNT — PRISON BREAK, never fall back to generic WANTED.
    [Fact]
    public void SecondEscapee_ManhuntReArms_WidgetShowsManhunt()
    {
        var (widget, renderer, justice, wanted, probe, _) = Build();

        // ── escape #1 ──
        wanted.CurrentStars = 5;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();
        justice.AdvanceTrialTime(60.0);
        justice.Tick();
        justice.Tick();                  // prison
        justice.AdvancePrisonTime(20.0);
        justice.Tick();                  // yard
        justice.TryOpenEscapeChoice();
        justice.ChooseEscape(EscapeKind.Dash);
        Assert.True(justice.IsManhunt, "escape #1 arms the manhunt");

        // ── recapture #1 (ends the manhunt) ──
        wanted.CurrentStars = 4;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();
        Assert.Equal(JusticeState.Captured, justice.State);
        Assert.False(justice.IsManhunt);

        // ── re-trial → re-prison (the court adds the escape charge) ──
        justice.AdvanceTrialTime(60.0);
        justice.Tick();                  // verdict #2 → prison again
        justice.Tick();                  // confinement #2 starts
        Assert.Equal(JusticeState.Prison, justice.State);

        // ── escape #2 — THE GLITCH: manhunt must re-arm ──
        justice.AdvancePrisonTime(20.0);
        justice.Tick();                  // yard phase #2
        justice.TryOpenEscapeChoice();
        justice.ChooseEscape(EscapeKind.Dash);

        Assert.True(justice.IsManhunt, "escape #2 must RE-ARM the manhunt (2nd escapee bug)");
        widget.Tick();
        Assert.Contains("MANHUNT — PRISON BREAK", renderer.Last!.StatusLine);
        Assert.Equal(JusticeStatusKind.Manhunt, renderer.Last.Kind);
    }

    // S22 v7: the manhunt must SURVIVE a game-day rollover while the player is
    // still actively wanted (5★ chase) — heat fades only after the stars clear.
    [Fact]
    public void Manhunt_SurvivesDayRollover_WhileStillWanted()
    {
        var (widget, renderer, justice, wanted, probe, _) = Build();

        wanted.CurrentStars = 5;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();
        justice.AdvanceTrialTime(60.0);
        justice.Tick();
        justice.Tick();                  // prison
        justice.AdvancePrisonTime(20.0);
        justice.Tick();                  // yard
        justice.TryOpenEscapeChoice();
        justice.ChooseEscape(EscapeKind.Dash);
        Assert.True(justice.IsManhunt);

        // the game day rolls while the chase is STILL live (5★)
        FakeClockAdvanceDay(justice);
        justice.Tick();                  // UpdateManhunt runs

        Assert.True(justice.IsManhunt, "manhunt survives the day roll while still wanted");
        widget.Tick();
        Assert.Contains("MANHUNT — PRISON BREAK", renderer.Last!.StatusLine);

        // now the player loses the cops (stars → 0) → day rolls → heat fades
        wanted.CurrentStars = 0;
        FakeClockAdvanceDay(justice);
        justice.Tick();
        Assert.False(justice.IsManhunt, "heat fades once off the street + day rolls");
    }

    private static void FakeClockAdvanceDay(JusticeService justice)
    {
        var clock = (FakeClock)justice.GetType().GetField("_clock",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(justice)!;
        clock.CurrentGameDay++;
    }
}
