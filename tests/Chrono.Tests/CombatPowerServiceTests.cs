using System;
using System.Collections.Generic;
using System.Numerics;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// v0.10 combat powers (SRS FR-B4..B12, ADR D3): energy gating, harm
/// self-reporting through the justice pipeline (stars up-only + record),
/// media + crowd wiring, harmless powers never record crimes.
/// </summary>
public class CombatPowerServiceTests
{
    internal sealed class FakePowerFx : IPowerFxBoundary
    {
        public PowerHitReport PushReport { get; set; } = new(0, 0, 0, 0);
        public PowerHitReport BlastReport { get; set; } = new(0, 0, 0, 0);
        public List<float> TimeScales { get; } = new();
        public int HealCalls { get; private set; }
        public int CancelHealCalls { get; private set; }

        public PowerHitReport Push(Vector3 o, Vector3 d, float r, float c, float vi) => PushReport;
        public PowerHitReport Blast(Vector3 t, float r, float ds) => BlastReport;
        public void SetWorldTimeScale(float s) => TimeScales.Add(s);
        public void HealOverTime(int seconds, float resist) => HealCalls++;
        public void CancelHeal() => CancelHealCalls++;
    }

    internal sealed class FakeMediaNotifier : IMediaNotifier
    {
        public List<string> NewsLines { get; } = new();
        public List<string> ViralLines { get; } = new();
        public void News(string headline) => NewsLines.Add(headline);
        public void Viral(string message) => ViralLines.Add(message);
    }

