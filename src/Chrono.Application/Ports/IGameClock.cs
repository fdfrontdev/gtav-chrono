namespace Chrono.Application.Ports;

/// <summary>Game clock control (pause/resume game time) + game-day number for justice trials.</summary>
public interface IGameClock
{
    bool IsPaused { get; }
    void Pause();
    void Resume();

    /// <summary>Monotonic in-game day number (year*372 + month*31 + day) — court dates.</summary>
    int CurrentGameDay { get; }
}
