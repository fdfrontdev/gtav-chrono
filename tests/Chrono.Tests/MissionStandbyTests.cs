using System;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S22 — mission standby (user UAT: "this mod makes a mess on main story
/// events"): while a scripted mission is active the justice pipeline must
/// FREEZE — no star-driving, no arrests, no cutscenes, no prison ticking.
/// </summary>
public class MissionStandbyTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeCrimeProbe probe, FakeRecordStore store) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var probe = new FakeCrimeProbe();
        var service = new JusticeService(wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            new FakeNotifier(), new FakeLog(),
            new JusticeConfig { TrialDelaySeconds = 60, PrisonDayRealSeconds = 30 },
            new FakeClock(), input: new FakeInput(), crimeProbe: probe);
        return (service, wanted, player, probe, store);
    }

    [Fact]
    public void Standby_StarIncrease_DoesNotRecordCrime()   // the story-mission mess: no auto-charges
    {
        var (service, wanted, _, _, store) = Build();
        service.MissionStandby = true;

        wanted.CurrentStars = 5;   // mission scripted the stars — must NOT become a crime
        service.Tick();

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Empty(store.Record.Events);   // nothing recorded during the mission
    }

    [Fact]
    public void Standby_CopNearby_NoArrest()
    {
        var (service, wanted, player, probe, _) = Build();
        service.MissionStandby = true;
        wanted.CurrentStars = 5;
        service.Tick();
        probe.NearestPoliceDistance = 2f;   // a cop is right there — must NOT cuff

        service.Tick();

        Assert.Equal(JusticeState.Free, service.State);
        Assert.False(player.ClearAnimCount > 0, "no capture anim during a mission");
    }

    [Fact]
    public void Standby_PrisonTime_DoesNotAdvance()
    {
        var (service, wanted, _, probe, _) = Build();
        // serve into prison first
        wanted.CurrentStars = 5;
        service.Tick();
        probe.NearestPoliceDistance = 2f;
        service.Tick();                  // captured
        service.AdvanceTrialTime(60.0);
        service.Tick();                  // verdict → prison
        service.Tick();                  // intake done → confinement
        service.AdvancePrisonTime(30.0); // serve 1 day normally (day = 30s)
        service.Tick();
        int before = service.ServedDays; // 1

        service.MissionStandby = true;   // mission starts mid-sentence
        service.Tick();                  // standby: PrisonTick is gated — no serving
        service.Tick();                  // real time passes but the calendar is frozen

        Assert.Equal(before, service.ServedDays);
        Assert.Equal(JusticeState.Prison, service.State);
    }

    [Fact]
    public void StandbyOff_ResumesNormalFlow()
    {
        var (service, wanted, _, probe, _) = Build();
        service.MissionStandby = true;
        wanted.CurrentStars = 5;
        service.Tick();                  // suppressed

        service.MissionStandby = false;  // mission over
        service.Tick();                  // the SAME star level now records
        probe.NearestPoliceDistance = 2f;
        service.Tick();

        Assert.Equal(JusticeState.Captured, service.State);
    }

    [Fact]
    public void Widget_Standby_ShowsStandbyStatus()   // the widget must SAY the law is frozen
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var config = new JusticeConfig { TrialDelaySeconds = 60 };
        var service = new JusticeService(wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            new FakeNotifier(), new FakeLog(), config, new FakeClock(),
            input: new FakeInput(), crimeProbe: new FakeCrimeProbe());
        var renderer = new FakeHudRenderer();
        var widget = new JusticeHudWidget(service, renderer, config);

        service.MissionStandby = true;
        widget.Tick();

        Assert.Contains("MISSION — JUSTICE ON STANDBY", renderer.Last!.StatusLine);
    }

    [Fact]
    public void Cutscene_Abort_ClearsState()   // mission takeover must not leave a frozen camera
    {
        var renderer = new FakeCutsceneRenderer();
        var player = new FakePlayer { Position = new System.Numerics.Vector3(100, 200, 30) };
        var notifier = new FakeNotifier();
        var service = new JusticeCutsceneService(renderer, player, new FakeLog(), notifier);

        service.Play(CutsceneKind.Arrest);
        service.Tick(0);                 // phase 1 entered — camera active
        Assert.True(service.IsActive);

        service.Abort();                 // mission takeover

        Assert.False(service.IsActive);
        Assert.Equal(1, renderer.EndCount);
    }

    // ── S22 v2 (user UAT: toggle mod / superpowers / justice on-off) ──

    [Fact]
    public void PowersDisabled_HotkeysDoNothing()
    {
        var service = BuildMenu(out var input, out var notifier, out _);
        input.TimeStopHotkey = true;
        service.Tick(0);                 // powers ON — time stop activates
        Assert.True(service.IsTimeStopActive);

        service.SetPowersEnabled(false); // powers OFF → force-off + freeze hotkeys
        Assert.False(service.IsTimeStopActive, "disabling powers force-offs active powers");

        input.TimeStopHotkey = false;
        service.Tick(16);
        input.TimeStopHotkey = true;
        service.Tick(32);                // hotkey pressed again — must NOT re-activate

        Assert.False(service.IsTimeStopActive, "hotkeys freeze when powers are disabled");
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("TIME STOP ON"));
    }

    [Fact]
    public void PowersDisabled_ActivePower_IsForceOff()
    {
        var service = BuildMenu(out var input, out _, out _);
        input.TimeStopHotkey = true;
        service.Tick(0);
        Assert.True(service.IsTimeStopActive);

        service.SetPowersEnabled(false); // powers OFF while active
        service.Tick(16);

        Assert.False(service.IsTimeStopActive, "active powers must force-off when disabled");
    }

    [Fact]
    public void ModDisabled_EverythingFreezes_MenuStillOpens()
    {
        var service = BuildMenu(out var input, out _, out _);
        input.MenuKeyPressed = true;
        service.Tick(0);                 // menu opens even with mod OFF
        Assert.True(service.IsMenuOpen);

        service.SetModEnabled(false);
        service.Tick(16);
        Assert.True(service.IsMenuOpen, "the menu stays open with mod OFF — it's the only way back in");
        input.MenuKeyPressed = false;
        service.Tick(32);                // no edge
        input.MenuKeyPressed = true;
        service.Tick(48);                // fresh edge → close the menu
        Assert.False(service.IsMenuOpen);

        input.TimeStopHotkey = true;
        service.Tick(64);                // hotkey — frozen
        Assert.False(service.IsTimeStopActive);
    }

    private static PowerMenuService BuildMenu(out FakeInput input, out FakeNotifier notifier, out FakePlayer player)
    {
        input = new FakeInput();
        notifier = new FakeNotifier();
        player = new FakePlayer { IsVisible = true, Money = 100000 };
        var config = new ChronoConfig();
        var menu = new MenuFramework(new FakeRenderer());
        var timeStop = new TimeStopService(new FakeRepository(), new FakeFreezer(), new FakeClock(), player,
            notifier, new FakeLog(), config.TimeStop);
        var teleport = new TeleportService(player, new FakeProbe(), notifier, new FakeLog(), config.Dash, config.Teleport);
        var vfx = new VfxService(new FakeVfx(), new FakeLog(), config.Visual);
        var service = new PowerMenuService(menu, timeStop, teleport, vfx, input, player,
            notifier, new FakeLog(), config, new FakeConfigStore());
        service.BuildMenu();
        return service;
    }

    private sealed class FakeHudRenderer : IHudRenderer
    {
        public JusticeHudState? Last { get; private set; }
        public void DrawJusticeHud(JusticeHudState state) => Last = state;
    }
}
