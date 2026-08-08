using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>
/// Realistic NPC reactions (v0.6.0): after a power use, NPCs/police cannot instantly
/// track the player. Grace period suppresses perception; expiry restores it.
/// </summary>
public class NpcReactionServiceTests
{
    private static (NpcReactionService service, FakePlayer player) Build(int delayMs = 2500)
    {
        var player = new FakePlayer();
        var service = new NpcReactionService(player, new FakeLog(), new NpcConfig { ReactionDelayMs = delayMs });
        return (service, player);
    }

    [Fact]
    public void Trigger_DisablesNpcAwareness()
    {
        var (service, player) = Build();

        service.TriggerGracePeriod();

        Assert.True(service.IsGraceActive);
        Assert.Contains(player.AwarenessCalls, a => a == false);   // ignore ON
    }

    [Fact]
    public void Tick_AfterDelay_RestoresAwareness()
    {
        var (service, player) = Build(delayMs: 100);

        service.TriggerGracePeriod();
        // service's internal Stopwatch — sleep past the delay
        Thread.Sleep(150);
        service.Tick();

        Assert.False(service.IsGraceActive);
        Assert.Contains(player.AwarenessCalls, a => a == true);    // ignore OFF — normal perception
    }

    [Fact]
    public void Trigger_ZeroDelay_IsNoOp()
    {
        var (service, player) = Build(delayMs: 0);

        service.TriggerGracePeriod();

        Assert.False(service.IsGraceActive);
        Assert.Empty(player.AwarenessCalls);
    }

    [Fact]
    public void Tick_NoActiveGrace_DoesNothing()
    {
        var (service, player) = Build();

        service.Tick();

        Assert.Empty(player.AwarenessCalls);
    }
}
