using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S5 plastic surgery clinic: physical door prompt, cooldown, scaling cost, Clean identity.</summary>
public class ClinicServiceTests
{
    private static (ClinicService service, FakePlayer player, FakeRecordStore store, FakeNotifier notifier, FakeClock clock, FakeInput input) Build()
    {
        var player = new FakePlayer { Money = 100000 };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var clock = new FakeClock();
        var input = new FakeInput();
        var identity = new IdentityService(store, new FakeLog());
        var service = new ClinicService(
            player, store, identity, notifier, new FakeLog(),
            new JusticeConfig(), clock, input);
        return (service, player, store, notifier, clock, input);
    }

    [Fact]
    public void Surgery_Success_CleansIdentity_KeepsRecord()
    {
        var (service, player, store, notifier, _, _) = Build();
        store.Record.Append(new CrimeEvent("e1", CrimeSeverity.Severe, "murder", "t", "Vinewood", true));
        store.Record.Append(new CrimeEvent("e2", CrimeSeverity.Minor, "assault", "t", "Vinewood", true));
        store.Status.Identity = IdentityState.Burned;
        var identity = new IdentityService(store, new FakeLog());

        // Rebuild with burned identity to verify the surgery cleans it
        var service2 = new ClinicService(player, store, identity, notifier, new FakeLog(), new JusticeConfig(), new FakeClock(), new FakeInput());
        bool ok = service2.TrySurgery();

        Assert.True(ok);
        Assert.Equal(IdentityState.Clean, store.Status.Identity);        // new face (FR-5.2)
        Assert.Equal(2, store.Record.Count);                             // record INTACT (FR-5.3)
        Assert.Equal(1, store.Profile.Surgeries);
        Assert.Equal(100000 - 5000 - 1000 * 2, player.Money);            // cost scales with record
    }

    [Fact]
    public void Surgery_CooldownActive_Refused()
    {
        var (service, player, store, notifier, clock, _) = Build();
        store.Status.LastSurgeryDay = clock.CurrentGameDay;   // surgery today
        int moneyBefore = player.Money;

        bool ok = service.TrySurgery();

        Assert.False(ok);
        Assert.Equal(moneyBefore, player.Money);              // no charge
        Assert.Contains(notifier.Messages, m => m.Contains("booked"));
    }

    [Fact]
    public void Surgery_InsufficientFunds_Refused()
    {
        var (service, player, _, notifier, _, _) = Build();
        player.Money = 100;

        bool ok = service.TrySurgery();

        Assert.False(ok);
        Assert.Equal(100, player.Money);
        Assert.Contains(notifier.Messages, m => m.Contains("afford"));
    }

    [Fact]
    public void Surgery_SetsCooldown_OnStatus()
    {
        var (service, player, store, _, clock, _) = Build();

        service.TrySurgery();

        Assert.Equal(clock.CurrentGameDay, store.Status.LastSurgeryDay);
    }

    [Fact]
    public void Tick_AtDoor_ShowsPromptOnce()
    {
        var (service, player, _, notifier, _, _) = Build();
        player.Position = ClinicService.ClinicDoor;   // exactly at the door

        service.Tick();
        service.Tick();   // prompt must not repeat

        Assert.Single(notifier.Messages, m => m.Contains("press G"));
    }

    [Fact]
    public void Tick_AtDoor_WithInteractEdge_TriggersSurgery()
    {
        var (service, player, store, notifier, _, input) = Build();
        player.Position = ClinicService.ClinicDoor;

        input.InteractHotkey = true;
        input.Update();
        service.Tick();

        Assert.Equal(1, store.Profile.Surgeries);
        Assert.Contains(notifier.Messages, m => m.Contains("SURGERY COMPLETE"));
    }

    [Fact]
    public void Tick_AwayFromDoor_NoPrompt()
    {
        var (service, player, _, notifier, _, _) = Build();
        player.Position = new System.Numerics.Vector3(0, 0, 0);   // far away

        service.Tick();

        Assert.DoesNotContain(notifier.Messages, m => m.Contains("press G"));
    }
}
