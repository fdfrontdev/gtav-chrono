namespace Chrono.Application.Ports;

/// <summary>Game clock control (pause/resume game time).</summary>
public interface IGameClock
{
    bool IsPaused { get; }
    void Pause();
    void Resume();
}
