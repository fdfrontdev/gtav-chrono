using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>
/// Regression tests for the F9 menu toggle (user-reported: "menu not open yet").
/// The toggle MUST use a key EDGE, not level polling — otherwise the menu flashes
/// open and instantly closes while the key is still held.
/// </summary>
public class PowerMenuServiceTests
{
    private static PowerMenuService BuildService(out FakeInput input, out FakeNotifier notifier, out FakeRepository repo, out FakePlayer player)
    {
        repo = new FakeRepository();
        input = new FakeInput();
        notifier = new FakeNotifier();
        var config = new ChronoConfig();
        player = new FakePlayer();

        var timeStop = new TimeStopService(
            repo, new FakeFreezer(), new FakeClock(), player,
            notifier, new FakeLog(), config.TimeStop);
        var teleport = new TeleportService(
            player, new FakeProbe(), notifier, new FakeLog(), config.Dash, config.Teleport);
        var vfx = new VfxService(new FakeVfx(), new FakeLog(), config.Visual);
        var menu = new MenuFramework(new FakeRenderer());

        var service = new PowerMenuService(
            menu, timeStop, teleport, vfx, input, player,
            notifier, new FakeLog(), config, new FakeConfigStore());
        service.BuildMenu();
        return service;
    }

    [Fact]
    public void F9Press_OpensMenu()
    {
        var service = BuildService(out var input, out _, out _, out _);

        input.MenuKeyPressed = true;
        service.Tick(0);   // edge detected

        Assert.True(service.IsMenuOpen);
    }

    [Fact]
    public void MenuOpen_FreezesCharacter_S8()
    {
        // S8: opening the cheat menu must freeze the character so WASD navigation
        // doesn't fight movement
        var service = BuildService(out var input, out _, out _, out var player);

        input.MenuKeyPressed = true;
        service.Tick(0);   // open → control off

        Assert.True(service.IsMenuOpen);
        Assert.False(player.ControlCalls.LastOrDefault());

        input.MenuKeyPressed = false;
        service.Tick(16);
        input.MenuKeyPressed = true;
        service.Tick(32);  // close → control on

        Assert.False(service.IsMenuOpen);
        Assert.True(player.ControlCalls.LastOrDefault());
    }

    [Fact]
    public void EscAtRoot_ClosesAnd_ReEnablesControl_S10()
    {
        // S10 fix: Esc (FrontendCancel) at the root screen closes via NavigateBack —
        // the character must NOT stay frozen
        var service = BuildService(out var input, out _, out _, out var player);

        input.MenuKeyPressed = true;
        service.Tick(0);   // open → control off
        input.MenuKeyPressed = false;
        service.Tick(16);

        input.MenuCancel = true;
        service.Tick(32);  // Esc at root → close + re-enable

        Assert.False(service.IsMenuOpen);
        Assert.True(player.ControlCalls.LastOrDefault());
    }

    [Fact]
    public void F9Held_DoesNotCloseMenu()
    {
        var service = BuildService(out var input, out _, out _, out _);

        input.MenuKeyPressed = true;
        service.Tick(0);   // opens
        input.MenuKeyPressed = true;
        service.Tick(16);  // still held — must stay OPEN

        Assert.True(service.IsMenuOpen);
    }

    [Fact]
    public void F9ReleaseThenPress_ClosesMenu()
    {
        var service = BuildService(out var input, out _, out _, out _);

        input.MenuKeyPressed = true;
        service.Tick(0);    // open
        input.MenuKeyPressed = false;
        service.Tick(16);   // released
        input.MenuKeyPressed = true;
        service.Tick(32);   // pressed again — edge → close

        Assert.False(service.IsMenuOpen);
    }

    [Fact]
    public void TimeStopMenuItem_ActivatesPower()
    {
        var service = BuildService(out var input, out var notifier, out _, out _);

        // Open menu, select Time Stop (index 0), accept
        input.MenuKeyPressed = true;
        service.Tick(0);
        input.MenuAccept = true;
        service.Tick(16);

        Assert.Contains(notifier.Messages, m => m == UiStrings.TimeStopOn);

        // Accept again → off
        input.MenuAccept = true;
        service.Tick(32);
        Assert.Contains(notifier.Messages, m => m == UiStrings.TimeStopOff);
    }

