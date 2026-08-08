using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S7 Criminal Record screen: stats snapshot feeds the F9 Justice submenu.</summary>
public class JusticeStatsServiceTests
{
    private static (JusticeStatsService stats, FakeRecordStore store) Build()
    {
        var store = new FakeRecordStore();
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        var stats = new JusticeStatsService(store, identity, warrant);
        return (stats, store);
    }

    [Fact]
    public void GetStats_ListsCrimesNewestFirst()
    {
        var (stats, store) = Build();
        store.Record.Append(new CrimeEvent("e1", CrimeSeverity.Minor, "assault", "2026-08-09T10:00:00", "Vinewood", true));
        store.Record.Append(new CrimeEvent("e2", CrimeSeverity.Severe, "murder", "2026-08-09T14:30:00", "Paleto", true));
        store.Record.Append(new CrimeEvent("e3", CrimeSeverity.Moderate, "robbery", "2026-08-09T12:00:00", "Sandy", true));

        var s = stats.GetStats();

        Assert.Equal(3, s.Crimes.Count);
        Assert.Equal("murder", s.Crimes[0].Kind);        // newest first
        Assert.Equal("robbery", s.Crimes[1].Kind);
        Assert.Equal("assault", s.Crimes[2].Kind);
    }

    [Fact]
    public void GetStats_CapsAtTwenty()
    {
        var (stats, store) = Build();
        for (int i = 0; i < 25; i++)
            store.Record.Append(new CrimeEvent($"e{i}", CrimeSeverity.Minor, "petty", $"2026-08-09T{i:00}:00:00", "Vinewood", false));

        var s = stats.GetStats();

        Assert.Equal(20, s.Crimes.Count);
    }

    [Fact]
    public void GetStats_IncludesStatus()
    {
        var (stats, store) = Build();
        store.Status.Identity = IdentityState.Burned;
        store.Status.WarrantActive = true;
        store.Profile = new CharacterProfile { AgeDays = 27 * 365 + 40 };
        store.Record.AddConviction(new Conviction(2000, 0, "t"));

        var s = stats.GetStats();

        Assert.Equal(IdentityState.Burned, s.Identity);
        Assert.True(s.WarrantActive);
        Assert.Equal(27 * 365 + 40, s.AgeDays);
        Assert.Equal(1, s.ConvictionCount);
    }
}
