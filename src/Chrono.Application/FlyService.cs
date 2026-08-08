using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Dragon Ball flight (user request v0.3.0). Camera-relative WASD + Space/Ctrl.
/// Gravity and ragdoll disabled while flying; velocity driven per tick (hover works).
/// </summary>
public sealed class FlyService
{
    private readonly IPlayerContext _player;
    private readonly IGameInput _input;
    private readonly ILogSink _log;
    private readonly FlyConfig _config;

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
        if (!enabled) _player.SetVelocity(Vector3.Zero);

        _log.Info(enabled ? "Flight enabled" : "Flight disabled");
    }

    public void Toggle() => SetEnabled(!IsEnabled);

    /// <summary>Per-tick flight control (only while enabled and on foot).</summary>
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
    }
}
