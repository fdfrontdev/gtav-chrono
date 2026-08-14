using System;
using System.Collections.Generic;
using System.Numerics;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// v0.10 survivor needs (SRS FR-C1..C15): decay, tiers, effects, pass-out,
/// sleep, eat/drink, and the phone delivery motivation loop.
/// </summary>
public class NeedsServiceTests
{
    internal sealed class FakeFoodBoundary : IFoodBoundary
    {
        public int SpawnedCount { get; private set; }
        public Vector3 LastSpawn { get; private set; }
        public int EatAnims { get; private set; }
        public int DrinkAnims { get; private set; }
        public Vector3? VendingPosition { get; set; }
        public bool EateryNearby { get; set; }

        public void SpawnFoodProp(Vector3 position, string model) { SpawnedCount++; LastSpawn = position; }
        public void PlayEatAnim() => EatAnims++;
        public void PlayDrinkAnim() => DrinkAnims++;
        public Vector3? FindVendingMachine(Vector3 center, float radiusM) => VendingPosition;
        public bool TryFindEatery(Vector3 center, float radiusM, out Vector3 spot) { spot = Vector3.Zero; return EateryNearby; }
    }

    internal sealed class FakeCompanionBoundary : ICompanionBoundary
    {
        public int SendCount { get; private set; }
        public int DismissCount { get; private set; }
        public bool Near { get; set; }
        public string? LastModel { get; private set; }

        public void SendCompanion(Vector3 playerPosition, string model) { SendCount++; LastModel = model; }
        public bool IsCompanionNear(Vector3 playerPosition) => Near;
        public void DismissCompanion() => DismissCount++;
    }

    internal sealed class FakeSleepBoundary : ISleepBoundary
    {
        public bool SpotAvailable { get; set; }
        public bool TvNearby { get; set; }
        public bool TryFindSleepSpot(Vector3 center, float radiusM, out Vector3 spot) { spot = Vector3.Zero; return SpotAvailable; }
        public bool TryFindTv(Vector3 center, float radiusM, out Vector3 spot) { spot = Vector3.Zero; return TvNearby; }
    }

    private static (NeedsService needs, FakePlayer player, FakeRecordStore store, FakeNotifier notifier,
        FakeFoodBoundary food, FakeSleepBoundary sleep, FakeVfx vfx, FakeInput input, FakeCompanionBoundary escort) Build(bool withFood = true)
    {
        var player = new FakePlayer { Money = 1000, IsVisible = true };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var food = withFood ? new FakeFoodBoundary() : null!;
        var sleep = new FakeSleepBoundary();
        var vfx = new FakeVfx();
        var input = new FakeInput();
        var escort = new FakeCompanionBoundary();
        var needs = new NeedsService(
            player, store, new NeedsConfig { GameHourRealSeconds = 120 },
            notifier, new FakeLog(), food, sleep, vfx, input: input, escort: escort);
        needs.Load();
        return (needs, player, store, notifier, food!, sleep, vfx, input, escort);
    }

    [Fact]
    public void Decay_AppliesPerGameHour()
    {
        var (needs, _, _, _, _, _, _, _, _) = Build();
        int before = needs.State.Thirst;

        needs.Tick(120);                    // 1 game hour

        Assert.True(needs.State.Thirst < before);   // decayed
    }

    [Fact]
    public void Decay_FractionalHours_Carry()
    {
        var (needs, _, _, _, _, _, _, _, _) = Build();
        needs.Tick(60);                     // half an hour — nothing decays yet
        int before = needs.State.Hunger;

        needs.Tick(60);                     // + half = one full hour

        Assert.True(needs.State.Hunger < before);   // the half was carried, not lost
    }

    [Fact]
    public void TierTransitions_NotifyOnce()
    {
        var (needs, _, _, notifier, _, _, _, _, _) = Build();
        // decay hunger to 39 (bad) — 61 points × (24h/100) = 14.6 game hours
        needs.Tick(120 * 15);

        int badMsgs = notifier.Messages.Count(m => m.Contains("hungry"));
        Assert.True(badMsgs >= 1);

        needs.Tick(120 * 30);               // way past critical
        Assert.Contains(notifier.Messages, m => m.Contains("STARVING"));
    }

    [Fact]
    public void Effects_BlockRegenAndDrainHealth_WhenCritical()
    {
        var (needs, player, _, _, _, _, _, _, _) = Build();
        needs.Tick(120 * 30);               // hunger + thirst way down

        Assert.Contains(0f, player.HealthRechargeCalls);   // regen blocked
        Assert.True(player.DamageTaken > 0);               // survivor drain
    }

