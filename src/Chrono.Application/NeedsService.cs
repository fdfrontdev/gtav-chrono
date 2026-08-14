using System;
using System.Collections.Generic;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// v0.10 survivor needs (SRS FR-C1..C15, ADR D4/D5): hunger/thirst/energy/mood
/// decay on the game clock, tier effects applied through ports, satisfaction
/// via vending/eateries/delivery/sleep. Survivor but never a hard-lock:
/// pass-out skips time, vending is everywhere, delivery always available.
/// </summary>
public sealed class NeedsService
{
    private readonly IPlayerContext _player;
    private readonly IRecordStore _store;
    private readonly NeedsConfig _config;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly IFoodBoundary? _food;
    private readonly ISleepBoundary? _sleep;
    private readonly IVfxBoundary? _vfx;
    private readonly MediaService? _media;
    private readonly IGameInput? _input;
    private readonly FoodDeliveryService _delivery;

    private NeedsState _state = new();
    private readonly Dictionary<NeedKind, NeedsTier> _lastTier = new();
    private bool _loaded;
    private bool _passOutFired;
    private bool _drunkApplied;
    private double _persistAccumulator;

    public NeedsService(
        IPlayerContext player,
        IRecordStore store,
        NeedsConfig config,
        INotifier notifier,
        ILogSink log,
        IFoodBoundary? food = null,
        ISleepBoundary? sleep = null,
        IVfxBoundary? vfx = null,
        MediaService? media = null,
        IGameInput? input = null)
    {
        _player = player;
        _store = store;
        _config = config;
        _notifier = notifier;
        _log = log;
        _food = food;
        _sleep = sleep;
        _vfx = vfx;
        _media = media;
        _input = input;
        _delivery = new FoodDeliveryService(
            food ?? new NullFoodBoundary(), player, config, notifier, log,
            meal =>
            {
                _state.Restore(NeedKind.Hunger, meal.HungerRestore);
                _state.Restore(NeedKind.Mood, meal.MoodGain);
                Persist();
            });
    }

    public NeedsState State => _state;
    public FoodDeliveryService Delivery => _delivery;
    public bool Enabled => _config.Enabled;

    /// <summary>v0.10 (NFR-6): frozen during scripted missions/cutscenes.</summary>
    public bool Standby { get; set; }

    /// <summary>Load persisted needs (status store schema v2).</summary>
    public void Load()
    {
        var status = _store.LoadStatus();
        _state = status.Needs ?? new NeedsState();
        _loaded = true;
    }

    /// <summary>Per-frame: decay → effects → prompts → delivery timer → persist.</summary>
    public void Tick(double deltaSeconds)
    {
        if (!_config.Enabled || !_loaded || Standby) return;

        double gameHours = deltaSeconds / _config.GameHourRealSeconds;
        _state.ApplyGameHours(gameHours, _config, active: _player.IsInVehicle);

        ApplyEffects(deltaSeconds);
        CheckTierTransitions();
        _delivery.Tick(deltaSeconds);
        CheckWorldPrompts();

        _persistAccumulator += deltaSeconds;
        if (_persistAccumulator >= 10)
        {
            _persistAccumulator = 0;
            Persist();
        }
    }

    // ── survivor effects (FR-C4..C7) ──

