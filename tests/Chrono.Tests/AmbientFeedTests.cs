using System;
using System.Linq;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S22 v8 r3 — ambient city chatter (user UAT: "the live feed seems too
/// quiet with all the events happening"): police blotter + WEBNET color when
/// the world is calm; silenced during chases/custody/missions; one line per
/// game day; flavor only (no justice-side effects).
/// </summary>
public class AmbientFeedTests
{
    private static (AmbientFeedService service, HudFeedBuffer feed, FakeClock clock) Build(int intervalMs = 60000)
    {
        var feed = new HudFeedBuffer();
        var clock = new FakeClock();
        var service = new AmbientFeedService(
            feed, new JusticeConfig { AmbientFeedEnabled = true, AmbientFeedIntervalMs = intervalMs },
            clock, new FakeLog(), random: () => 0.0);
        return (service, feed, clock);
    }

    [Fact]
    public void QuietWorld_AfterInterval_PushesOneLine()
    {
        var (service, feed, _) = Build(intervalMs: 1000);
        service.Tick(worldQuiet: true);         // starts the stopwatch
        System.Threading.Thread.Sleep(1100);
        service.Tick(worldQuiet: true);

        Assert.Single(feed.Items);
        // random()=0.0 → first district "VINEWOOD", first blotter line (Message kind)
        Assert.Contains("VINEWOOD", feed.Items[0].Text);
    }

    [Fact]
    public void OneLinePerGameDay_NotPerInterval()
    {
        var (service, feed, clock) = Build(intervalMs: 1000);
        service.Tick(worldQuiet: true);
        System.Threading.Thread.Sleep(1100);
        service.Tick(worldQuiet: true);         // line 1 (day 0)
        System.Threading.Thread.Sleep(1100);
        service.Tick(worldQuiet: true);         // same day → no line 2

        Assert.Single(feed.Items);
    }

    [Fact]
    public void NextGameDay_AllowsAnotherLine()
    {
        var (service, feed, clock) = Build(intervalMs: 0);
        service.Tick(worldQuiet: true);         // line 1
        clock.CurrentGameDay = 1;               // new game day
        service.Tick(worldQuiet: true);         // line 2

        Assert.Equal(2, feed.Items.Count);
    }

    [Fact]
    public void WorldNotQuiet_NoPushAndTimerResets()
    {
        var (service, feed, clock) = Build(intervalMs: 1000);
        service.Tick(worldQuiet: true);
        service.Tick(worldQuiet: false);        // chase — silences + resets timer
        System.Threading.Thread.Sleep(1100);
        service.Tick(worldQuiet: false);        // still busy → nothing
        Assert.Empty(feed.Items);

        service.Tick(worldQuiet: true);         // calm restarts the countdown
        System.Threading.Thread.Sleep(1100);
        service.Tick(worldQuiet: true);
        Assert.Single(feed.Items);
    }

    [Fact]
    public void Disabled_NoPush()
    {
        var feed = new HudFeedBuffer();
        var clock = new FakeClock();
        var service = new AmbientFeedService(
            feed, new JusticeConfig { AmbientFeedEnabled = false, AmbientFeedIntervalMs = 0 },
            clock, new FakeLog(), random: () => 0.0);

        service.Tick(worldQuiet: true);
        service.Tick(worldQuiet: true);
        Assert.Empty(feed.Items);
    }

    [Fact]
    public void WebnetRandomBranch_PushesWebnetKind()
    {
        var feed = new HudFeedBuffer();
        var clock = new FakeClock();
        var service = new AmbientFeedService(
            feed, new JusticeConfig { AmbientFeedEnabled = true, AmbientFeedIntervalMs = 0 },
            clock, new FakeLog(), random: () => 0.4);   // <0.5 → WEBNET line

        service.Tick(worldQuiet: true);
        Assert.Equal(FeedKind.Webnet, feed.Items[0].Kind);
    }

    // ── MediaService dedupe: the widget feed gets ONE entry per headline ──

    [Fact]
    public void News_PushesSingleWebnetEntry_NoDuplicate()
    {
        var feed = new HudFeedBuffer();
        var media = new MediaService(
            new FakeMediaNotifier(), new FakeLog(),
            new JusticeConfig { NewsEnabled = true, ViralEnabled = true },
            feed, characterName: () => "Franklin");

        media.News("FEEL-GOOD: locals praise a quiet, law-abiding day in the city");

        Assert.Single(feed.Items);
        Assert.Equal(FeedKind.Webnet, feed.Items[0].Kind);
    }

    [Fact]
    public void Viral_PushesSingleViralEntry_NoDuplicate()
    {
        var feed = new HudFeedBuffer();
        var media = new MediaService(
            new FakeMediaNotifier(), new FakeLog(),
            new JusticeConfig { NewsEnabled = true, ViralEnabled = true },
            feed, characterName: () => "Franklin");

        media.Viral("MANHUNT: fugitive escaped");

        Assert.Single(feed.Items);
        Assert.Equal(FeedKind.Viral, feed.Items[0].Kind);
    }
}
