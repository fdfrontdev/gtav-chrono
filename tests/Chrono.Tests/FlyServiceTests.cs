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

        service.Tick(0.1f);   // inertia ramp (v0.8.0) — velocity builds over ~0.5s
        service.Tick(0.1f);

        Assert.Contains("skydive@freefall/free_forward", player.LoopedAnims);
        Assert.NotEmpty(player.HeadingCalls);
        Assert.Equal(0f, player.HeadingCalls[player.HeadingCalls.Count - 1], 2);   // north = heading 0
    }

    [Fact]
    public void Hovering_StandsStill_NoDivePose()
    {
        // Anime spec: hovering/ascending/descending = standing (takeoff/landing pose),
        // dive pose ONLY when moving horizontally (user report v0.4.0)
        var (service, player, input) = Build();
        service.SetEnabled(true);
        input.FlyAscend = true;   // vertical-only movement

        service.Tick(0.1f);
        service.Tick(0.1f);

        Assert.DoesNotContain("skydive@freefall/free_forward", player.LoopedAnims);
        Assert.Empty(player.HeadingCalls);
    }

    [Fact]
    public void Ascending_Stands_NoDivePose()
    {
        var (service, player, input) = Build();
        service.SetEnabled(true);
        input.FlyAscend = true;

        service.Tick(0.1f);
        service.Tick(0.1f);

        Assert.DoesNotContain("skydive@freefall/free_forward", player.LoopedAnims);
    }

    [Fact]
    public void DiveToHover_ClearsAnimation()
    {
        // Transition: forward dive → hover must clear the dive anim (return to stand)
        var (service, player, input) = Build();
        service.SetEnabled(true);
        input.FlyForward = true;
        service.Tick(0.1f);
        service.Tick(0.1f);              // dive pose active
        Assert.Contains("skydive@freefall/free_forward", player.LoopedAnims);

        input.FlyForward = false;
        for (int i = 0; i < 8; i++) service.Tick(0.1f);   // decay → hover → clear

        Assert.Equal(1, player.ClearAnimCount);
    }

    [Fact]
    public void PoseNotReTriggered_WhenUnchanged()
    {
        var (service, player, input) = Build();
        service.SetEnabled(true);
        input.FlyForward = true;

        service.Tick(0.1f);
        service.Tick(0.1f);

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