    private void ApplyEffects(double dt)
    {
        bool blocked = _state.Tier(NeedKind.Hunger) != NeedsTier.Ok
                    || _state.Tier(NeedKind.Thirst) != NeedsTier.Ok;
        _player.SetHealthRechargeMultiplier(blocked ? 0f : 1f);

        if (_state.IsCritical(NeedKind.Hunger))
            _player.ApplyHealthDamage((float)(_config.CriticalHungerDrainPerSecond * dt));
        if (_state.IsCritical(NeedKind.Thirst))
            _player.ApplyHealthDamage((float)(_config.CriticalThirstDrainPerSecond * dt));

        float run = _state.Tier(NeedKind.Energy) switch
        {
            NeedsTier.Ok => 1f,
            NeedsTier.Bad => (float)_config.BadRunMultiplier,
            _ => (float)_config.CriticalRunMultiplier
        };
        _player.SetRunSpeedMultiplier(run);

        bool drunk = _state.IsCritical(NeedKind.Thirst);
        if (drunk != _drunkApplied)
        {
            _drunkApplied = drunk;
            _player.SetDrunkVisual(drunk);
        }

        // Pass-out (FR-C6): survivor, never lethal — fade, skip hours, wake.
        if (_state.IsCritical(NeedKind.Energy) && !_passOutFired)
        {
            _passOutFired = true;
            _vfx?.ScreenFadeOut(600);
            _notifier.Show("EXHAUSTION — you pass out...");
            _state.ApplyGameHours(_config.PassOutSkipGameHours, _config, active: false);
            _state.Restore(NeedKind.Energy, _config.PassOutEnergyRestore);
            _vfx?.ScreenFadeIn(600);
            _notifier.Show("You wake up hours later — find a bed before it happens again");
            _log.Info("Passed out (energy critical)");
            Persist();
        }
        else if (_state.Tier(NeedKind.Energy) == NeedsTier.Ok)
        {
            _passOutFired = false;   // re-arm once recovered
        }
    }

    private void CheckTierTransitions()
    {
        foreach (NeedKind kind in Enum.GetValues(typeof(NeedKind)))
        {
            var tier = _state.Tier(kind);
            if (_lastTier.TryGetValue(kind, out var prev) && prev == tier) continue;
            _lastTier[kind] = tier;
            if (tier == NeedsTier.Ok) continue;

            string msg = tier == NeedsTier.Critical
                ? kind switch
                {
                    NeedKind.Hunger => "STARVING — find food NOW",
                    NeedKind.Thirst => "DEHYDRATED — you need water",
                    NeedKind.Energy => "ABOUT TO PASS OUT — find a bed",
                    _ => "DEEP DEPRESSION — eat well, rest, show off"
                }
                : kind switch
                {
                    NeedKind.Hunger => "You're getting hungry...",
                    NeedKind.Thirst => "Your throat is dry...",
                    NeedKind.Energy => "You're getting tired...",
                    _ => "You're in a sour mood..."
                };
            _notifier.Show(msg);
            if (kind == NeedKind.Mood && tier == NeedsTier.Critical)
                _media?.Viral("WEBNET: local 'superhero' spotted looking miserable — the grind gets everyone");
        }
    }

    // ── world prompts: delivered food / vending (FR-C9/C11) ──

    private void CheckWorldPrompts()
    {
        if (_input == null || !_input.IsInteractKeyJustPressed) return;

        if (_delivery.HasArrivedFood)
        {
            _delivery.TryConsume();
            return;
        }
        if (_food != null && _food.FindVendingMachine(_player.Position, _config.EateryRadiusM).HasValue)
        {
            TryBuyDrink(energyDrink: _state.Energy < 50);
        }
    }

    /// <summary>Buy a drink at the nearest vending machine (FR-C9).</summary>
    public bool TryBuyDrink(bool energyDrink)
    {
        var drink = energyDrink ? FoodCatalog.Drinks[2] : FoodCatalog.Drinks[0];
        if (_player.GetMoney() < drink.Price)
        {
            _notifier.Show($"Not enough cash for a drink (${drink.Price})");
            return false;
        }
        _player.AddMoney(-drink.Price);
        _state.Restore(NeedKind.Thirst, drink.ThirstRestore);
        _state.Restore(NeedKind.Energy, drink.EnergyRestore);
        _food?.PlayEatAnim();   // drink anim reuses the eat slot — flavor
        _notifier.Show($"Drank {drink.Name} — thirst quenched");
        Persist();
        return true;
    }

