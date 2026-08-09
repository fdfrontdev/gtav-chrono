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

    /// <summary>Yard-time window per in-game day (escape window, FR-10.1). Must be &lt; PrisonDayRealSeconds.</summary>
    public double PrisonYardSeconds { get; set; } = 10;

    /// <summary>Seconds until the court date after capture (real time — a full GTA
    /// game day is 48 real minutes, so the trial must NOT wait for it).</summary>
    public double TrialDelaySeconds { get; set; } = 45;

    /// <summary>World interaction key (clinic door, FR-5.2).</summary>
    public string InteractKey { get; set; } = "G";

    /// <summary>Warrant enforcement (S9): burned + visible + near civilians can tip
    /// the police (stars rise WITHOUT a new crime — the warrant IS the crime).
    /// Each report ESCALATES the wanted level (S12) until captured at 4★+.</summary>
    public bool WarrantReportEnabled { get; set; } = true;
    public double WarrantReportSeconds { get; set; } = 10;
    public double WarrantReportChance { get; set; } = 0.35;

    /// <summary>Unpaid fine converts to prison days (S12 — debtor's prison):
    /// $<see cref="FineToPrisonRate"/> short = 1 day served.</summary>
    public int FineToPrisonRate { get; set; } = 1000;

    /// <summary>S13 escape-by-choice: stealth/fight success chances, the decision
    /// window length, and solitary-confinement penalties for failed attempts.</summary>
    public double EscapeStealthChance { get; set; } = 0.5;
    public double EscapeFightChance { get; set; } = 0.7;
    public double EscapeChoiceSeconds { get; set; } = 10;
    public int SolitaryStealthDays { get; set; } = 3;
    public int SolitaryFightDays { get; set; } = 5;

    /// <summary>Bail (S15 — realism): during custody, G posts bail = fraction of the
    /// projected fine (min cost). Out on bail: warrant cleared, charges pending, court
    /// at the next arrest; a NEW crime revokes bail (warrant + escalation).</summary>
    public double BailFraction { get; set; } = 0.5;
    public int BailMinCost { get; set; } = 1000;

    /// <summary>Parole (S15 — realism): after a prison term, release is supervised for
    /// N game days — a new crime during parole = instant 3★+ + PAROLE VIOLATION.</summary>
    public int ParoleDays { get; set; } = 3;

    /// <summary>S19 — arrest confrontation: at 4★+ the police CONFRONT (no instant
    /// capture): comply (G) → cuffed; resist (X/Z/B) → they open fire (chance to be
    /// shot down → custody via death; else you break away, chase continues with a
    /// RESISTING ARREST charge). Window + cooldown in seconds.</summary>
    public double ConfrontationChoiceSeconds { get; set; } = 6;
    public double ResistCaptureChance { get; set; } = 0.6;
    public double ConfrontationCooldownSeconds { get; set; } = 45;

    /// <summary>S19 — use-of-force realism: a stationary, unarmed suspect at 3★+
    /// makes police stand down (stars decay; no shooting). Seconds of stillness.</summary>
    public double ComplianceSeconds { get; set; } = 3;
    public bool NewsEnabled { get; set; } = true;          // FR-4.1
    public bool ViralEnabled { get; set; } = true;         // FR-4.2
}
