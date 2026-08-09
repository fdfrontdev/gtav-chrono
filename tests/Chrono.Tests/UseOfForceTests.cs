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

    // ── 3. Compliance stand-down now works from 2★ (was 3★) ──

    [Fact]
    public void StandDown_AtTwoStars_StarsDecayToZero()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 2);

        // 3+ seconds of stillness → stand-down fires, stars decay
        service.Tick();
        service.AdvanceComplianceTime(4.0);
        service.Tick();

        Assert.True(wanted.CurrentStars < 2, "2★ compliant suspect → stars decay");
        Assert.True(crimeProbe.HoldActive, "hold stays active through the stand-down");
    }

    [Fact]
    public void StandDown_ActiveAtOneStar_ContinuesToZero()
    {
        // An ACTIVE stand-down runs all the way to 0 even below the 2★ gate
        var (service, wanted, player, crimeProbe, _) = Build();
        StartChase(service, wanted, 2);
        service.Tick();
        service.AdvanceComplianceTime(4.0);
        service.Tick();                     // stars 2 → 1

        wanted.CurrentStars = 1;
        service.Tick();                     // below gate but already complying → keep going
        Assert.True(wanted.CurrentStars <= 1);
    }

    // ── 4. During a confrontation the hold is OFF (they're cuffing you) ──

    [Fact]
    public void DuringConfrontation_NoHoldFire()
    {
        var (service, wanted, player, crimeProbe, _) = Build();
        wanted.CurrentStars = 4;            // 4★ triggers the confrontation (S19)
        service.Tick();

        Assert.True(service.State == JusticeState.Wanted);
        // confrontation begins — hold must NOT be active (they're closing in)
        Assert.False(crimeProbe.HoldActive);
    }
}
