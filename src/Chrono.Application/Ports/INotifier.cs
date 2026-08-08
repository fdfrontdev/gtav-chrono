namespace Chrono.Application.Ports;

/// <summary>In-game notifications (implemented by the SHVDN boundary).</summary>
public interface INotifier
{
    void Show(string message);
}