    private static (CombatPowerService combat, FakePowerFx fx, PowerEnergyService energy, FakeWantedMonitor wanted,
        FakePlayer player, FakeRecordStore store, FakeProbe probe, FakeNotifier notifier, FakeMediaNotifier mediaOut) Build()
    {
        var fx = new FakePowerFx();
        var energy = new PowerEnergyService(new PowersConfig());
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000 };
        var store = new FakeRecordStore();
        var probe = new FakeProbe { NearbyCivilians = 5 };
        var notifier = new FakeNotifier();
        var mediaOut = new FakeMediaNotifier();
        var media = new MediaService(mediaOut, new FakeLog(), new JusticeConfig(), new HudFeedBuffer(),
            characterName: () => "Franklin");
        var identity = new IdentityService(store, new FakeLog());
        var warrant = new WarrantService(store, new FakeLog());
        var justice = new JusticeService(
            wanted, player, store, identity, warrant, notifier, new FakeLog(),
            new JusticeConfig(), new FakeClock(), media,
            probe: probe, crimeProbe: new FakeCrimeProbe());
        var crowd = new CrowdReactionService(player, probe, identity, new ReputationService(store, new FakeClock(), media, new JusticeConfig()), notifier, new FakeLog());
        var combat = new CombatPowerService(fx, player, energy, justice, new PowersConfig(), notifier, new FakeLog(), media, crowd);
        return (combat, fx, energy, wanted, player, store, probe, notifier, mediaOut);
    }

    [Fact]
    public void Push_WithHarm_RecordsModerateCrimeAndRaisesStars()
    {
        var (combat, fx, _, wanted, _, store, probe, notifier, _) = Build();
        fx.PushReport = new PowerHitReport(1, 0, 0, 0);   // one ped shoved into a wall

        Assert.True(combat.TryForcePush());

        Assert.Single(store.Record.Events);
        Assert.Equal(CrimeSeverity.Moderate, store.Record.Events[0].Severity);
        Assert.Equal("TELEKINETIC ASSAULT", store.Record.Events[0].Kind);   // kind is the crime name
        Assert.Equal(3, wanted.CurrentStars);             // stars up-only (FR-B8)
        Assert.True(probe.FleeCalls > 0);                 // crowd flees (FR-B11)
        Assert.Contains(notifier.Messages, m => m.Contains("FORCE PUSH"));
    }

    [Fact]
    public void Push_NoHarm_NoCrimeNoStars()
    {
        var (combat, fx, _, wanted, _, store, _, notifier, _) = Build();
        fx.PushReport = new PowerHitReport(0, 0, 0, 0);   // empty air

        Assert.True(combat.TryForcePush());

        Assert.Empty(store.Record.Events);                // FR-B12: no harm = no crime
        Assert.Equal(0, wanted.CurrentStars);
    }

    [Fact]
    public void Blast_KillingPed_SevereCrimeAndNews()
    {
        var (combat, fx, _, wanted, _, store, _, _, mediaOut) = Build();
        fx.BlastReport = new PowerHitReport(0, 1, 0, 0);   // one ped killed

        Assert.True(combat.TryEnergyBlast());

        Assert.Single(store.Record.Events);
        Assert.Equal(CrimeSeverity.Severe, store.Record.Events[0].Severity);
        Assert.Equal("ENERGY BLAST", store.Record.Events[0].Kind);
        Assert.Equal(5, wanted.CurrentStars);
        Assert.Contains(mediaOut.NewsLines, n => n.Contains("ENERGY BLAST"));   // FR-B10
        Assert.Contains(mediaOut.ViralLines, v => v.Contains("ENERGY BLAST"));
    }

    [Fact]
    public void Blast_DamagingVehicle_PropertyCrime()
    {
        var (combat, fx, _, wanted, _, store, _, _, _) = Build();
        fx.BlastReport = new PowerHitReport(0, 0, 2, 0);   // two cars

        combat.TryEnergyBlast();

        Assert.Single(store.Record.Events);
        Assert.Equal("SUPER-POWERED VANDALISM", store.Record.Events[0].Kind);
        Assert.Equal(3, wanted.CurrentStars);
    }

    [Fact]
    public void OutOfEnergy_RefusesPower()
    {
        var (combat, fx, energy, _, _, store, _, notifier, _) = Build();
        energy.TrySpend(energy.Max);                        // drain the pool

        Assert.False(combat.TryForcePush());

        Assert.Empty(store.Record.Events);                  // no harm, no crime
        Assert.Contains(notifier.Messages, m => m.Contains("OUT OF ENERGY"));
    }

    [Fact]
    public void BulletTime_TogglesAndDrains()
    {
        var (combat, fx, energy, _, _, _, _, _, _) = Build();

        Assert.True(combat.ToggleBulletTime());
        Assert.True(combat.IsBulletTimeActive);
        Assert.Contains(0.3f, fx.TimeScales);

        combat.Tick(2);                                     // 2s × 12/s = 24 energy
        Assert.Equal(100 - 12 - 24, energy.Current);        // initial toggle 12 + drain

        combat.ToggleBulletTime();
        Assert.False(combat.IsBulletTimeActive);
        Assert.Contains(1f, fx.TimeScales);                 // world restored
    }

    [Fact]
    public void BulletTime_EmptyPool_ForcesOff()
    {
        var (combat, fx, energy, _, _, _, _, notifier, _) = Build();
        combat.ToggleBulletTime();
        energy.TrySpend(energy.Current);                    // drain everything that's left

        combat.Tick(5);                                     // drain attempt fails

        Assert.False(combat.IsBulletTimeActive);
        Assert.Contains(1f, fx.TimeScales);
        Assert.Contains(notifier.Messages, m => m.Contains("Bullet Time ended"));
    }

    [Fact]
    public void Regen_HealsInPulses_AndCostsEnergy()
    {
        var (combat, fx, energy, _, player, _, _, _, _) = Build();

        Assert.True(combat.TryRegenerate());
        Assert.Equal(100 - 35, energy.Current);

        combat.Tick(0.5);                                    // first pulse
        combat.Tick(0.5);                                    // second pulse
        Assert.True(player.RefillCount >= 2);
        Assert.Equal(1, fx.HealCalls);

        for (int i = 0; i < 10; i++) combat.Tick(0.5);       // window over
        Assert.Equal(1, fx.CancelHealCalls);
    }
}
