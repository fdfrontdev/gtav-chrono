namespace Chrono.Application.Ports;

/// <summary>Current wanted level (boundary reads the game; edge logic lives in JusticeService).</summary>
public interface IWantedMonitor
{
    int CurrentStars { get; }
}
