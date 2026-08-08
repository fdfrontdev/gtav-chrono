using Chrono.Application.Ports;

namespace Chrono.Application;

/// <summary>Invisibility (user request v0.3.0): NPCs cannot see or react to the player.</summary>
public sealed class InvisibilityService
{
    private readonly IPlayerContext _player;
    private readonly ILogSink _log;

    public InvisibilityService(IPlayerContext player, ILogSink log)
    {
        _player = player;
        _log = log;
    }

    public bool IsEnabled { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return;
        IsEnabled = enabled;
        _player.SetVisible(!enabled);
        _log.Info(enabled ? "Invisible ON" : "Invisible OFF");
    }

    public void Toggle() => SetEnabled(!IsEnabled);

    public void Tick()
    {
        if (!IsEnabled) return;
        _player.SetVisible(false);   // re-assert (model swaps / cutscenes can reset visibility)
    }
}
