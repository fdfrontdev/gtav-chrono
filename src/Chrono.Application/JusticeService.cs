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
    private readonly ICrimeProbe? _crimeProbe;   // S20: act detection + police hold-fire
    private readonly Stopwatch _reportClock = Stopwatch.StartNew();
    private int _suppressStars;           // S12: suppress ONLY the edge at the report's
                                         // star level (a later, higher crime still records)
    private bool _wasPrisonTerm;          // release teleports only for real prison terms
    private int _reportStreak;            // S12: each recognition escalates the response
    private bool _onBail;                 // S15: out on bail — charges pending, court next arrest
    private int _paroleUntilDay;          // S15: supervised release after a prison term (0 = none)
    // S21 — physical capture (user UAT r15): police must REACH you (~3 m) to cuff you.
    private bool _surrenderPrompted;      // hands-up prompt shown this proximity episode
    private Vector3 _captureLastPos;      // movement gate: you must STOP to be cuffed
    // S19 — compliance: a stationary unarmed suspect makes police stand down
    private bool _complying;
    private Vector3 _lastPos;
    private readonly Stopwatch _complianceClock = new();
    private double _complianceElapsedMs;
    private bool _complianceArmed;
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
    private int _cellAnimStartedMs;   // S21 v3: one-shot idle re-arm clock
    private int _manhuntUntilDay;
    /// <summary>
    /// S21 v3 — sentence days REMAINING when the player escaped. Carried across
    /// the manhunt so a recapture = original remaining days + new charges
    /// (user UAT: "the escapee will be back to prison + court will add more
    /// sentence"). Cleared when the new verdict applies.
    /// </summary>
    private int _remainingDaysAtEscape;
    /// <summary>S21 v3: died INSIDE prison (escape attempt / yard) → respawn in the cell.</summary>
    private bool _diedInPrison;
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
        IPrisonOutfit? outfit = null,
        ICrimeProbe? crimeProbe = null)
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
        _crimeProbe = crimeProbe;
        _prisonCalendar = new PrisonCalendar(config.PrisonDayRealSeconds);
        _lastStars = wanted.CurrentStars;
    }

    public JusticeState State { get; private set; } = JusticeState.Free;
    public CriminalRecord Record => _record;
    public IdentityService Identity => _identity;
    public WarrantService Warrant => _warrant;
    public int ServedDays => _servedDays;
    public int SentenceDays => _sentenceDays;

    // ── S21: HUD widget probes (user UAT r15: on-screen feedback) ──

    /// <summary>Current wanted stars (widget + capture logic).</summary>
    public int CurrentStars => _wanted.CurrentStars;

    /// <summary>Seconds until the court date (0 when not captured).</summary>
    public double TrialSecondsLeft => State == JusticeState.Captured
        ? Math.Max(0, _config.TrialDelaySeconds - _trialElapsedMs / 1000.0)
        : 0;

    /// <summary>Progress (0..1) through the CURRENT prison day — for the widget bar.</summary>
    public double PrisonDayProgress => _prisonCalendar.DayProgressSeconds / Math.Max(1.0, _config.PrisonDayRealSeconds);

    /// <summary>Real seconds left in the current prison day (0 when not serving).</summary>
    public double PrisonDaySecondsLeft => State == JusticeState.Prison
        ? Math.Max(0, _config.PrisonDayRealSeconds - _prisonCalendar.DayProgressSeconds)
        : 0;

    // S21 — confinement gate: prison time only serves AFTER the intake cutscene
    // calls BeginConfinement (the S21 bug: State=Prison was set before the intake
    // cutscene, so PrisonTick served days from the service-construction Stopwatch
    // during the cutscene → a 14-day sentence released in ~1 second).
    private bool _confinementStarted;

    /// <summary>Per-tick: star edges → crimes; capture/trial/prison flow.</summary>
    public void Tick()
    {
        if (_input != null && _input.IsInteractKeyJustPressed)
        {
            if (State == JusticeState.Prison)
                TryOpenEscapeChoice();   // S13: G during confinement = escape-plan window
            else if (State == JusticeState.Captured)
                PostBail();              // S15: G during custody = post bail
            else if (State == JusticeState.Wanted)
                TrySurrender();          // S21: G near a cop = hands up (physical capture)
        }

        int stars = _wanted.CurrentStars;

        // Death edge FIRST — GTA may reset stars/state on death, so the wanted flag
        // must be captured before any cleanup runs (S7)
        bool dead = _player.IsDead;
        if (dead && !_wasDead)
        {
            // S21 v3 (user UAT: "shot dead during escape → respawn at HOSPITAL,
            // expected prison"): dying INSIDE prison (yard/escape attempt, stars=0)
            // is a death-in-custody — flag it so the respawn puts you back in the
            // cell, never the hospital.
            _diedInPrison = State == JusticeState.Prison;
            _diedWanted = stars > 0 || _episodeSeverity != null || State == JusticeState.Wanted;
            _deathMoneySnapshot = _player.GetMoney();
            _log.Info(_diedInPrison ? "Player died INSIDE prison — respawn in the cell"
                      : _diedWanted ? "Player died while wanted — custody on respawn"
                                    : "Player died (no wanted episode — no custody)");
        }
        else if (!dead && _wasDead)
        {
            if (_diedInPrison)
            {
                _diedInPrison = false;
                OnPrisonDeathRespawn();
            }
            else if (_diedWanted)
            {
                _diedWanted = false;
                OnDeathCapture();
            }
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
            && _episodeSeverity != null && State == JusticeState.Wanted && !_complying)
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

        // S21 — physical capture (user UAT r15): NO auto-cuff. Police must
        // physically REACH you (~3 m) while you're not escaping → they cuff you;
        // G = surrender when a cop is near; shot down while wanted → custody.
        UpdatePhysicalCapture();
        UpdateCompliance();   // S19/S20 hold-fire + stand-down (stationary unarmed)

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
        UpdateParole();
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

    /// <summary>
    /// S21 v3 (user UAT: "shot dead during escape → respawn at hospital,
    /// expected prison") — died INSIDE the prison (yard time / escape attempt).
    /// The sentence CONTINUES: back to the cell, yard closed, no hospital.
    /// </summary>
    private void OnPrisonDeathRespawn()
    {
        _player.Teleport(PrisonCenter);
        _isYardPhase = false;
        _yardNotified = false;
        _escapeChoiceOpen = false;
        _escapeChoiceDeadlineMs = 0;
        _player.PlayAnimationOnce(CuffedDict, CuffedAnim, 1500);
        _notifier.Show("WASTED during the escape — the guards drag you back to your cell");
        _log.Info("Prison death respawn — back in the cell, sentence continues");
    }

    /// <summary>Death while wanted → you wake up in POLICE CUSTODY (S7). GTA's $5k
    /// hospital fee is refunded — the court fine replaces it (one justice bill, not two).</summary>
    private void OnDeathCapture()
    {
        int delta = _deathMoneySnapshot - _player.GetMoney();
        if (delta != 0) _player.AddMoney(delta);

        State = JusticeState.Captured;
        _arrested = true;
        _trialElapsedMs = 0;
        _trialClock.Restart();
        _wanted.SetStars(0);              // custody — no wanted chase (S11)
        // S21 v3 (user UAT: "respawn at hospital — I expect prison"): death
        // capture wakes you AT the prison holding area, not the hospital —
        // the court scene + verdict play there (a recaptured fugitive doesn't
        // come back from the morgue, they come back in cuffs).
        _player.Teleport(PrisonCenter);
        bool manhuntEnded = IsManhunt;
        _manhuntUntilDay = 0;             // S21 v3: recaptured — the manhunt is OVER
        // S21 v3 (user UAT: "busted, wasted, captured → back to prison + more
        // sentence"): a manhunt death reads as a RECAPTURE — the escape charge
        // + remaining sentence await at trial.
        if (manhuntEnded)
            _notifier.Show("WASTED during the manhunt — RECAPTURED. The court adds the escape charge");
        else
            _notifier.Show("You wake up in POLICE CUSTODY — the court date is set");
        _cutscene?.Play(CutsceneKind.Arrest);   // booking cinematic (S11)
        _log.Info("Death-capture: custody started, hospital fee refunded");
    }

    private void OnStarsIncreased(int stars)
    {
        HandleBailAndParole(stars);
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
            // S19: only Moderate+ (3★+) offenses carry a standing warrant — a minor
            // scrape does NOT make the whole city report you forever (user UAT r13)
            if (severity >= CrimeSeverity.Moderate)
                _warrant.Activate(evt.GameTime);
        }

        // S17: while in custody (busted → court) the suspect is not "wanted on the
        // street" — new crimes are ADDED to the case instead; civilians must not
        // resume reporting during the countdown (user UAT round 11).
        if (State != JusticeState.Captured)
            State = JusticeState.Wanted;
        _log.Info($"Crime recorded: {severity} (burned={burned}) in {evt.District}");
        _notifier.Show(burned
            ? $"CRIME RECORDED ({severity}) — they saw your face"
            : $"CRIME RECORDED ({severity}) — no face seen");
        _media?.ReportCrime(evt);   // S2: news/viral coverage
    }

    /// <summary>
    /// S20 — record a classified ACT (ADR-04): the mod drives the wanted level from
    /// the act (murder → instant 5★) instead of inheriting the game's coarse stars.
    /// Witness gating is done by <see cref="CrimeDetectionService"/> BEFORE this is
    /// called (FR-1.4: only witnessed + visible acts record).
    /// </summary>
    public void RecordDetectedCrime(ClassifiedCrime crime)
    {
        HandleBailAndParole(crime.Stars);
        _episodeSeverity ??= crime.Severity;
        _reputation?.OnCrime(crime.Severity);
        bool burned = _player.IsVisible;
        var evt = new CrimeEvent(
            Guid.NewGuid().ToString("N"),
            crime.Severity,
            crime.Name,
            DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss"),
            _player.GetDistrictName(),
            burned);

        _record.Append(evt);
        _store.SaveAtomic(_record);

        if (burned)
        {
            _identity.SetBurned();
            if (crime.Severity >= CrimeSeverity.Moderate)
                _warrant.Activate(evt.GameTime);
        }

        if (State != JusticeState.Captured)
            State = JusticeState.Wanted;

        // Drive the wanted level from the ACT (ADR-04 D1) — and suppress the
        // star-proxy edge at this level so the same act doesn't record twice.
        _wanted.SetStars(crime.Stars);
        _suppressStars = crime.Stars;

        _log.Info($"Crime detected: {crime.Name} ({crime.Severity}) burned={burned} in {evt.District}");
        _notifier.Show(burned
            ? $"CRIME RECORDED ({crime.Name.ToUpperInvariant()}) — they saw your face"
            : $"CRIME RECORDED ({crime.Name.ToUpperInvariant()}) — no face seen");
        _media?.ReportCrime(evt);
    }

    /// <summary>S15 realism — a NEW crime while out on bail revokes it (warrant +
    /// escalation); a new crime on parole is an instant 3★+ violation.</summary>
    private void HandleBailAndParole(int stars)
    {
        if (_onBail)
        {
            _onBail = false;
            _warrant.Activate(DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss"));
            _media?.News("BAIL REVOKED — flight risk, warrant issued");
            _notifier.Show("BAIL REVOKED — new crime while on bail. No bail next time.");
            _log.Info("Bail revoked — new crime while out");
        }
        if (_paroleUntilDay > 0 && _clock.CurrentGameDay < _paroleUntilDay)
        {
            _wanted.SetStars(Math.Max(stars, 3));
            _paroleUntilDay = 0;
            _media?.News("PAROLE VIOLATION — warrant issued");
            _notifier.Show("PAROLE VIOLATION — the state was watching");
            _log.Info("Parole violated");
        }
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
        bool manhuntEnded = IsManhunt;
        _manhuntUntilDay = 0;             // S21 v3: recaptured — the manhunt is OVER
        _vfx?.ScreenFadeOut(300);
        _vfx?.ScreenFlash(300);
        if (manhuntEnded)
        {
            // prison-break vibe (user UAT): a manhunt ends with a RECAPTURE, not a routine arrest
            _notifier.Show("RECAPTURED! The manhunt is over — back to the system");
            _media?.News("MANHUNT OVER: escaped fugitive recaptured in " + _player.GetDistrictName());
        }
        else
        {
            _notifier.Show($"ARRESTED — bail {BailCost():$#,##0} (press G) or face the court");
            _media?.News($"BREAKING: {_player.GetCharacterName().ToUpperInvariant()} taken into custody in {_player.GetDistrictName()}");
        }
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
        // S21 v3: a recaptured escapee serves the REMAINING original sentence
        // PLUS the new charges ("court will add more sentence" — user UAT)
        int carried = Math.Max(0, _remainingDaysAtEscape);
        _remainingDaysAtEscape = 0;
        if (carried > 0) totalDays += carried;
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
        string carriedNote = carried > 0 ? $" + {carried} remaining" : "";
        string verdictLine = $"GUILTY — ${sentence.Fine} fine · {sentence.PrisonDays} day{(sentence.PrisonDays == 1 ? "" : "s")}{carriedNote}";
        _media?.News($"COURT: {_player.GetCharacterName().ToUpperInvariant()} sentenced — ${sentence.Fine} fine · {sentence.PrisonDays} days");
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

        _record.AddConviction(new Conviction(
            Guid.NewGuid().ToString("N"), paid, days,
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
        _confinementStarted = true;   // S21: gate prison time — see field comment
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
        // S21: never serve time before confinement actually starts (intake cutscene gate)
        if (!_confinementStarted) return;

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

    /// <summary>
    /// S21 v3 fix (user UAT: "while in prison I can't move — how do I escape?"):
    /// the cell idle must NEVER be an infinite loop — a looped full-body anim
    /// owns the ped task slot and blocks ALL movement input (the same bug class
    /// as the release-cuff loop). Played as a finite one-shot (CellIdleMs) that
    /// re-arms only while the player stays still, so movement is always possible
    /// — the yard escape (G) stays reachable every day.
    /// </summary>
    private const int CellIdleMs = 8000;

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
            // one-shot — ends on its own, so the player can ALWAYS move
            _player.PlayAnimationOnce(CellIdleDict, CellIdleAnim, CellIdleMs);
            _cellAnimPlaying = true;
            _cellAnimStartedMs = Environment.TickCount;
        }
        else if (Environment.TickCount - _cellAnimStartedMs >= CellIdleMs)
        {
            // the one-shot ended while still — re-arm (idle flavor, never a lock)
            _player.PlayAnimationOnce(CellIdleDict, CellIdleAnim, CellIdleMs);
            _cellAnimStartedMs = Environment.TickCount;
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
    public bool IsOnBail => _onBail;                        // S15
    /// <summary>S21 v3: yard time is open — the escape prompt (G) is live (widget hint).</summary>
    public bool IsYardPhase => _isYardPhase;
    /// <summary>S21 v3 (prison-break vibe): manhunt active after an escape — the
    /// whole state is looking for you until the heat dies down.</summary>
    public bool IsManhunt => _manhuntUntilDay > 0 && _clock.CurrentGameDay < _manhuntUntilDay;
    public int ManhuntUntilDay => _manhuntUntilDay;
    public int ParoleDaysLeft => Math.Max(0, _paroleUntilDay - _clock.CurrentGameDay);   // S15

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
        if (!_escapeChoiceOpen || _input == null) return;

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
        // S21 v3: keep the UNSERVED remainder — recapture = remaining + new charges
        _remainingDaysAtEscape = Math.Max(0, _sentenceDays - _servedDays);
        _sentenceDays = 0;
        _servedDays = 0;

        _outfit?.Restore();   // S13: out of prison → your own clothes back
        _player.ClearCurrentAnimation();   // S16: stop the cell-idle loop before the escape hop

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
        _media?.News($"PRISON BREAK: {_player.GetCharacterName().ToUpperInvariant()} escapes Bolingbroke — MANHUNT underway");
        _log.Info($"Prison escape via {kind} — manhunt until game-day {_manhuntUntilDay}");
    }

    /// <summary>Bail cost = fraction of the projected fine (all pending charges ×
    /// recidivism), floored at BailMinCost.</summary>
    public int BailCost()
    {
        var charges = _record.Events.Where(e => !e.Charged).ToList();
        if (charges.Count == 0) return _config.BailMinCost;
        double mult = Math.Min(3.0, 1 + 0.5 * Math.Max(0, _record.ConvictionCount));
        int projected = (int)Math.Round(charges.Sum(c => SentencingPolicy.BaseSentence(c.Severity).Fine) * mult);
        return Math.Max(_config.BailMinCost, (int)Math.Round(projected * _config.BailFraction));
    }

    /// <summary>S15: post bail during custody — released now, charges pending, court
    /// at the next arrest. A new crime while on bail revokes it (warrant + escalation).</summary>
    public void PostBail()
    {
        if (State != JusticeState.Captured || _onBail) return;
        if (_cutscene != null && _cutscene.IsActive)
        {
            _notifier.Show("The court is in session — it's too late for bail");
            return;
        }
        int cost = BailCost();
        if (_player.GetMoney() < cost)
        {
            _notifier.Show($"Not enough cash for bail (${cost:#,##0}) — the court it is");
            return;
        }
        _player.AddMoney(-cost);
        _onBail = true;
        _reportStreak = 0;   // S16: fresh start — the escalation ladder resets
        _arrested = false;
        State = JusticeState.Free;
        _player.ClearCurrentAnimation();   // S21 v3: bail = released — drop the cuffed loop
        _warrant.Clear();   // on bail = in the system's hands, not a fugitive
        _episodeSeverity = null;
        _media?.News($"COURT: suspect released on ${cost:#,##0} bail — charges pending");
        _notifier.Show($"Bail posted (${cost:#,##0}) — charges pending. Court at your next arrest.");
        _log.Info($"Bail posted for ${cost} — out pending trial");
    }

    /// <summary>Parole check (S15): supervised release expires after ParoleDays game days.</summary>
    private void UpdateParole()
    {
        if (_paroleUntilDay > 0 && _clock.CurrentGameDay >= _paroleUntilDay)
        {
            _paroleUntilDay = 0;
            _notifier.Show("Parole complete — you're a free citizen again");
            _log.Info("Parole period completed");
        }
    }

    /// <summary>
    /// S21 — physical capture (user UAT r15 ruling 1): police must physically
    /// REACH the player to cuff them. NO auto-cuff timer, NO forced court date
    /// on star count. Three paths into custody:
    ///   1. PROXIMITY — a cop within <see cref="JusticeConfig.CaptureRangeM"/>
    ///      (~3 m) while the player has STOPPED moving (not sprinting/dashing/
    ///      flying away) → the officers cuff you.
    ///   2. SURRENDER — G while a cop is within <see cref="JusticeConfig.SurrenderRangeM"/>
    ///      (~12 m) → hands up, custody (no shooting).
    ///   3. SHOT DOWN — death while wanted → custody on respawn (S7 path, unchanged).
    /// While you fight/run/teleport, the chase continues — capture is earned.
    /// </summary>
    private void UpdatePhysicalCapture()
    {
        if (State != JusticeState.Wanted || _arrested || _crimeProbe == null) return;

        float nearest = _crimeProbe.NearestPoliceDistanceM;
        if (nearest >= float.MaxValue / 2f)
        {
            _surrenderPrompted = false;   // cops gone — reset the prompt state
            _captureLastPos = _player.Position;
            return;
        }

        // Hands-up prompt when a cop closes in (12 m) — surrender is always on the table
        if (nearest <= _config.SurrenderRangeM && !_surrenderPrompted)
        {
            _surrenderPrompted = true;
            _notifier.Show($"POLICE! HANDS WHERE I CAN SEE THEM — press G to surrender");
            _log.Info($"Surrender prompt at {nearest:F1} m");
        }
        else if (nearest > _config.SurrenderRangeM)
        {
            _surrenderPrompted = false;
        }

        // Proximity capture: a cop got within ~3 m AND you stopped moving.
        // Moving resets the gate — running/teleporting = still a free suspect.
        bool stopped = (_player.Position - _captureLastPos).Length() < 1.0f;
        _captureLastPos = _player.Position;
        if (nearest <= _config.CaptureRangeM && stopped && !_player.IsInVehicle)
        {
            _log.Info($"Physical capture — cop at {nearest:F1} m, suspect stopped");
            OnCaptured();
        }
    }

    /// <summary>S21 — G near a cop = hands up → custody (no gunfight needed).</summary>
    private void TrySurrender()
    {
        if (_arrested || _crimeProbe == null) return;
        float nearest = _crimeProbe.NearestPoliceDistanceM;
        if (nearest > _config.SurrenderRangeM) return;

        _log.Info($"Surrendered to police at {nearest:F1} m");
        _notifier.Show("HANDS UP — the officers cuff you");
        OnCaptured();
    }

    /// <summary>S19/S20 use-of-force realism: at 2★+ (S20 lowered from 3★+), a
    /// stationary UNARMED suspect makes the officers stand down — hold-fire
    /// (S20: aim but don't shoot — user UAT r14). Moving or drawing a weapon
    /// re-engages the chase and lifts the hold.
    /// S21 v2 (user UAT): complying = ARREST, not star decay. Standing still
    /// with hands up lets the nearest officer (≤ surrenderRangeM) close in and
    /// cuff you — custody → trial → verdict clears the warrant, killing the
    /// "civilian reports → stars clear → reports again" loop.</summary>
    private void UpdateCompliance()
    {
        if (State != JusticeState.Wanted)
        {
            _crimeProbe?.SetPoliceHoldFire(false);
            return;
        }
        int stars = _wanted.CurrentStars;
        if (stars < _config.UseOfForceMinStars && !_complying)   // an ACTIVE stand-down runs all the way to 0
        {
            _complying = false;
            _complianceArmed = false;
            _crimeProbe?.SetPoliceHoldFire(false);
            return;
        }

        bool still = (_player.Position - _lastPos).Length() < 1.2f;
        _lastPos = _player.Position;
        bool unarmed = !_player.HasWeapon;

        // S20: the hold-fire is ACTIVE whenever the suspect is still + unarmed —
        // even during the armed-timer window, so cops never open fire on a
        // compliant suspect (ADR-04 D2). Idempotent at the boundary.
        _crimeProbe?.SetPoliceHoldFire(still && unarmed);

        _complianceElapsedMs += _complianceClock.Elapsed.TotalMilliseconds;
        _complianceClock.Restart();

        if (still && unarmed)
        {
            if (!_complianceArmed)
            {
                _complianceArmed = true;
                _complianceElapsedMs = 0;
            }
            if (!_complying && _complianceElapsedMs >= _config.ComplianceSeconds * 1000)
            {
                _complying = true;
                _notifier.Show("STAY STILL — hands up, the officers close in");
                _log.Info("Suspect still + unarmed — officers close in");
            }
            // S21 v2: complying with a cop in reach = UNDER ARREST. No star
            // decay, no "officers leave" — the officers cuff you (physical
            // capture via compliance, same range as the G-surrender rule).
            if (_complying && _crimeProbe != null
                && _crimeProbe.NearestPoliceDistanceM <= _config.SurrenderRangeM)
            {
                _log.Info($"Compliance capture — cop at {_crimeProbe.NearestPoliceDistanceM:F1} m");
                _notifier.Show("The officers cuff you — you are under arrest");
                OnCaptured();
            }
        }
        else
        {
            if (_complying || _complianceArmed)
            {
                _complying = false;
                _complianceArmed = false;
                _notifier.Show("You moved — the chase is back on");
            }
            _complianceElapsedMs = 0;
        }
    }

    /// <summary>S19 test seam — accumulate time only; the decay happens in
    /// UpdateCompliance during real ticks.</summary>
    public void AdvanceComplianceTime(double realSeconds)
        => _complianceElapsedMs += realSeconds * 1000;

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
        _player.ClearCurrentAnimation();   // S16: the cell-idle loop must not follow you out
        _warrant.Clear();   // justice served

        // S11: only REAL prison terms end at the prison gate — a fine-only release
        // is in place (no jarring teleport to Bolingbroke for a downtown fine)
        if (_wasPrisonTerm)
        {
            _player.Teleport(PrisonGate);
            _paroleUntilDay = _clock.CurrentGameDay + _config.ParoleDays;   // S15: supervised release
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
