using System;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Reputation (S9): everything you do shapes how the city sees you.
/// Two counters — Notoriety (crimes, escapes, hacks) and Fame (clean days,
/// paying fines, rehabilitation). The public image label + a report-chance
/// modifier drive crowd reactions (scared flee / warm greeting) and warrant
/// enforcement. Media covers milestone shifts (the city talks).
/// </summary>
public sealed class ReputationService
{
    // Milestone thresholds (Notoriety) / (Fame)
    private const int SuspiciousAt = 30;
    private const int KnownCriminalAt = 75;
    private const int MenaceAt = 150;
    private const int RespectedAt = 10;
    private const int LocalFavoriteAt = 40;
    private const int BelovedAt = 100;

    private readonly IRecordStore _store;
    private readonly IGameClock _clock;
    private readonly MediaService? _media;
    private readonly JusticeConfig _config;
    private int _lastCleanDayChecked;
    private bool _notorietyMilestoneFiredSuspicious, _notorietyMilestoneFiredCriminal, _notorietyMilestoneFiredMenace;
    private bool _fameMilestoneFiredRespected, _fameMilestoneFiredFavorite, _fameMilestoneFiredBeloved;

    public ReputationService(IRecordStore store, IGameClock clock, MediaService? media, JusticeConfig config)
    {
        _store = store;
        _clock = clock;
        _media = media;
        _config = config;
    }

    public int Notoriety => _store.LoadStatus().Notoriety;
    public int Fame => _store.LoadStatus().Fame;

    /// <summary>How the street sees you.</summary>
    public string PublicImage
    {
        get
        {
            int n = Notoriety, f = Fame;
            if (n >= MenaceAt) return "City Menace";
            if (n >= KnownCriminalAt) return "Known Criminal";
            if (n >= SuspiciousAt) return "Suspicious Figure";
            if (f >= BelovedAt) return "Beloved Hero";
            if (f >= LocalFavoriteAt) return "Local Favorite";
            if (f >= RespectedAt) return "Respected Citizen";
            return "Unknown";
        }
    }

    /// <summary>Warrant-report probability multiplier: infamy raises it, fame lowers it.</summary>
    public double ReportChanceModifier
    {
        get
        {
            double m = 1.0 + Notoriety / 100.0 - Fame / 200.0;
            return m < 0.2 ? 0.2 : (m > 3.0 ? 3.0 : m);
        }
    }

    public void OnCrime(CrimeSeverity severity)
    {
        int points = severity switch
        {
            CrimeSeverity.Minor => 5,
            CrimeSeverity.Moderate => 10,
            _ => 25
        };
        AddNotoriety(points);
    }

    public void OnEscape() => AddNotoriety(40);
    public void OnHack() => AddNotoriety(30);
    public void OnConviction() { AddNotoriety(-10); AddFame(3); }   // debt paid, justice served
    public void OnRelease() => AddFame(10);                         // rehabilitation

    /// <summary>Daily: a clean day (no new crime) earns fame — the good-deed news.</summary>
    public void Tick()
    {
        int today = _clock.CurrentGameDay;
        if (_lastCleanDayChecked == today) return;
        _lastCleanDayChecked = today;
        if (today <= 1) return;   // profile era start

        // A clean day = no crime recorded that game-day (record timestamps are ISO)
        var record = _store.Load();
        bool clean = true;
        foreach (var e in record.Events)
        {
            if (e.GameTime.Length >= 10 && e.GameTime.StartsWith(FormatDay(today), StringComparison.Ordinal))
            {
                clean = false;
                break;
            }
        }

        if (clean && _config.NewsEnabled)
        {
            AddFame(2);
            _media?.News("FEEL-GOOD: locals praise a quiet, law-abiding day in the city");
        }
    }

    private static string FormatDay(int gameDay)
        => $"{gameDay / 372:D4}-{gameDay % 372 / 31 + 1:D2}-{gameDay % 31 + 1:D2}";

    private void AddNotoriety(int delta)
    {
        if (delta == 0) return;
        var status = _store.LoadStatus();
        int before = status.Notoriety;
        status.Notoriety = Math.Max(0, before + delta);
        _store.SaveStatusAtomic(status);

        if (status.Notoriety >= MenaceAt && !_notorietyMilestoneFiredMenace)
        {
            _notorietyMilestoneFiredMenace = true;
            _media?.News("CITY ON EDGE: a super-powered menace walks the streets");
            _media?.Viral("WEBNET: sightings of the 'Menace' spread like wildfire");
        }
        else if (status.Notoriety >= KnownCriminalAt && !_notorietyMilestoneFiredCriminal)
        {
            _notorietyMilestoneFiredCriminal = true;
            _media?.News("KNOWN CRIMINAL: police warn the public about a dangerous figure");
        }
        else if (status.Notoriety >= SuspiciousAt && !_notorietyMilestoneFiredSuspicious)
        {
            _notorietyMilestoneFiredSuspicious = true;
            _media?.News("SUSPICIOUS: residents report a strange figure in the city");
        }
    }

    private void AddFame(int delta)
    {
        if (delta == 0) return;
        var status = _store.LoadStatus();
        int before = status.Fame;
        status.Fame = Math.Max(0, before + delta);
        _store.SaveStatusAtomic(status);

        if (status.Fame >= BelovedAt && !_fameMilestoneFiredBeloved)
        {
            _fameMilestoneFiredBeloved = true;
            _media?.News("BELOVED: the city celebrates its mysterious protector");
            _media?.Viral("WEBNET: fan pages flood the net for the local hero");
        }
        else if (status.Fame >= LocalFavoriteAt && !_fameMilestoneFiredFavorite)
        {
            _fameMilestoneFiredFavorite = true;
            _media?.News("LOCAL FAVORITE: neighbors vouch for the community's good figure");
        }
        else if (status.Fame >= RespectedAt && !_fameMilestoneFiredRespected)
        {
            _fameMilestoneFiredRespected = true;
            _media?.News("RESPECTED: a good reputation opens doors in the city");
        }
    }
}
