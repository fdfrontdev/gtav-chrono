using System.Numerics;
using Chrono.Application;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S21 — physical capture (user UAT r15 ruling 1): police must physically REACH
/// the player (~3 m) while the player is stopped → cuff. G near a cop = surrender.
/// Shot down while wanted → custody. NO auto-cuff timer — while you run/fight,
/// the chase continues. Warrants still only for Moderate+ (S19). Compliance
/// stand-down + hold-fire (S19/S20) unchanged.
/// </summary>
public class ArrestConfrontationTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeNotifier notifier, FakeRecordStore store, FakeMediaNotifier media, FakeInput input, FakeCrimeProbe crimeProbe) Build(double roll = 0.5, bool seededBurned = false, bool seededWarrant = false)
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var media = new FakeMediaNotifier();
        var input = new FakeInput();
        var crimeProbe = new FakeCrimeProbe();
        // S9 lesson: seed BEFORE constructing
        store.Status.Identity = seededBurned ? IdentityState.Burned : IdentityState.Clean;
        store.Status.WarrantActive = seededWarrant;
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(), new JusticeConfig { WarrantReportSeconds = 0 }, new FakeClock(),
            new MediaService(media, new FakeLog(), new JusticeConfig()),
            input: input, probe: new FakeProbe { NearbyCivilians = 5 }, random: () => roll,
            crimeProbe: crimeProbe);
        return (service, wanted, player, notifier, store, media, input, crimeProbe);
    }

    // ── 1. Warrants only for Moderate+ (S19, unchanged) ──

    [Fact]
    public void MinorCrime_NoWarrant()
    {
        var (service, wanted, _, _, store, _, _, _) = Build();

        wanted.CurrentStars = 1;         // Minor, face visible
        service.Tick();

        Assert.False(store.Status.WarrantActive, "a 1★ scrape must not start a manhunt");
        Assert.True(store.Status.Identity == IdentityState.Burned);   // but the face IS on file
    }

    [Fact]
    public void ModerateCrime_ActivatesWarrant()
    {
        var (service, wanted, _, _, store, _, _, _) = Build();

        wanted.CurrentStars = 3;         // Moderate, face visible
        service.Tick();

        Assert.True(store.Status.WarrantActive);
    }

    [Fact]
    public void MinorCrime_ThenNoReportLoop()
    {
        // The user's UAT: after a 1★ bust, civilians must NOT keep calling the cops
        var (service, wanted, player, _, store, _, _, _) = Build(roll: 0.0, seededBurned: true);
        store.Status.WarrantActive = false;   // no warrant for the minor offense

        wanted.CurrentStars = 1;
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();
        service.Tick();                  // would fire a report if a warrant existed

        Assert.Equal(0, wanted.CurrentStars);   // civilians stay quiet — no manhunt
    }

    // ── 2. Physical capture (S21 — NO auto-cuff) ──

    [Fact]
    public void FourStars_NoCopsNearby_NoCapture()
    {
        var (service, wanted, _, _, _, _, _, crimeProbe) = Build();
        crimeProbe.NearestPoliceDistance = float.MaxValue;   // no cops in range

        wanted.CurrentStars = 4;
        service.Tick();

        Assert.Equal(JusticeState.Wanted, service.State);   // NOT captured — chase continues
    }

    [Fact]
    public void CopReachesPlayer_Stopped_Captured()
    {
        var (service, wanted, _, notifier, _, _, _, crimeProbe) = Build();

        wanted.CurrentStars = 4;
        service.Tick();                  // Wanted state
        crimeProbe.NearestPoliceDistance = 2f;   // a cop closes in
        service.Tick();                  // stopped + within 3m → cuffed

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Equal(0, wanted.CurrentStars);   // S11: handcuffed — chase over
        Assert.Contains(notifier.Messages, m => m.Contains("HANDS WHERE I CAN SEE"));
    }

    [Fact]
    public void CopReachesPlayer_ButMoving_NoCapture()
    {
        var (service, wanted, player, _, _, _, _, crimeProbe) = Build();

        wanted.CurrentStars = 4;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 2f;
        player.Position = new Vector3(10, 0, 0);   // you're RUNNING — not caught
        service.Tick();

        Assert.Equal(JusticeState.Wanted, service.State);   // still free — you're moving
    }

    [Fact]
    public void CopFarAway_NoCapture()
    {
        var (service, wanted, _, _, _, _, _, crimeProbe) = Build();

        wanted.CurrentStars = 4;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 50f;   // cops still far
        service.Tick();

        Assert.Equal(JusticeState.Wanted, service.State);
    }

    [Fact]
    public void SurrenderWithG_NearCop_Captured()
    {
        var (service, wanted, _, notifier, _, _, input, crimeProbe) = Build();

        wanted.CurrentStars = 4;
        service.Tick();                  // Wanted state
        crimeProbe.NearestPoliceDistance = 8f;   // a cop is near (≤12m)
        input.InteractHotkey = true;     // G pressed
        input.Update();
        service.Tick();                  // surrender → custody

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Equal(0, wanted.CurrentStars);
        Assert.Contains(notifier.Messages, m => m.Contains("HANDS UP"));
    }

    [Fact]
    public void SurrenderWithG_CopTooFar_Refused()
    {
        var (service, wanted, _, _, _, _, input, crimeProbe) = Build();

        wanted.CurrentStars = 4;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 100f;   // no cop near
        input.InteractHotkey = true;
        input.Update();
        service.Tick();

        Assert.Equal(JusticeState.Wanted, service.State);   // nobody to surrender to
    }

    [Fact]
    public void DeathWhileWanted_CustodyOnRespawn()
    {
        var (service, wanted, player, _, store, _, _, _) = Build();

        wanted.CurrentStars = 4;
        service.Tick();                  // Wanted
        player.IsDead = true;            // shot down
        service.Tick();                  // death edge captured
        player.IsDead = false;           // respawn
        service.Tick();                  // → custody

        Assert.Equal(JusticeState.Captured, service.State);
    }

    // ── 3. Compliance stand-down (S19/S20, unchanged) ──

    [Fact]
    public void Compliance_StationaryUnarmed_StarsDecay_NoViral()
    {
        var (service, wanted, player, notifier, _, media, _, crimeProbe) = Build();
        crimeProbe.NearestPoliceDistance = 5f;   // cops present (hold-fire targets)

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
        var (service, wanted, player, _, _, _, _, _) = Build();

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
        var (service, wanted, player, _, _, _, _, _) = Build();

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
