using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S6 police DB hack: F9 cheat purges the record; refused while chased; cooldown.</summary>
public class PoliceDbHackServiceTests
{
    private static (PoliceDbHackService hack, JusticeService justice, FakeWantedMonitor wanted, FakeRecordStore store, FakeNotifier notifier, FakeClock clock) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000 };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var clock = new FakeClock();
        var input = new FakeInput();
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        var justice = new JusticeService(
            wanted, player, store, identity, warrant, notifier, new FakeLog(),
            new JusticeConfig(), clock);
        var hack = new PoliceDbHackService(
            wanted, store, identity, warrant, justice, notifier, new FakeLog(),
            new JusticeConfig(), clock, player, new HackConfig { BaseCost = 10000, PerEventCost = 1500 });
        return (hack, justice, wanted, store, notifier, clock);
    }

    private static void CommitCrime(JusticeService justice, FakeWantedMonitor wanted)
    {
        wanted.CurrentStars = 5;
        justice.Tick();   // Severe crime recorded
    }

    [Fact]
    public void Hack_Success_PurgesRecord_AndCleansIdentity()
    {
        var (hack, justice, wanted, store, notifier, _) = Build();
        CommitCrime(justice, wanted);
        wanted.CurrentStars = 0;              // chase over — cops lost you
        justice.Tick();
        store.Status.Identity = IdentityState.Burned;
        store.Status.WarrantActive = true;
        Assert.Equal(1, store.Record.Count);

        bool ok = hack.TryHack();

        Assert.True(ok);
        Assert.Empty(store.Record.Events);                       // events gone (FR-6.1)
        Assert.Equal(IdentityState.Clean, store.Status.Identity);
        Assert.False(store.Status.WarrantActive);
        Assert.Contains(notifier.Messages, m => m.Contains("PURGED"));
    }

    [Fact]
    public void Hack_WhileChased_Refused()
    {
        var (hack, justice, wanted, store, notifier, _) = Build();
        CommitCrime(justice, wanted);
        wanted.CurrentStars = 2;   // actively chased

        bool ok = hack.TryHack();

        Assert.False(ok);
        Assert.Equal(1, store.Record.Count);                     // untouched
        Assert.Contains(notifier.Messages, m => m.Contains("cops are on you"));
    }

    [Fact]
    public void Hack_NoCooldown_HackAgainSameDaySucceeds()   // v0.10 (FR-A2): money is the gate
    {
        var (hack, _, _, store, _, clock) = Build();
        store.Status.LastHackDay = clock.CurrentGameDay;      // "hacked today" — irrelevant now

        bool ok = hack.TryHack();

        Assert.True(ok);                                       // no lock (ADR D1)
    }

    [Fact]
    public void Hack_SetsCooldown()
    {
        var (hack, _, _, store, _, clock) = Build();

        hack.TryHack();

        Assert.Equal(clock.CurrentGameDay, store.Status.LastHackDay);
    }

    [Fact]
    public void Hack_AfterPurge_NextCrimeAppendsToFreshRecord()
    {
        // The cached record inside JusticeService must stay valid after the purge
        var (hack, justice, wanted, store, _, _) = Build();
        CommitCrime(justice, wanted);
        wanted.CurrentStars = 0;   // chase over
        justice.Tick();
        hack.TryHack();

        wanted.CurrentStars = 0;
        justice.Tick();                    // episode over
        wanted.CurrentStars = 3;
        justice.Tick();                    // new crime on the FRESH record

        Assert.Single(store.Record.Events);                    // not 2 — purge won
        Assert.Equal(CrimeSeverity.Moderate, store.Record.Events[0].Severity);
    }
}
