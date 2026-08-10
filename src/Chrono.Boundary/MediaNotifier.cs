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
    public void News(string headline)
    {
        // S22 v8 r3 (user UAT: "FEEL-GOOD appears twice in the feed"): the
        // widget feed is written ONLY by MediaService.PushFeed (Webnet/Viral
        // kinds). Calling _notifier.Show here DOUBLE-pushed every headline
        // (gray Message + blue Webnet). TV channel push stays.
        try
        {
            Function.Call(Hash.SET_TV_CHANNEL, 1);   // Weasel News
        }
        catch
        {
            // HUD-only fallback
        }
    }

    public void Viral(string message)
    {
        // S22 v8 r3: same dedupe as News — feed written only by PushFeed.
        _log?.Info($"Media: viral display suppressed (feed owns it) — {message}");
    }

    private readonly ILogSink? _log;

    public MediaNotifier(ILogSink? log = null)
    {
        _log = log;
    }
}
