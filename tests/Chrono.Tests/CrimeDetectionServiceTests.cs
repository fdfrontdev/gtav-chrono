using System.Numerics;
using Chrono.Application;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S20 — act-based crime detection (ADR-04 D1): the mod classifies the ACT and
/// drives the wanted level from it (murder → instant 5★), witness-gated per the
/// user ruling r14 (only acts SEEN by NPCs record; invisible = nothing).
/// </summary>
public class CrimeDetectionServiceTests
{
    private const long T0 = 100_000;

    private static (CrimeDetectionService detection, JusticeService justice, FakeWantedMonitor wanted,
        FakePlayer player, FakeCrimeProbe probe, FakeProbe world, FakeRecordStore store, FakeNotifier notifier)
        Build(JusticeConfig? config = null)
    {
        var cfg = config ?? new JusticeConfig { WarrantReportSeconds = 0 };
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var probe = new FakeCrimeProbe();
        var world = new FakeProbe { NearbyCivilians = 5 };   // witnesses present by default
        var justice = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(), cfg, new FakeClock(),
            new MediaService(new FakeMediaNotifier(), new FakeLog(), cfg),
            input: new FakeInput(), probe: world, random: () => 0.5,
            crimeProbe: probe);
        var detection = new CrimeDetectionService(probe, world, player, justice, new FakeLog(), cfg);
        return (detection, justice, wanted, player, probe, world, store, notifier);
    }

    // ── 1. Murder drives 5★ immediately ──

    [Fact]
    public void GunKill_RecordsMurder_AndDrivesFiveStars()
    {
        var (detection, _, wanted, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Gun);

        detection.Tick(T0);

        Assert.Equal(5, wanted.CurrentStars);                       // murder → instant 5★
        var evt = Assert.Single(store.Record.Events);
        Assert.Equal("murder", evt.Kind);
        Assert.Equal(CrimeSeverity.Severe, evt.Severity);
        Assert.True(evt.Burned);                                    // visible + witnessed
        Assert.True(store.Status.WarrantActive);                    // Severe → warrant
    }

    [Fact]
    public void MeleeKill_DrivesFourStars()
    {
        var (detection, _, wanted, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Melee, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Melee);

        detection.Tick(T0);

        Assert.Equal(4, wanted.CurrentStars);
        Assert.Equal(CrimeSeverity.Severe, Assert.Single(store.Record.Events).Severity);
    }

    // ── 2. Witness gating (user ruling r14) ──

    [Fact]
    public void NoWitnesses_NoRecord_NoStars()
    {
        var (detection, _, wanted, _, probe, world, store, _) = Build();
        world.NearbyCivilians = 0;
        probe.PoliceCount = 0;
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Gun);

        detection.Tick(T0);

        Assert.Empty(store.Record.Events);      // nobody saw → never happened on paper
        Assert.Equal(0, wanted.CurrentStars);   // and no forced stars
    }

    [Fact]
    public void InvisiblePlayer_NoRecord_NoStars()
    {
        var (detection, _, wanted, player, probe, _, store, _) = Build();
        player.IsVisible = false;
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Gun);

        detection.Tick(T0);

        Assert.Empty(store.Record.Events);      // invisible → stealth preserved
        Assert.Equal(0, wanted.CurrentStars);
    }

    // ── 3. Brandishing / robbery / property / assault ──

    [Fact]
    public void BrandishingGun_WithWitnesses_RecordsMinorOneStar()
    {
        var (detection, _, wanted, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, true, false, 0f);

        detection.Tick(T0);

        var evt = Assert.Single(store.Record.Events);
        Assert.Equal("brandishing", evt.Kind);
        Assert.Equal(CrimeSeverity.Minor, evt.Severity);
        Assert.Equal(1, wanted.CurrentStars);
        Assert.False(store.Status.WarrantActive);   // Minor → no warrant
    }

    [Fact]
    public void AimingAtPedCloseRange_IsAttemptedRobbery()
    {
        var (detection, _, wanted, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, true, false, 0f);
        probe.CrosshairDistance = 3f;

        detection.Tick(T0);

        var evt = Assert.Single(store.Record.Events);
        Assert.Equal("attempted_robbery", evt.Kind);
        Assert.Equal(CrimeSeverity.Moderate, evt.Severity);
        Assert.Equal(3, wanted.CurrentStars);
    }

    [Fact]
    public void VehicleKillAtSpeed_IsVehicularManslaughter()
    {
        var (detection, _, wanted, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.None, false, true, 25f);
        probe.Kills.Enqueue(DeathCauseKind.Vehicle);

        detection.Tick(T0);

        var evt = Assert.Single(store.Record.Events);
        Assert.Equal("vehicular_manslaughter", evt.Kind);
        Assert.Equal(CrimeSeverity.Moderate, evt.Severity);
        Assert.Equal(3, wanted.CurrentStars);
    }

    [Fact]
    public void VehicleDamage_IsPropertyDamage()
    {
        var (detection, _, wanted, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.VehicleDamage = true;

        detection.Tick(T0);

        var evt = Assert.Single(store.Record.Events);
        Assert.Equal("property_damage", evt.Kind);
        Assert.Equal(CrimeSeverity.Minor, evt.Severity);
        Assert.Equal(1, wanted.CurrentStars);
    }

    [Fact]
    public void NonLethalPedDamage_IsAssault()
    {
        var (detection, _, wanted, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Melee, false, false, 0f);
        probe.PedDamage = true;

        detection.Tick(T0);

        var evt = Assert.Single(store.Record.Events);
        Assert.Equal("assault", evt.Kind);
        Assert.Equal(CrimeSeverity.Minor, evt.Severity);
        Assert.Equal(2, wanted.CurrentStars);
    }

    // ── 4. Dedupe per kind (cooldown) ──

    [Fact]
    public void SameKindWithinCooldown_RecordsOnce()
    {
        var (detection, _, _, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Gun);

        detection.Tick(T0);
        probe.Kills.Enqueue(DeathCauseKind.Gun);   // second kill inside the window
        detection.Tick(T0 + 5_000);

        Assert.Single(store.Record.Events);        // one murder charge, not two
    }

    [Fact]
    public void DifferentKinds_RecordBoth()
    {
        var (detection, _, _, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Gun);
        detection.Tick(T0);

        probe.Context = new PlayerActContext(DeathCauseKind.None, false, true, 25f);
        probe.Kills.Enqueue(DeathCauseKind.Vehicle);
        detection.Tick(T0 + 5_000);

        Assert.Equal(2, store.Record.Events.Count);
        Assert.Equal("murder", store.Record.Events[0].Kind);
        Assert.Equal("vehicular_manslaughter", store.Record.Events[1].Kind);
    }

    [Fact]
    public void SameKindAfterCooldown_RecordsAgain()
    {
        var (detection, _, _, _, probe, _, store, _) = Build(new JusticeConfig { WarrantReportSeconds = 0, CrimeKindCooldownSeconds = 20 });
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Gun);
        detection.Tick(T0);

        probe.Kills.Enqueue(DeathCauseKind.Gun);
        detection.Tick(T0 + 25_000);

        Assert.Equal(2, store.Record.Events.Count);
    }

    // ── 5. Bail revocation via detected act ──

    [Fact]
    public void NewCrimeWhileOnBail_RevokesBail()
    {
        var (detection, justice, wanted, _, probe, _, store, notifier) = Build();
        // Simulate capture → bail via the real service flow
        wanted.CurrentStars = 4;
        justice.Tick();
        probe.NearestPoliceDistance = 2f;
        justice.Tick();                       // cuffed
        justice.PostBail();                   // on bail
        Assert.True(justice.IsOnBail);

        // New witnessed murder while on bail → revoked
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Gun);
        detection.Tick(T0 + 10_000);

        Assert.False(justice.IsOnBail);
        Assert.True(store.Status.WarrantActive);
        Assert.Contains(notifier.Messages, m => m.Contains("BAIL REVOKED"));
    }

    // ── 6. The star-proxy does not double-record the same act ──

    [Fact]
    public void DetectedAct_SuppressesProxyRecordAtSameLevel()
    {
        var (detection, justice, wanted, _, probe, _, store, _) = Build();
        probe.Context = new PlayerActContext(DeathCauseKind.Gun, false, false, 0f);
        probe.Kills.Enqueue(DeathCauseKind.Gun);

        detection.Tick(T0);      // records murder + sets 5★
        wanted.CurrentStars = 5;
        justice.Tick();          // proxy sees 5★ edge → must NOT record a second event

        Assert.Single(store.Record.Events);
        Assert.Equal("murder", store.Record.Events[0].Kind);
    }
}
