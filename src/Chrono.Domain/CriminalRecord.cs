using System.Collections.Generic;
using System.Linq;

namespace Chrono.Domain;

/// <summary>One recorded offense (SRS FR-1). Never auto-clears; only police-DB hack purges it.
/// <see cref="Charged"/> = already sentenced in a court session (S12 — every crime gets
/// charged at the next bust, real-world style).</summary>
public sealed record CrimeEvent(
    string Id,
    CrimeSeverity Severity,
    string Kind,          // e.g. "assault", "murder", "property_damage"
    string GameTime,      // ISO-ish stamp at record time
    string District,      // for media flavor
    bool Burned,          // face seen? (FR-1.4)
    bool Charged = false); // S12: charged in a past court session?

/// <summary>A court judgment (FR-8.4). <see cref="Id"/> is the relational key for the
/// conviction↔events junction (S22: SQLite — idempotent conviction rows, the
/// sequence of what each verdict sentenced is recoverable).</summary>
public sealed record Conviction(string Id, int Fine, int PrisonDays, string GameTime);

/// <summary>Sentencing result (FR-8.3).</summary>
public sealed record Sentence(int Fine, int PrisonDays);

/// <summary>
/// Permanent criminal record (FR-1.5). Capped to keep the file bounded (FR-1.6).
/// JSON-serializable for the record store.
/// </summary>
public sealed class CriminalRecord
{
    public const int MaxEvents = 500;

    public List<CrimeEvent> Events { get; set; } = new();
    public List<Conviction> Convictions { get; set; } = new();

    public int Count => Events.Count;
    public int ConvictionCount => Convictions.Count;

    public void Append(CrimeEvent e)
    {
        Events.Add(e);
        if (Events.Count > MaxEvents) Events.RemoveAt(0);
    }

    public void AddConviction(Conviction c) => Convictions.Add(c);
    /// <summary>Erase everything (police-DB hack, FR-6.2).</summary>
    public void Purge()
    {
        Events.Clear();
        Convictions.Clear();
    }

    public bool HasSeverity(CrimeSeverity severity) => Events.Any(e => e.Severity == severity);
}
