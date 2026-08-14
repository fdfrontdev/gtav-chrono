using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Police database hack (FR-6, ADR D3): the F9 cheat that erases your criminal history
/// entirely — events AND convictions gone, identity Clean, warrant cleared (a ghost in
/// the system). Refused while actively chased (you can't focus while cops are on you);
/// 1 in-game day cooldown. The clinic (S5) changes the FACE; the hack deletes the FILE.
/// </summary>
public sealed class PoliceDbHackService
{
    private readonly IWantedMonitor _wanted;
    private readonly IRecordStore _store;
    private readonly IdentityService _identity;
    private readonly WarrantService _warrant;
    private readonly JusticeService _justice;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly JusticeConfig _config;
    private readonly IGameClock _clock;
    private readonly IPlayerContext _player;      // v0.10: pays for the hack
    private readonly HackConfig _hackConfig;      // v0.10: pricing (FR-A1)
    private readonly VfxService? _vfx;
    private readonly ReputationService? _reputation;
    private readonly MediaService? _media;

    public PoliceDbHackService(
        IWantedMonitor wanted,
        IRecordStore store,
        IdentityService identity,
        WarrantService warrant,
        JusticeService justice,
        INotifier notifier,
        ILogSink log,
        JusticeConfig config,
        IGameClock clock,
        IPlayerContext player,
        HackConfig hackConfig,
        VfxService? vfx = null,
        ReputationService? reputation = null,
        MediaService? media = null)
    {
        _wanted = wanted;
        _store = store;
        _identity = identity;
        _warrant = warrant;
        _justice = justice;
        _notifier = notifier;
        _log = log;
        _config = config;
        _clock = clock;
        _player = player;
        _hackConfig = hackConfig;
        _vfx = vfx;
        _reputation = reputation;
        _media = media;
    }

    /// <summary>Attempt the hack. Returns true when the record was purged.</summary>
    public bool TryHack()
    {
        if (_wanted.CurrentStars > 0)   // FR-6.3: refused while actively chased
        {
            _notifier.Show("Can't hack while the cops are on you");
            return false;
        }

        // v0.10 (FR-A1/A2, ADR D1): no more day cooldown — the PRICE is the
        // gate. Erasing history costs money, scaled by the file size.
        int cost = HackPricingPolicy.Cost(_hackConfig, _justice.Record);
        if (_player.GetMoney() < cost)
        {
            _notifier.Show($"Not enough cash for the hack (${cost:#,##0}) — more crimes made it pricier");
            return false;
        }

        _player.AddMoney(-cost);                       // FR-A5: pay first
        _justice.PurgeRecord();                        // events + convictions gone (FR-6.1)
        var status = _store.LoadStatus();
        status.LastHackDay = _clock.CurrentGameDay;    // audit trail only — no lock
        _store.SaveStatusAtomic(status);
        _identity.SetClean();                          // no face on file
        _warrant.Clear();                              // nothing to warrant
        _reputation?.OnHack();                         // S9: ghost-hacker notoriety
        if (_media != null)
        {
            _media.News("POLICE DATABASE BREACHED — investigators baffled");
            _media.Viral("WEBNET: police records erased overnight — who is the ghost?");
        }

        _vfx?.ScreenFlash(200);
        _notifier.Show($"POLICE DB PURGED (${cost:#,##0}) — you don't exist anymore");
        _log.Info($"Police database hacked for ${cost} — record purged, identity Clean");
        return true;
    }
}
