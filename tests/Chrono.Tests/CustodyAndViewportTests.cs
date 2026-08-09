using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Boundary;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S17 — UAT round 11: (1) no civilian reporting during the busted→court
/// countdown; (2) menu viewport scrolling math (long lists scroll, selection stays
/// visible); (3) long lists are no longer capped at 14.</summary>
public class CustodyAndViewportTests
{
    // ── 1. Custody quiet period ──

    private static JusticeService BuildCaptured(out FakeWantedMonitor wanted, out FakePlayer player, out FakeRecordStore store)
    {
        wanted = new FakeWantedMonitor();
        player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Vinewood" };
        store = new FakeRecordStore();
        // S9 lesson: services cache status at construction — seed BEFORE constructing
        store.Status.Identity = IdentityState.Burned;
        store.Status.WarrantActive = true;
        var notifier = new FakeNotifier();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(), new JusticeConfig { WarrantReportSeconds = 0 }, new FakeClock(),
            probe: new FakeProbe { NearbyCivilians = 5 }, random: () => 0.0);
        return service;
    }

    [Fact]
    public void Custody_NoCivilianReports_DuringCourtCountdown()
    {
        var service = BuildCaptured(out var wanted, out _, out _);
        service.Tick();                    // report → 1★
        wanted.CurrentStars = 0; service.Tick(); service.Tick();   // 2★
        wanted.CurrentStars = 0; service.Tick(); service.Tick();   // 3★
        wanted.CurrentStars = 0; service.Tick(); service.Tick();   // 4★
        service.Tick();                    // S19 confrontation begins
        service.AdvanceConfrontationTime(6.0);
        service.Tick();                    // cuffed
        Assert.Equal(JusticeState.Captured, service.State);
        int starsAtCapture = wanted.StarSets.Count;

        // S17: a new crime during the countdown must NOT re-enable reports
        wanted.CurrentStars = 2;           // crime during custody
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();
        service.Tick();                    // would fire a report if State flipped to Wanted

        Assert.Equal(starsAtCapture, wanted.StarSets.Count);   // no extra star sets — civilians stay quiet
    }

    // ── 2. Viewport scrolling math ──

    [Fact]
    public void Viewport_ShortList_ShowsAll()
    {
        var (first, visible) = ModernMenuRenderer.ViewportWindow(3, 5, 12);
        Assert.Equal(0, first);
        Assert.Equal(5, visible);
    }

    [Fact]
    public void Viewport_SelectionAtStart_ClampsToTop()
    {
        var (first, visible) = ModernMenuRenderer.ViewportWindow(0, 40, 12);
        Assert.Equal(0, first);
        Assert.Equal(12, visible);
    }

    [Fact]
    public void Viewport_SelectionAtEnd_ClampsToBottom()
    {
        var (first, visible) = ModernMenuRenderer.ViewportWindow(39, 40, 12);
        Assert.Equal(28, first);
        Assert.Equal(12, visible);
    }

    [Fact]
    public void Viewport_SelectionInMiddle_Centers()
    {
        var (first, _) = ModernMenuRenderer.ViewportWindow(20, 40, 12);
        Assert.Equal(14, first);           // 20 - 12/2
    }

    [Fact]
    public void Viewport_SelectionVisibleInWindow()
    {
        for (int sel = 0; sel < 60; sel++)
        {
            var (first, visible) = ModernMenuRenderer.ViewportWindow(sel, 60, 12);
            Assert.InRange(sel, first, first + visible - 1);
        }
    }

    // ── 3. Long lists no longer capped ──

    [Fact]
    public void WebnetScreen_LongFeed_NotCappedAt14()
    {
        var feed = new List<NewsFeedItem>();
        for (int i = 0; i < 30; i++) feed.Add(new NewsFeedItem($"Story {i}", "12:00", i % 3 == 0));

        var repo = new FakeRepository();
        var input = new FakeInput();
        var player = new FakePlayer();
        var config = new ChronoConfig();
        var menu = new MenuFramework(new FakeRenderer());
        var timeStop = new TimeStopService(repo, new FakeFreezer(), new FakeClock(), player,
            new FakeNotifier(), new FakeLog(), config.TimeStop);
        var teleport = new TeleportService(player, new FakeProbe(), new FakeNotifier(), new FakeLog(), config.Dash, config.Teleport);
        var vfx = new VfxService(new FakeVfx(), new FakeLog(), config.Visual);
        var service = new PowerMenuService(
            menu, timeStop, teleport, vfx, input, player,
            new FakeNotifier(), new FakeLog(), config, new FakeConfigStore(),
            feedProvider: () => feed);
        service.BuildMenu();

        // navigate: root (0 TimeStop, 1 Dash, 2 Teleport, 3 God, 4 Invisible, 5 Fly,
        // 6 Justice, 7 WEBNET, 8 Settings) → WEBNET at index 7
        input.MenuKeyPressed = true;
        service.Tick(0);
        for (int i = 0; i < 7; i++) menu.NavigateDown();
        menu.Accept();

        Assert.Equal("WEBNET News", menu.CurrentScreen?.Title);
        Assert.Equal(30, menu.CurrentScreen!.Items.Count);   // ALL posts, viewport scrolls them
    }
}
