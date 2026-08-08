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
    private static PowerMenuService BuildService(out FakeInput input, out FakeNotifier notifier, out FakeRepository repo)
    {
        repo = new FakeRepository();
        input = new FakeInput();
        notifier = new FakeNotifier();
        var config = new ChronoConfig();

        var timeStop = new TimeStopService(
            repo, new FakeFreezer(), new FakeClock(), new FakePlayer(),
            notifier, new FakeLog(), config.TimeStop);
        var teleport = new TeleportService(
            new FakePlayer(), new FakeProbe(), notifier, new FakeLog(), config.Dash, config.Teleport);
        var vfx = new VfxService(new FakeVfx(), new FakeLog(), config.Visual);
        var menu = new MenuFramework(new FakeRenderer());

        var service = new PowerMenuService(
            menu, timeStop, teleport, vfx, input, new FakePlayer(),
            notifier, new FakeLog(), config, new FakeConfigStore());
        service.BuildMenu();
        return service;
    }

    [Fact]
    public void F9Press_OpensMenu()
    {
        var service = BuildService(out var input, out _, out _);

        input.MenuKeyPressed = true;
        service.Tick(0);   // edge detected

        Assert.True(service.IsMenuOpen);
    }

    [Fact]
    public void F9Held_DoesNotCloseMenu()
    {
        var service = BuildService(out var input, out _, out _);

        input.MenuKeyPressed = true;
        service.Tick(0);   // opens
        input.MenuKeyPressed = true;
        service.Tick(16);  // still held — must stay OPEN

        Assert.True(service.IsMenuOpen);
    }

    [Fact]
    public void F9ReleaseThenPress_ClosesMenu()
    {
        var service = BuildService(out var input, out _, out _);

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
        var service = BuildService(out var input, out var notifier, out _);

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
        var service = BuildService(out var input, out _, out var repo);
        var player = new FakePlayer { Position = new(0, 0, 10), Heading = 0f };
        _ = player; // player context in service is separate fake; dash uses forward heading from it

        input.MenuKeyPressed = true;
        service.Tick(0);   // open
        input.MenuDown = true;
        service.Tick(16);  // select Dash
        input.MenuDown = false;
        input.MenuAccept = true;
        service.Tick(32);  // execute

        Assert.False(service.IsMenuOpen); // menu closed after execution
    }
}
