using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    private readonly ReputationService? _reputation;
    private readonly IWorldProbe? _probe;
    private readonly JusticeCutsceneService? _cutscene;
    private readonly Func<double> _random;
    private readonly Stopwatch _reportClock = Stopwatch.StartNew();
    private int _suppressStars;           // S12: suppress ONLY the edge at the report's
                                         // star level (a later, higher crime still records)
    private bool _wasPrisonTerm;          // release teleports only for real prison terms
    private int _reportStreak;            // S12: each recognition escalates the response
    private readonly Stopwatch _escapeClock = Stopwatch.StartNew();   // S13: choice window
    private readonly IPrisonOutfit? _outfit;                          // S13: prison look
    private readonly CriminalRecord _record;
    private readonly Stopwatch _prisonRealClock = Stopwatch.StartNew();
    private readonly PrisonCalendar _prisonCalendar;
    private CrimeSeverity? _episodeSeverity;   // original offense of the current chase
    private int _lastStars;
    private double _trialElapsedMs;             // real-time court countdown (S8)
    private readonly Stopwatch _trialClock = Stopwatch.StartNew();
    private int _sentenceDays;
    private int _servedDays;
    private bool _arrested;
    private bool _isYardPhase;
    private bool _yardNotified;
    private Vector3 _lastCellPos;
    private bool _cellAnimPlaying;
    private int _manhuntUntilDay;
    private bool _wasDead;
    private bool _diedWanted;         // died during a wanted episode → custody on respawn
    private int _deathMoneySnapshot;  // hospital fee refund (justice, not GTA's $5k)

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
        IGameInput? input = null,
        ReputationService? reputation = null,
        IWorldProbe? probe = null,
        Func<double>? random = null,
        JusticeCutsceneService? cutscene = null,
        IPrisonOutfit? outfit = null)
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
        _reputation = reputation;
        _probe = probe;
        _random = random ?? (() => new Random().NextDouble());
        _cutscene = cutscene;
        _outfit = outfit;
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
        if (_input != null && _input.IsInteractKeyJustPressed && State == JusticeState.Prison)
            TryOpenEscapeChoice();   // S13: G during confinement = escape-plan window

        int stars = _wanted.CurrentStars;

        // Death edge FIRST — GTA may reset stars/state on death, so the wanted flag
        // must be captured before any cleanup runs (S7)
        bool dead = _player.IsDead;
        if (dead && !_wasDead)
        {
            _diedWanted = stars > 0 || _episodeSeverity != null || State == JusticeState.Wanted;
            _deathMoneySnapshot = _player.GetMoney();
            _log.Info(_diedWanted ? "Player died while wanted — custody on respawn"
                                  : "Player died (no wanted episode — no custody)");
        }
        else if (!dead && _wasDead && _diedWanted)
        {
            _diedWanted = false;
            OnDeathCapture();
        }
        _wasDead = dead;

        if (stars > _lastStars && _config.RecordFromWanted)
        {
            if (_suppressStars == stars) _suppressStars = 0;   // warrant report, not a crime
            else OnStarsIncreased(stars);   // ONE event per episode, at the new max star level
        }

        int previousStars = _lastStars;
        _lastStars = stars;

        // CHASE ESCAPE (S10): the episode ended WITHOUT capture → the police lost
        // you. Media loves a vanishing suspect (viral). Guarded against death/capture.
        if (stars == 0 && previousStars > 0 && !dead && !_arrested
            && _episodeSeverity != null && State == JusticeState.Wanted)
        {
            _episodeSeverity = null;
            OnChaseEscaped();
        }

        // Keep the episode severity through custody — the verdict sentences the
        // ORIGINAL offense (S11: stars are cleared at capture, so the episode must
        // survive until the court session; it clears on release)
        if (stars == 0 && !_arrested) _episodeSeverity = null;

        if (State == JusticeState.Free && stars > 0) State = JusticeState.Wanted;
        else if (State == JusticeState.Wanted && stars == 0) State = JusticeState.Free;

        // S3 flow
        if (State == JusticeState.Wanted && stars >= ArrestStars && !_arrested)
            OnCaptured();

        if (State == JusticeState.Captured && (_cutscene == null || !_cutscene.IsActive))
        {
            _trialElapsedMs += _trialClock.Elapsed.TotalMilliseconds;
            _trialClock.Restart();
            if (_trialElapsedMs >= _config.TrialDelaySeconds * 1000)
                OnTrialVerdict();
        }

        if (State == JusticeState.Prison)
            PrisonTick();

        UpdateManhunt();
        UpdateWarrantReports();
        _reputation?.Tick();
    }

    /// <summary>You lost the cops (S10): the chase ended without capture — viral news.</summary>
    private void OnChaseEscaped()
    {
        var district = _player.GetDistrictName();
        _media?.News($"POLICE LOSE SUPER-POWERED SUSPECT in {district}");
        _media?.Viral($"WEBNET: {district} chase footage goes viral — suspect vanishes");
        _log.Info($"Chase escaped in {district} — media frenzy");
    }

    /// <summary>Warrant enforcement (S9): burned + visible + near civilians → they
    /// call the police. Stars rise WITHOUT recording a new crime (the warrant IS
    /// the crime). Fame lowers the chance; notoriety raises it.</summary>
    private void UpdateWarrantReports()
    {
        if (!_config.WarrantReportEnabled) return;
        if (State != JusticeState.Free || !_warrant.IsActive || !_identity.IsBurned) return;
        if (!_player.IsVisible || _player.IsDead) return;             // unseen = unreported
        if (_probe == null || _probe.CountNearbyCivilians(_player.Position, 30f) == 0) return;
        if (_reportClock.ElapsedMilliseconds < _config.WarrantReportSeconds * 1000) return;
        _reportClock.Restart();

        double chance = _config.WarrantReportChance * (_reputation?.ReportChanceModifier ?? 1.0);
        if (_random() >= chance) return;

        // S12 escalation: recognition #1 → 1★, #2 → 2★ ... capped at 5★. A wanted
        // felon on the street draws a heavier response each time — capture (4★+) is
        // REACHABLE from reports alone, so the loop ends in a trial, not in limbo.
        int escalate = Math.Min(5, 1 + _reportStreak++);
        _suppressStars = escalate;
        _wanted.SetStars(escalate);
        _notifier.Show($"A civilian recognized you — police dispatched ({escalate}★)");
        _log.Info($"Warrant report #{_reportStreak}: police dispatched ({escalate}★)");
    }

    /// <summary>Death while wanted → you wake up in POLICE CUSTODY (S7). GTA's $5k
    /// hospital fee is refunded — the court fine replaces it (one justice bill, not two).</summary>
    private void OnDeathCapture()
    {
        // Refund GTA's hospital fee so the sentence fine is the ONLY charge
        int delta = _deathMoneySnapshot - _player.GetMoney();
        if (delta != 0) _player.AddMoney(delta);

        State = JusticeState.Captured;
        _arrested = true;
        _trialElapsedMs = 0;
        _trialClock.Restart();
        _wanted.SetStars(0);              // custody — no wanted chase (S11)
        _notifier.Show("You wake up in POLICE CUSTODY — the court date is set");
        _cutscene?.Play(CutsceneKind.Arrest);   // booking cinematic (S11)
        _log.Info("Death-capture: custody started, hospital fee refunded");
    }

    private void OnStarsIncreased(int stars)
    {
        var severity = SeverityFromStars(stars);
        _episodeSeverity ??= severity;   // sentence uses the ORIGINAL offense of the episode
        _reputation?.OnCrime(severity);  // S9: crimes build notoriety
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
        _trialElapsedMs = 0;
        _trialClock.Restart();
        _reportStreak = 0;                // justice pipeline takes over (S12)
        _wanted.SetStars(0);              // handcuffed — the chase is OVER (S11: no re-arrest loop)
        _vfx?.ScreenFadeOut(300);
        _vfx?.ScreenFlash(300);
        _notifier.Show("ARRESTED — the court date is set");
        _cutscene?.Play(CutsceneKind.Arrest);   // booking cinematic (S11)
        _log.Info("Captured at 4★+ — custody started");
    }

    /// <summary>Testable seam — the real loop accumulates elapsed time from the Stopwatch.</summary>
    public void AdvanceTrialTime(double realSeconds) => _trialElapsedMs += realSeconds * 1000;

    private void OnTrialVerdict()
    {
        // S12: EVERY uncharged crime in the record becomes a charge — real-world
        // style ("each crime will be charged; the fine + sentence total all of them").
        // Recidivism (repeat-offender multiplier) applies once to the totals.
        var charges = _record.Events.Where(e => !e.Charged).ToList();
        if (charges.Count == 0)
        {
            // no events on file (config edge) — fall back to the episode offense
            var severity = _episodeSeverity ?? CrimeSeverity.Minor;
            charges = new List<CrimeEvent>
            {
                new CrimeEvent("episode", severity, severity.ToString().ToLowerInvariant(),
                    DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss"), _player.GetDistrictName(), true)
            };
        }

        double multiplier = 1 + 0.5 * Math.Max(0, _record.ConvictionCount);
        int totalFine = (int)Math.Round(charges.Sum(c => SentencingPolicy.BaseSentence(c.Severity).Fine) * multiplier);
        int totalDays = (int)Math.Round(charges.Sum(c => SentencingPolicy.BaseSentence(c.Severity).PrisonDays) * multiplier);
        var sentence = new Sentence(totalFine, totalDays);

        // Mark every charge as sentenced (they stay visible in the record, flagged)
        for (int i = 0; i < _record.Events.Count; i++)
        {
            if (!_record.Events[i].Charged)
                _record.Events[i] = _record.Events[i] with { Charged = true };
        }
        _store.SaveAtomic(_record);

        // Court cinematic first (S11): the sentence applies when the gavel falls
        string chargeLine = $"{charges.Count} CHARGE{(charges.Count > 1 ? "S" : "")} — {ChargeSummary(charges)}";
        string verdictLine = $"GUILTY — ${sentence.Fine} fine · {sentence.PrisonDays} day{(sentence.PrisonDays == 1 ? "" : "s")}";
        if (_cutscene != null)
        {
            _cutscene.Play(CutsceneKind.Trial, () => ApplySentence(sentence), chargeLine, verdictLine);
        }
        else
        {
            ApplySentence(sentence);
        }

        _log.Info($"Verdict: {charges.Count} charges → ${sentence.Fine} fine, {sentence.PrisonDays}d (convictions={_record.ConvictionCount})");
    }

    private static string ChargeSummary(List<CrimeEvent> charges)
    {
        var parts = charges.GroupBy(c => c.Severity)
            .Select(g => $"{g.Count()}×{g.Key}")
            .OrderByDescending(p => p);
        return string.Join(", ", parts);
    }

    private void ApplySentence(Sentence sentence)
    {
        // Fine: seize what the player has; the SHORTFALL converts to prison time
        // (S12 — debtor's prison: $FineToPrisonRate short = 1 day served)
        int money = _player.GetMoney();
        int paid = Math.Min(sentence.Fine, money);
        _player.AddMoney(-paid);
        int shortfall = sentence.Fine - paid;
        int days = sentence.PrisonDays
            + (int)Math.Ceiling(shortfall / (double)Math.Max(1, _config.FineToPrisonRate));

        _record.AddConviction(new Conviction(paid, days,
            DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss")));
        _store.SaveAtomic(_record);
        _reputation?.OnConviction();   // S9: debt paid

        _vfx?.ScreenFadeOut(300);
        _vfx?.ScreenFlash(300);

        if (days > 0)
        {
            _sentenceDays = days;
            _servedDays = 0;
            State = JusticeState.Prison;
            _wasPrisonTerm = true;
            string shortfallNote = shortfall > 0 ? $" (${shortfall} unpaid → {days - sentence.PrisonDays}d served)" : "";
            _notifier.Show($"SENTENCED: ${paid} fine + {days} days{shortfallNote}");
            if (_cutscene != null)
                _cutscene.Play(CutsceneKind.Intake, BeginConfinement);   // intake cinematic (S11)
            else
                BeginConfinement();
        }
        else
        {
            _wasPrisonTerm = false;
            _notifier.Show($"SENTENCED: ${paid} fine — released");
            OnReleased();
        }
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
        _outfit?.ApplyPrison();   // S13: prisoner uniform
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
    /// <summary>Testable seam — advances the escape-plan decision window (S13).</summary>
    public void AdvanceEscapeTime(double realSeconds)
    {
        if (!_escapeChoiceOpen) return;
        if (_escapeChoiceDeadlineMs == 0) _escapeChoiceDeadlineMs = _escapeClock.Elapsed.TotalMilliseconds + _config.EscapeChoiceSeconds * 1000;
        _escapeChoiceDeadlineMs -= realSeconds * 1000;
    }

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
            _notifier.Show("Yard time — the fence is unlocked. Press G to choose your escape");
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

        // S13: NO auto-escape. Wandering past the yard radius is CONTAINMENT — a
        // guard escorts you back to your cell. Escaping is always the player's call.
        if (distFromCenter > PrisonConfineRadiusM)
        {
            _player.Teleport(PrisonCenter);
            _player.PlayAnimationOnce(CuffedDict, CuffedAnim, 1500);
            _notifier.Show("A guard escorts you back to your cell");
            _isYardPhase = false;
            return;
        }

        UpdateEscapeChoice();
    }

    // ── S13: escape is a CHOICE, not an accident ──────────────────────────────
    // Yard time → press G → 10s window → pick a method:
    //   X = POWERS (always works) · Z = STEALTH (chance) · B = FIGHT (chance)
    // Failure = caught → solitary confinement (+days), back in the cell.
    private bool _escapeChoiceOpen;
    private double _escapeChoiceDeadlineMs;

    /// <summary>Testable seam — true when the escape-plan window is open.</summary>
    public bool IsEscapeChoiceOpen => _escapeChoiceOpen;

    public void TryOpenEscapeChoice()
    {
        if (!_isYardPhase || _escapeChoiceOpen || _input == null) return;
        _escapeChoiceOpen = true;
        _escapeChoiceDeadlineMs = 0;
        _notifier.Show("ESCAPE PLAN — X: POWERS · Z: STEALTH · B: FIGHT (choose now)");
        _log.Info("Escape plan window opened (yard time)");
    }

    /// <summary>Player picks an escape method (S13). Failure → solitary confinement.</summary>
    public void ChooseEscape(EscapeKind kind)
    {
        if (!_escapeChoiceOpen) return;
        _escapeChoiceOpen = false;
        _escapeChoiceDeadlineMs = 0;

        bool succeeds = kind switch
        {
            EscapeKind.Dash or EscapeKind.Fly or EscapeKind.Invisible or EscapeKind.TimeStop => true,   // powers: guaranteed
            EscapeKind.Stealth => _random() < _config.EscapeStealthChance,
            _ => _random() < _config.EscapeFightChance
        };

        if (succeeds)
        {
            Escape(kind);
            return;
        }

        // Caught — solitary confinement
        int extraDays = kind == EscapeKind.Stealth ? _config.SolitaryStealthDays : _config.SolitaryFightDays;
        _sentenceDays += extraDays;
        _player.Teleport(PrisonCenter);
        _player.PlayAnimationOnce(CuffedDict, CuffedAnim, 1500);
        _isYardPhase = false;
        _notifier.Show($"CAUGHT! SOLITARY CONFINEMENT — {extraDays} extra days ({_sentenceDays} total)");
        _log.Info($"Escape attempt failed ({kind}) — +{extraDays} days solitary");
    }

    private void UpdateEscapeChoice()
    {
        if (!_escapeChoiceOpen) return;

        if (_escapeChoiceDeadlineMs == 0)
        {
            _escapeChoiceDeadlineMs = _escapeClock.Elapsed.TotalMilliseconds + _config.EscapeChoiceSeconds * 1000;
            return;
        }

        if (_escapeClock.Elapsed.TotalMilliseconds >= _escapeChoiceDeadlineMs)
        {
            _escapeChoiceOpen = false;
            _escapeChoiceDeadlineMs = 0;
            _notifier.Show("Escape plan abandoned");
            return;
        }

        if (_input.IsDashKeyJustPressed) ChooseEscape(EscapeKind.Dash);           // X = powers
        else if (_input.IsTimeStopHotkeyJustPressed) ChooseEscape(EscapeKind.Stealth);  // Z = stealth
        else if (_input.IsInvisibleHotkeyJustPressed) ChooseEscape(EscapeKind.Fight);   // B = fight
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
        _reputation?.OnEscape();                     // S9

        State = JusticeState.Free;
        _arrested = false;
        _sentenceDays = 0;
        _servedDays = 0;

        _outfit?.Restore();   // S13: out of prison → your own clothes back

        string flavor = kind switch
        {
            EscapeKind.Dash => "You blinked over the fence!",
            EscapeKind.Fly => "You flew over the wall!",
            EscapeKind.Invisible => "You slipped past the guards unseen!",
            EscapeKind.Stealth => "You snuck out through the loading bay!",
            EscapeKind.Fight => "You fought your way past the guards!",
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
            profile.DaysServed += _servedDays;   // stat page (S8)
            _store.SaveProfileAtomic(profile);
            int after = profile.AgeYears;
            if (after > before)
                _notifier.Show($"Time served — you are now {after} years old");
        }

        State = JusticeState.Free;
        _arrested = false;
        _sentenceDays = 0;
        _servedDays = 0;
        _reputation?.OnRelease();   // S9: rehabilitation builds fame
        _outfit?.Restore();   // S13: your own clothes back
        _warrant.Clear();   // justice served

        // S11: only REAL prison terms end at the prison gate — a fine-only release
        // is in place (no jarring teleport to Bolingbroke for a downtown fine)
        if (_wasPrisonTerm)
        {
            _player.Teleport(PrisonGate);
            if (_cutscene != null)
                _cutscene.Play(CutsceneKind.Release);   // release cinematic (S11)
        }
        else if (_cutscene != null)
        {
            _cutscene.Play(CutsceneKind.Release);
        }

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
