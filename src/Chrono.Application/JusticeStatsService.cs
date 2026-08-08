using System.Collections.Generic;
using System.Linq;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>Read-only justice snapshot for the F9 Criminal Record screen (S7/S8).</summary>
public sealed record JusticeStats(
    IReadOnlyList<CrimeEvent> Crimes,       // newest first, capped at 20
    int ConvictionCount,
    int TotalFines,                          // sum of all conviction fines (S8)
    int DaysServed,                          // total prison days (S8)
    IdentityState Identity,
    bool WarrantActive,
    int AgeDays,
    int Surgeries,
    bool ClinicReady,
    bool HackReady,
    int Fame,                                // S9
    int Notoriety,
    string PublicImage);

/// <summary>Builds the stats view from the store (menu display — never mutates).</summary>
public sealed class JusticeStatsService
{
    private readonly IRecordStore _store;
    private readonly IdentityService _identity;
    private readonly WarrantService _warrant;
    private readonly IGameClock _clock;
    private readonly JusticeConfig _config;
    private readonly ReputationService? _reputation;

    public JusticeStatsService(IRecordStore store, IdentityService identity, WarrantService warrant, IGameClock clock, JusticeConfig config, ReputationService? reputation = null)
    {
        _store = store;
        _identity = identity;
        _warrant = warrant;
        _clock = clock;
        _config = config;
        _reputation = reputation;
    }

    public JusticeStats GetStats()
    {
        var record = _store.Load();
        var profile = _store.LoadProfile();
        var status = _store.LoadStatus();

        int totalFines = 0;
        foreach (var c in record.Convictions) totalFines += c.Fine;

        return new JusticeStats(
            record.Events
                .OrderByDescending(e => e.GameTime)   // ISO timestamps sort lexically
                .Take(20)
                .ToList(),
            record.ConvictionCount,
            totalFines,
            profile.DaysServed,
            status.Identity,
            status.WarrantActive,
            profile.AgeDays,
            profile.Surgeries,
            ClinicReady: status.LastSurgeryDay == 0
                || status.LastSurgeryDay + _config.SurgeryCooldownDays <= _clock.CurrentGameDay,
            HackReady: status.LastHackDay == 0
                || status.LastHackDay + _config.HackCooldownDays <= _clock.CurrentGameDay,
            Fame: status.Fame,
            Notoriety: status.Notoriety,
            PublicImage: _reputation?.PublicImage ?? "Unknown");
    }
}
