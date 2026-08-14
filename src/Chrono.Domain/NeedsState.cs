using System;

namespace Chrono.Domain;

/// <summary>The four survivor needs (SRS FR-C1).</summary>
public enum NeedKind { Hunger, Thirst, Energy, Mood }

/// <summary>Severity tier per need (SRS FR-C3): OK 100-40 · Bad 40-15 · Critical &lt;15.</summary>
public enum NeedsTier { Ok, Bad, Critical }

/// <summary>
/// Survivor needs state (SRS FR-C1..C3) — pure decay/tier math, persisted via
/// the status store (schema v2). Decay is per GAME hour (config rates);
/// fractional hours carry between ticks so nothing is lost.
/// </summary>
public sealed class NeedsState
{
    public int Hunger { get; set; } = 100;
    public int Thirst { get; set; } = 100;
    public int Energy { get; set; } = 100;
    public int Mood { get; set; } = 100;

    /// <summary>Carry of &lt; 1 game hour between ticks (no decay loss).</summary>
    public double FractionalHours { get; set; }

    public int Value(NeedKind kind) => kind switch
    {
        NeedKind.Hunger => Hunger,
        NeedKind.Thirst => Thirst,
        NeedKind.Energy => Energy,
        _ => Mood
    };

    public NeedsTier Tier(NeedKind kind) => Value(kind) switch
    {
        < 15 => NeedsTier.Critical,
        < 40 => NeedsTier.Bad,
        _ => NeedsTier.Ok
    };

    public bool IsCritical(NeedKind kind) => Tier(kind) == NeedsTier.Critical;

    /// <summary>Decay over game hours. `active` = the player is exerting (sprint/dash/fly) → energy drains faster.
    /// `includeMood` = false during SLEEP — rest is restorative, it never sours your mood.</summary>
    public void ApplyGameHours(double hours, NeedsConfig c, bool active, bool includeMood = true)
    {
        if (hours <= 0) return;
        FractionalHours += hours;
        double whole = Math.Floor(FractionalHours);
        if (whole < 1) return;
        FractionalHours -= whole;
        Hunger = Decay(Hunger, whole * c.HungerPerGameHour);
        Thirst = Decay(Thirst, whole * c.ThirstPerGameHour);
        if (includeMood) Mood = Decay(Mood, whole * c.MoodPerGameHour);
        Energy = Decay(Energy, whole * c.EnergyIdlePerGameHour * (active ? c.EnergyActiveMultiplier : 1.0));
    }

    /// <summary>Restore a need (eat/drink/sleep/mood boost), capped at 100.</summary>
    public void Restore(NeedKind kind, int amount)
    {
        int v = Value(kind) + amount;
        if (v < 0) v = 0;
        if (v > 100) v = 100;
        switch (kind)
        {
            case NeedKind.Hunger: Hunger = v; break;
            case NeedKind.Thirst: Thirst = v; break;
            case NeedKind.Energy: Energy = v; break;
            default: Mood = v; break;
        }
    }

    private static int Decay(int value, double amount)
        => (int)Math.Max(0, value - amount);
}
