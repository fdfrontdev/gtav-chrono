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
    private const int PrisonConfineRadiusM = 90;   // yard fence distance
    private const int EscapeFenceM = 70;           // at-fence trigger distance (escape window)
    private const int ManhuntStars = 4;
    // Verified anim dicts (DurtyFree gta-v-data-dumps, 2026-08-08)
    private const string CuffedDict = "mp_arrest_paired";
    private const string CuffedAnim = "crook_p1_front";          // booking pose
    private const string CellIdleDict = "anim@heists@prison_heist";
    private const string CellIdleAnim = "ped_a_loop_a";          // prisoner idle loop
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
    private readonly IGameInput? _input;
    private readonly CriminalRecord _record;
    private readonly Stopwatch _prisonRealClock = Stopwatch.StartNew();
    private readonly PrisonCalendar _prisonCalendar;
    private CrimeSeverity? _episodeSeverity;   // original offense of the current chase
    private int _lastStars;
    private int _trialDueDay;
    private int _sentenceDays;
    private int _servedDays;
    private bool _arrested;
    private bool _isYardPhase;
    private bool _yardNotified;
    private Vector3 _lastCellPos;
    private bool _cellAnimPlaying;
    private int _manhuntUntilDay;

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
        VfxService? vfx = null,
        IGameInput? input = null)
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
        _input = input;
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

        UpdateManhunt();
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

    // --- S4: confinement with phases, animations, escape (FR-9/FR-10) ---

    private void BeginConfinement()
    {
        _prisonRealClock.Restart();
        _prisonCalendar.Reset();
        _isYardPhase = false;
        _yardNotified = false;
        _cellAnimPlaying = false;
        _lastCellPos = _player.Position;
        _player.Teleport(PrisonCenter);
        _player.PlayAnimationOnce(CuffedDict, CuffedAnim, 2000);   // booking pose (FR-9.3)
        _notifier.Show($"PRISON TERM — {_sentenceDays} in-game days");
        _log.Info($"Confinement started at Bolingbroke ({_sentenceDays} days)");
    }

    private void PrisonTick()
    {
        // Accelerated day serving (FR-9.1): 1 in-game day ≈ prisonDayRealSeconds real time
        double dt = _prisonRealClock.Elapsed.TotalSeconds;
        _prisonRealClock.Restart();
        AdvancePrisonTime(dt);
        if (State != JusticeState.Prison) return;   // released during the advance

        UpdateYardPhase();
        UpdateCellAnimation();
        CheckEscape();
    }

    /// <summary>Serve real-seconds of prison time; fires day notifications and release.
    /// Testable seam — the real loop drives it from the Stopwatch.</summary>
    public void AdvancePrisonTime(double realSeconds)
    {
        if (!_prisonCalendar.Advance(realSeconds)) return;

        _servedDays++;
        _isYardPhase = false;
        _yardNotified = false;
        _notifier.Show($"Day {_servedDays} of {_sentenceDays}");
        if (_servedDays >= _sentenceDays)
            OnReleased();
    }

    private void UpdateYardPhase()
    {
        // Yard opens at the END of each in-game day (FR-10.1 escape window)
        double yardOpenProgress = _config.PrisonDayRealSeconds - _config.PrisonYardSeconds;
        if (_prisonCalendar.DayProgressSeconds >= yardOpenProgress && !_yardNotified)
        {
            _isYardPhase = true;
            _yardNotified = true;
            _notifier.Show("Yard time — the fence is ahead. A power can get you out...");
        }
    }

    private void UpdateCellAnimation()
    {
        var pos = _player.Position;
        bool moving = (pos - _lastCellPos).LengthSquared() > 0.25f;
        _lastCellPos = pos;

        if (_isYardPhase || moving)
        {
            if (_cellAnimPlaying)
            {
                _player.ClearCurrentAnimation();
                _cellAnimPlaying = false;
            }
            return;
        }

        if (!_cellAnimPlaying)
        {
            _player.PlayLoopedAnimation(CellIdleDict, CellIdleAnim);   // cell idle (FR-9.3)
            _cellAnimPlaying = true;
        }
    }

    private void CheckEscape()
    {
        var pos = _player.Position;
        float distFromCenter = (pos - PrisonCenter).Length();

        // 1) Crossed the fence/radius (fly over the wall, any crossing) → escaped
        if (distFromCenter > PrisonConfineRadiusM)
        {
            Escape(EscapeKind.Fly);   // you got over the wall somehow
            return;
        }

        // 2) At the fence during yard time + a power hotkey pressed → escape (FR-10.1)
        if (!_isYardPhase || distFromCenter < EscapeFenceM || _input == null) return;

        if (_input.IsTimeStopHotkeyJustPressed) { Escape(EscapeKind.TimeStop); return; }
        if (_input.IsInvisibleHotkeyJustPressed) { Escape(EscapeKind.Invisible); return; }
        if (_input.IsFlyAscend || _input.IsFlyForward || _input.IsFlyRight || _input.IsFlyLeft)
        { Escape(EscapeKind.Fly); return; }
        if (_input.IsDashHotkeyPressed) { Escape(EscapeKind.Dash); return; }
    }

    /// <summary>Escape with a superpower (FR-10): fade out, teleport beyond the fence,
    /// ESCAPED on record, identity burned, warrant re-activated, 4★ manhunt + media.</summary>
    public void Escape(EscapeKind kind)
    {
        var pos = _player.Position;
        var dir = pos - PrisonCenter;
        if (dir.LengthSquared() < 0.01f) dir = new Vector3(0f, 1f, 0f);
        dir = Vector3.Normalize(dir);

        var outside = pos + dir * 20f;   // hop beyond the fence (same elevation — flat plateau)
        if (!TeleportMath.IsInsideWorldBounds(outside))
        {
            _notifier.Show("Escape route blocked — try another spot");
            return;
        }

        _vfx?.ScreenFadeOut(300);
        _player.Teleport(outside);
        _vfx?.ScreenFadeIn(300);

        // Consequences (FR-10.2): record + identity + warrant + manhunt + media
        _record.Append(new CrimeEvent(
            Guid.NewGuid().ToString("N"), CrimeSeverity.Moderate, "prison_escape",
            DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss"), "Bolingbroke", true));
        _store.SaveAtomic(_record);

        _identity.SetBurned();                       // the state knows your face
        _warrant.Activate(DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss"));
        _manhuntUntilDay = _clock.CurrentGameDay + 1;
        _wanted.SetStars(ManhuntStars);
        _media?.ReportEscape("Bolingbroke");

        State = JusticeState.Free;
        _arrested = false;
        _sentenceDays = 0;
        _servedDays = 0;

        string flavor = kind switch
        {
            EscapeKind.Dash => "You blinked over the fence!",
            EscapeKind.Fly => "You flew over the wall!",
            EscapeKind.Invisible => "You slipped past the guards unseen!",
            _ => "You froze the guards and walked out!"
        };
        _notifier.Show($"{flavor} ESCAPED — the whole state is looking for you");
        _log.Info($"Prison escape via {kind} — manhunt until game-day {_manhuntUntilDay}");
    }

    private void UpdateManhunt()
    {
        if (_manhuntUntilDay > 0 && _clock.CurrentGameDay >= _manhuntUntilDay)
        {
            _manhuntUntilDay = 0;
            _notifier.Show("The heat dies down... but your warrant is still active");
        }
    }

    /// <summary>Purge the record in place (police-DB hack, FR-6.1) — cached copy stays valid.</summary>
    public void PurgeRecord()
    {
        _record.Purge();
        _store.SaveAtomic(_record);
        _log.Info("Criminal record purged");
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
