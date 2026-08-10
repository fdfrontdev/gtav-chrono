using System;
using System.Diagnostics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// S22 v8 r3 (user UAT: "the live feed seems too quiet with all the events
/// happening") — ambient WORLD chatter for the widget feed. When the player
/// is FREE and no crime has been recorded for a while, the city still talks:
/// police blotter color, traffic/weather WEBNET lines, neighbourhood gossip.
/// One slow stream (~60s cadence, configurable), separate from the 30s crime
/// slot in <see cref="MediaService"/> — throttle noise, don't starve signal
/// (EMS activity-feed lesson from Second Brain).
///
/// The justice layer stays quiet (no warrant, no stars, no reputation change)
/// — ambient is FLAVOR ONLY.
/// </summary>
public sealed class AmbientFeedService
{
    private static readonly string[] Districts =
    {
        "VINEWOOD", "MIRROR PARK", "DEL PERRO", "ROCKFORD HILLS", "LA MESA",
        "DAVIS", "PALETO BAY", "SANDY SHORES", "CHUMASH", "DOWNTOWN",
    };

    private static readonly string[] BlotterLines =
    {
        "Police blotter: {0} — noise complaint, no arrests",
        "Police blotter: {0} — parking dispute resolved",
        "Police blotter: {0} — routine patrol, all quiet",
        "Police blotter: {0} — lost dog reunited with owner",
    };

    private static readonly string[] WebnetLines =
    {
        "WEBNET: traffic slow on the {0} freeway — expect delays",
        "WEBNET: {0} food festival this weekend — vendors wanted",
        "WEBNET: {0} street fair tonight, road closures until 10pm",
        "WEBNET: heatwave forecast for {0} — stay hydrated",
    };

    private readonly HudFeedBuffer _feed;
    private readonly JusticeConfig _config;
    private readonly IGameClock _clock;
    private readonly Func<double> _random;
    private readonly ILogSink _log;

    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _lastDay = -1;

    public AmbientFeedService(
        HudFeedBuffer feed,
        JusticeConfig config,
        IGameClock clock,
        ILogSink log,
        Func<double>? random = null)
    {
        _feed = feed;
        _config = config;
        _clock = clock;
        _log = log;
        _random = random ?? (() => new Random().NextDouble());
    }

    /// <summary>
    /// Drive the ambient stream. Call every tick; <paramref name="worldQuiet"/>
    /// = the justice state is FREE (no chase, no custody, no manhunt) AND the
    /// player isn't mid-mission. Only then does the city chatter.
    /// </summary>
    public void Tick(bool worldQuiet)
    {
        if (!_config.AmbientFeedEnabled) return;
        if (!worldQuiet)
        {
            _stopwatch.Restart();   // a chase resets the quiet timer — chatter only after calm
            return;
        }

        // One ambient line per game day at most, ~60s after the last one —
        // the FEEL-GOOD clean-day news and police chatter stay the loudest.
        if (_stopwatch.ElapsedMilliseconds < _config.AmbientFeedIntervalMs) return;
        int today = _clock.CurrentGameDay;
        if (_lastDay == today) return;
        _lastDay = today;

        string district = Districts[(int)(_random() * Districts.Length) % Districts.Length];
        bool webnet = _random() < 0.5;
        string line = webnet
            ? string.Format(WebnetLines[(int)(_random() * WebnetLines.Length) % WebnetLines.Length], district)
            : string.Format(BlotterLines[(int)(_random() * BlotterLines.Length) % BlotterLines.Length], district);
        _feed.Push(line, webnet ? FeedKind.Webnet : FeedKind.Message);
        _log.Info($"Ambient feed: {line}");
    }
}
