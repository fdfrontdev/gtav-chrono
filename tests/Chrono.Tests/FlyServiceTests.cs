using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>Flight behavior: pose switching (superman dive vs hover) + heading.</summary>
public class FlyServiceTests
{
    private static (FlyService service, FakePlayer player, FakeInput input) Build()
    {
        var player = new FakePlayer { AimDirection = new(0, 1, 0) }; // camera north
        var input = new FakeInput();
        var service = new FlyService(player, input, new FakeLog(), new FlyConfig { Speed = 25f });
        return (service, player, input);
    }

    [Fact]
    public void Enable_DisablesGravityAndRagdoll()
    {
        var (service, player, _) = Build();
        service.SetEnabled(true);

        Assert.False(player.GravityCalls[player.GravityCalls.Count - 1]);
        Assert.False(player.RagdollCalls[player.RagdollCalls.Count - 1]);
    }

    [Fact]
    public void Disable_RestoresGravityAndClearsAnimation()
    {
        var (service, player, _) = Build();
        service.SetEnabled(true);
        service.SetEnabled(false);

        Assert.True(player.GravityCalls[player.GravityCalls.Count - 1]);
        Assert.True(player.RagdollCalls[player.RagdollCalls.Count - 1]);
        Assert.Equal(1, player.ClearAnimCount);
    }

    [Fact]
    public void MovingForward_PlaysDivePoseAndFacesHeading()
    {
        var (service, player, input) = Build();
        service.SetEnabled(true);
        input.FlyForward = true;

        service.Tick();

        Assert.Contains("skydive@freefall/free_forward", player.LoopedAnims);
        Assert.Single(player.HeadingCalls);
        Assert.Equal(0f, player.HeadingCalls[0], 2);   // north = heading 0
    }

    [Fact]
    public void Hovering_PlaysIdlePose()
    {
        var (service, player, input) = Build();
        service.SetEnabled(true);
        // no input → hover

        service.Tick();

        Assert.Contains("skydive@base/free_idle", player.LoopedAnims);
        Assert.Empty(player.HeadingCalls);   // no direction to face
    }

    [Fact]
    public void Ascending_PlaysDivePose()
    {
        var (service, player, input) = Build();
        service.SetEnabled(true);
        input.FlyAscend = true;

        service.Tick();

        // pure vertical: heading unchanged but dive pose (moving)
        Assert.Contains("skydive@freefall/free_forward", player.LoopedAnims);
    }

    [Fact]
    public void PoseNotReTriggered_WhenUnchanged()
    {
        var (service, player, input) = Build();
        service.SetEnabled(true);
        input.FlyForward = true;

        service.Tick();
        service.Tick();

        Assert.Single(player.LoopedAnims);   // same pose → played once
    }

    [Fact]
    public void InVehicle_NoFlight()
    {
        var (service, player, input) = Build();
        service.SetEnabled(true);
        player.IsInVehicle = true;
        input.FlyForward = true;

        service.Tick();

        Assert.Single(player.VelocityCalls);   // only the enable-time zero velocity
        Assert.Empty(player.LoopedAnims);      // no flight pose in a vehicle
    }
}
