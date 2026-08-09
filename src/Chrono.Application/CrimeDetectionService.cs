using System;
using System.Collections.Generic;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// S20 — act-based crime detection (ADR-04 D1). Polls the Boundary's
/// <see cref="ICrimeProbe"/> each tick, classifies the player's ACTS via the pure
/// <see cref="CrimeClassifier"/>, applies witness gating (FR-1.4 user ruling r14:
/// a crime records ONLY when NPCs witness it AND the face is visible), dedupes
/// per kind, then records through <see cref="JusticeService"/> which drives the
/// wanted level from the classified act (murder → instant 5★).
/// </summary>
public sealed class CrimeDetectionService
{
    private readonly ICrimeProbe _probe;
    private readonly IWorldProbe _world;
    private readonly IPlayerContext _player;
    private readonly JusticeService _justice;
    private readonly ILogSink _log;
    private readonly JusticeConfig _config;
    private readonly Dictionary<CrimeKind, long> _lastKindMs = new();

    public CrimeDetectionService(
        ICrimeProbe probe,
        IWorldProbe world,
        IPlayerContext player,
        JusticeService justice,
        ILogSink log,
        JusticeConfig config)
    {
        _probe = probe;
        _world = world;
        _player = player;
        _justice = justice;
        _log = log;
        _config = config;
    }

    /// <summary>Per-tick: sample → classify → witness-gate → record.</summary>
    public void Tick(long nowMs)
    {
        if (!_config.CrimeDetectionEnabled) return;

        var ctx = _probe.SampleContext();
        var sample = new ActSample(
            ctx.WeaponOut,
            ctx.IsAiming,
            ctx.InVehicle,
            ctx.VehicleSpeedMps,
            _probe.PollKillSinceLastPoll(),
            _probe.PollPedDamageSinceLastPoll(),
            _probe.PollVehicleDamageSinceLastPoll(),
            _probe.CrosshairPedDistanceM,
            WitnessCount());

        var crime = CrimeClassifier.Classify(sample);
        if (crime == null) return;

        // FR-1.4 witness gating (user ruling r14): only acts SEEN by NPCs record.
        // Invisible player or no witnesses → the act never happened on paper.
        if (!_player.IsVisible || sample.WitnessCount == 0)
        {
            _log.Info($"Act skipped (unwitnessed): {crime.Name} witnesses={sample.WitnessCount} visible={_player.IsVisible}");
            return;
        }

        // Per-kind dedupe window — a burst of punches/kills is ONE event, not spam.
        if (_lastKindMs.TryGetValue(crime.Kind, out long last) && nowMs - last < _config.CrimeKindCooldownSeconds * 1000)
            return;
        _lastKindMs[crime.Kind] = nowMs;

        _justice.RecordDetectedCrime(crime);
    }

    private int WitnessCount()
    {
        float r = _config.CrimeWitnessRadiusM;
        return _world.CountNearbyCivilians(_player.Position, r)
             + _probe.CountNearbyPolice(r);
    }
}
