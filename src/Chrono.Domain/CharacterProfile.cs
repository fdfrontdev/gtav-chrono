namespace Chrono.Domain;

/// <summary>
/// Character identity (FR-7): age tracked in DAYS so prison time advances age naturally.
/// AgeYears derives from AgeDays (single source of truth, no drift).
/// </summary>
public sealed class CharacterProfile
{
    public const int DaysPerYear = 365;

    public int AgeDays { get; set; } = 27 * DaysPerYear;
    public string DateOfBirth { get; set; } = "1999-08-08";
    public int Surgeries { get; set; }

    public int AgeYears => AgeDays / DaysPerYear;

    /// <summary>Prison aging (FR-7.2/FR-9.4): served in-game days add to age.</summary>
    public void AddDays(int days)
    {
        if (days > 0) AgeDays += days;
    }

    public void RecordSurgery() => Surgeries++;
}
