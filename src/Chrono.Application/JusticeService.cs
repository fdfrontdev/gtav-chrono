using System;
using System.Diagnostics;
using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Justice orchestrator (S1 core + S2 media + S3 capture/trial/sentence).
/// Watches wanted-level increases (FR-1.2 proxy), records CrimeEvents, burns identity
/// when the face was visible, activates warrants. Capture at 5★ → arrest → court date
/// (1 in-game day) → verdict (fine + prison) → minimal confinement (full prison polish
/// incl. animations/escape is S4). Escaping clears stars but NEVER the warrant.
/// </summary>
public sealed class JusticeService
{
    private const int ArrestStars = 4;   // 4★ = Moderate (fine-only reachable); 5★ = Severe (prison)
    private const int PrisonConfineRadiusM = 90;   // minimal area lock until S4
    private static readonly Vector3 PrisonCenter = new(1826f, 2635f, 46f);
    private static readonly Vector3 PrisonGate = new(1878f, 2592f, 45.9f);

    private readonly IWantedMonitor _wanted;
    private readonly IPlayerContext _player;
    private readonly IRecordStore _store;
    private readonly IdentityService _identity;
    private readonly WarrantService _warrant;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly JusticeConfig _config;
    private readonly IGameClock _clock;
    private readonly MediaService? _media;
    private readonly VfxService? _vfx;
    private readonly CriminalRecord _record;
    private readonly Stopwatch _prisonRealClock = Stopwatch.StartNew();
    private readonly PrisonCalendar _prisonCalendar;
    private CrimeSeverity? _episodeSeverity;   // original offense of the current chase
    private int _lastStars;
    private int _trialDueDay;
    private int _sentenceDays;
    private int _servedDays;
    private bool _arrested;

    public JusticeService(
        IWantedMonitor wanted,
        IPlayerContext player,
        IRecordStore store,
        IdentityService identity,
        WarrantService warrant,
        INotifier notifier,
        ILogSink log,
        JusticeConfig config,
        IGameClock clock,
        MediaService? media = null,
        VfxService? vfx = null)
    {
        _wanted = wanted;
        _player = player;
        _store = store;
        _identity = identity;
        _warrant = warrant;
        _notifier = notifier;
        _log = log;
        _config = config;
        _clock = clock;
        _record = store.Load();
        _media = media;
        _vfx = vfx;
        _prisonCalendar = new PrisonCalendar(config.PrisonDayRealSeconds);
        _lastStars = wanted.CurrentStars;
    }

    public JusticeState State { get; private set; } = JusticeState.Free;
    public CriminalRecord Record => _record;
    public IdentityService Identity => _identity;
    public WarrantService Warrant => _warrant;
    public int ServedDays => _servedDays;
    public int SentenceDays => _sentenceDays;

    /// <summary>Per-tick: star edges → crimes; capture/trial/prison flow.</summary>
    public void Tick()
    {
        int stars = _wanted.CurrentStars;

        if (stars > _lastStars && _config.RecordFromWanted)
            OnStarsIncreased(stars);   // ONE event per episode, at the new max star level

        _lastStars = stars;

        if (stars == 0) _episodeSeverity = null;   // episode over — next chase re-seeds

        if (State == JusticeState.Free && stars > 0) State = JusticeState.Wanted;
        else if (State == JusticeState.Wanted && stars == 0) State = JusticeState.Free;

        // S3 flow
        if (State == JusticeState.Wanted && stars >= ArrestStars && !_arrested)
            OnCaptured();

        if (State == JusticeState.Captured && _clock.CurrentGameDay >= _trialDueDay)
            OnTrialVerdict();

        if (State == JusticeState.Prison)
            PrisonTick();
    }

