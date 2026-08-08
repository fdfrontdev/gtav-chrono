namespace Chrono.Domain;

/// <summary>Justice layer configuration (SRS §7 contract). All knobs validated by ConfigValidator.</summary>
public sealed class JusticeConfig
{
    /// <summary>Record crimes from wanted-level increases (FR-1.2 proxy).</summary>
    public bool RecordFromWanted { get; set; } = true;

    public int ClinicBaseCost { get; set; } = 5000;        // FR-5.4
    public int PerEventCost { get; set; } = 1000;          // FR-5.4 (scales with record)
    public int SurgeryCooldownDays { get; set; } = 1;      // FR-5.5
    public int HackCooldownDays { get; set; } = 1;         // FR-6.4
    public double PrisonDayRealSeconds { get; set; } = 30; // FR-9.1 (1 in-game day ≈ 30 real s)
    public bool NewsEnabled { get; set; } = true;          // FR-4.1
    public bool ViralEnabled { get; set; } = true;         // FR-4.2
}
