using System;

namespace Chrono.Domain;

/// <summary>
/// Pure sentencing table (FR-8.3): severity → {fine, prisonDays}, scaled by recidivism
/// (repeat offenders pay more: 1 + 0.5 × convictions). Fully testable.
/// </summary>
public static class SentencingPolicy
{
    public const int MinorFine = 2000;
    public const int ModerateFine = 8000;
    public const int SevereFine = 25000;
    public const int ModeratePrisonDays = 7;
    public const int SeverePrisonDays = 30;

    public static Sentence BaseSentence(CrimeSeverity severity) => severity switch
    {
        CrimeSeverity.Minor => new Sentence(MinorFine, 0),
        CrimeSeverity.Moderate => new Sentence(ModerateFine, ModeratePrisonDays),
        _ => new Sentence(SevereFine, SeverePrisonDays)
    };

    public static Sentence SentenceWith(CrimeSeverity severity, int convictions)
    {
        var baseSentence = BaseSentence(severity);
        // Real-world recidivism escalates (Bard Prison Initiative: ~50% reoffend) but
        // stays bounded for gameplay: 1.0 → 1.5 → 2.0 → ... capped at 3.0
        double multiplier = Math.Min(3.0, 1 + 0.5 * Math.Max(0, convictions));
        return new Sentence(
            (int)Math.Round(baseSentence.Fine * multiplier),
            (int)Math.Round(baseSentence.PrisonDays * multiplier));
    }
}
