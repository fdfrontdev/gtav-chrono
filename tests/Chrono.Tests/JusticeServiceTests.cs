using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S1 justice core: wanted edges → crimes, burning, warrants, state machine.</summary>
public class JusticeServiceTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeRecordStore store, FakeNotifier notifier) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer();
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        var service = new JusticeService(wanted, player, store, identity, warrant, notifier, new FakeLog(), new JusticeConfig());
        return (service, wanted, player, store, notifier);
    }

    [Fact]
    public void StarsIncrease_RecordsSingleCrimeAtMaxSeverity()
    {
        var (service, wanted, _, store, _) = Build();

        wanted.CurrentStars = 5;   // 0 → 5 jump = ONE Severe episode
        service.Tick();

        Assert.Single(store.Record.Events);
        Assert.Equal(CrimeSeverity.Severe, store.Record.Events[0].Severity);
        Assert.Equal("Vinewood", store.Record.Events[0].District);
    }

    [Fact]
    public void StarsIncrease_TwoStars_IsMinor()
    {
        var (service, wanted, _, store, _) = Build();
        wanted.CurrentStars = 2;
        service.Tick();
        Assert.Equal(CrimeSeverity.Minor, store.Record.Events[0].Severity);
    }

    [Fact]
    public void StarsIncrease_FourStars_IsModerate()
    {
        var (service, wanted, _, store, _) = Build();
        wanted.CurrentStars = 4;
        service.Tick();
        Assert.Equal(CrimeSeverity.Moderate, store.Record.Events[0].Severity);
    }

    [Fact]
    public void VisibleCrime_BurnsIdentity_AndActivatesWarrant()
    {
        var (service, wanted, player, store, _) = Build();
        player.IsVisible = true;

        wanted.CurrentStars = 3;
        service.Tick();

        Assert.True(store.Record.Events[0].Burned);
        Assert.Equal(IdentityState.Burned, store.Status.Identity);
        Assert.True(store.Status.WarrantActive);
    }

    [Fact]
    public void InvisibleCrime_DoesNotBurn()
    {
        // FR-2.4: no face seen while invisible → identity stays Clean, no warrant
        var (service, wanted, player, store, _) = Build();
        player.IsVisible = false;

        wanted.CurrentStars = 5;
        service.Tick();

        Assert.False(store.Record.Events[0].Burned);
        Assert.Equal(IdentityState.Clean, store.Status.Identity);
        Assert.False(store.Status.WarrantActive);
    }

    [Fact]
    public void RecordFromWantedDisabled_NoEvent()
    {
        var (service, wanted, _, store, _) = Build();
        var config = new JusticeConfig { RecordFromWanted = false };
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        service = new JusticeService(wanted, new FakePlayer(), store, identity, warrant, new FakeNotifier(), new FakeLog(), config);

        wanted.CurrentStars = 4;
        service.Tick();

        Assert.Empty(store.Record.Events);
    }

    [Fact]
    public void NoStarChange_NoEvent()
    {
        var (service, wanted, _, store, _) = Build();
        wanted.CurrentStars = 0;
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();

        Assert.Empty(store.Record.Events);
    }

    [Fact]
    public void StarsDrop_StateReturnsToFree_WarrantPersists()
    {
        var (service, wanted, player, store, _) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 3;
        service.Tick();
        Assert.Equal(JusticeState.Wanted, service.State);

        wanted.CurrentStars = 0;   // escape — stars clear...
        service.Tick();

        Assert.Equal(JusticeState.Free, service.State);
        Assert.True(store.Status.WarrantActive, "escaping must NOT clear the warrant (FR-3.1)");
    }

    [Fact]
    public void SeverityFromStars_MatchesTable()
    {
        Assert.Equal(CrimeSeverity.Minor, JusticeService.SeverityFromStars(1));
        Assert.Equal(CrimeSeverity.Minor, JusticeService.SeverityFromStars(2));
        Assert.Equal(CrimeSeverity.Moderate, JusticeService.SeverityFromStars(3));
        Assert.Equal(CrimeSeverity.Moderate, JusticeService.SeverityFromStars(4));
        Assert.Equal(CrimeSeverity.Severe, JusticeService.SeverityFromStars(5));
    }

    [Fact]
    public void Release_ClearsWarrant()
    {
        // FR-8.4: justice served → warrant cleared
        var (service, wanted, player, store, _) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
        Assert.True(service.Warrant.IsActive);

        service.OnReleased();

        Assert.Equal(JusticeState.Free, service.State);
        Assert.False(service.Warrant.IsActive);
    }
}

public class IdentityServiceTests
{
    [Fact]
    public void SetBurned_PersistsAndFlags()
    {
        var store = new FakeRecordStore();
        var identity = new IdentityService(store, new FakeLog());

        identity.SetBurned();

        Assert.True(identity.IsBurned);
        Assert.Equal(IdentityState.Burned, store.Status.Identity);
    }

    [Fact]
    public void SetClean_Persists()
    {
        var store = new FakeRecordStore();
        var identity = new IdentityService(store, new FakeLog());
        identity.SetBurned();
        identity.SetClean();

        Assert.False(identity.IsBurned);
        Assert.Equal(IdentityState.Clean, store.Status.Identity);
    }

    [Fact]
    public void LoadsPersistedState()
    {
        var store = new FakeRecordStore { Status = new JusticeStatus { Identity = IdentityState.Burned } };
        var identity = new IdentityService(store, new FakeLog());

        Assert.True(identity.IsBurned);
    }
}

public class WarrantServiceTests
{
    [Fact]
    public void Activate_SetsAndPersists()
    {
        var store = new FakeRecordStore();
        var warrant = new WarrantService(store, new FakeLog());

        warrant.Activate("2026-08-08T12:00:00");

        Assert.True(warrant.IsActive);
        Assert.True(store.Status.WarrantActive);
        Assert.Equal("2026-08-08T12:00:00", store.Status.WarrantSinceGameTime);
    }

    [Fact]
    public void Clear_RemovesAndPersists()
    {
        var store = new FakeRecordStore();
        var warrant = new WarrantService(store, new FakeLog());
        warrant.Activate("t");
        warrant.Clear();

        Assert.False(warrant.IsActive);
        Assert.False(store.Status.WarrantActive);
        Assert.Null(store.Status.WarrantSinceGameTime);
    }

    [Fact]
    public void LoadsPersistedWarrant()
    {
        var store = new FakeRecordStore { Status = new JusticeStatus { WarrantActive = true, WarrantSinceGameTime = "t" } };
        var warrant = new WarrantService(store, new FakeLog());

        Assert.True(warrant.IsActive);
        Assert.Equal("t", warrant.SinceGameTime);
    }
}
