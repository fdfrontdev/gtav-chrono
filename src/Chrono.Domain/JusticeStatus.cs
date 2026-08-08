namespace Chrono.Domain;

/// <summary>Justice flow state machine (HLD §3.1). Free/Wanted active in S1; Captured/
/// Trial/Prison driven by S3/S4.</summary>
public enum JusticeState { Free, Wanted, Captured, Trial, Prison }

/// <summary>
/// Persisted justice status (identity + warrant) — `status.json` (HLD data model).
/// </summary>
public sealed class JusticeStatus
{
    public IdentityState Identity { get; set; } = IdentityState.Clean;
    public bool WarrantActive { get; set; }
    public string? WarrantSinceGameTime { get; set; }

    /// <summary>Game-day of the last plastic surgery (clinic cooldown, FR-5.5); 0 = never.</summary>
    public int LastSurgeryDay { get; set; }
}
