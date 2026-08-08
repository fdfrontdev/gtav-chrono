using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

public class TeleportServiceTests
{
    private static TeleportService Create(FakePlayer player, FakeProbe probe, FakeNotifier notifier,
        FakeLog log, DashConfig? dash = null, TeleportConfig? tp = null)
    {
        return new TeleportService(player, probe, notifier, log,
            dash ?? new DashConfig { Range = 7f, MaxRange = 15f },
            tp ?? new TeleportConfig { GroundProbeDistance = 100f });
    }

    [Fact]
    public void TryDash_NotAiming_TeleportsForward()
    {
        var player = new FakePlayer { Position = new(0, 0, 10), Heading = 0f, IsAiming = false };
        var probe = new FakeProbe { GroundHeight = 10f };
        var service = Create(player, probe, new FakeNotifier(), new FakeLog());

        var result = service.TryDash();

        Assert.Equal(TeleportOutcome.Success, result.Outcome);
        Assert.Single(player.TeleportCalls);
        Assert.Equal(7f, player.TeleportCalls[0].Y, 2); // north 7m
        Assert.Equal(10f, player.TeleportCalls[0].Z, 2); // ground-snapped
    }

    [Fact]
    public void TryDash_Aiming_UsesAimDirection()
    {
        var player = new FakePlayer
        {
            Position = new(0, 0, 10),
            IsAiming = true,
            AimDirection = new(0, 1, 0) // aiming north
        };
        var probe = new FakeProbe { GroundHeight = 10f };
        var service = Create(player, probe, new FakeNotifier(), new FakeLog());

        var result = service.TryDash();

        Assert.Equal(TeleportOutcome.Success, result.Outcome);
        Assert.True(player.TeleportCalls[0].Y > 7f); // clamped to 15m max range → 15
        Assert.Equal(15f, player.TeleportCalls[0].Y, 2);
    }

    [Fact]
    public void TryDash_WallBlocked_RefusesAndNotifies()
    {
        var player = new FakePlayer { Position = new(0, 0, 10), Heading = 0f };
        var probe = new FakeProbe
        {
            GroundHeight = 10f,
            RaycastResult = new RaycastSample(new(0, 0, 10), new(0, 7, 10), true, new(0, 3, 10))
        };
        var notifier = new FakeNotifier();
        var service = Create(player, probe, notifier, new FakeLog());

        var result = service.TryDash();

        Assert.Equal(TeleportOutcome.NoClearPath, result.Outcome);
        Assert.Empty(player.TeleportCalls);
        Assert.Contains(notifier.Messages, m => m == UiStrings.DashBlocked);
    }

    [Fact]
    public void TryDash_NoGround_StillTeleportsWithFallback()
    {
        var player = new FakePlayer { Position = new(0, 0, 10), Heading = 0f };
        var probe = new FakeProbe { GroundHeight = null };
        var service = Create(player, probe, new FakeNotifier(), new FakeLog());

        var result = service.TryDash();

        Assert.Equal(TeleportOutcome.Success, result.Outcome);
        Assert.Single(player.TeleportCalls);
    }

    [Fact]
    public void TryMapTeleport_NoWaypoint_Refuses()
    {
        var player = new FakePlayer { WaypointActive = false };
        var notifier = new FakeNotifier();
        var service = Create(player, new FakeProbe(), notifier, new FakeLog());

        var result = service.TryMapTeleport();

        Assert.Equal(TeleportOutcome.NoWaypoint, result.Outcome);
        Assert.Empty(player.TeleportCalls);
        Assert.Contains(notifier.Messages, m => m == UiStrings.NoWaypoint);
    }

    [Fact]
    public void TryMapTeleport_Waypoint_TeleportsToGround()
    {
        var player = new FakePlayer
        {
            WaypointActive = true,
            WaypointPosition = new(100, 100, 0)
        };
        var probe = new FakeProbe { GroundHeight = 25f };
        var service = Create(player, probe, new FakeNotifier(), new FakeLog());

        var result = service.TryMapTeleport();

        Assert.Equal(TeleportOutcome.Success, result.Outcome);
        Assert.Single(player.TeleportCalls);
        Assert.Equal(25f, player.TeleportCalls[0].Z, 2); // ground-snapped
    }
}