    [Fact]
    public void EnergyBad_SlowsRun()
    {
        var (needs, player, _, _, _, _, _, _, _) = Build();
        needs.Tick(120 * 40);               // energy critical

        Assert.Contains(0.7f, player.RunSpeedCalls);       // critical multiplier
    }

    [Fact]
    public void ThirstCritical_DrunkVisual()
    {
        var (needs, player, _, _, _, _, _, _, _) = Build();
        needs.Tick(120 * 25);               // thirst < 15

        Assert.Contains(true, player.DrunkCalls);
    }

    [Fact]
    public void PassOut_WhenEnergyCritical_NeverKills()
    {
        var (needs, player, store, notifier, _, _, vfx, _, _) = Build();
        store.Status.Needs = new NeedsState { Hunger = 100, Thirst = 100, Energy = 5, Mood = 100 };
        needs.Load();

        needs.Tick(0.1);                    // one frame at critical energy

        Assert.True(needs.State.Energy >= 60);              // restored, not dead
        Assert.Contains(notifier.Messages, m => m.Contains("pass out", StringComparison.OrdinalIgnoreCase));
        Assert.True(player.DamageTaken == 0);               // pass-out never drains health
        Assert.Contains(vfx.Calls, c => c.Contains("fadeout"));
    }

    [Fact]
    public void Sleep_AtSpot_RestoresEnergyAndMood()
    {
        var (needs, _, _, notifier, _, sleep, _, _, _) = Build();
        sleep.SpotAvailable = true;
        needs.Tick(120 * 10);               // some decay first
        int moodBefore = needs.State.Mood;

        Assert.True(needs.TrySleep());

        Assert.Equal(100, needs.State.Energy);
        Assert.True(needs.State.Mood > moodBefore);
        Assert.Contains(notifier.Messages, m => m.Contains("Refreshed"));
    }

    [Fact]
    public void Sleep_NoSpot_Refused()
    {
        var (needs, _, _, notifier, _, sleep, _, _, _) = Build();
        sleep.SpotAvailable = false;

        Assert.False(needs.TrySleep());
        Assert.Contains(notifier.Messages, m => m.Contains("No bed nearby"));
    }

    [Fact]
    public void EatAtEatery_Nearby_PaysAndRestores()
    {
        var (needs, player, _, notifier, food, _, _, _, _) = Build();
        food.EateryNearby = true;
        needs.Tick(120 * 10);               // hunger down
        int hungerBefore = needs.State.Hunger;
        int moneyBefore = player.Money;

        Assert.True(needs.TryEatAtEatery());

        Assert.True(needs.State.Hunger > hungerBefore);
        Assert.True(player.Money < moneyBefore);
        Assert.Contains(notifier.Messages, m => m.Contains("Ate"));
    }

    [Fact]
    public void BuyDrink_RestoresThirst()
    {
        var (needs, player, _, notifier, _, _, _, _, _) = Build();
        needs.Tick(120 * 10);
        int thirstBefore = needs.State.Thirst;
        int moneyBefore = player.Money;

        Assert.True(needs.TryBuyDrink(energyDrink: false));

        Assert.True(needs.State.Thirst > thirstBefore);
        Assert.True(player.Money < moneyBefore);
    }

    // ── delivery (FR-C10/C11) ──

    [Fact]
    public void Delivery_OrderPayArriveEat_FullLoop()
    {
        var (needs, player, _, notifier, food, _, _, _, _) = Build();
        needs.Tick(120 * 10);               // hunger down
        int hungerBefore = needs.State.Hunger;
        int moneyBefore = player.Money;

        Assert.True(needs.TryOrderMeal(0));                       // order + pay
        Assert.True(player.Money < moneyBefore);
        Assert.True(needs.Delivery.HasPendingOrder);

        needs.Tick(100);                                          // delivery arrives
        Assert.True(needs.Delivery.HasArrivedFood);
        Assert.Equal(1, food.SpawnedCount);
        Assert.Equal(player.Position, food.LastSpawn);            // at CURRENT position

        Assert.True(needs.Delivery.TryConsume());                 // eat

        Assert.True(needs.State.Hunger > hungerBefore);
        Assert.False(needs.Delivery.HasPendingOrder);
        Assert.True(food.EatAnims >= 1);
    }

    [Fact]
    public void Delivery_OrderedTwice_SecondRefused()
    {
        var (needs, _, _, notifier, _, _, _, _, _) = Build();
        Assert.True(needs.TryOrderMeal(0));

        Assert.False(needs.TryOrderMeal(1));
        Assert.Contains(notifier.Messages, m => m.Contains("already on the way"));
    }

