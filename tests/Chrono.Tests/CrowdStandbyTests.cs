using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S22 v3 — crowd reaction standby (user UAT screenshot: "I start a new game,
/// but people run because of me (caused by mod behavior) — this should not
/// happen"). The notoriety-driven "people scatter" reactor ran during story
/// sequences — scripted NPCs belong to the mission, not to the mod.
/// </summary>
public class CrowdStandbyTests
{
    private static (CrowdReactionService service, FakePlayer player, FakeProbe probe, FakeNotifier notifier) Build()
    {
        var player = new FakePlayer { IsVisible = true, DistrictName = "Vinewood" };
        var probe = new FakeProbe();
        var notifier = new FakeNotifier();
        var store = new FakeRecordStore();
        var reputation = new ReputationService(store, new FakeClock(), media: null, new JusticeConfig());
        store.Status.Notoriety = 200;   // Menace tier — civilians would flee
        store.Status.Identity = IdentityState.Burned;
        var identity = new IdentityService(store, new FakeLog());
        var service = new CrowdReactionService(player, probe, identity, reputation, notifier, new FakeLog());
        return (service, player, probe, notifier);
    }

    [Fact]
    public void Standby_NoFlee_NoNotify()
    {
        var (service, _, probe, notifier) = Build();
        service.Standby = true;          // story sequence active

        service.Tick(0);                 // notoriety 200 + burned + visible — would flee
        service.Tick(10000);             // past the 8s reaction interval

        Assert.Equal(0, probe.FleeCalls);
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("People scatter"));
    }

    [Fact]
    public void NormalMode_CiviliansFlee_OncePerState()
    {
        var (service, _, probe, notifier) = Build();
        service.Standby = false;         // freeroam — the Menace walks

        service.Tick(0);
        Assert.True(probe.FleeCalls >= 1);
        Assert.Contains(notifier.Messages, m => m.Contains("People scatter"));

        service.Tick(10000);             // still fleeing — but notified ONCE
        Assert.DoesNotContain(notifier.Messages.Where(m => m.Contains("People scatter")).Skip(1), m => true);
    }
}