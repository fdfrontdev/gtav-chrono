using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

public class TimeStopServiceTests
{
    private static TimeStopService Create(
        FakeRepository repo, FakeFreezer freezer, FakeClock clock, FakePlayer player,
        FakeNotifier notifier, FakeLog log, TimeStopConfig? config = null)
    {
        return new TimeStopService(repo, freezer, clock, player, notifier, log,
            config ?? new TimeStopConfig { MaintenanceIntervalMs = 2000, MaxFrozenEntities = 512 });
    }

    [Fact]
    public void Activate_QueuesAllEligibleEntities()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));
        repo.Vehicles.Add(new GameEntity(20, EntityKind.Vehicle));
        repo.Props.Add(new GameEntity(30, EntityKind.Prop));

        var freezer = new FakeFreezer(10, 20, 30);
        var clock = new FakeClock();
        var player = new FakePlayer { PlayerHandle = 1 };

        var service = Create(repo, freezer, clock, player, new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Tick(0);

        Assert.True(service.IsActive);
        Assert.True(freezer.FreezeFlags[10]);
        Assert.True(freezer.FreezeFlags[20]);
        Assert.True(freezer.FreezeFlags[30]);
        Assert.Equal(3, service.FrozenCount);
    }

    [Fact]
    public void Activate_PausesClockWhenConfigured()
    {
        var repo = new FakeRepository();
        var clock = new FakeClock();
        var service = Create(repo, new FakeFreezer(), clock, new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Tick(0);

        Assert.True(clock.IsPaused);
    }

    [Fact]
    public void Activate_DoesNotPauseClockWhenDisabled()
    {
        var repo = new FakeRepository();
        var clock = new FakeClock();
        var cfg = new TimeStopConfig { PauseClock = false };
        var service = Create(repo, new FakeFreezer(), clock, new FakePlayer(), new FakeNotifier(), new FakeLog(), cfg);

        service.Activate();
        service.Tick(0);

        Assert.False(clock.IsPaused);
    }

    [Fact]
    public void Activate_NeverFreezesPlayerOrPlayerVehicle()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(1, EntityKind.Ped));       // player
        repo.Vehicles.Add(new GameEntity(2, EntityKind.Vehicle)); // player vehicle
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));

        var freezer = new FakeFreezer(1, 2, 10);
        var player = new FakePlayer { PlayerHandle = 1, PlayerVehicleHandle = 2 };

        var service = Create(repo, freezer, new FakeClock(), player, new FakeNotifier(), new FakeLog());
        service.Activate();
        service.Tick(0);

        Assert.Single(freezer.FreezeFlags);
        Assert.True(freezer.FreezeFlags.ContainsKey(10));
        Assert.False(freezer.FreezeFlags.ContainsKey(1));
        Assert.False(freezer.FreezeFlags.ContainsKey(2));
    }

    [Fact]
    public void FreezeQueue_ProcessedInBatchesOf100()
    {
        var repo = new FakeRepository();
        for (int i = 0; i < 250; i++) repo.Peds.Add(new GameEntity(1000 + i, EntityKind.Ped));

        var freezer = new FakeFreezer(Enumerable.Range(1000, 250).ToArray());
        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Tick(0);      // first batch
        Assert.Equal(100, service.FrozenCount);
        service.Tick(100);    // second batch
        Assert.Equal(200, service.FrozenCount);
        service.Tick(200);    // third batch
        Assert.Equal(250, service.FrozenCount);
        Assert.False(service.IsFreezingInProgress);
    }

    [Fact]
    public void Deactivate_RestoresAllAndResumesClock()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));
        var freezer = new FakeFreezer(10);
        var clock = new FakeClock();
        var service = Create(repo, freezer, clock, new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Tick(0);
        service.Deactivate();
        service.Tick(100);   // restore is batched — drains on the next ticks

        Assert.False(service.IsActive);
        Assert.False(freezer.FreezeFlags[10]); // unfrozen
        Assert.True(freezer.Restored.ContainsKey(10));
        Assert.False(clock.IsPaused);
        Assert.Equal(0, service.FrozenCount);
    }

    [Fact]
    public void Deactivate_RestoresInBatchesOf100()
    {
        var repo = new FakeRepository();
        for (int i = 0; i < 250; i++) repo.Peds.Add(new GameEntity(1000 + i, EntityKind.Ped));
        var freezer = new FakeFreezer(Enumerable.Range(1000, 250).ToArray());
        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Tick(0);   // freeze batch 1
        service.Tick(100); // freeze batch 2
        service.Tick(200); // freeze batch 3 (all 250 frozen)

        service.Deactivate();
        Assert.True(service.IsRestoringInProgress);

        service.Tick(300); // restore batch 1
        Assert.Equal(100, freezer.Restored.Count);
        service.Tick(400); // restore batch 2
        Assert.Equal(200, freezer.Restored.Count);
        service.Tick(500); // restore batch 3
        Assert.Equal(250, freezer.Restored.Count);
        Assert.False(service.IsRestoringInProgress);
        Assert.False(service.IsActive);
    }

    [Fact]
    public void Deactivate_SkipsDeadEntitiesWithoutCrash()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));
        repo.Peds.Add(new GameEntity(11, EntityKind.Ped));
        var freezer = new FakeFreezer(10, 11);
        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Tick(0);
        freezer.ExistsSet.Remove(11); // entity dies mid-freeze

        service.Deactivate();
        service.Tick(100);   // must not throw

        Assert.True(freezer.Restored.ContainsKey(10));
        Assert.False(freezer.Restored.ContainsKey(11));
    }

    [Fact]
    public void MaintenanceSweep_CapturesLateSpawnedEntities()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));
        var freezer = new FakeFreezer(10);
        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Tick(0);
        Assert.Equal(1, service.FrozenCount);

        // late spawn
        repo.Peds.Add(new GameEntity(20, EntityKind.Ped));
        freezer.ExistsSet.Add(20);

        service.Tick(2000); // sweep captures entity 20 into the freeze queue
        service.Tick(2100); // next tick drains the queue and freezes it
        Assert.Equal(2, service.FrozenCount);
    }

    [Fact]
    public void MaintenanceSweep_RespectsCap()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));
        var cfg = new TimeStopConfig { MaintenanceIntervalMs = 2000, MaxFrozenEntities = 1 };
        var freezer = new FakeFreezer(10);
        var notifier = new FakeNotifier();
        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), notifier, new FakeLog(), cfg);

        service.Activate();
        service.Tick(0);

        repo.Peds.Add(new GameEntity(20, EntityKind.Ped));
        freezer.ExistsSet.Add(20);

        service.Tick(2000); // cap reached → warning, no new freeze

        Assert.Equal(1, service.FrozenCount);
        Assert.Contains(notifier.Messages, m => m == UiStrings.TimeStopCapped);
    }

    [Fact]
    public void Activate_Twice_IsIdempotent()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));
        var service = Create(repo, new FakeFreezer(10), new FakeClock(), new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Activate();
        service.Tick(0);

        Assert.Equal(1, service.FrozenCount);
    }

    [Fact]
    public void RadiusFilter_ExcludesDistantEntities()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped, new(0, 0, 0)));        // 0m — inside
        repo.Peds.Add(new GameEntity(11, EntityKind.Ped, new(200, 0, 0)));      // 200m — outside

        var freezer = new FakeFreezer(10, 11);
        var player = new FakePlayer { PlayerHandle = 1 };   // at (0,0,0)
        var cfg = new TimeStopConfig { MaintenanceIntervalMs = 2000, MaxFrozenEntities = 512, FreezeRadius = 100f };

        var service = Create(repo, freezer, new FakeClock(), player, new FakeNotifier(), new FakeLog(), cfg);
        service.Activate();
        service.Tick(0);

        Assert.Equal(1, service.FrozenCount);
        Assert.True(freezer.FreezeFlags.ContainsKey(10));
        Assert.False(freezer.FreezeFlags.ContainsKey(11));
    }

    [Fact]
    public void RadiusZero_DisablesFilter()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped, new(500, 500, 0)));   // far away

        var freezer = new FakeFreezer(10);
        var cfg = new TimeStopConfig { MaintenanceIntervalMs = 2000, MaxFrozenEntities = 512, FreezeRadius = 0f };

        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), new FakeNotifier(), new FakeLog(), cfg);
        service.Activate();
        service.Tick(0);

        Assert.Equal(1, service.FrozenCount);
    }

    [Fact]
    public void AirborneVehicles_Skipped_NoCrashOnResume()
    {
        // Planes/helicopters in flight must NOT be frozen — freezing breaks their flight
        // model and they crash on restore (user report v0.3.0)
        var repo = new FakeRepository();
        repo.Vehicles.Add(new GameEntity(10, EntityKind.Vehicle, new(0, 0, 0)));            // grounded car
        repo.Vehicles.Add(new GameEntity(11, EntityKind.Vehicle, new(0, 5, 0), true));      // flying plane
        repo.Peds.Add(new GameEntity(20, EntityKind.Ped, new(0, 0, 0)));

        var freezer = new FakeFreezer(10, 11, 20);
        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Activate();
        service.Tick(0);

        Assert.Equal(2, service.FrozenCount);   // car + ped frozen, plane NOT
        Assert.True(freezer.FreezeFlags.ContainsKey(10));
        Assert.False(freezer.FreezeFlags.ContainsKey(11));
        Assert.True(freezer.FreezeFlags.ContainsKey(20));
    }

    [Fact]
    public void CapWarning_ShownOnlyOncePerActivation()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));
        var cfg = new TimeStopConfig { MaintenanceIntervalMs = 2000, MaxFrozenEntities = 1 };
        var freezer = new FakeFreezer(10);
        var notifier = new FakeNotifier();
        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), notifier, new FakeLog(), cfg);

        service.Activate();
        service.Tick(0);

        repo.Peds.Add(new GameEntity(20, EntityKind.Ped));
        freezer.ExistsSet.Add(20);
        service.Tick(2000);   // sweep 1 → cap → notify (first time)
        service.Tick(4000);   // sweep 2 → cap → NOT notified again

        Assert.Equal(1, notifier.Messages.Count(m => m == UiStrings.TimeStopCapped));
    }

    [Fact]
    public void Tick_WhileInactive_DoesNothing()
    {
        var repo = new FakeRepository();
        repo.Peds.Add(new GameEntity(10, EntityKind.Ped));
        var freezer = new FakeFreezer(10);
        var service = Create(repo, freezer, new FakeClock(), new FakePlayer(), new FakeNotifier(), new FakeLog());

        service.Tick(5000);

        Assert.Empty(freezer.FreezeFlags);
    }
}
