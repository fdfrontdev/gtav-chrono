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

    private static (JusticeHudWidget widget, FakeHudRenderer renderer, JusticeService justice, FakeWantedMonitor wanted, FakeCrimeProbe probe) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var clock = new FakeClock();
        var probe = new FakeCrimeProbe();
        var config = new JusticeConfig { TrialDelaySeconds = 60, PrisonDayRealSeconds = 30 };
        var justice = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            new FakeNotifier(), new FakeLog(), config, clock,
            input: new FakeInput(), crimeProbe: probe);
        var renderer = new FakeHudRenderer();
        var widget = new JusticeHudWidget(justice, renderer, config);
        return (widget, renderer, justice, wanted, probe);
    }

    [Fact]
    public void FreeState_ShowsCleanIdentity()
    {
        var (widget, renderer, _, _, _) = Build();
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
        var (widget, renderer, justice, wanted, _) = Build();
        wanted.CurrentStars = 3;
        justice.Tick();                  // enter Wanted state
        widget.Tick();

        Assert.Equal("WANTED 3*", renderer.Last!.StatusLine);
        Assert.Equal(3, renderer.Last.Stars);
    }

    [Fact]
    public void CapturedState_ShowsCourtCountdown()
    {
        var (widget, renderer, justice, wanted, probe) = Build();
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
        var (widget, renderer, justice, wanted, probe) = Build();
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
    public void ToggleOff_HidesWidget()
    {
        var (widget, renderer, _, _, _) = Build();
        widget.Enabled = false;
        widget.Tick();

        Assert.NotNull(renderer.Last);
        Assert.False(renderer.Last.Visible);
    }
}
