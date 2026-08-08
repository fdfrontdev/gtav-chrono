namespace Chrono.Domain;

/// <summary>
/// Accelerated prison clock (FR-9.1): maps real seconds to in-game days.
/// Advance() returns true exactly on day boundaries. Safe for zero/negative dt.
/// </summary>
public sealed class PrisonCalendar
{
    private readonly double _dayRealSeconds;

    public int DayIndex { get; private set; }
    public double DayProgressSeconds { get; private set; }

    public PrisonCalendar(double dayRealSeconds)
    {
        _dayRealSeconds = dayRealSeconds > 0 ? dayRealSeconds : 30.0;
    }

    /// <summary>Advance the clock; true when an in-game day boundary was crossed.</summary>
    public bool Advance(double realSeconds)
    {
        if (realSeconds <= 0) return false;

        DayProgressSeconds += realSeconds;
        if (DayProgressSeconds < _dayRealSeconds) return false;

        DayProgressSeconds -= _dayRealSeconds;
        DayIndex++;
        return true;
    }
}
