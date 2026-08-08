using System;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Justice orchestrator (S1 core): watches wanted-level increases (FR-1.2 proxy —
/// the game raises stars exactly when harm/damage is SEEN), records CrimeEvents,
/// burns the identity when the face was visible, and activates warrants. State
/// machine: Free/Wanted now; Captured/Trial/Prison hooks for S3/S4.
/// </summary>
public sealed class JusticeService
{
    private readonly IWantedMonitor _wanted;
    private readonly IPlayerContext _player;
    private readonly IRecordStore _store;
    private readonly IdentityService _identity;
    private readonly WarrantService _warrant;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly JusticeConfig _config;
    private readonly CriminalRecord _record;
    private readonly MediaService? _media;
    private int _lastStars;

    public JusticeService(
        IWantedMonitor wanted,
        IPlayerContext player,
        IRecordStore store,
        IdentityService identity,
        WarrantService warrant,
        INotifier notifier,
        ILogSink log,
        JusticeConfig config,
        MediaService? media = null)
    {
        _wanted = wanted;
        _player = player;
        _store = store;
        _identity = identity;
        _warrant = warrant;
        _notifier = notifier;
        _log = log;
        _config = config;
        _record = store.Load();
        _media = media;
        _lastStars = wanted.CurrentStars;
    }

    public JusticeState State { get; private set; } = JusticeState.Free;
    public CriminalRecord Record => _record;
    public IdentityService Identity => _identity;
    public WarrantService Warrant => _warrant;

    /// <summary>Per-tick: star edges → crimes; state derivation.</summary>
    public void Tick()
    {
        int stars = _wanted.CurrentStars;

        if (stars > _lastStars && _config.RecordFromWanted)
            OnStarsIncreased(stars);   // ONE event per episode, at the new max star level

        _lastStars = stars;

        if (State == JusticeState.Free && stars > 0) State = JusticeState.Wanted;
        else if (State == JusticeState.Wanted && stars == 0) State = JusticeState.Free;
    }

    private void OnStarsIncreased(int stars)
    {
        var severity = SeverityFromStars(stars);
        bool burned = _player.IsVisible;   // face seen? (invisible → no burn, FR-2.4)
        var evt = new CrimeEvent(
            Guid.NewGuid().ToString("N"),
            severity,
            "public_offense",
            DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss"),
            _player.GetDistrictName(),
            burned);

        _record.Append(evt);
        _store.SaveAtomic(_record);

        if (burned)
        {
            _identity.SetBurned();
            _warrant.Activate(evt.GameTime);
        }

        State = JusticeState.Wanted;
        _log.Info($"Crime recorded: {severity} (burned={burned}) in {evt.District}");
        _notifier.Show(burned
            ? $"CRIME RECORDED ({severity}) — they saw your face"
            : $"CRIME RECORDED ({severity}) — no face seen");
        _media?.ReportCrime(evt);   // S2: news/viral coverage
    }

    public static CrimeSeverity SeverityFromStars(int stars) => stars switch
    {
        <= 2 => CrimeSeverity.Minor,
        <= 4 => CrimeSeverity.Moderate,
        _ => CrimeSeverity.Severe
    };

    // --- hooks for later slices (S3 arrest/trial, S4 prison) ---

    public void OnCaptured() => State = JusticeState.Captured;
    public void OnTrialStarted() => State = JusticeState.Trial;
    public void OnImprisoned() => State = JusticeState.Prison;

    /// <summary>Release: aging happens here (FR-7.2) once S4 passes served days.</summary>
    public void OnReleased()
    {
        State = JusticeState.Free;
        _warrant.Clear();   // justice served (FR-8.4)
    }
}
