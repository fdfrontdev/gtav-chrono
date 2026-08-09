using System.Numerics;
using Chrono.Application;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S20 — use-of-force enforcement (ADR-04 D2): police HOLD FIRE (aim, don't shoot)
/// while the suspect is stationary + unarmed at 2★+; the hold lifts instantly when
/// the player moves, draws a weapon, or attacks. Stand-down gate lowered 3★+ → 2★+
/// (user UAT r14: cops opened fire on a still unarmed player at 2★).
/// </summary>
public class UseOfForceTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeCrimeProbe crimeProbe, FakeNotifier notifier)
        Build(JusticeConfig? config = null)
    {
        var cfg = config ?? new JusticeConfig { WarrantReportSeconds = 0 };
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, Position = new Vector3(0, 0, 0) };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var crimeProbe = new FakeCrimeProbe();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(), cfg, new FakeClock(),
            new MediaService(new FakeMediaNotifier(), new FakeLog(), cfg),
            input: new FakeInput(), probe: new FakeProbe { NearbyCivilians = 5 },
            random: () => 0.5, crimeProbe: crimeProbe);
        return (service, wanted, player, crimeProbe, notifier);
    }

    private static void StartChase(JusticeService service, FakeWantedMonitor wanted, int stars)
    {
        wanted.CurrentStars = stars;
        service.Tick();   // Wanted state + (if 4★+) confrontation begins — avoid by using 2-3★ here
    }

    // ── 1. Hold-fire engages for a stationary unarmed suspect at 2★+ ──

    [Fact]
    public void StationaryUnarmed_AtTwoStars_CopsHoldFire()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 2);

        service.Tick();   // second tick: still + unarmed → hold engages

        Assert.True(crimeProbe.HoldActive, "cops must hold fire on a still unarmed suspect at 2★");
    }

    [Fact]
    public void StationaryUnarmed_AtOneStar_NoHoldFire()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 1);

        service.Tick();

        Assert.False(crimeProbe.HoldActive, "1★ minor scrape → no police presence, no hold needed");
    }

    [Fact]
    public void StationaryArmed_NoHoldFire()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        player.HasWeapon = true;
        StartChase(service, wanted, 2);

        service.Tick();

        Assert.False(crimeProbe.HoldActive, "an ARMED suspect is a threat — cops may shoot");
    }

    [Fact]
    public void Moving_NoHoldFire()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 2);
        player.Position = new Vector3(10, 0, 0);   // moved > 1.2 m since last tick

        service.Tick();

        Assert.False(crimeProbe.HoldActive, "a RUNNING suspect is a flight risk — chase resumes");
    }

    // ── 2. The hold lifts when the player moves after complying ──

    [Fact]
    public void HoldEngages_ThenLifts_WhenPlayerMoves()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 2);
        service.Tick();
        Assert.True(crimeProbe.HoldActive);

        player.Position = new Vector3(5, 0, 0);
        service.Tick();

        Assert.False(crimeProbe.HoldActive, "the instant the suspect moves, the hold must lift");
        Assert.Contains(crimeProbe.HoldFireCalls, h => h == false);
    }

    [Fact]
    public void HoldLifts_WhenChaseEnds()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 2);
        service.Tick();
        Assert.True(crimeProbe.HoldActive);

        wanted.CurrentStars = 0;      // cops stood down / chase over
        service.Tick();

        Assert.False(crimeProbe.HoldActive, "no chase → no hold-fire needed");
    }

    // ── 3. S21 v2: compliance = ARREST (user UAT 2026-08-09 — the old star-decay
    // "officers leave" release was removed: complying with a cop in reach cuffs
    // you; the chase-escape event fires only when stars drop WITHOUT capture) ──

    [Fact]
    public void Comply_WithCopInReach_ArrestsYou()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 2);
        crimeProbe.NearestPoliceDistance = 6f;   // a cop is within surrender range (12m)

        // 3+ seconds of stillness + unarmed → compliance completes → ARRESTED
        service.Tick();
        service.AdvanceComplianceTime(4.0);
        service.Tick();

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Equal(0, wanted.CurrentStars);   // handcuffed — chase over (S11)
    }

    [Fact]
    public void Comply_NoCopInReach_DoesNotRelease()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 3);
        crimeProbe.NearestPoliceDistance = 50f;   // no cop in surrender range

        service.Tick();
        service.AdvanceComplianceTime(10.0);      // long stillness
        service.Tick();

        // NO star decay, NO release — the player must wait for a cop or run
        Assert.Equal(JusticeState.Wanted, service.State);
        Assert.Equal(3, wanted.CurrentStars);
        Assert.True(crimeProbe.HoldActive, "hold stays active — cops aim, don't shoot");
    }

    [Fact]
    public void ComplianceArrest_ClearsWarrant_ViaJusticePipeline()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        var warrant = service.Warrant;
        warrant.Activate("2026-08-09T12:00:00");
        StartChase(service, wanted, 2);
        crimeProbe.NearestPoliceDistance = 6f;

        service.Tick();
        service.AdvanceComplianceTime(4.0);
        service.Tick();                      // compliance → custody

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.True(warrant.IsActive, "warrant stays through custody (cleared on release/bail)");

        // Serve the pipeline: trial → sentence (fine-only) → released → warrant cleared
        service.AdvanceTrialTime(service.TrialSecondsLeft + 1);
        service.Tick();
        if (service.State == JusticeState.Captured)
        {
            service.AdvanceTrialTime(service.TrialSecondsLeft + 1);
            service.Tick();
        }
        Assert.Equal(JusticeState.Free, service.State);
        Assert.False(warrant.IsActive, "justice served — warrant cleared, no more civilian reports");
    }

    // ── 4. S21: a cop closing in does NOT open fire on a still unarmed suspect —
    // the hold stays ON until the suspect moves (physical capture replaced S19's
    // confrontation: cops reach you → cuff; they never shoot a compliant suspect) ──

    [Fact]
    public void CopNear_StationaryUnarmed_HoldStaysActive()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        wanted.CurrentStars = 4;            // Severe wanted level
        service.Tick();                     // Wanted state
        crimeProbe.NearestPoliceDistance = 6f;   // a cop is closing in (outside 3m cuff range)
        service.Tick();                     // still + unarmed → hold ON (aim, don't shoot)

        Assert.True(service.State == JusticeState.Wanted);   // not captured — cop not within 3m
        Assert.True(crimeProbe.HoldActive);                  // the officers are NOT shooting
    }

    [Fact]
    public void CopNear_Moving_LiftsHold()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        wanted.CurrentStars = 4;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 6f;
        service.Tick();                     // hold ON
        Assert.True(crimeProbe.HoldActive);

        player.Position = new Vector3(10, 0, 0);   // suspect runs
        service.Tick();
        Assert.False(crimeProbe.HoldActive);       // chase re-engages — they fire
    }
}
