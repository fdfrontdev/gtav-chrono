namespace Chrono.Application.Ports;

/// <summary>Current wanted level (boundary reads the game; edge logic lives in JusticeService).</summary>
public interface IWantedMonitor
{
    int CurrentStars { get; }

    /// <summary>Set the wanted level directly (escape manhunt, FR-10.2).</summary>
    void SetStars(int stars);
}
