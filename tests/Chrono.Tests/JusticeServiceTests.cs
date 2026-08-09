using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S1 justice core: wanted edges → crimes, burning, warrants, state machine.</summary>
public class JusticeServiceTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeRecordStore store, FakeNotifier notifier, FakeClock clock, FakeInput input, FakeCrimeProbe crimeProbe) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer();
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var clock = new FakeClock();
        var input = new FakeInput();
        var crimeProbe = new FakeCrimeProbe();
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        var service = new JusticeService(wanted, player, store, identity, warrant, notifier, new FakeLog(), new JusticeConfig { PrisonDayRealSeconds = 30 }, clock, null, null, input, crimeProbe: crimeProbe);
        return (service, wanted, player, store, notifier, clock, input, crimeProbe);
    }

    [Fact]
    public void StarsIncrease_RecordsSingleCrimeAtMaxSeverity()
    {
        var (service, wanted, _, store, _, _, _, crimeProbe) = Build();

        wanted.CurrentStars = 5;   // 0 → 5 jump = ONE Severe episode
        service.Tick();

        Assert.Single(store.Record.Events);
        Assert.Equal(CrimeSeverity.Severe, store.Record.Events[0].Severity);
        Assert.Equal("Vinewood", store.Record.Events[0].District);
    }

    [Fact]
    public void StarsIncrease_TwoStars_IsMinor()
    {
        var (service, wanted, _, store, _, _, _, crimeProbe) = Build();
        wanted.CurrentStars = 2;
        service.Tick();
        Assert.Equal(CrimeSeverity.Minor, store.Record.Events[0].Severity);
    }

    [Fact]
    public void StarsIncrease_FourStars_IsModerate()
    {
        var (service, wanted, _, store, _, _, _, crimeProbe) = Build();
        wanted.CurrentStars = 4;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        Assert.Equal(CrimeSeverity.Moderate, store.Record.Events[0].Severity);
    }

    [Fact]
    public void VisibleCrime_BurnsIdentity_AndActivatesWarrant()
    {
        var (service, wanted, player, store, _, _, _, crimeProbe) = Build();
        player.IsVisible = true;

        wanted.CurrentStars = 3;
        service.Tick();

        Assert.True(store.Record.Events[0].Burned);
        Assert.Equal(IdentityState.Burned, store.Status.Identity);
        Assert.True(store.Status.WarrantActive);
    }

    [Fact]
    public void InvisibleCrime_DoesNotBurn()
    {
        // FR-2.4: no face seen while invisible → identity stays Clean, no warrant
        var (service, wanted, player, store, _, _, _, crimeProbe) = Build();
        player.IsVisible = false;

        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed

        Assert.False(store.Record.Events[0].Burned);
        Assert.Equal(IdentityState.Clean, store.Status.Identity);
        Assert.False(store.Status.WarrantActive);
    }

    [Fact]
    public void RecordFromWantedDisabled_NoEvent()
    {
        var (service, wanted, _, store, _, _, _, crimeProbe) = Build();
        var config = new JusticeConfig { RecordFromWanted = false };
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        service = new JusticeService(wanted, new FakePlayer(), store, identity, warrant, new FakeNotifier(), new FakeLog(), config, new FakeClock());

        wanted.CurrentStars = 4;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed

        Assert.Empty(store.Record.Events);
    }

    [Fact]
    public void NoStarChange_NoEvent()
    {
        var (service, wanted, _, store, _, _, _, crimeProbe) = Build();
        wanted.CurrentStars = 0;
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();

        Assert.Empty(store.Record.Events);
    }

    [Fact]
    public void StarsDrop_StateReturnsToFree_WarrantPersists()
    {
        var (service, wanted, player, store, _, _, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 3;
        service.Tick();
        Assert.Equal(JusticeState.Wanted, service.State);

        wanted.CurrentStars = 0;   // escape — stars clear...
        service.Tick();

        Assert.Equal(JusticeState.Free, service.State);
        Assert.True(store.Status.WarrantActive, "escaping must NOT clear the warrant (FR-3.1)");
    }

    [Fact]
    public void SeverityFromStars_MatchesTable()
    {
        Assert.Equal(CrimeSeverity.Minor, JusticeService.SeverityFromStars(1));
        Assert.Equal(CrimeSeverity.Minor, JusticeService.SeverityFromStars(2));
        Assert.Equal(CrimeSeverity.Moderate, JusticeService.SeverityFromStars(3));
        Assert.Equal(CrimeSeverity.Moderate, JusticeService.SeverityFromStars(4));
        Assert.Equal(CrimeSeverity.Severe, JusticeService.SeverityFromStars(5));
    }

    [Fact]
    public void Release_ClearsWarrant()
    {
        // FR-8.4: justice served → warrant cleared
        var (service, wanted, player, store, _, _, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        Assert.True(service.Warrant.IsActive);

        service.OnReleased();

        Assert.Equal(JusticeState.Free, service.State);
        Assert.False(service.Warrant.IsActive);
    }

    // --- S3: capture → trial → sentence (FR-8) ---

    [Fact]
    public void FourStars_TriggersArrest()
    {
        // FR-8.1 (amended): capture at 4★+ — Moderate crimes can end in arrest
        var (service, wanted, player, _, notifier, _, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 4;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S21: cop reaches you → cuffed

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Contains(notifier.Messages, m => m.Contains("ARRESTED"));
    }

    [Fact]
    public void Arrest_FiresOnce_NotEveryTick()
    {
        var (service, wanted, player, _, notifier, _, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.Tick();
        service.Tick();

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Single(notifier.Messages, m => m.Contains("ARRESTED"));
    }


    /// S12: warrant-report escalation — 4 recognitions raise stars 1★→4★ and the
    /// capture fires with NO new crimes recorded (the original event is the only charge).
    private static void EscalateToCapture(JusticeService service, FakeWantedMonitor wanted, FakeCrimeProbe crimeProbe)
    {
        for (int i = 0; i < 3; i++)
        {
            service.Tick();              // report → stars rise
            wanted.CurrentStars = 0;     // escape
            service.Tick();
        }
        service.Tick();                  // report #4 → 4★
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                  // S21: cop reaches you → cuffed
    }

    [Fact]
    public void TrialDayArrives_FineOnlySentence_ReleasesAndDeductsMoney()
    {
        // S12: a Minor offense + report escalation → captured with ONE charge →
        // fine-only release, warrant cleared (justice served)
        var (service, wanted, player, store, notifier, _, crimeProbe) = BuildWithProbe(0.0, burned: true);
        wanted.CurrentStars = 2;
        service.Tick();                  // Minor event recorded
        wanted.CurrentStars = 0;
        service.Tick();                  // escape
        EscalateToCapture(service, wanted, crimeProbe);
        Assert.Equal(JusticeState.Captured, service.State);

        service.AdvanceTrialTime(45.0);
        service.Tick();                  // court time elapsed → verdict

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(player.MoneyCalls, m => m == -2000);
        Assert.Single(store.Record.Convictions);
        Assert.Contains(notifier.Messages, m => m.Contains("SENTENCED"));
        Assert.False(service.Warrant.IsActive);   // justice served
    }

    [Fact]
    public void TrialDayArrives_PrisonSentence_ConfinementStarts()
    {
        // 5★ crime (Severe: 25000 + 30d) → prison
        var (service, wanted, player, store, _, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();                  // crime + arrest same tick
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // verdict

        Assert.Equal(JusticeState.Prison, service.State);
        Assert.Equal(30, service.SentenceDays);
        Assert.Single(player.TeleportCalls);   // confinement teleport
        Assert.Contains(player.MoneyCalls, m => m == -25000);
        Assert.Single(store.Record.Convictions);
    }

    [Fact]
    public void Recidivism_SecondVerdict_HarsherFine()
    {
        var (service, wanted, player, store, _, _, crimeProbe) = BuildWithProbe(0.0, burned: true);

        // First conviction: Minor offense → fine 2000 (multiplier 1.0) → release
        wanted.CurrentStars = 2;
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted, crimeProbe);
        service.AdvanceTrialTime(45.0);
        service.Tick();
        Assert.Equal(JusticeState.Free, service.State);
        Assert.Single(store.Record.Convictions);
        Assert.Contains(player.MoneyCalls, m => m == -2000);

        // Second offense: Moderate (3★ — S19: only Moderate+ crimes carry a warrant,
        // so the escalation loop can run again) → recidivism multiplier 1.5 → 12000
        wanted.CurrentStars = 3;
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted, crimeProbe);
        service.AdvanceTrialTime(45.0);
        service.Tick();

        Assert.Contains(player.MoneyCalls, m => m == -12000);
        Assert.Equal(2, store.Record.ConvictionCount);
    }

    [Fact]
    public void PrisonDays_Served_ReleasesAndAges()
    {
        var (service, wanted, player, store, _, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S21: cop reaches you -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // 30-day sentence, confined
        Assert.Equal(JusticeState.Prison, service.State);

        // Serve 30 in-game days (30s each, accelerated) — deterministic via the seam
        for (int i = 0; i < 30; i++) service.AdvancePrisonTime(30.0);

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Equal(27 * 365 + 30, store.Profile.AgeDays);   // FR-7.2 aging
        Assert.False(service.Warrant.IsActive);               // justice served
    }

    [Fact]
    public void PrisonWanderer_CrossesRadius_GuardEscortsBack()
    {
        // S13: NO auto-escape — wandering past the yard radius is containment; a
        // guard brings you back to the cell. Escaping is always the player's choice.
        var (service, wanted, player, store, notifier, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // confined

        player.Position = new System.Numerics.Vector3(1826 + 100, 2635, 46);   // just beyond the fence
        service.Tick();

        Assert.Equal(JusticeState.Prison, service.State);                    // still confined
        Assert.Contains(notifier.Messages, m => m.Contains("escorts you back"));
        Assert.DoesNotContain(store.Record.Events, e => e.Kind == "prison_escape");
    }

    // --- S4: yard time + escape with powers (FR-10) ---

    [Fact]
    public void YardTime_OpensAtEndOfDay_WithHint()
    {
        var (service, wanted, player, _, notifier, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // confined (day = 30s, yard opens at 20s)

        service.AdvancePrisonTime(20.0); // yard window reached (no day boundary)
        service.Tick();                  // PrisonTick → UpdateYardPhase

        Assert.Contains(notifier.Messages, m => m.Contains("Yard time"));
    }

        [Fact]
    public void Escape_Choice_TimeStopFreezesGuards()
    {
        var (service, wanted, player, _, notifier, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();

        service.AdvancePrisonTime(20.0);
        service.Tick();
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.TimeStop);

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(notifier.Messages, m => m.Contains("froze the guards"));
    }


    [Fact]
    public void Escape_Choice_PowersBlink()
    {
        // S13: escape is a CHOICE — yard time, open the plan, pick powers (X)
        var (service, wanted, player, _, notifier, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();

        service.AdvancePrisonTime(20.0);
        service.Tick();                  // yard opens
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Dash);

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(notifier.Messages, m => m.Contains("blinked over the fence"));
    }

    [Fact]
    public void Escape_Choice_InvisibleSlipsPastGuards()
    {
        // S13: stealth-flavored power pick — invisibility (the choice mechanic, not the fence)
        var (service, wanted, player, _, notifier, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();

        service.AdvancePrisonTime(20.0);
        service.Tick();
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Invisible);

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(notifier.Messages, m => m.Contains("slipped past the guards"));
    }

    [Fact]
    public void Escape_Choice_FlyOverWall()
    {
        // S13: fly is a power pick in the escape plan
        var (service, wanted, player, _, notifier, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();

        service.AdvancePrisonTime(20.0);
        service.Tick();
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Fly);

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(notifier.Messages, m => m.Contains("flew over the wall"));
    }

    [Fact]
    public void Escape_SetsManhuntStars_AndMedia()
    {
        var (service, wanted, player, store, _, clock, input, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();

        service.AdvancePrisonTime(20.0);
        service.Tick();
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Dash);   // S13: player-chosen escape

        Assert.Contains(wanted.StarSets, s => s == 4);   // manhunt stars (FR-10.2)
        Assert.Equal(JusticeState.Free, service.State);
        Assert.True(service.Warrant.IsActive);
    }

    [Fact]
    public void Manhunt_ExpiresAfterOneGameDay()
    {
        var (service, wanted, player, _, notifier, clock, input, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();

        service.AdvancePrisonTime(20.0);
        service.Tick();
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.TimeStop);   // escape — manhunt until day + 1

        clock.CurrentGameDay = clock.CurrentGameDay + 1;
        service.Tick();

        Assert.Contains(notifier.Messages, m => m.Contains("heat dies down"));
    }

    [Fact]
    public void Confinement_BookingPoseAndCellIdle()
    {
        var (service, wanted, player, _, _, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // confined → booking anim

        Assert.Contains(player.OneShotAnims, a => a.StartsWith("mp_arrest_paired/crook_p1_front"));

        service.Tick();                  // still in cell, not moving → idle loop
        Assert.Contains(player.LoopedAnims, a => a == "anim@heists@prison_heist/ped_a_loop_a");

        player.Position = new System.Numerics.Vector3(1830, 2640, 46);   // moving
        service.Tick();
        Assert.True(player.ClearAnimCount >= 1, "idle anim must clear when the player moves");
    }

    [Fact]
    public void Escape_NotPossibleOutsideYardTime()
    {
        // In cell phase, the at-fence + hotkey combo must NOT escape (only crossing does)
        var (service, wanted, player, _, notifier, clock, input, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 5;
        service.Tick();
                crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S19: confrontation window expires -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // confined, cell phase

        player.Position = new System.Numerics.Vector3(1826 + 75, 2635, 46);
        input.DashHotkey = true;
        input.Update();
        service.Tick();

        Assert.Equal(JusticeState.Prison, service.State);   // still inside
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("ESCAPED"));
    }

    // --- S7: death while wanted → police custody ---

    [Fact]
    public void DeathWhileWanted_OnRespawn_CustodyAndHospitalRefund()
    {
        var (service, wanted, player, store, notifier, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 3;
        service.Tick();                    // Moderate crime
        player.Money = 12000;              // GTA deducts $5k hospital fee on death

        player.IsDead = true;
        service.Tick();                    // died while wanted (stars still 3)
        wanted.CurrentStars = 0;           // GTA clears stars on respawn
        player.IsDead = false;
        player.Money = 7000;               // hospital fee already deducted
        service.Tick();                    // respawn → custody + refund

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Equal(12000, player.Money);                       // fee refunded (S7)
        Assert.Contains(notifier.Messages, m => m.Contains("POLICE CUSTODY"));
    }

    [Fact]
    public void DeathWhileWanted_TrialSchedulesVerdict()
    {
        var (service, wanted, player, store, _, clock, _, crimeProbe) = Build();
        player.IsVisible = true;
        wanted.CurrentStars = 2;
        service.Tick();                    // Minor crime
        player.IsDead = true;
        service.Tick();
        wanted.CurrentStars = 0;
        player.IsDead = false;
        service.Tick();                    // custody, court countdown starts

        service.AdvanceTrialTime(45.0);
        service.Tick();                    // court time elapsed → Minor sentence → fine-only release

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(player.MoneyCalls, m => m == -2000);
    }

    [Fact]
    public void DeathNotWanted_NoCustody()
    {
        var (service, wanted, player, _, notifier, _, _, crimeProbe) = Build();
        player.IsVisible = true;
        player.IsDead = true;
        service.Tick();                    // died with no wanted episode
        player.IsDead = false;
        service.Tick();

        Assert.Equal(JusticeState.Free, service.State);          // no custody
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("POLICE CUSTODY"));
    }

    // --- S8: trial in REAL time (a GTA game day is 48 real minutes — too slow) ---

    [Fact]
    public void Trial_FiresAfterDelaySeconds_NotBefore()
    {
        // Minor offense + report escalation → arrested; fine-only release (S12)
        var (service, wanted, _, _, _, _, crimeProbe) = BuildWithProbe(0.0, burned: true);
        wanted.CurrentStars = 2;
        service.Tick();                    // Minor crime
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted, crimeProbe);

        service.AdvanceTrialTime(44.9);    // 45s default delay
        service.Tick();
        Assert.Equal(JusticeState.Captured, service.State);      // not yet

        service.AdvanceTrialTime(0.2);
        service.Tick();
        Assert.Equal(JusticeState.Free, service.State);          // court time elapsed
    }

    // --- S10: chase escape media (you lost the cops → viral) ---

    private static (JusticeService service, FakeWantedMonitor wanted, FakeMediaNotifier media, FakePlayer player, FakeCrimeProbe crimeProbe) BuildWithMedia()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var media = new FakeMediaNotifier();
        var crimeProbe = new FakeCrimeProbe();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            new FakeNotifier(), new FakeLog(), new JusticeConfig(), new FakeClock(),
            new MediaService(media, new FakeLog(), new JusticeConfig()),
            crimeProbe: crimeProbe);
        return (service, wanted, media, player, crimeProbe);
    }

    [Fact]
    public void ChaseEscape_StarsDropWithoutCapture_ViralNews()
    {
        var (service, wanted, media, _, crimeProbe) = BuildWithMedia();
        wanted.CurrentStars = 3;
        service.Tick();                    // Moderate crime
        wanted.CurrentStars = 0;
        service.Tick();                    // lost the cops

        Assert.Contains(media.Headlines, h => h.Contains("POLICE LOSE"));
        Assert.Contains(media.ViralMessages, v => v.Contains("vanishes"));
    }

    [Fact]
    public void ChaseEscape_NotAfterArrest()
    {
        var (service, wanted, media, _, crimeProbe) = BuildWithMedia();
        wanted.CurrentStars = 4;
        service.Tick();                    // Wanted
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S21: cop reaches you -> cuffed
        wanted.CurrentStars = 0;
        service.Tick();                    // stars cleared after capture

        Assert.DoesNotContain(media.Headlines, h => h.Contains("POLICE LOSE"));
    }

    [Fact]
    public void ChaseEscape_NotOnDeath()
    {
        var (service, wanted, media, player, crimeProbe) = BuildWithMedia();
        wanted.CurrentStars = 3;
        service.Tick();                    // Moderate crime
        player.IsDead = true;              // died in the chase
        wanted.CurrentStars = 0;
        service.Tick();                    // stars cleared at death — NO escape news

        Assert.DoesNotContain(media.Headlines, h => h.Contains("POLICE LOSE"));
    }

    // --- S12: warrant escalation + total-crimes sentencing ---

    [Fact]
    public void WarrantReports_Escalate_UntilCapture()
    {
        // Each recognition raises the response: 1★ → 2★ → 3★ → 4★ → captured
        var (service, wanted, _, store, _, _, crimeProbe) = BuildWithProbe(0.0, burned: true);

        service.Tick();
        Assert.Equal(1, wanted.CurrentStars);
        wanted.CurrentStars = 0;
        service.Tick();
        service.Tick();
        Assert.Equal(2, wanted.CurrentStars);
        wanted.CurrentStars = 0;
        service.Tick();
        service.Tick();
        Assert.Equal(3, wanted.CurrentStars);
        wanted.CurrentStars = 0;
        service.Tick();
        service.Tick();                  // 4th report → 4★
        service.Tick();                  // S19 confrontation begins
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                  // cuffed

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Equal(0, wanted.CurrentStars);
    }

    [Fact]
    public void Verdict_ChargesAllUnchargedCrimes_TotalFine()
    {
        // Two episodes → two charges at the next court date: 2000 + 2000 = 4000
        var (service, wanted, player, store, _, _, crimeProbe) = BuildWithProbe(0.0, burned: true);
        wanted.CurrentStars = 2;
        service.Tick();                  // Minor #1
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted, crimeProbe);
        service.AdvanceTrialTime(45.0);
        service.Tick();
        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(player.MoneyCalls, m => m == -2000);
        Assert.All(store.Record.Events, e => Assert.True(e.Charged, "all events charged"));
        Assert.Equal(0, store.Record.Events.Count(e => !e.Charged));
    }

    [Fact]
    public void Verdict_TotalsAcrossEpisodes()
    {
        // Minor (2000) + Moderate (8000) episodes → 10000 fine + 7 days
        var (service, wanted, player, store, _, _, crimeProbe) = BuildWithProbe(0.0, burned: true);
        wanted.CurrentStars = 2;
        service.Tick();                  // Minor
        wanted.CurrentStars = 0;
        service.Tick();
        wanted.CurrentStars = 3;
        service.Tick();                  // Moderate
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted, crimeProbe);
        service.AdvanceTrialTime(45.0);
        service.Tick();

        Assert.True(store.Record.Events.Count >= 2, $"expected 2 events, got {store.Record.Events.Count}: [{string.Join(", ", store.Record.Events.Select(e => e.Severity))}]");
        Assert.Equal(JusticeState.Prison, service.State);
        Assert.Equal(7, service.SentenceDays);
        Assert.Contains(player.MoneyCalls, m => m == -10000);
        Assert.Single(store.Record.Convictions);
    }

    [Fact]
    public void UnpaidFine_ConvertsToPrisonDays()
    {
        // Moderate charge (8000) with only 3000 cash → pay 3000, $5,000 short →
        // +5 days at the $1,000/day rate → prison
        var (service, wanted, player, store, _, _, crimeProbe) = BuildWithProbe(0.0, burned: true);
        player.Money = 3000;
        wanted.CurrentStars = 3;
        service.Tick();                  // Moderate
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted, crimeProbe);
        service.AdvanceTrialTime(45.0);
        service.Tick();

        Assert.Equal(JusticeState.Prison, service.State);
        Assert.Equal(12, service.SentenceDays);   // 7 base + 5 from the $5,000 shortfall
        Assert.Contains(player.MoneyCalls, m => m == -3000);
    }

    // --- S9: warrant enforcement (civilians report a burned face) ---

    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeRecordStore store, FakeNotifier notifier, FakeProbe probe, FakeCrimeProbe crimeProbe) BuildWithProbe(double? roll = null, bool burned = false)
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true };
        var store = new FakeRecordStore();
        if (burned)
        {
            store.Status.Identity = IdentityState.Burned;   // seed BEFORE ctor — services cache at construction
            store.Status.WarrantActive = true;
        }
        var notifier = new FakeNotifier();
        var clock = new FakeClock();
        var probe = new FakeProbe { NearbyCivilians = 5 };
        var crimeProbe = new FakeCrimeProbe();
        var config = new JusticeConfig { WarrantReportSeconds = 0 };   // no cooldown in tests
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        config.PrisonDayRealSeconds = 30;   // tests keep fast deterministic days
        var service = new JusticeService(
            wanted, player, store, identity, warrant, notifier, new FakeLog(),
            config, clock, null, null, null, null, probe,
            roll.HasValue ? () => roll.Value : null, crimeProbe: crimeProbe);
        return (service, wanted, player, store, notifier, probe, crimeProbe);
    }

    [Fact]
    public void WarrantReport_BurnedVisibleNearCivilians_StarsRise()
    {
        var (service, wanted, _, _, notifier, _, crimeProbe) = BuildWithProbe(0.0, burned: true);   // always reports;

        service.Tick();
        service.Tick();   // next tick: stars 0 → 1 edge (suppressed crime)

        Assert.Equal(1, wanted.CurrentStars);
        Assert.Contains(notifier.Messages, m => m.Contains("recognized you"));
    }

    [Fact]
    public void WarrantReport_DoesNotRecordNewCrime()
    {
        var (service, wanted, _, store, _, _, crimeProbe) = BuildWithProbe(0.0, burned: true);

        service.Tick();   // report raises stars to 1
        service.Tick();   // edge processed — must NOT create a crime event

        Assert.Empty(store.Record.Events);
    }

    [Fact]
    public void WarrantReport_NotWhenCleanIdentity()
    {
        var (service, wanted, _, _, notifier, _, crimeProbe) = BuildWithProbe(0.0);   // identity stays Clean;

        service.Tick();
        service.Tick();

        Assert.Equal(0, wanted.CurrentStars);
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("recognized you"));
    }

    [Fact]
    public void WarrantReport_NotWhenInvisible()
    {
        var (service, wanted, player, _, notifier, _, crimeProbe) = BuildWithProbe(0.0, burned: true);
        player.IsVisible = false;

        service.Tick();
        service.Tick();

        Assert.Equal(0, wanted.CurrentStars);
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("recognized you"));
    }

    [Fact]
    public void WarrantReport_NotWithoutCivilians()
    {
        var (service, wanted, _, _, notifier, probe, crimeProbe) = BuildWithProbe(0.0, burned: true);
        probe.NearbyCivilians = 0;

        service.Tick();
        service.Tick();

        Assert.Equal(0, wanted.CurrentStars);
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("recognized you"));
    }

    [Fact]
    public void WarrantReport_ChanceMiss_NoReport()
    {
        var (service, wanted, _, _, notifier, _, crimeProbe) = BuildWithProbe(0.99);   // roll misses;

        service.Tick();
        service.Tick();

        Assert.Equal(0, wanted.CurrentStars);
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("recognized you"));
    }
}