    [Fact]
    public void Delivery_Broke_Refused()
    {
        var (needs, player, _, notifier, _, _, _, _, _) = Build();
        player.Money = 10;

        Assert.False(needs.TryOrderMeal(0));
        Assert.Contains(notifier.Messages, m => m.Contains("Not enough cash for delivery"));
    }

    [Fact]
    public void Delivery_TeleportMidOrder_SpawnsAtNewPosition()
    {
        var (needs, player, _, _, food, _, _, _, _) = Build();
        needs.TryOrderMeal(0);
        player.Position = new Vector3(999, 999, 10);   // player moves across town

        needs.Tick(100);

        Assert.Equal(player.Position, food.LastSpawn);  // spawn follows the player (FR-C11)
    }

    [Fact]
    public void Needs_PersistEvery10Seconds()
    {
        var (needs, _, store, _, _, _, _, _, _) = Build();
        int savesBefore = store.SaveCount;

        needs.Tick(5);
        needs.Tick(5);
        needs.Tick(0.1);                                 // crosses 10s

        Assert.True(store.SaveCount > savesBefore);
        Assert.NotNull(store.Status.Needs);
    }

    // ── v0.12 escort (FR-D2) ──

    [Fact]
    public void Escort_FullLoop_PaysAndBoostsMood()
    {
        var (needs, player, _, notifier, _, _, _, _, escort) = Build();
        needs.Tick(120 * 10);               // mood + energy down a bit
        int moodBefore = needs.State.Mood;
        int energyBefore = needs.State.Energy;
        int moneyBefore = player.Money;

        Assert.True(needs.TryOrderEscort());
        Assert.Equal(moneyBefore - 100, player.Money);        // paid
        Assert.Contains(notifier.Messages, m => m.Contains("ESCORT on the way"));

        needs.Tick(25);                     // past 20s ETA → she arrives
        Assert.Equal(1, escort.SendCount);
        Assert.Contains(notifier.Messages, m => m.Contains("companion is here"));

        needs.Tick(0.1);                    // still walking — no payoff yet
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("refreshing"));

        escort.Near = true;                 // she reaches you
        needs.Tick(0.1);