    /// <summary>Eat at a nearby eatery (menu action; spot check in boundary) (FR-C8).</summary>
    public bool TryEatAtEatery()
    {
        if (_food == null || !_food.TryFindEatery(_player.Position, _config.EateryRadiusM, out _))
        {
            _notifier.Show("No eatery nearby — try the phone delivery instead");
            return false;
        }
        var meal = FoodCatalog.Meals[0];
        if (_player.GetMoney() < meal.Price)
        {
            _notifier.Show($"Not enough cash for a meal (${meal.Price})");
            return false;
        }
        _player.AddMoney(-meal.Price);
        _food.PlayEatAnim();
        _state.Restore(NeedKind.Hunger, meal.HungerRestore);
        _state.Restore(NeedKind.Mood, meal.MoodGain);
        _notifier.Show($"Ate {meal.Name} at the eatery");
        Persist();
        return true;
    }

    /// <summary>Phone delivery order (menu action) (FR-C10).</summary>
    public bool TryOrderMeal(int mealIndex)
    {
        if (mealIndex < 0 || mealIndex >= FoodCatalog.Meals.Length) return false;
        return _delivery.TryOrder(FoodCatalog.Meals[mealIndex], _player.GetMoney());
    }

    /// <summary>Sleep at the nearest bed/spot (FR-C12).</summary>
    public bool TrySleep()
    {
        if (_sleep == null || !_sleep.TryFindSleepSpot(_player.Position, _config.EateryRadiusM * 2f, out _))
        {
            _notifier.Show("No bed nearby — find a bed or a safehouse");
            return false;
        }
        _vfx?.ScreenFadeOut(600);
        _state.ApplyGameHours(_config.SleepSkipGameHours, _config, active: false, includeMood: false);   // sleep never sours mood
        _state.Restore(NeedKind.Energy, 100);
        _state.Restore(NeedKind.Mood, _config.SleepMoodGain);
        _vfx?.ScreenFadeIn(600);
        _notifier.Show("Refreshed — energy full");
        _log.Info("Slept — energy restored");
        Persist();
        return true;
    }

    /// <summary>v0.11 cheat: restore all four needs to full, reset tier flags (FR-B3).</summary>
    public void FillAll()
    {
        _state.Hunger = 100;
        _state.Thirst = 100;
        _state.Energy = 100;
        _state.Mood = 100;
        _passOutFired = false;
        _lastTier.Clear();
        Persist();
        _log.Info("Cheat: all needs filled");
    }

    /// <summary>Needs status lines for the menu (label, value, tier word).</summary>
    public (string Label, int Value, string Tier)[] StatusLines()
        => new[]
        {
            ("HUNGER", _state.Hunger, TierWord(NeedKind.Hunger)),
            ("THIRST", _state.Thirst, TierWord(NeedKind.Thirst)),
            ("ENERGY", _state.Energy, TierWord(NeedKind.Energy)),
            ("MOOD", _state.Mood, TierWord(NeedKind.Mood)),
        };

    private string TierWord(NeedKind kind) => _state.Tier(kind) switch
    {
        NeedsTier.Ok => "OK",
        NeedsTier.Bad => "BAD",
        _ => "CRITICAL"
    };

    private void Persist()
    {
        var status = _store.LoadStatus();
        status.Needs = _state;
        _store.SaveStatusAtomic(status);
    }

    /// <summary>Boundary-less fallback (tests / no food wiring) — delivery still works on paper.</summary>
    private sealed class NullFoodBoundary : IFoodBoundary
    {
        public void SpawnFoodProp(System.Numerics.Vector3 position, string model) { }
        public void PlayEatAnim() { }
        public System.Numerics.Vector3? FindVendingMachine(System.Numerics.Vector3 center, float radiusM) => null;
        public bool TryFindEatery(System.Numerics.Vector3 center, float radiusM, out System.Numerics.Vector3 spot) { spot = default; return false; }
    }
}
