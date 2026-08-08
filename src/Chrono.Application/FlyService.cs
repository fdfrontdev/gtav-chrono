using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Dragon Ball flight (v0.3.0) + superman pose (v0.4.0). Camera-relative WASD + Space/Ctrl.
/// Gravity/ragdoll off; per-tick velocity; ped faces movement direction and plays
/// skydive@freefall/free_forward (dive pose) while moving, free_idle while hovering
/// (anim names verified against the DurtyFree GTA V anim dict dump).
/// </summary>
public sealed class FlyService
{
    // Verified anim dicts (DurtyFree gta-v-data-dumps, 2026-08-08)
    private const string FlyDict = "skydive@freefall";
    private const string FlyAnim = "free_forward";

    private readonly IPlayerContext _player;
    private readonly IGameInput _input;
    private readonly ILogSink _log;
    private readonly FlyConfig _config;
    private string? _currentAnim;

    public FlyService(IPlayerContext player, IGameInput input, ILogSink log, FlyConfig config)
    {
        _player = player;
        _input = input;
        _log = log;
        _config = config;
    }

    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return;
        IsEnabled = enabled;

        _player.SetGravityEnabled(!enabled);
        _player.SetRagdollEnabled(!enabled);
        _player.SetVelocity(Vector3.Zero);
        if (!enabled)
        {
            _player.ClearCurrentAnimation();
            _currentAnim = null;
        }

        _log.Info(enabled ? "Flight enabled" : "Flight disabled");
    }

    public void Toggle() => SetEnabled(!IsEnabled);

    /// <summary>
    /// Per-tick flight control. Pose spec (user report v0.4.0): standing while
    /// hovering/ascending/descending (takeoff &amp; landing = stand animation),
    /// superman dive pose ONLY while moving horizontally at speed.
    /// </summary>
    public void Tick()
    {
        if (!IsEnabled) return;
        if (_player.IsInVehicle) return; // no flying in a car

        var camDir = _player.GetAimDirection();
        var forward = new Vector3(camDir.X, camDir.Y, 0f);
        if (forward.LengthSquared() < 0.0001f) forward = new Vector3(0f, 1f, 0f);
        forward = Vector3.Normalize(forward);
        var right = Vector3.Cross(forward, Vector3.UnitZ); // facing north → east (GTA right-hand)

        var velocity = FlyMath.CalculateVelocity(
            forward, right, _config.Speed,
            _input.IsFlyForward, _input.IsFlyBack, _input.IsFlyLeft, _input.IsFlyRight,
            _input.IsFlyAscend, _input.IsFlyDescend);

        _player.SetVelocity(velocity);

        var horizontal = new Vector3(velocity.X, velocity.Y, 0f);
        if (horizontal.LengthSquared() > 0.5f)
        {
            // Flying forward/sideways → superman dive pose, facing the movement direction
            _player.SetHeading(TeleportMath.HeadingFromVelocity(velocity));
            EnsureAnim(FlyDict, FlyAnim);
        }
        else
        {
            // Hovering, ascending or descending → standing (anime takeoff/landing spec)
            if (_currentAnim != null)
            {
                _player.ClearCurrentAnimation();
                _currentAnim = null;
            }
        }
    }

    private void EnsureAnim(string dict, string anim)
    {
        if (_currentAnim == anim) return;
        _player.PlayLoopedAnimation(dict, anim);
        _currentAnim = anim;
    }
}
