using System.Diagnostics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Media coverage (FR-4): reports crimes — never causes them. Moderate+ crimes get a
/// BREAKING news headline; Severe also goes viral on WEBNET; escapes trigger a MANHUNT.
/// Throttled (max one news event per window) so a chase doesn't spam the screen.
/// </summary>
public sealed class MediaService
{
    private const int NewsCooldownMs = 30000;

    private readonly IMediaNotifier _media;
    private readonly ILogSink _log;
    private readonly JusticeConfig _config;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastNewsMs = -NewsCooldownMs;   // first event always passes (no overflow)

    public MediaService(IMediaNotifier media, ILogSink log, JusticeConfig config)
    {
        _media = media;
        _log = log;
        _config = config;
    }

    public void ReportCrime(CrimeEvent evt)
    {
        if (!_config.NewsEnabled) return;
        if (evt.Severity == CrimeSeverity.Minor) return;   // FR-4.1: Moderate+ only

        if (!TryTakeNewsSlot()) return;

        switch (evt.Severity)
        {
            case CrimeSeverity.Moderate:
                _media.News($"BREAKING: super-powered suspect seen in {evt.District}");
                break;
            case CrimeSeverity.Severe:
                _media.News($"BREAKING: WANTED super-powered suspect terrorizes {evt.District}");
                if (_config.ViralEnabled)
                    _media.Viral($"WEBNET: {evt.District} dash-cam footage goes viral");
                break;
        }

        _log.Info($"Media: news for {evt.Severity} in {evt.District}");
    }

    /// <summary>Prison escape (FR-4.2/FR-10.2): manhunt + viral footage.</summary>
    public void ReportEscape(string district)
    {
        if (!_config.NewsEnabled) return;
        if (!TryTakeNewsSlot()) return;

        _media.News($"MANHUNT: convicted super-powered fugitive escaped {district}");
        if (_config.ViralEnabled)
            _media.Viral("WEBNET: prison escape footage explodes online");

        _log.Info("Media: manhunt news");
    }

    private bool TryTakeNewsSlot()
    {
        var now = _clock.ElapsedMilliseconds;
        if (now - _lastNewsMs < NewsCooldownMs) return false;
        _lastNewsMs = now;
        return true;
    }
}
