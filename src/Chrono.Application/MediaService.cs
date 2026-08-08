using System;
using System.Collections.Generic;
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
    private readonly List<NewsFeedItem> _feed = new();
    private long _lastNewsMs = -NewsCooldownMs;   // first event always passes (no overflow)

    public MediaService(IMediaNotifier media, ILogSink log, JusticeConfig config)
    {
        _media = media;
        _log = log;
        _config = config;
    }

    /// <summary>Session social feed (WEBNET phone, S7) — newest last, cap 20.</summary>
    public IReadOnlyList<NewsFeedItem> Feed => _feed;

    private void PushFeed(string text, bool viral)
    {
        _feed.Add(new NewsFeedItem(text, DateTime.Now.ToString("HH:mm"), viral));
        if (_feed.Count > 20) _feed.RemoveAt(0);
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
                PushFeed($"BREAKING: super-powered suspect seen in {evt.District}", false);
                break;
            case CrimeSeverity.Severe:
                _media.News($"BREAKING: WANTED super-powered suspect terrorizes {evt.District}");
                PushFeed($"BREAKING: WANTED suspect terrorizes {evt.District}", false);
                if (_config.ViralEnabled)
                {
                    _media.Viral($"WEBNET: {evt.District} dash-cam footage goes viral");
                    PushFeed($"{evt.District} dash-cam footage goes viral", true);
                }
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
        PushFeed($"MANHUNT: fugitive escaped {district}", true);
        if (_config.ViralEnabled)
        {
            _media.Viral("WEBNET: prison escape footage explodes online");
            PushFeed("Prison escape footage explodes online", true);
        }

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
