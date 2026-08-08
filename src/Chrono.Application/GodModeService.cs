using Chrono.Application.Ports;

namespace Chrono.Application;

/// <summary>God Mode (user request v0.3.0): invincible + health refilled every tick.</summary>
public sealed class GodModeService
{
    private readonly IPlayerContext _player;
    private readonly ILogSink _log;

    public GodModeService(IPlayerContext player, ILogSink log)
    {
        _player = player;
        _log = log;
    }

    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return;
        IsEnabled = enabled;
        _player.SetInvincible(enabled);
        if (enabled) _player.RefillHealth();
        _log.Info(enabled ? "God mode ON" : "God mode OFF");
    }

    public void Toggle() => SetEnabled(!IsEnabled);

    public void Tick()
    {
        if (!IsEnabled) return;
        _player.SetInvincible(true);   // re-assert (some events can clear the flag)
        _player.RefillHealth();
    }
}
