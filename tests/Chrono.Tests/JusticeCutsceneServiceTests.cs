using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S11 cinematic justice: scripted arrest/booking, court, intake and release
/// sequences with camera beats + banners; the sentence applies when the gavel falls.</summary>
public class JusticeCutsceneServiceTests
{
    private static (JusticeCutsceneService service, FakeCutsceneRenderer renderer, FakePlayer player) Build()
    {
        var renderer = new FakeCutsceneRenderer();
        var player = new FakePlayer { Position = new System.Numerics.Vector3(100, 200, 30) };
        var service = new JusticeCutsceneService(renderer, player, new FakeLog());
        return (service, renderer, player);
    }

    [Fact]
    public void Arrest_PhasesAdvance_AndEnd()
    {
        var (service, renderer, _) = Build();

        service.Play(CutsceneKind.Arrest);
        service.Tick(0);        // phase 1 enter
        Assert.Equal(1, renderer.BeginCount);
        Assert.Contains(renderer.Anims, a => a.StartsWith("anim@move_m@prisoner_cuffed"));

        service.Tick(2500);     // phase 2
        Assert.Contains(renderer.Banners, b => b.Contains("POLICE CUSTODY"));

        service.Tick(5500);     // phase 3
        Assert.Contains(renderer.Banners, b => b.Contains("BOOKING"));

        service.Tick(8500);     // end
        Assert.False(service.IsActive);
        Assert.Equal(1, renderer.EndCount);
    }

    [Fact]
    public void Trial_Completes_CallsOnComplete()
    {
        var (service, renderer, _) = Build();
        bool completed = false;

        service.Play(CutsceneKind.Trial, () => completed = true, "Murder", "GUILTY — $25,000 · 30 days");
        service.Tick(0);        // session
        service.Tick(2700);     // charge beat
        service.Tick(5700);     // verdict beat
        service.Tick(9000);     // gavel — end

        Assert.False(service.IsActive);
        Assert.True(completed, "onComplete must fire when the gavel falls");
        Assert.Contains(renderer.Banners, b => b.Contains("GUILTY"));
    }

    [Fact]
    public void Intake_AndRelease_RunTheirBeats()
    {
        var (service, renderer, _) = Build();

        service.Play(CutsceneKind.Intake);
        service.Tick(0);        // intake p1
        service.Tick(2700);     // p2
        service.Tick(5200);     // end
        Assert.False(service.IsActive);
        Assert.Contains(renderer.Banners, b => b.Contains("BOLINGBROKE"));

        service.Play(CutsceneKind.Release);
        service.Tick(0);
        service.Tick(2700);
        Assert.False(service.IsActive);
        Assert.Contains(renderer.Banners, b => b.Contains("RELEASED"));
    }
}