    [Fact]
    public void Dash_FromMenu_TeleportsAndCloses()
    {
        var service = BuildService(out var input, out _, out _, out _);

        input.MenuKeyPressed = true;
        service.Tick(0);   // open
        input.MenuDown = true;
        service.Tick(16);  // select Dash
        input.MenuDown = false;
        input.MenuAccept = true;
        service.Tick(32);  // execute

        Assert.False(service.IsMenuOpen); // menu closed after execution
    }

    [Fact]
    public void ZHotkey_TogglesTimeStop()
    {
        var service = BuildService(out var input, out var notifier, out _, out _);

        input.TimeStopHotkey = true;
        service.Tick(0);   // edge → time stop ON

        Assert.True(service.IsTimeStopActive);
        Assert.Contains(notifier.Messages, m => m == UiStrings.TimeStopOn);

        input.TimeStopHotkey = false;
        service.Tick(16);
        input.TimeStopHotkey = true;
        service.Tick(32);  // edge → OFF

        Assert.False(service.IsTimeStopActive);
        Assert.Contains(notifier.Messages, m => m == UiStrings.TimeStopOff);
    }

    [Fact]
    public void BHotkey_TogglesInvisibility()
    {
        var service = BuildService(out var input, out _, out _, out _);

        input.InvisibleHotkey = true;
        service.Tick(0);   // edge → invisible ON

        Assert.True(service.IsInvisible);

        input.InvisibleHotkey = false;
        service.Tick(16);
        input.InvisibleHotkey = true;
        service.Tick(32);  // edge → OFF

        Assert.False(service.IsInvisible);
    }

    [Fact]
    public void Hotkeys_Held_DoNotRepeat()
    {
        var service = BuildService(out var input, out _, out _, out _);

        input.TimeStopHotkey = true;
        service.Tick(0);   // ON
        service.Tick(16);  // held — must stay ON
        service.Tick(32);  // held — must stay ON

        Assert.True(service.IsTimeStopActive);
    }

    [Fact]
    public void DashSuccess_TriggersNpcGrace()
    {
        // Realistic reactions: after the blink, NPCs cannot instantly track the player
        var service = BuildService(out var input, out _, out _, out var player);

        input.MenuKeyPressed = true;
        service.Tick(0);   // open
        input.MenuDown = true;
        service.Tick(16);  // select Dash
        input.MenuDown = false;
        input.MenuAccept = true;
        service.Tick(32);  // execute dash

        Assert.Contains(player.AwarenessCalls, a => a == false);   // grace ON
    }

    [Fact]
    public void TimeStopOff_TriggersNpcGrace()
    {
        var service = BuildService(out var input, out _, out _, out var player);

        input.TimeStopHotkey = true;
        service.Tick(0);    // time stop ON
        input.TimeStopHotkey = false;
        service.Tick(16);
        input.TimeStopHotkey = true;
        service.Tick(32);   // time stop OFF → grace

        Assert.Contains(player.AwarenessCalls, a => a == false);
    }

    [Fact]
    public void InvisibleOn_SuppressesAwarenessEveryTick()
    {
        // Persistent suppression while invisible — NPCs cannot perceive the player
        var service = BuildService(out var input, out _, out _, out var player);

        input.InvisibleHotkey = true;
        service.Tick(0);   // invisible ON
        service.Tick(16);  // still invisible — suppressed again
        service.Tick(32);  // still invisible — suppressed again

        Assert.True(player.AwarenessCalls.Count(a => a == false) >= 3);
    }

    [Fact]
    public void InvisibleOff_TriggersNpcGrace()
    {
        var service = BuildService(out var input, out _, out _, out var player);

        input.InvisibleHotkey = true;
        service.Tick(0);    // invisible ON
        input.InvisibleHotkey = false;
        service.Tick(16);
        input.InvisibleHotkey = true;
        service.Tick(32);   // invisible OFF → grace for uncloaking

        Assert.Contains(player.AwarenessCalls, a => a == false);
    }
}
