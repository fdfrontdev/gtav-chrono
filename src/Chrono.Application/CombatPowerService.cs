using System;
using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// v0.10 combat powers (SRS FR-B4..B12, ADR D3): Force Push, Energy Blast,
/// Bullet Time, Regeneration — all gated by the energy pool, all harm
/// self-reported through the justice pipeline (stars up-only, media, crowd).
/// Harmless powers (Bullet Time, Regen) never record crimes.
/// </summary>
public sealed class CombatPowerService
{
    private readonly IPowerFxBoundary _fx;
    private readonly IPlayerContext _player;
    private readonly PowerEnergyService _energy;
    private readonly JusticeService _justice;
    private readonly PowersConfig _config;
    private readonly MediaService? _media;
    private readonly CrowdReactionService? _crowd;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly NpcReactionService? _npcReaction;

    private bool _bulletTimeActive;
    private double _bulletTimeAccumulator;   // fractional drain carry (FR-B5: 12/s)
    private double _healSecondsLeft;
    private double _healTick;

    public CombatPowerService(
        IPowerFxBoundary fx,
        IPlayerContext player,
        PowerEnergyService energy,
        JusticeService justice,
        PowersConfig config,
        INotifier notifier,
        ILogSink log,
        MediaService? media = null,
        CrowdReactionService? crowd = null,
        NpcReactionService? npcReaction = null)
    {
        _fx = fx;
        _player = player;
        _energy = energy;
        _justice = justice;
        _config = config;
        _media = media;
        _crowd = crowd;
        _notifier = notifier;
        _log = log;
        _npcReaction = npcReaction;
    }

    public bool IsBulletTimeActive => _bulletTimeActive;

    /// <summary>FR-B4 — aimed impulse on peds + light vehicles in a forward cone.</summary>
    public bool TryForcePush()
    {
        if (!_energy.TrySpend(_config.PushCost))
        {
            OutOfEnergy();
            return false;
        }
        var dir = _player.GetAimDirection();
        if (dir.LengthSquared() < 0.01f) dir = new Vector3(0f, 1f, 0f);
        var report = _fx.Push(_player.Position, Vector3.Normalize(dir), _config.PushRangeM, _config.PushConeDeg, _config.PushVehicleImpulse);
        if (report.HasHarm)
        {
            ReportHarm(report, PowerHarmKind.PushImpact, "TELEKINETIC ASSAULT");
            _crowd?.FleeFrom(_player.Position, _config.PushRangeM);
        }
        _npcReaction?.TriggerGracePeriod();
        _notifier.Show(report.HasHarm ? "FORCE PUSH — they felt that" : "FORCE PUSH");
        _log.Info($"Force Push: {report}");
        return true;
    }

    /// <summary>FR-B5 — energy blast at a fixed distance ahead of the camera aim.</summary>
    public bool TryEnergyBlast()
    {
        if (!_energy.TrySpend(_config.BlastCost))
        {
            OutOfEnergy();
            return false;
        }
        var dir = _player.GetAimDirection();
        if (dir.LengthSquared() < 0.01f) dir = new Vector3(0f, 1f, 0f);
        var target = _player.Position + Vector3.Normalize(dir) * _config.BlastRangeM;
        var report = _fx.Blast(target, _config.BlastRadiusM, _config.BlastDamageScale);
        if (report.HasHarm)
        {
            ReportHarm(report, PowerHarmKind.BlastPedKill, "ENERGY BLAST");
            _crowd?.FleeFrom(target, _config.BlastRadiusM * 2f);
        }
        _notifier.Show(report.HasHarm ? "ENERGY BLAST — the city felt that" : "ENERGY BLAST");
        _log.Info($"Energy Blast: {report}");
        return true;
    }

    /// <summary>FR-B6 — world slow-mo toggle with per-second energy drain.</summary>
    public bool ToggleBulletTime()
    {
        if (_bulletTimeActive)
        {
            _bulletTimeActive = false;
            _fx.SetWorldTimeScale(1f);
            _notifier.Show("Bullet Time off");
            return true;
        }
        if (!_energy.TrySpend(_config.BulletTimeCostPerSecond))
        {
            OutOfEnergy();
            return false;
        }
        _bulletTimeActive = true;
        _fx.SetWorldTimeScale(_config.BulletTimeScale);
        _notifier.Show("BULLET TIME — the world crawls");
        return true;
    }

    /// <summary>FR-B7 — regeneration: heal-over-time + resistance window.</summary>
    public bool TryRegenerate()
    {
        if (_healSecondsLeft > 0) return false;   // already regenerating
        if (!_energy.TrySpend(_config.RegenCost))
        {
            OutOfEnergy();
            return false;
        }
        _healSecondsLeft = _config.RegenSeconds;
        _healTick = 0;
        _fx.HealOverTime(_config.RegenSeconds, _config.RegenDamageResist);
        _notifier.Show("REGENERATION — wounds closing");
        return true;
    }

    /// <summary>Per-frame: bullet-time drain, regen window upkeep. Call every tick.</summary>
    public void Tick(double deltaSeconds)
    {
        if (_bulletTimeActive)
        {
            // FR-B5: 12/s drain — accumulate fractional cost, spend whole points
            // (per-frame rounding would either over- or under-drain)
            _bulletTimeAccumulator += _config.BulletTimeCostPerSecond * deltaSeconds;
            while (_bulletTimeAccumulator >= 1)
            {
                _bulletTimeAccumulator -= 1;
                if (!_energy.TrySpend(1))
                {
                    _bulletTimeActive = false;
                    _fx.SetWorldTimeScale(1f);
                    _notifier.Show("OUT OF ENERGY — Bullet Time ended");
                    break;
                }
            }
        }

        if (_healSecondsLeft > 0)
        {
            _healSecondsLeft -= deltaSeconds;
            _healTick += deltaSeconds;
            if (_healTick >= 0.4)   // refill in pulses — feels like regen, not a snap
            {
                _healTick = 0;
                _player.RefillHealth();
            }
            if (_healSecondsLeft <= 0)
            {
                _healSecondsLeft = 0;
                _fx.CancelHeal();
            }
        }
    }

    /// <summary>Force-off when the mod/powers toggle goes off (menu wiring).</summary>
    public void Deactivate()
    {
        if (_bulletTimeActive)
        {
            _bulletTimeActive = false;
            _fx.SetWorldTimeScale(1f);
        }
        _healSecondsLeft = 0;
        _fx.CancelHeal();
    }

    private void ReportHarm(PowerHitReport report, PowerHarmKind kind, string name)
    {
        // FR-B8: classify from the WORST harm in the report (kill > injury > vehicle > prop)
        PowerHarmKind worst = report.PedsKilled > 0 ? PowerHarmKind.BlastPedKill
            : report.PedsInjured > 0 ? PowerHarmKind.BlastPedHurt
            : report.VehiclesDamaged > 0 ? PowerHarmKind.BlastVehicleDamage
            : PowerHarmKind.BlastPropDamage;
        if (kind == PowerHarmKind.PushImpact) worst = PowerHarmKind.PushImpact;   // push is always assault-grade
        var crime = PowerHarmClassifier.Classify(worst);
        _justice.RecordDetectedCrime(crime);
        _media?.News($"SUPER-POWERED {name} in {_player.GetDistrictName()}");
        _media?.Viral($"WEBNET: {name} caught on camera — who IS this?!");
    }

    private void OutOfEnergy() => _notifier.Show("OUT OF ENERGY — wait for the pool to refill");
}
