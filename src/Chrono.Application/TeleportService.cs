using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Teleport use cases (SRS FR-3, FR-4, ADR-02). Every teleport is validated:
/// raycast wall-check + ground snap. Never teleports into solid geometry.
/// </summary>
public sealed class TeleportService
{
    private readonly IPlayerContext _player;
    private readonly IWorldProbe _probe;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly DashConfig _dashConfig;
    private readonly TeleportConfig _teleportConfig;
    private Vector3? _lastAimTarget;   // reticle cache (v0.8.0)

    public TeleportService(
        IPlayerContext player,
        IWorldProbe probe,
        INotifier notifier,
        ILogSink log,
        DashConfig dashConfig,
        TeleportConfig teleportConfig)
    {
        _player = player;
        _probe = probe;
        _notifier = notifier;
        _log = log;
        _dashConfig = dashConfig;
        _teleportConfig = teleportConfig;
    }

    /// <summary>Dash: aimed point (roof/wall-aware, v0.8.0) or forward-facing blink. Wall-safe.</summary>
    public TeleportResult TryDash()
    {
        Vector3 origin = _player.Position;

        Vector3 target;
        if (_player.IsAiming)
        {
            var camDir = GetAimDirection();

            // Aim raycast FIRST: if the crosshair hits a roof or wall within range,
            // blink TO the hit point (pulled back 0.6 m so we don't embed in it) —
            // this is what enables blinking onto building rooftops (user report
            // v0.8.0: "can't blink to the top of a building").
            var aimRay = _probe.Raycast(origin, Vector3.Normalize(camDir), _dashConfig.MaxRange);
            if (aimRay.Hit && Vector3.Distance(origin, aimRay.HitPosition) >= 2.0f)
            {
                var back = Vector3.Normalize(camDir) * 0.6f;
                var hitLanding = aimRay.HitPosition - back;
                _lastAimTarget = hitLanding;

                if (!TeleportMath.IsInsideWorldBounds(hitLanding))
                {
                    _log.Info("Dash blocked — outside world bounds");
                    _notifier.Show(UiStrings.MapEdge);
                    return TeleportResult.NoClearPath();
                }

                ExecuteTeleport(hitLanding);
                _notifier.Show(UiStrings.DashSuccess);
                return TeleportResult.Success(hitLanding);
            }

            // Nothing hit: clamp to max range along the aim, ground-snap below.
            var clamped = TeleportMath.ClampToRange(origin, origin + camDir * _dashConfig.MaxRange, 5.0f, _dashConfig.MaxRange);
            if (clamped == null) return TeleportResult.NoClearPath();
            target = clamped.Value;
        }
        else
        {
            target = TeleportMath.CalculateForwardTarget(origin, _player.Heading, _dashConfig.Range);
        }

        // Wall check: raycast from player toward target — blocked if something solid is in the way
        var ray = _probe.Raycast(origin, Vector3.Normalize(target - origin), Vector3.Distance(origin, target));
        if (!TeleportMath.IsPathClear(ray, 1.0f))
        {
            _log.Info("Dash blocked — path not clear");
            _notifier.Show(UiStrings.DashBlocked);
            return TeleportResult.NoClearPath();
        }

        // Ground snap the landing point
        var ground = _probe.GetGroundHeight(target);
        var landing = ground.HasValue
            ? new Vector3(target.X, target.Y, ground.Value)
            : target;

        // Map boundary guard (user report v0.3.0: dash must not go off-map)
        if (!TeleportMath.IsInsideWorldBounds(landing))
        {
            _log.Info("Dash blocked — outside world bounds");
            _notifier.Show(UiStrings.MapEdge);
            return TeleportResult.NoClearPath();
        }

        _lastAimTarget = null;
        ExecuteTeleport(landing);
        _notifier.Show(UiStrings.DashSuccess);
        return TeleportResult.Success(landing);
    }

    /// <summary>Last computed aim-blink landing point (for the targeting reticle); null when not aiming.</summary>
    public Vector3? GetAimTarget()
    {
        if (!_player.IsAiming) return null;
        if (_lastAimTarget.HasValue) return _lastAimTarget.Value;

        // No cached raycast this tick — compute the no-hit fallback landing for display
        var camDir = GetAimDirection();
        var clamped = TeleportMath.ClampToRange(_player.Position, _player.Position + camDir * _dashConfig.MaxRange, 5.0f, _dashConfig.MaxRange);
        return clamped;
    }

    /// <summary>Map teleport: waypoint required; ground-snapped landing.</summary>
    public TeleportResult TryMapTeleport()
    {
        if (!_player.IsWaypointActive())
        {
            _notifier.Show(UiStrings.NoWaypoint);
            return TeleportResult.NoWaypoint();
        }

        var waypoint = _player.GetWaypointPosition();
        var probeStart = new Vector3(waypoint.X, waypoint.Y, waypoint.Z + _teleportConfig.GroundProbeDistance);
        var ground = _probe.GetGroundHeight(probeStart);
        var landing = TeleportMath.SnapToGround(probeStart, ground.HasValue ? new Vector3(waypoint.X, waypoint.Y, ground.Value) : (Vector3?)null, _teleportConfig.GroundProbeDistance, waypoint);

        if (!TeleportMath.IsInsideWorldBounds(landing))
        {
            _log.Info("Map teleport refused — waypoint outside world bounds");
            return TeleportResult.Failed();
        }

        ExecuteTeleport(landing);
        return TeleportResult.Success(landing);
    }

    private void ExecuteTeleport(Vector3 landing)
    {
        _player.Teleport(landing);
        _log.Info($"Teleported to ({landing.X:F1}, {landing.Y:F1}, {landing.Z:F1})");
    }

    private Vector3 GetAimDirection() => _player.GetAimDirection();
}
