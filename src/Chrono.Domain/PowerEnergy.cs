using System;

namespace Chrono.Domain;

/// <summary>
/// Session energy pool (SRS FR-B1) — anime mana for combat powers.
/// Pure math: regen capped at Max, spend never below 0.
/// </summary>
public sealed record PowerEnergy(int Current, int Max, double RegenPerSecond)
{
    public static PowerEnergy Create(int max, double regenPerSecond)
        => new(Math.Max(0, max), Math.Max(0, max), Math.Max(0, regenPerSecond));

    /// <summary>Regen over real seconds, capped at Max.</summary>
    public PowerEnergy Tick(double deltaSeconds)
    {
        if (Current >= Max) return this;
        int next = (int)Math.Min(Max, Current + RegenPerSecond * Math.Max(0, deltaSeconds));
        return next == Current ? this : this with { Current = next };
    }

    public PowerEnergy Spend(int cost) => this with { Current = Math.Max(0, Current - cost) };

    public bool CanAfford(int cost) => Current >= cost;
}
