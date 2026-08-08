using Chrono.Application.Ports;
using GTA.UI;

namespace Chrono.Boundary;

/// <summary>In-game feed notifications.</summary>
public sealed class Notifier : INotifier
{
    public void Show(string message)
        => Notification.PostTicker(message, false, false);
}