    private void OnStarsIncreased(int stars)
    {
        var severity = SeverityFromStars(stars);
        _episodeSeverity ??= severity;   // sentence uses the ORIGINAL offense of the episode
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

    // --- S3: capture → trial → sentence (FR-8) ---

    private void OnCaptured()
    {
        _arrested = true;
        State = JusticeState.Captured;
        _trialDueDay = _clock.CurrentGameDay + 1;   // court date 1 in-game day later
        _vfx?.ScreenFadeOut(300);
        _vfx?.ScreenFlash(300);
        _notifier.Show("ARRESTED — court date tomorrow");
        _log.Info($"Arrested; trial due game-day {_trialDueDay}");
    }

    private void OnTrialVerdict()
    {
        // Sentenced for the ORIGINAL offense of the episode (a 2★ theft that escalated
        // to a 4★ chase is still a minor crime — FR-8.3, realism ruling)
        var severity = _episodeSeverity
            ?? (_record.Events.Count > 0 ? _record.Events[_record.Events.Count - 1].Severity : CrimeSeverity.Minor);
        var sentence = SentencingPolicy.SentenceWith(severity, _record.ConvictionCount);

        // Fine: seize cash (assets confiscated if short — never go negative)
        int money = _player.GetMoney();
        int fine = Math.Min(sentence.Fine, money);
        _player.AddMoney(-fine);

        _record.AddConviction(new Conviction(fine, sentence.PrisonDays,
            DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss")));
        _store.SaveAtomic(_record);

        _vfx?.ScreenFadeOut(300);
        _vfx?.ScreenFlash(300);

        if (sentence.PrisonDays > 0)
        {
            _sentenceDays = sentence.PrisonDays;
            _servedDays = 0;
            State = JusticeState.Prison;
            _notifier.Show($"SENTENCED: ${fine} fine + {sentence.PrisonDays} days");
            BeginConfinement();
        }
        else
        {
            _notifier.Show($"SENTENCED: ${fine} fine — released");
            OnReleased();
        }

        _log.Info($"Verdict: fine ${fine}, prison {sentence.PrisonDays}d (convictions={_record.ConvictionCount})");
    }

    // --- S3 minimal confinement (S4 adds animations/yard/escape/manhunt) ---

    private void BeginConfinement()
    {
        _prisonRealClock.Restart();
        _prisonCalendar.Reset();
        _player.Teleport(PrisonCenter);
        _notifier.Show($"PRISON TERM — {_sentenceDays} in-game days");
        _log.Info($"Confinement started at Bolingbroke ({_sentenceDays} days)");
    }

    private void PrisonTick()
    {
        // Accelerated day serving (FR-9.1): 1 in-game day ≈ prisonDayRealSeconds real time
        double dt = _prisonRealClock.Elapsed.TotalSeconds;
        _prisonRealClock.Restart();
        AdvancePrisonTime(dt);
        EnforceConfinement();
    }

    /// <summary>Serve real-seconds of prison time; fires day notifications and release.
    /// Testable seam — the real loop drives it from the Stopwatch.</summary>
    public void AdvancePrisonTime(double realSeconds)
    {
        if (!_prisonCalendar.Advance(realSeconds)) return;

        _servedDays++;
        _notifier.Show($"Day {_servedDays} of {_sentenceDays}");
        if (_servedDays >= _sentenceDays)
            OnReleased();
    }

    private void EnforceConfinement()
    {
        // Minimal area lock: wanderers get pulled back to the cell (S4 polishes this)
        if ((_player.Position - PrisonCenter).LengthSquared() > PrisonConfineRadiusM * PrisonConfineRadiusM)
        {
            _player.Teleport(PrisonCenter);
            _notifier.Show("Guards escort you back to your cell");
        }
    }

    /// <summary>Release: aging (FR-7.2), warrant cleared (justice served, FR-8.4).</summary>
    public void OnReleased()
    {
        // Aging: served in-game days advance the character's age (FR-7.2/FR-9.4)
        if (_servedDays > 0)
        {
            var profile = _store.LoadProfile();
            int before = profile.AgeYears;
            profile.AddDays(_servedDays);
            _store.SaveProfileAtomic(profile);
            int after = profile.AgeYears;
            if (after > before)
                _notifier.Show($"Time served — you are now {after} years old");
        }

        State = JusticeState.Free;
        _arrested = false;
        _sentenceDays = 0;
        _servedDays = 0;
        _warrant.Clear();   // justice served
        _player.Teleport(PrisonGate);
        _vfx?.ScreenFadeIn(300);
        _notifier.Show("RELEASED — justice served");
        _log.Info("Released from prison");
    }

    public static CrimeSeverity SeverityFromStars(int stars) => stars switch
    {
        <= 2 => CrimeSeverity.Minor,
        <= 4 => CrimeSeverity.Moderate,
        _ => CrimeSeverity.Severe
    };
}
