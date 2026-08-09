using Chrono.Application;
using Chrono.Application.Ports;

namespace Chrono.Boundary;

/// <summary>
/// In-game notifications. S21 v2 (user UAT): messages go INTO the HUD widget
/// feed (bottom-right) instead of the vanilla bottom-left ticker — the player
/// asked for all messages to live inside the widget. If no feed is wired
/// (preview/edge cases) the vanilla ticker is the fallback.
/// </summary>
public sealed class Notifier : INotifier
{
    private readonly HudFeedBuffer? _feed;

    public Notifier(HudFeedBuffer? feed = null)
    {
        _feed = feed;
    }

    public void Show(string message)
    {
        if (_feed != null)
            _feed.Push(message, FeedKind.Message);
        else
            GTA.UI.Notification.PostTicker(message, false, false);   // fallback only
    }
}
