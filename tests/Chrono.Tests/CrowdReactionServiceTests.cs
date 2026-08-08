using System;
using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S9 crowd reactions: infamy → civilians flee; fame → warm recognition.</summary>
public class CrowdReactionServiceTests
{
    private static (CrowdReactionService service, FakePlayer player, FakeProbe probe, FakeRecordStore store, FakeNotifier notifier, FakeClock clock) Build(Action<FakeRecordStore>? seed = null)
    {
        var player = new FakePlayer();
        var probe = new FakeProbe();
        var store = new FakeRecordStore();
        seed?.Invoke(store);   // seed BEFORE ctor — IdentityService caches at construction
        var notifier = new FakeNotifier();
        var clock = new FakeClock();
        var identity = new IdentityService(store, new FakeLog());
        var rep = new ReputationService(store, clock, null, new JusticeConfig());
        var service = new CrowdReactionService(player, probe, identity, rep, notifier, new FakeLog());
        return (service, player, probe, store, notifier, clock);
    }

    [Fact]
    public void InfamousAndBurned_CiviliansFlee()
    {
        var (service, player, probe, _, notifier, _) = Build(s => { s.Status.Notoriety = 40; s.Status.Identity = IdentityState.Burned; });
        player.IsVisible = true;
        probe.NearbyCivilians = 5;

        service.Tick(0);
        service.Tick(9000);

        Assert.True(probe.FleeCalls >= 1, "civilians must flee");
        Assert.Contains(notifier.Messages, m => m.Contains("scatter"));
    }

    [Fact]
    public void InfamousButInvisible_NoFlee()
    {
        var (service, player, probe, _, _, _) = Build(s => { s.Status.Notoriety = 40; s.Status.Identity = IdentityState.Burned; });
        player.IsVisible = false;   // invisible → nobody sees you

        service.Tick(9000);

        Assert.Equal(0, probe.FleeCalls);
    }

    [Fact]
    public void BelovedAndClean_WarmRecognition()
    {
        var (service, player, _, _, notifier, _) = Build(s => { s.Status.Fame = 50; s.Status.Identity = IdentityState.Clean; });
        player.IsVisible = true;

        service.Tick(9000);

        Assert.Contains(notifier.Messages, m => m.Contains("nod as you pass"));
    }

    [Fact]
    public void Reaction_NotifiesOncePerStateChange()
    {
        var (service, player, _, _, notifier, _) = Build(s => { s.Status.Fame = 50; s.Status.Identity = IdentityState.Clean; });

        service.Tick(0);
        service.Tick(9000);
        service.Tick(18000);

        Assert.Single(notifier.Messages, m => m.Contains("nod as you pass"));
    }
}
