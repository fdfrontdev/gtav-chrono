using System.Text.Json.Serialization;

namespace Chrono.Domain;

/// <summary>Root configuration model (contract per SRS §7 + Animation doc §7).</summary>
public sealed class ChronoConfig
{
    public string MenuKey { get; set; } = "F9";
    public DashConfig Dash { get; set; } = new();
    public TimeStopConfig TimeStop { get; set; } = new();
    public InvisibleConfig Invisible { get; set; } = new();
    public TeleportConfig Teleport { get; set; } = new();
    public FlyConfig Fly { get; set; } = new();
    public NpcConfig Npc { get; set; } = new();
    public JusticeConfig Justice { get; set; } = new();
    public VisualConfig Visual { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
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
