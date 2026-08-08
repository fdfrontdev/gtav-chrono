using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S7 WEBNET phone feed: phone key toggles the social feed of viral/news items.</summary>
public class PhoneNewsServiceTests
{
    private sealed class FakeOverlay : IPhoneOverlay
    {
        public IReadOnlyList<NewsFeedItem>? Shown { get; private set; }
        public int HideCount { get; private set; }
        public void ShowFeed(IReadOnlyList<NewsFeedItem> items) => Shown = items;
        public void Hide() => HideCount++;
    }

    private static (PhoneNewsService service, FakeInput input, FakeOverlay overlay, List<NewsFeedItem> feed) Build()
    {
        var input = new FakeInput();
        var overlay = new FakeOverlay();
        var feed = new List<NewsFeedItem>
        {
            new("Vinewood dash-cam footage goes viral", "14:02", true),
            new("BREAKING: super-powered suspect seen in Paleto", "13:55", false)
        };
        var service = new PhoneNewsService(input, () => feed, overlay);
        return (service, input, overlay, feed);
    }

    [Fact]
    public void PhoneEdge_OpensFeed_WithLatestPosts()
    {
        var (service, input, overlay, _) = Build();
        input.PhoneKey = true;
        service.Tick();

        Assert.True(service.IsOpen);
        Assert.NotNull(overlay.Shown);
        Assert.Equal(2, overlay.Shown!.Count);
    }

    [Fact]
    public void PhoneEdge_TogglesClosed()
    {
        var (service, input, overlay, _) = Build();
        input.PhoneKey = true;
        service.Tick();
        input.PhoneKey = false;
        service.Tick();   // release
        input.PhoneKey = true;
        service.Tick();   // toggle → close

        Assert.False(service.IsOpen);
        Assert.Equal(1, overlay.HideCount);
    }

    [Fact]
    public void Esc_ClosesFeed()
    {
        var (service, input, overlay, _) = Build();
        input.PhoneKey = true;
        service.Tick();                  // open
        input.PhoneKey = false;
        input.MenuCancel = true;
        service.Tick();                  // Esc

        Assert.False(service.IsOpen);
        Assert.Equal(1, overlay.HideCount);
    }

    [Fact]
    public void Open_FeedRefreshesLive()
    {
        var (service, input, overlay, feed) = Build();
        input.PhoneKey = true;
        service.Tick();                  // open with 2 items

        feed.Add(new NewsFeedItem("MANHUNT: fugitive escaped Bolingbroke", "14:10", true));
        service.Tick();                  // live refresh while open

        Assert.Equal(3, overlay.Shown!.Count);
    }
}