public class IdentityServiceTests
{
    [Fact]
    public void SetBurned_PersistsAndFlags()
    {
        var store = new FakeRecordStore();
        var identity = new IdentityService(store, new FakeLog());

        identity.SetBurned();

        Assert.True(identity.IsBurned);
        Assert.Equal(IdentityState.Burned, store.Status.Identity);
    }

    [Fact]
    public void SetClean_Persists()
    {
        var store = new FakeRecordStore();
        var identity = new IdentityService(store, new FakeLog());
        identity.SetBurned();
        identity.SetClean();

        Assert.False(identity.IsBurned);
        Assert.Equal(IdentityState.Clean, store.Status.Identity);
    }

    [Fact]
    public void LoadsPersistedState()
    {
        var store = new FakeRecordStore { Status = new JusticeStatus { Identity = IdentityState.Burned } };
        var identity = new IdentityService(store, new FakeLog());

        Assert.True(identity.IsBurned);
    }
}

public class WarrantServiceTests
{
    [Fact]
    public void Activate_SetsAndPersists()
    {
        var store = new FakeRecordStore();
        var warrant = new WarrantService(store, new FakeLog());

        warrant.Activate("2026-08-08T12:00:00");

        Assert.True(warrant.IsActive);
        Assert.True(store.Status.WarrantActive);
        Assert.Equal("2026-08-08T12:00:00", store.Status.WarrantSinceGameTime);
    }

    [Fact]
    public void Clear_RemovesAndPersists()
    {
        var store = new FakeRecordStore();
        var warrant = new WarrantService(store, new FakeLog());
        warrant.Activate("t");
        warrant.Clear();

        Assert.False(warrant.IsActive);
        Assert.False(store.Status.WarrantActive);
        Assert.Null(store.Status.WarrantSinceGameTime);
    }

    [Fact]
    public void LoadsPersistedWarrant()
    {
        var store = new FakeRecordStore { Status = new JusticeStatus { WarrantActive = true, WarrantSinceGameTime = "t" } };
        var warrant = new WarrantService(store, new FakeLog());

        Assert.True(warrant.IsActive);
        Assert.Equal("t", warrant.SinceGameTime);
    }
}
