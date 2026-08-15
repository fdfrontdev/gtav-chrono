using System;
using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// v0.10 phone food delivery (SRS FR-C10/C11, ADR D6) — the motivation loop:
/// order → pay → wait → food prop spawns at your feet → eat. One pending
/// order; teleport-safe (spawns at the CURRENT position on arrival).
/// </summary>
public sealed class FoodDeliveryService
{
    private readonly IFoodBoundary _food;
    private readonly IPlayerContext _player;
    private readonly NeedsConfig _config;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly Action<FoodItem> _onEaten;   // NeedsService restores hunger/mood

    private FoodItem? _pending;
    private double _etaSeconds;
    private bool _arrived;

    public FoodDeliveryService(
        IFoodBoundary food,
        IPlayerContext player,
        NeedsConfig config,
        INotifier notifier,
        ILogSink log,
        Action<FoodItem> onEaten)
    {
        _food = food;
        _player = player;
        _config = config;
        _notifier = notifier;
        _log = log;
        _onEaten = onEaten;
    }

    public bool HasPendingOrder => _pending != null;
    public bool HasArrivedFood => _arrived;
    public string? PendingMealName => _pending?.Name;
    public double EtaSeconds => Math.Max(0, _etaSeconds);

    /// <summary>Order + pay (price + delivery fee). One order at a time (FR-C11).</summary>
    public bool TryOrder(FoodItem meal, int money)
    {
        if (_pending != null)
        {
            _notifier.Show("A delivery is already on the way");
            return false;
        }
        int total = meal.Price + _config.DeliveryFee;
        if (money < total)
        {
            _notifier.Show($"Not enough cash for delivery (${total:#,##0})");
            return false;
        }
        _player.AddMoney(-total);
        _pending = meal;
        _arrived = false;
        _etaSeconds = _config.DeliverySecondsMin
            + new Random().NextDouble() * (_config.DeliverySecondsMax - _config.DeliverySecondsMin);
        _notifier.Show($"ORDER PLACED — {meal.Name} in {Math.Ceiling(_etaSeconds)}s (${total:#,##0})");
        _log.Info($"Delivery ordered: {meal.Name} (${total}), eta {_etaSeconds:F0}s");
        return true;
    }

    /// <summary>v0.12: order a drink from the phone catalog (FR-D1).</summary>
    public bool TryOrderDrink(int drinkIndex, int money)
    {
        if (drinkIndex < 0 || drinkIndex >= FoodCatalog.DeliveryDrinks.Length) return false;
        return TryOrder(FoodCatalog.DeliveryDrinks[drinkIndex], money);
    }

    /// <summary>Per-frame countdown; arrival spawns the prop at the CURRENT position (meals only — drinks have no prop).</summary>
    public void Tick(double deltaSeconds)
    {
        if (_pending == null || _arrived) return;
        _etaSeconds -= deltaSeconds;
        if (_etaSeconds > 0) return;

        _arrived = true;
        if (!string.IsNullOrEmpty(_pending.PropModel))
            _food.SpawnFoodProp(_player.Position, _pending.PropModel!);
        string verb = _pending.IsDrink ? "drink" : "eat";
        _notifier.Show($"DELIVERY ARRIVED — press G to {verb} your {_pending.Name}");
    }

    /// <summary>Consume the delivered item (interact key or menu).</summary>
    public bool TryConsume()
    {
        if (_pending == null || !_arrived) return false;
        var meal = _pending;
        _pending = null;
        _arrived = false;
        if (meal.IsDrink) _food.PlayDrinkAnim();
        else _food.PlayEatAnim();
        _onEaten(meal);
        string msg = meal.IsDrink ? $"Drank the {meal.Name} — thirst quenched" : $"Ate the {meal.Name} — hunger restored";
        _notifier.Show(msg);
        _log.Info($"Delivered item consumed: {meal.Name}");
        return true;
    }

    /// <summary>Cancel (e.g. mission standby) — refund nothing (the courier kept the fee).</summary>
    public void Cancel() { _pending = null; _arrived = false; }
}
