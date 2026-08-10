using System;
using System.Linq;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S22 v8 r4 — the WORLD reacts to superpowers (user UAT: "when I use
/// superpower, the citizen didn't surprise, nothing on webnet"):
/// witness-gated crowd surprise + WEBNET posts + small notoriety.
/// </summary>
public class PowerReactionTests
{
    private static (PowerReactionService service, FakeProbe probe, FakeRecordStore store, HudFeedBuffer feed, FakeNotifier notifier, FakePlayer player) Build(bool invisible = false)
    {
        var probe = new FakeProbe { NearbyCivilians = 3 };
        var store = new FakeRecordStore();
        var player = new FakePlayer { IsVisible = true };
        var feed = new HudFeedBuffer();
        var notifier = new FakeNotifier();
        var media = new MediaService(
            new FakeMediaNotifier(), new FakeLog(),
            new JusticeConfig { NewsEnabled = true, ViralEnabled = true },
            feed, characterName: () => "Franklin");
        var reputation = new ReputationService(store, new FakeClock(), media, new JusticeConfig());
        var service = new PowerReactionService(
            player, probe, reputation, media, notifier, new FakeLog(),
            isInvisible: () => invisible, random: () => 0.0);
        return (service, probe, store, feed, notifier, player);
    }

    [Fact]
    public void WitnessedDash_CiviliansFlee_WebnetPosts_NotorietyGains()
    {
        var (service, probe, store, feed, notifier, _) = Build();

        service.Report(PowerReactionService.PowerKind.Dash);

        Assert.Equal(1, probe.FleeCalls);                       // crowd surprise
        Assert.Contains(feed.Items, i => i.Text.Contains("dash-cam"));   // WEBNET post
        Assert.Contains(notifier.Messages, m => m.Contains("blink"));
        Assert.Equal(5, store.LoadStatus().Notoriety);          // +5 public image
    }

    [Fact]
    public void NoWitnesses_NoReaction_NoPost_NoNotoriety()
    {
        var (service, probe, store, feed, notifier, _) = Build();
        probe.NearbyCivilians = 0;

        service.Report(PowerReactionService.PowerKind.Dash);

        Assert.Equal(0, probe.FleeCalls);
        Assert.Empty(feed.Items);
        Assert.Equal(0, store.LoadStatus().Notoriety);
    }

    [Fact]
    public void Invisible_NoReaction_ExceptReveal()
    {
        var (service, probe, store, feed, notifier, _) = Build(invisible: true);

        service.Report(PowerReactionService.PowerKind.Dash);      // invisible — silent
        service.Report(PowerReactionService.PowerKind.InvisibleOff);   // reveal — witnessed

        Assert.Equal(1, probe.FleeCalls);                        // only the reveal reacted
        Assert.Contains(feed.Items, i => i.Text.Contains("out of thin air"));
        Assert.Equal(5, store.LoadStatus().Notoriety);
    }

    [Fact]
    public void TimeStop_ReportedOnDeactivate()
    {
        var (service, probe, store, feed, notifier, _) = Build();

        service.Report(PowerReactionService.PowerKind.TimeStop);

        Assert.Equal(1, probe.FleeCalls);
        Assert.Contains(feed.Items, i => i.Text.Contains("frozen moment"));
    }

    [Fact]
    public void Throttled_PerKind_SecondUseWithin30sIsSilent()
    {
        var (service, probe, store, feed, notifier, _) = Build();

        service.Report(PowerReactionService.PowerKind.Fly);       // fires
        service.Report(PowerReactionService.PowerKind.Fly);       // throttled — silent

        Assert.Equal(1, probe.FleeCalls);
        Assert.Single(feed.Items);
        Assert.Equal(5, store.LoadStatus().Notoriety);
    }

    [Fact]
    public void DifferentKinds_NotThrottledTogether()
    {
        var (service, probe, store, feed, notifier, _) = Build();

        service.Report(PowerReactionService.PowerKind.Fly);
        service.Report(PowerReactionService.PowerKind.GodMode);   // different kind — fires

        Assert.Equal(2, probe.FleeCalls);
        Assert.Equal(2, feed.Items.Count);
        Assert.Equal(10, store.LoadStatus().Notoriety);
    }

    [Fact]
    public void InVehicle_NoReaction()
    {
        var (service, probe, store, feed, notifier, player) = Build();
        player.IsInVehicle = true;

        service.Report(PowerReactionService.PowerKind.Fly);

        Assert.Equal(0, probe.FleeCalls);
        Assert.Empty(feed.Items);
    }
}
