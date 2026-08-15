using System.Linq;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// v0.10 hack pricing (SRS FR-A1/A2, ADR D1): money cost replaces the day
/// cooldown — erase history anytime you can pay; the price scales with the
/// file size. Purge-in-place + mid-chase refusal unchanged.
/// </summary>
public class HackPricingTests
{
    private static (PoliceDbHackService hack, FakeWantedMonitor wanted, FakePlayer player, FakeRecordStore store) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { Money = 100000, IsVisible = true };
        var store = new FakeRecordStore();
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        var justice = new JusticeService(
            wanted, player, store, identity, warrant,
            new FakeNotifier(), new FakeLog(), new JusticeConfig(),
            new FakeClock(), crimeProbe: new FakeCrimeProbe());
        var hack = new PoliceDbHackService(
            wanted, store, identity, warrant, justice,
            new FakeNotifier(), new FakeLog(), new JusticeConfig(),
            new FakeClock(), player, new HackConfig { BaseCost = 10000, PerEventCost = 1500 });
        return (hack, wanted, player, store);
    }

    [Fact]
    public void Cost_EmptyRecord_IsBaseOnly()
    {
        var c = new HackConfig { BaseCost = 10000, PerEventCost = 1500 };
        Assert.Equal(10000, HackPricingPolicy.Cost(c, new CriminalRecord()));
    }

    [Fact]
    public void Cost_ScalesWithEventsAndConvictions()
    {
        var c = new HackConfig { BaseCost = 10000, PerEventCost = 1500 };
        var record = new CriminalRecord();
        record.Append(new CrimeEvent("1", CrimeSeverity.Minor, "shove", "2026-08-14", "Vinewood", true));
        record.Append(new CrimeEvent("2", CrimeSeverity.Severe, "murder", "2026-08-14", "Vinewood", true));
        record.AddConviction(new Conviction("c1", 5000, 30, "2026-08-14"));
        Assert.Equal(10000 + 1500 * 3, HackPricingPolicy.Cost(c, record));
    }

    [Fact]
    public void TryHack_NoCooldown_SecondHackSameDayWorks()
    {
        var (hack, _, player, store) = Build();
        store.Record.Append(new CrimeEvent("1", CrimeSeverity.Minor, "shove", "2026-08-14", "Vinewood", true));

        Assert.True(hack.TryHack());          // first hack — pays 11,500
        Assert.Equal(100000 - 11500, player.Money);
        Assert.Empty(store.Record.Events);

        store.Record.Append(new CrimeEvent("2", CrimeSeverity.Minor, "shove", "2026-08-14", "Vinewood", true));
        Assert.True(hack.TryHack());          // second hack, same day — NO lock (FR-A2)
        Assert.Empty(store.Record.Events);
    }

    [Fact]
    public void TryHack_RefusedWhileChased()
    {
        var (hack, wanted, player, store) = Build();
        wanted.CurrentStars = 2;

        Assert.False(hack.TryHack());
        Assert.Equal(100000, player.Money);   // nothing paid, nothing purged
    }

    [Fact]
    public void TryHack_RefusedWhenBroke()
    {
        var (hack, _, player, store) = Build();
        store.Record.Append(new CrimeEvent("1", CrimeSeverity.Minor, "shove", "2026-08-14", "Vinewood", true));
        player.Money = 500;                    // below 11,500

        Assert.False(hack.TryHack());
        Assert.Single(store.Record.Events);    // record intact
    }

    [Fact]
    public void TryHack_PaysAndPurges()
    {
        var (hack, _, player, store) = Build();
        store.Record.Append(new CrimeEvent("1", CrimeSeverity.Severe, "murder", "2026-08-14", "Vinewood", true));

        Assert.True(hack.TryHack());

        Assert.Equal(100000 - 11500, player.Money);   // paid
        Assert.Empty(store.Record.Events);            // purged
    }
}
