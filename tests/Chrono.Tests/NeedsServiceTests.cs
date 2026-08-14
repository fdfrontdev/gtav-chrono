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
        public Vector3? VendingPosition { get; set; }
        public bool EateryNearby { get; set; }

        public void SpawnFoodProp(Vector3 position, string model) { SpawnedCount++; LastSpawn = position; }
        public void PlayEatAnim() => EatAnims++;
        public Vector3? FindVendingMachine(Vector3 center, float radiusM) => VendingPosition;
        public bool TryFindEatery(Vector3 center, float radiusM, out Vector3 spot) { spot = Vector3.Zero; return EateryNearby; }
    }

    internal sealed class FakeSleepBoundary : ISleepBoundary
    {
        public bool SpotAvailable { get; set; }
        public bool TryFindSleepSpot(Vector3 center, float radiusM, out Vector3 spot) { spot = Vector3.Zero; return SpotAvailable; }
    }

    private static (NeedsService needs, FakePlayer player, FakeRecordStore store, FakeNotifier notifier,
        FakeFoodBoundary food, FakeSleepBoundary sleep, FakeVfx vfx, FakeInput input) Build(bool withFood = true)
    {
        var player = new FakePlayer { Money = 1000, IsVisible = true };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var food = withFood ? new FakeFoodBoundary() : null!;
        var sleep = new FakeSleepBoundary();
        var vfx = new FakeVfx();
        var input = new FakeInput();
        var needs = new NeedsService(
            player, store, new NeedsConfig { GameHourRealSeconds = 120 },
            notifier, new FakeLog(), food, sleep, vfx, input: input);
        needs.Load();
        return (needs, player, store, notifier, food!, sleep, vfx, input);
    }

    [Fact]
    public void Decay_AppliesPerGameHour()
    {
        var (needs, _, _, _, _, _, _, _) = Build();
        int before = needs.State.Thirst;

        needs.Tick(120);                    // 1 game hour

        Assert.True(needs.State.Thirst < before);   // decayed
    }

    [Fact]
    public void Decay_FractionalHours_Carry()
    {
        var (needs, _, _, _, _, _, _, _) = Build();
        needs.Tick(60);                     // half an hour — nothing decays yet
        int before = needs.State.Hunger;

        needs.Tick(60);                     // + half = one full hour

        Assert.True(needs.State.Hunger < before);   // the half was carried, not lost
    }

    [Fact]
    public void TierTransitions_NotifyOnce()
    {
        var (needs, _, _, notifier, _, _, _, _) = Build();
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
        var (needs, player, _, _, _, _, _, _) = Build();
        needs.Tick(120 * 30);               // hunger + thirst way down

        Assert.Contains(0f, player.HealthRechargeCalls);   // regen blocked
        Assert.True(player.DamageTaken > 0);               // survivor drain
    }

    [Fact]
    public void EnergyBad_SlowsRun()
    {
        var (needs, player, _, _, _, _, _, _) = Build();
        needs.Tick(120 * 40);               // energy critical

        Assert.Contains(0.7f, player.RunSpeedCalls);       // critical multiplier
    }

    [Fact]
    public void ThirstCritical_DrunkVisual()
    {
        var (needs, player, _, _, _, _, _, _) = Build();
        needs.Tick(120 * 25);               // thirst < 15

        Assert.Contains(true, player.DrunkCalls);
    }

    [Fact]
    public void PassOut_WhenEnergyCritical_NeverKills()
    {
        var (needs, player, store, notifier, _, _, vfx, _) = Build();
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
        var (needs, _, _, notifier, _, sleep, _, _) = Build();
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
        var (needs, _, _, notifier, _, sleep, _, _) = Build();
        sleep.SpotAvailable = false;

        Assert.False(needs.TrySleep());
        Assert.Contains(notifier.Messages, m => m.Contains("No bed nearby"));
    }

    [Fact]
    public void EatAtEatery_Nearby_PaysAndRestores()
    {
        var (needs, player, _, notifier, food, _, _, _) = Build();
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
        var (needs, player, _, notifier, _, _, _, _) = Build();
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
        var (needs, player, _, notifier, food, _, _, _) = Build();
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
        var (needs, _, _, notifier, _, _, _, _) = Build();
        Assert.True(needs.TryOrderMeal(0));

        Assert.False(needs.TryOrderMeal(1));
        Assert.Contains(notifier.Messages, m => m.Contains("already on the way"));
    }

    [Fact]
    public void Delivery_Broke_Refused()
    {
        var (needs, player, _, notifier, _, _, _, _) = Build();
        player.Money = 10;

        Assert.False(needs.TryOrderMeal(0));
        Assert.Contains(notifier.Messages, m => m.Contains("Not enough cash for delivery"));
    }

    [Fact]
    public void Delivery_TeleportMidOrder_SpawnsAtNewPosition()
    {
        var (needs, player, _, _, food, _, _, _) = Build();
        needs.TryOrderMeal(0);
        player.Position = new Vector3(999, 999, 10);   // player moves across town

        needs.Tick(100);

        Assert.Equal(player.Position, food.LastSpawn);  // spawn follows the player (FR-C11)
    }

    [Fact]
    public void Needs_PersistEvery10Seconds()
    {
        var (needs, _, store, _, _, _, _, _) = Build();
        int savesBefore = store.SaveCount;

        needs.Tick(5);
        needs.Tick(5);
        needs.Tick(0.1);                                 // crosses 10s

        Assert.True(store.SaveCount > savesBefore);
        Assert.NotNull(store.Status.Needs);
    }
}
