using System.Text.Json.Serialization;

namespace Chrono.Domain;

/// <summary>Root configuration model (contract per SRS §7 + Animation doc §7).</summary>
public sealed class ChronoConfig
{
    public string MenuKey { get; set; } = "Shift+0";   // S8: F9 collided with other bindings

    /// <summary>
    /// S22 (user UAT: "add a setting to toggle the mod on/off, superpowers
    /// on/off, justice on/off"): master switches. The MENU itself always works
    /// (it's the only way back in). Powers and justice freeze when disabled.
    /// </summary>
    public bool ModEnabled { get; set; } = true;
    public bool PowersEnabled { get; set; } = true;
    public bool JusticeEnabled { get; set; } = true;

    public DashConfig Dash { get; set; } = new();
    public TimeStopConfig TimeStop { get; set; } = new();
    public InvisibleConfig Invisible { get; set; } = new();
    public TeleportConfig Teleport { get; set; } = new();
    public FlyConfig Fly { get; set; } = new();
    public NpcConfig Npc { get; set; } = new();
    public JusticeConfig Justice { get; set; } = new();
    public VisualConfig Visual { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();

    // --- v0.10 (VA 04): combat powers, hack pricing, survivor needs ---
    public PowersConfig Powers { get; set; } = new();
    public HackConfig Hack { get; set; } = new();
    public NeedsConfig Needs { get; set; } = new();
}

public sealed class DashConfig
{
    public float Range { get; set; } = 12.0f;
    public float MaxRange { get; set; } = 30.0f;
    public string Hotkey { get; set; } = "X";   // Minato-style dash hotkey ("" = disabled)
}

public sealed class TimeStopConfig
{
    public bool FreezeProps { get; set; } = true;
    public bool PauseClock { get; set; } = true;
    public int MaintenanceIntervalMs { get; set; } = 2000;
    public int MaxFrozenEntities { get; set; } = 1024;
    public float FreezeRadius { get; set; } = 100.0f;   // 0 = no limit; keeps only visible entities
    public string Hotkey { get; set; } = "Z";           // quick toggle ("" = disabled)
}

public sealed class InvisibleConfig
{
    public string Hotkey { get; set; } = "B";           // quick toggle ("" = disabled)
}

public sealed class FlyConfig
{
    public float Speed { get; set; } = 25.0f;

    /// <summary>Velocity/heading smoothing rate (v0.8.0 natural flight feel). Higher = snappier.</summary>
    public float Acceleration { get; set; } = 6.0f;
}

public sealed class NpcConfig
{
    /// <summary>Grace period after a power use during which NPCs/police cannot
    /// instantly react to or track the player (realistic surprise → digest → search).</summary>
    public int ReactionDelayMs { get; set; } = 3000;
}

public sealed class TeleportConfig
{
    public float GroundProbeDistance { get; set; } = 100.0f;
}

public sealed class VisualConfig
{
    public TimeStopVisual TimeStop { get; set; } = new();
    public DashVisual Dash { get; set; } = new();
    public MapTeleportVisual MapTeleport { get; set; } = new();
}

public sealed class TimeStopVisual
{
    public float TintStrength { get; set; } = 0.4f;
}

public sealed class DashVisual
{
    public bool Enabled { get; set; } = true;
    public bool Trail { get; set; } = true;
}

public sealed class MapTeleportVisual
{
    public bool Enabled { get; set; } = true;
    public bool UseScreenFlash { get; set; } = false;
    public bool Shake { get; set; } = true;
}

public sealed class LoggingConfig
{
    public string Level { get; set; } = "info";
}

// ── v0.10 (VA 04 / SRS 05): combat powers + energy pool (FR-B) ──

public sealed class PowersConfig
{
    public int EnergyMax { get; set; } = 100;                 // FR-B1
    public double EnergyRegenPerSecond { get; set; } = 8;     // FR-B1

    public int PushCost { get; set; } = 25;                   // FR-B4
    public float PushRangeM { get; set; } = 12f;
    public float PushConeDeg { get; set; } = 60f;
    public float PushVehicleImpulse { get; set; } = 1.5f;
    public string PushHotkey { get; set; } = "N";

    public int BlastCost { get; set; } = 40;                  // FR-B5
    public float BlastRadiusM { get; set; } = 6f;
    public float BlastRangeM { get; set; } = 30f;             // fixed-distance aim (no raycast dependency)
    public float BlastDamageScale { get; set; } = 1.0f;
    public string BlastHotkey { get; set; } = "K";

    public int BulletTimeCostPerSecond { get; set; } = 12;    // FR-B6
    public float BulletTimeScale { get; set; } = 0.3f;
    public string BulletTimeHotkey { get; set; } = "V";

    public int RegenCost { get; set; } = 35;                  // FR-B7
    public int RegenSeconds { get; set; } = 5;
    public float RegenDamageResist { get; set; } = 0.5f;
    public string RegenHotkey { get; set; } = "U";
}

// ── v0.10: hack pricing (FR-A) ──

public sealed class HackConfig
{
    public int BaseCost { get; set; } = 10000;                // FR-A1
    public int PerEventCost { get; set; } = 1500;             // FR-A1 (per event + conviction)
}

// ── v0.10: survivor needs (FR-C) ──

public sealed class NeedsConfig
{
    public bool Enabled { get; set; } = true;                 // FR-C1
    public double GameHourRealSeconds { get; set; } = 120;    // 1 GTA game hour ≈ 2 real minutes

    // points per game hour (full→empty over N game hours)
    public double ThirstPerGameHour { get; set; } = 100.0 / 18.0;   // FR-C2 (kills fastest)
    public double HungerPerGameHour { get; set; } = 100.0 / 24.0;
    public double MoodPerGameHour { get; set; } = 100.0 / 30.0;
    public double EnergyIdlePerGameHour { get; set; } = 100.0 / 24.0;
    public double EnergyActiveMultiplier { get; set; } = 1.5;

    // survivor effects (FR-C4..C7)
    public double CriticalHungerDrainPerSecond { get; set; } = 1.2;
    public double CriticalThirstDrainPerSecond { get; set; } = 2.0;
    public double BadRunMultiplier { get; set; } = 0.85f;
    public double CriticalRunMultiplier { get; set; } = 0.7f;

    // pass-out + sleep (FR-C6/C12)
    public int PassOutSkipGameHours { get; set; } = 4;
    public int PassOutEnergyRestore { get; set; } = 60;
    public int SleepSkipGameHours { get; set; } = 6;
    public int SleepMoodGain { get; set; } = 20;

    // satisfaction (FR-C8..C11)
    public int DeliveryFee { get; set; } = 25;
    public int DeliverySecondsMin { get; set; } = 30;
    public int DeliverySecondsMax { get; set; } = 60;
    public int EatRestoreHunger { get; set; } = 45;
    public int EatMoodGain { get; set; } = 5;
    public int DrinkRestoreThirst { get; set; } = 40;
    public int EnergyDrinkRestore { get; set; } = 25;
    public int DrinkPrice { get; set; } = 8;
    public int EnergyDrinkPrice { get; set; } = 15;
    public int MealPrice { get; set; } = 20;
    public float EateryRadiusM { get; set; } = 8f;        // interact range at eateries/vending
    public bool HudBarsEnabled { get; set; } = true;          // FR-C14
}
