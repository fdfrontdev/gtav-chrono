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
    private readonly VfxService? _vfx;

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
        VfxService? vfx = null)
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
        _vfx = vfx;
    }

    /// <summary>Attempt the hack. Returns true when the record was purged.</summary>
    public bool TryHack()
    {
        if (_wanted.CurrentStars > 0)   // FR-6.3: refused while actively chased
        {
            _notifier.Show("Can't hack while the cops are on you");
            return false;
        }

        var status = _store.LoadStatus();
        if (status.LastHackDay > 0
            && status.LastHackDay + _config.HackCooldownDays > _clock.CurrentGameDay)
        {
            int wait = status.LastHackDay + _config.HackCooldownDays - _clock.CurrentGameDay;
            _notifier.Show($"Police DB is locked down — retry in {wait} day(s)");
            return false;
        }

        _justice.PurgeRecord();                        // events + convictions gone (FR-6.1)
        status.LastHackDay = _clock.CurrentGameDay;
        _store.SaveStatusAtomic(status);
        _identity.SetClean();                          // no face on file
        _warrant.Clear();                              // nothing to warrant

        _vfx?.ScreenFlash(200);
        _notifier.Show("POLICE DB PURGED — you don't exist anymore");
        _log.Info("Police database hacked — record purged, identity Clean");
        return true;
    }
}
