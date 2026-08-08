using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// Media output: HUD notifications + TV channel push. Weasel News (channel 1) is set
/// so any TV the player approaches afterwards shows the news (FR-4). HUD-only fallback
/// if the native ever misbehaves — media is flavor, never a crash vector.
/// </summary>
public sealed class MediaNotifier : IMediaNotifier
{
    private readonly INotifier _notifier;

    public MediaNotifier(INotifier notifier)
    {
        _notifier = notifier;
    }

    public void News(string headline)
    {
        _notifier.Show(headline);
        try
        {
            Function.Call(Hash.SET_TV_CHANNEL, 1);   // Weasel News
        }
        catch
        {
            // HUD-only fallback
        }
    }

    public void Viral(string message) => _notifier.Show(message);
}
