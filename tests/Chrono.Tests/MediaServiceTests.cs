using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Tests;

public sealed class FakeMediaNotifier : IMediaNotifier
{
    public List<string> Headlines { get; } = new();
    public List<string> ViralMessages { get; } = new();
    public void News(string headline) => Headlines.Add(headline);
    public void Viral(string message) => ViralMessages.Add(message);
}

/// <summary>S2 media: severity gating, throttle, viral toggles, escape coverage.</summary>
public class MediaServiceTests
{
    private static (MediaService service, FakeMediaNotifier media) Build(JusticeConfig? config = null)
    {
        var media = new FakeMediaNotifier();
        var service = new MediaService(media, new FakeLog(), config ?? new JusticeConfig());
        return (service, media);
    }

    private static CrimeEvent Event(CrimeSeverity s, string district = "Vinewood")
        => new("e", s, "public_offense", "2026-08-08T12:00:00", district, true);

    [Fact]
    public void MinorCrime_NoNews()
    {
        var (service, media) = Build();
        service.ReportCrime(Event(CrimeSeverity.Minor));
        Assert.Empty(media.Headlines);
    }

    [Fact]
    public void ModerateCrime_BreakingNewsWithDistrict()
    {
        var (service, media) = Build();
        service.ReportCrime(Event(CrimeSeverity.Moderate, "Paleto Bay"));

        Assert.Single(media.Headlines);
        Assert.Contains("Paleto Bay", media.Headlines[0]);
        Assert.Contains("BREAKING", media.Headlines[0]);
        Assert.Empty(media.ViralMessages);   // viral is Severe-only
    }

    [Fact]
    public void SevereCrime_NewsAndViral()
    {
        var (service, media) = Build();
        service.ReportCrime(Event(CrimeSeverity.Severe));

        Assert.Single(media.Headlines);
        Assert.Single(media.ViralMessages);
        Assert.Contains("WEBNET", media.ViralMessages[0]);
    }

    [Fact]
    public void ViralDisabled_SevereStillGetsNews()
    {
        var (service, media) = Build(new JusticeConfig { ViralEnabled = false });
        service.ReportCrime(Event(CrimeSeverity.Severe));

        Assert.Single(media.Headlines);
        Assert.Empty(media.ViralMessages);
    }

    [Fact]
    public void NewsDisabled_NoOutputAtAll()
    {
        var (service, media) = Build(new JusticeConfig { NewsEnabled = false });
        service.ReportCrime(Event(CrimeSeverity.Severe));
        service.ReportEscape("Bolingbroke");

        Assert.Empty(media.Headlines);
        Assert.Empty(media.ViralMessages);
    }

    [Fact]
    public void Throttle_SecondEventWithinWindow_Silent()
    {
        var (service, media) = Build();
        service.ReportCrime(Event(CrimeSeverity.Severe));
        service.ReportCrime(Event(CrimeSeverity.Severe));   // within 30s window

        Assert.Single(media.Headlines);
    }

    [Fact]
    public void Escape_ManhuntNewsAndViral()
    {
        var (service, media) = Build();
        service.ReportEscape("Bolingbroke");

        Assert.Single(media.Headlines);
        Assert.Contains("MANHUNT", media.Headlines[0]);
        Assert.Contains("Bolingbroke", media.Headlines[0]);
        Assert.Single(media.ViralMessages);
    }

    [Fact]
    public void JusticeService_SevereCrime_FeedsMedia()
    {
        // Integration: stars → crime event → media coverage (S1+S2 handshake)
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer();
        var store = new FakeRecordStore();
        var media = new FakeMediaNotifier();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            new FakeNotifier(), new FakeLog(), new JusticeConfig(), new FakeClock(),
            new MediaService(media, new FakeLog(), new JusticeConfig()));

        wanted.CurrentStars = 5;
        service.Tick();

        Assert.Single(media.Headlines);
        Assert.Single(media.ViralMessages);
    }

    // --- S7: WEBNET feed ---

    [Fact]
    public void SevereCrime_PushesViralFeedPost()
    {
        var (service, _) = Build();
        service.ReportCrime(Event(CrimeSeverity.Severe, "Sandy"));

        Assert.Contains(service.Feed, f => f.Viral && f.Text.Contains("Sandy"));
        Assert.Contains(service.Feed, f => f.Text.Contains("BREAKING"));
    }

    [Fact]
    public void Escape_PushesManhuntFeedPosts()
    {
        var (service, _) = Build();
        service.ReportEscape("Bolingbroke");

        Assert.Contains(service.Feed, f => f.Text.Contains("MANHUNT"));
        Assert.Contains(service.Feed, f => f.Text.Contains("escape footage"));
    }

    [Fact]
    public void Feed_CapsAtTwenty()
    {
        var (service, _) = Build();
        for (int i = 0; i < 12; i++)
            service.ReportCrime(Event(CrimeSeverity.Severe, $"D{i}"));

        Assert.True(service.Feed.Count <= 20, $"feed capped (got {service.Feed.Count})");
    }

    // --- S10: news for everything ---

    [Fact]
    public void MinorCrime_PushesBlotterFeedPost_NoTvNews()
    {
        var (service, media) = Build();
        service.ReportCrime(Event(CrimeSeverity.Minor, "Paleto Bay"));

        Assert.Empty(media.Headlines);                                    // no TV spam
        Assert.Contains(service.Feed, f => f.Text.Contains("Police blotter"));
        Assert.Contains(service.Feed, f => f.Text.Contains("Paleto Bay"));
    }
}
