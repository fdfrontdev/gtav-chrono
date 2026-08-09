using System.Numerics;
using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S19 — user UAT round 13 (use-of-force realism):
/// (1) Minor (1–2★) crimes carry NO standing warrant — no civilian report loop;
/// (2) capture at 4★+ is a CONFRONTATION: comply (G) or resist (X/Z/B) — the police
/// cuff you or open fire, never an instant grab;
/// (3) at 3★+, a stationary unarmed suspect makes the officers stand down.</summary>
public class ArrestConfrontationTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeNotifier notifier, FakeRecordStore store, FakeMediaNotifier media, FakeInput input) Build(double roll = 0.5, bool seededBurned = false, bool seededWarrant = false)
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var media = new FakeMediaNotifier();
        var input = new FakeInput();
        // S9 lesson: seed BEFORE constructing
        store.Status.Identity = seededBurned ? IdentityState.Burned : IdentityState.Clean;
        store.Status.WarrantActive = seededWarrant;
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(), new JusticeConfig { WarrantReportSeconds = 0 }, new FakeClock(),
            new MediaService(media, new FakeLog(), new JusticeConfig()),
            input: input, probe: new FakeProbe { NearbyCivilians = 5 }, random: () => roll);
        return (service, wanted, player, notifier, store, media, input);
    }

    // ── 1. Warrants only for Moderate+ ──

    [Fact]
    public void MinorCrime_NoWarrant()
    {
        var (service, wanted, _, _, store, _, _) = Build();

        wanted.CurrentStars = 1;         // Minor, face visible
        service.Tick();

        Assert.False(store.Status.WarrantActive, "a 1★ scrape must not start a manhunt");
        Assert.True(store.Status.Identity == IdentityState.Burned);   // but the face IS on file
    }

    [Fact]
    public void ModerateCrime_ActivatesWarrant()
    {
        var (service, wanted, _, _, store, _, _) = Build();

        wanted.CurrentStars = 3;         // Moderate, face visible
        service.Tick();

        Assert.True(store.Status.WarrantActive);
    }

    [Fact]
    public void MinorCrime_ThenNoReportLoop()
    {
        // The user's UAT: after a 1★ bust, civilians must NOT keep calling the cops
        var (service, wanted, player, _, store, _, _) = Build(roll: 0.0, seededBurned: true);
        store.Status.WarrantActive = false;   // no warrant for the minor offense

        wanted.CurrentStars = 1;
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();
        service.Tick();                  // would fire a report if a warrant existed

        Assert.Equal(0, wanted.CurrentStars);   // civilians stay quiet — no manhunt
    }

    // ── 2. Confrontation ──

    [Fact]
    public void FourStars_ConfrontsNotCaptures()
    {
        var (service, wanted, _, _, _, _, _) = Build();

        wanted.CurrentStars = 4;
        service.Tick();

        Assert.Equal(JusticeState.Wanted, service.State);   // NOT captured yet
    }

    [Fact]
    public void Confrontation_ComplyWithG_Captures()
    {
        var (service, wanted, _, notifier, _, _, input) = Build();

        wanted.CurrentStars = 4;
        service.Tick();                  // confrontation begins
        service.AdvanceConfrontationTime(2.5);   // choice window opens
        service.Tick();
        input.InteractHotkey = true;
        input.Update();                  // G edge
        service.Tick();                  // G = comply

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Equal(0, wanted.CurrentStars);
    }

    [Fact]
    public void Confrontation_Timeout_AutoCuffs()
    {
        var (service, wanted, _, _, _, _, _) = Build();

        wanted.CurrentStars = 4;
        service.Tick();
        service.AdvanceConfrontationTime(6.5);
        service.Tick();                  // you froze — the officers cuff you

        Assert.Equal(JusticeState.Captured, service.State);
    }

    [Fact]
    public void Confrontation_ResistSuccess_BreaksAway()
    {
        var (service, wanted, _, notifier, store, media, input) = Build(roll: 0.9);   // 0.9 ≥ 0.6 → escape

        wanted.CurrentStars = 4;
        service.Tick();
        service.AdvanceConfrontationTime(2.5);
        service.Tick();
        input.IsDashKeyJustPressed = true;
        service.Tick();                  // X = resist → broke away

        Assert.Equal(JusticeState.Wanted, service.State);   // chase continues
        Assert.Contains(store.Record.Events, e => e.Kind == "resisting_arrest");
        Assert.Contains(notifier.Messages, m => m.Contains("broke away"));
        Assert.Contains(media.Headlines, h => h.Contains("BREAKS FREE"));

        // cooldown: an immediate 4★ tick must NOT re-confront instantly
        service.Tick();
        Assert.Equal(JusticeState.Wanted, service.State);
    }

    [Fact]
    public void Confrontation_ResistFail_OfficersOpenFire()
    {
        var (service, wanted, _, notifier, _, media, input) = Build(roll: 0.1);   // 0.1 < 0.6 → shot

        wanted.CurrentStars = 4;
        service.Tick();
        service.AdvanceConfrontationTime(2.5);
        service.Tick();
        input.InvisibleHotkey = true;
        input.Update();                  // B edge
        service.Tick();                  // B = resist → they open fire

        Assert.Equal(5, wanted.CurrentStars);   // max heat — the vanilla AI finishes it
        Assert.Contains(notifier.Messages, m => m.Contains("OPEN FIRE"));
        Assert.Contains(media.Headlines, h => h.Contains("RESISTS ARREST"));
    }

    // ── 3. Compliance stand-down ──

    [Fact]
    public void Compliance_StationaryUnarmed_StarsDecay_NoViral()
    {
        var (service, wanted, player, notifier, _, media, _) = Build();

        wanted.CurrentStars = 3;
        service.Tick();                  // Moderate crime → Wanted
        player.Position = new Vector3(10, 10, 0);
        service.Tick();                  // baseline position (moved vs origin)
        service.Tick();                  // settle — still now

        service.AdvanceComplianceTime(3.2);
        service.Tick();                  // 3s still + unarmed → stand down + decay 3★ → 2★
        Assert.Contains(notifier.Messages, m => m.Contains("stand down"));
        Assert.Equal(2, wanted.CurrentStars);

        service.AdvanceComplianceTime(1.6);
        service.Tick();                  // decay → 1★
        Assert.Equal(1, wanted.CurrentStars);
        service.AdvanceComplianceTime(1.6);
        service.Tick();                  // decay → 0★ — complied, no shooting
        service.Tick();                  // state edge sees the cleared stars → Free

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Contains(notifier.Messages, m => m.Contains("complied"));
        Assert.DoesNotContain(media.Headlines, h => h.Contains("vanishe") || h.Contains("LOSE"));
    }

    [Fact]
    public void Compliance_Moving_NoDecay()
    {
        var (service, wanted, player, _, _, _, _) = Build();

        wanted.CurrentStars = 3;
        service.Tick();
        player.Position = new Vector3(10, 10, 0);
        service.Tick();

        service.AdvanceComplianceTime(3.2);
        player.Position = new Vector3(20, 10, 0);   // moved during the window
        service.Tick();

        Assert.Equal(3, wanted.CurrentStars);       // no decay — chase stays hot
    }

    [Fact]
    public void Compliance_Armed_NoDecay()
    {
        var (service, wanted, player, _, _, _, _) = Build();

        wanted.CurrentStars = 3;
        service.Tick();
        player.Position = new Vector3(10, 10, 0);
        player.HasWeapon = true;                    // armed suspect = still a threat
        service.Tick();

        service.AdvanceComplianceTime(3.2);
        service.Tick();

        Assert.Equal(3, wanted.CurrentStars);       // the officers do NOT stand down
    }
}