        Assert.True(needs.State.Mood > moodBefore);           // mood payoff
        Assert.True(needs.State.Energy > energyBefore);       // energy payoff
        Assert.Equal(1, escort.DismissCount);
        Assert.Contains(notifier.Messages, m => m.Contains("refreshing"));
    }

    [Fact]
    public void Escort_Broke_Refused()
    {
        var (needs, player, _, notifier, _, _, _, _, _) = Build();
        player.Money = 50;

        Assert.False(needs.TryOrderEscort());
        Assert.Contains(notifier.Messages, m => m.Contains("Not enough cash for the escort"));
    }

    [Fact]
    public void Escort_DuplicateOrder_Refused()
    {
        var (needs, _, _, notifier, _, _, _, _, _) = Build();
        needs.TryOrderEscort();

        Assert.False(needs.TryOrderEscort());
        Assert.Contains(notifier.Messages, m => m.Contains("already have company"));
    }

    [Fact]
    public void Escort_ForceCompletes_IfSheNeverReaches()
    {
        var (needs, _, _, notifier, _, _, _, _, escort) = Build();
        needs.TryOrderEscort();
        needs.Tick(25);                     // she arrives but stays far
        Assert.Equal(1, escort.SendCount);

        needs.Tick(46);                     // > 45s force timer

        Assert.Equal(1, escort.DismissCount);                 // never a soft-lock
        Assert.Contains(notifier.Messages, m => m.Contains("refreshing"));
    }

    [Fact]
    public void Escort_BlockedWhileDeliveryPending()
    {
        var (needs, _, _, notifier, _, _, _, _, _) = Build();
        needs.TryOrderMeal(0);              // delivery first

        Assert.False(needs.TryOrderEscort());
        Assert.Contains(notifier.Messages, m => m.Contains("Finish your delivery first"));
    }

    // ── v0.12 phone drinks (FR-D1) ──

    [Fact]
    public void Delivery_Drink_RestoresThirst_NoPropSpawned()
    {
        var (needs, _, _, notifier, food, _, _, _, _) = Build();
        needs.Tick(120 * 10);               // thirst down
        int thirstBefore = needs.State.Thirst;

        Assert.True(needs.TryOrderDrink(0));                 // bottled water
        needs.Tick(70);                     // past delivery ETA
        Assert.True(needs.Delivery.HasArrivedFood);
        Assert.Equal(0, food.SpawnedCount);                  // drinks: no world prop

        Assert.True(needs.Delivery.TryConsume());

        Assert.True(needs.State.Thirst > thirstBefore);      // thirst restored
        Assert.True(food.DrinkAnims >= 1);                   // drink anim, not eat
        Assert.Equal(0, food.EatAnims);
        Assert.Contains(notifier.Messages, m => m.Contains("thirst quenched"));
    }

    [Fact]
    public void Delivery_EnergyDrink_RestoresEnergy()
    {
        var (needs, _, _, _, food, _, _, _, _) = Build();
        needs.Tick(120 * 40);               // energy critical
        int energyBefore = needs.State.Energy;

        needs.TryOrderDrink(2);                             // energy drink
        needs.Tick(70);
        needs.Delivery.TryConsume();

        Assert.True(needs.State.Energy > energyBefore);
    }

    // ── v0.13 mood passives (ADR 09, SB-grounded) ──
    // Semantics (floor decay): 1h idle = -4 pts (floor(3.33)); fresh air 5.0/h
    // = net +1/h (recovers); driving 4.0/h = net 0 (holds level).

    [Fact]
    public void Mood_FreshAir_WalkingOutdoors_Rises()
    {
        var (needs, player, _, _, _, _, _, _, _) = Build();
        player.IsInVehicle = false;
        player.Outdoors = false;
        needs.Tick(120 * 10);               // idle indoors 10h → mood 100 → 66
        Assert.True(needs.State.Mood <= 66);
        player.Outdoors = true;             // now walk outside 2h

        needs.Tick(120 * 2);

        // decay -8, fresh air +10 → mood ~68 > 66
        Assert.True(needs.State.Mood > 66, $"mood {needs.State.Mood} should recover above 66");
    }

    [Fact]
    public void Mood_FreshAir_Indoors_NoGain()
    {
        var (needs, player, _, _, _, _, _, _, _) = Build();
        player.IsInVehicle = false;
        player.Outdoors = false;
        needs.Tick(120 * 10);               // to ~66

        needs.Tick(120 * 2);                // 2 more indoor hours → floor(66−6.67) = 59

        Assert.True(needs.State.Mood <= 59, $"mood {needs.State.Mood} should keep decaying indoors");
    }

    [Fact]
    public void Mood_DrivingCruising_HoldsLevel()
    {
        var (needs, player, _, _, _, _, _, _, _) = Build();
        player.IsInVehicle = false;
        player.Outdoors = false;
        needs.Tick(120 * 10);               // idle → mood ~66
        int before = needs.State.Mood;
        player.IsInVehicle = true;
        player.Position = System.Numerics.Vector3.Zero;
        needs.Tick(1);                      // anchor position
        for (int i = 0; i < 120; i++)       // 120 × 1s ticks at 10 m/s = 1 game hour cruising
        {
            player.Position += new System.Numerics.Vector3(10, 0, 0);
            needs.Tick(1);
        }

        // decay -4, drive +4 → holds
        Assert.True(needs.State.Mood >= before - 2, $"mood {needs.State.Mood} should hold ~{before}");
    }

    [Fact]
    public void Mood_ParkedVehicle_NoDrivingGain()
    {
        var (needs, player, _, _, _, _, _, _, _) = Build();
        player.IsInVehicle = false;
        player.Outdoors = false;
        needs.Tick(120 * 10);               // to ~66
        int before = needs.State.Mood;
        player.IsInVehicle = true;
        player.Position = System.Numerics.Vector3.Zero;
        needs.Tick(1);                      // anchor
        // position unchanged → speed 0 → no drive passive

        needs.Tick(120);                    // 1 game hour parked

        Assert.True(needs.State.Mood <= before - 2, $"mood {needs.State.Mood} should decay while parked");
    }

    [Fact]
    public void Tv_WatchNearTv_BoostsMood()
    {
        var (needs, _, _, notifier, _, sleep, _, _, _) = Build();
        sleep.TvNearby = true;
        needs.Tick(120 * 10);               // mood down
        int moodBefore = needs.State.Mood;

        Assert.True(needs.TryWatchTv());

        Assert.True(needs.State.Mood > moodBefore);
        Assert.Contains(notifier.Messages, m => m.Contains("watched TV"));
    }

    [Fact]
    public void Tv_NoTvNearby_Refused()
    {
        var (needs, _, _, notifier, _, sleep, _, _, _) = Build();
        sleep.TvNearby = false;

        Assert.False(needs.TryWatchTv());
        Assert.Contains(notifier.Messages, m => m.Contains("No TV nearby"));
    }
}