/// <summary>JusticeService × cutscene integration (S11): the verdict is presented
/// cinematically and the sentence applies on completion; stars clear at capture
/// (no re-arrest loop); fine-only release no longer teleports to the prison.</summary>
public class JusticeCutsceneIntegrationTests
{
    private static (JusticeService service, JusticeCutsceneService cutscene, FakeCutsceneRenderer renderer, FakeWantedMonitor wanted, FakePlayer player, FakeRecordStore store, FakeClock clock) Build(bool burned = false)
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000 };
        var store = new FakeRecordStore();
        if (burned)
        {
            store.Status.Identity = IdentityState.Burned;   // seed BEFORE ctor — services cache
            store.Status.WarrantActive = true;
        }
        var clock = new FakeClock();
        var notifier = new FakeNotifier();
        var renderer = new FakeCutsceneRenderer();
        var cutscene = new JusticeCutsceneService(renderer, player, new FakeLog());
        var probe = new FakeProbe { NearbyCivilians = 5 };
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(),
            new JusticeConfig { WarrantReportSeconds = 0 }, clock,
            cutscene: cutscene, probe: probe, random: () => 0.0);
        return (service, cutscene, renderer, wanted, player, store, clock);
    }

    /// S12: 4 warrant recognitions escalate 1★→4★ → capture with NO new crimes
    private static void EscalateToCapture(JusticeService service, FakeWantedMonitor wanted)
    {
        for (int i = 0; i < 3; i++)
        {
            service.Tick();
            wanted.CurrentStars = 0;
            service.Tick();
        }
        service.Tick();
        service.Tick();
    }

    [Fact]
    public void Capture_PlaysArrestCutscene_AndClearsStars()
    {
        var (service, cutscene, renderer, wanted, _, _, _) = Build();
        wanted.CurrentStars = 4;
        service.Tick();

        Assert.True(cutscene.IsActive);
        Assert.Equal(1, renderer.BeginCount);
        Assert.Equal(0, wanted.CurrentStars);   // S11: handcuffed — chase over
        FinishArrest(cutscene);
    }

    private static void FinishArrest(JusticeCutsceneService cutscene)
    {
        cutscene.Tick(0);        // phase 1
        cutscene.Tick(2500);     // phase 2
        cutscene.Tick(5500);     // phase 3
        cutscene.Tick(8000);     // end — countdown resumes
    }

    [Fact]
    public void Verdict_PlaysCourt_AndSentenceAppliesAtGavel()
    {
        var (service, cutscene, renderer, wanted, player, store, _) = Build(burned: true);
        wanted.CurrentStars = 2;
        service.Tick();                    // Minor crime
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted);   // reports → 4★ → arrested
        FinishArrest(cutscene);            // booking cinematic plays out
        service.AdvanceTrialTime(45.0);
        service.Tick();                    // court session starts

        Assert.True(cutscene.IsActive, "trial cutscene must be playing");
        Assert.Equal(JusticeState.Captured, service.State);   // sentence NOT yet applied

        cutscene.Tick(0);
        cutscene.Tick(2700);
        cutscene.Tick(5700);
        cutscene.Tick(9000);               // gavel falls → sentence + release cutscene

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(player.MoneyCalls, m => m == -2000);   // fine applied at completion

        cutscene.Tick(9500);
        cutscene.Tick(12200);              // release cutscene ends
        Assert.False(cutscene.IsActive);
    }

    [Fact]
    public void PrisonVerdict_IntakeThenConfinement()
    {
        var (service, cutscene, renderer, wanted, player, store, clock) = Build();
        wanted.CurrentStars = 5;
        service.Tick();                    // crime + arrest
        FinishArrest(cutscene);            // booking cinematic plays out
        service.AdvanceTrialTime(45.0);
        service.Tick();                    // court session

        cutscene.Tick(0);
        cutscene.Tick(2700);
        cutscene.Tick(5700);
        cutscene.Tick(9000);               // gavel → intake cutscene

        Assert.True(cutscene.IsActive, "intake must play before confinement");
        Assert.Equal(JusticeState.Prison, service.State);

        cutscene.Tick(9500);               // intake p1
        cutscene.Tick(12100);              // intake p2
        cutscene.Tick(14600);              // intake done → confinement

        Assert.False(cutscene.IsActive);
        Assert.Single(player.TeleportCalls);   // confinement teleport to Bolingbroke
    }

    [Fact]
    public void FineOnlyRelease_DoesNotTeleport()
    {
        // S11 fix: paying a downtown fine must NOT wake you at the prison gate
        var (service, cutscene, renderer, wanted, player, store, _) = Build(burned: true);
        wanted.CurrentStars = 2;
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted);   // Minor-only charge → fine-only
        FinishArrest(cutscene);            // booking cinematic plays out
        service.AdvanceTrialTime(45.0);
        service.Tick();                    // court
        cutscene.Tick(0);
        cutscene.Tick(2700);
        cutscene.Tick(5700);
        cutscene.Tick(9000);               // gavel → release

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Empty(player.TeleportCalls);   // no prison-gate teleport for a fine
    }

    [Fact]
    public void Bail_RefusedWhileTrialCutscenePlays()
    {
        // S16 audit: posting bail during the court session would double-bill
        // (bail AND the fine) — the court refuses once the gavel is near
        var (service, cutscene, renderer, wanted, player, store, _) = Build(burned: true);
        wanted.CurrentStars = 2;
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted);
        FinishArrest(cutscene);
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // court session starts (cutscene active)

        service.PostBail();

        Assert.Equal(JusticeState.Captured, service.State);   // still in custody
        Assert.Empty(player.MoneyCalls);                       // no double bill
    }

    [Fact]
    public void ArrestedStarsCleared_NoImmediateRearrest()
    {
        // S11 fix: stars cleared at capture → after release the wanted level stays 0
        var (service, cutscene, renderer, wanted, player, store, _) = Build(burned: true);
        wanted.CurrentStars = 2;
        service.Tick();                    // Minor crime
        wanted.CurrentStars = 0;
        service.Tick();
        EscalateToCapture(service, wanted);   // arrest (stars → 0)
        FinishArrest(cutscene);            // booking cinematic plays out
        service.AdvanceTrialTime(45.0);
        service.Tick();                    // court
        cutscene.Tick(0);
        cutscene.Tick(2700);
        cutscene.Tick(5700);
        cutscene.Tick(9000);               // gavel → release

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Equal(0, wanted.CurrentStars);   // no re-arrest loop
    }
}
