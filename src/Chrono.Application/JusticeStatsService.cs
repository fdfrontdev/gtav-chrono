using System.Collections.Generic;
using System.Linq;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>Read-only justice snapshot for the F9 Criminal Record screen (S7).</summary>
public sealed record JusticeStats(
    IReadOnlyList<CrimeEvent> Crimes,       // newest first, capped at 20
    int ConvictionCount,
    IdentityState Identity,
    bool WarrantActive,
    int AgeDays,
    int Surgeries);

/// <summary>Builds the stats view from the store (menu display — never mutates).</summary>
public sealed class JusticeStatsService
{
    private readonly IRecordStore _store;
    private readonly IdentityService _identity;
    private readonly WarrantService _warrant;

    public JusticeStatsService(IRecordStore store, IdentityService identity, WarrantService warrant)
    {
        _store = store;
        _identity = identity;
        _warrant = warrant;
    }

    public JusticeStats GetStats()
    {
        var record = _store.Load();
        var profile = _store.LoadProfile();
        var status = _store.LoadStatus();
        return new JusticeStats(
            record.Events
                .OrderByDescending(e => e.GameTime)   // ISO timestamps sort lexically
                .Take(20)
                .ToList(),
            record.ConvictionCount,
            status.Identity,
            status.WarrantActive,
            profile.AgeDays,
            profile.Surgeries);
    }
}
