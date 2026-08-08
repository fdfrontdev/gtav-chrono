using Chrono.Application;
using Chrono.Domain;
using System.Numerics;

namespace Chrono.Tests;

public class TeleportServiceTests
{
    private static TeleportService Create(FakePlayer player, FakeProbe probe, FakeNotifier notifier,
        FakeLog log, DashConfig? dash = null, TeleportConfig? tp = null)
    {
        return new TeleportService(player, probe, notifier, log,
            dash ?? new DashConfig(),                          // defaults: range 12, maxRange 30
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
        Assert.Equal(12f, player.TeleportCalls[0].Y, 2); // default range 12m north
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
        Assert.True(player.TeleportCalls[0].Y > 12f); // clamped to 30m max range → 30
        Assert.Equal(30f, player.TeleportCalls[0].Y, 2);
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
        Assert.Equal(10f, result.Point!.Value.Z, 2);   // fallback = origin Z (no ground snap)
    }

    [Fact]
    public void TryDash_AimHitOnRoof_LandsOnHitPoint()
    {
        // User report v0.8.0: "can't blink to the top of a building" — a roof hit
        // must land ON the roof, NOT ground-snap down to street level.
        var player = new FakePlayer { Position = new(0, 0, 0), IsAiming = true };
        player.AimDirection = new Vector3(0, 1, 0.5f);   // looking up at a roof
        var probe = new FakeProbe
        {
            RaycastResult = new RaycastSample(new(0, 0, 0), new(0, 30, 15), true, new(0, 30, 15))
        };
        var service = Create(player, probe, new FakeNotifier(), new FakeLog());

        var result = service.TryDash();

        Assert.Equal(TeleportOutcome.Success, result.Outcome);
        Assert.True(result.Point!.Value.Z > 14.5f && result.Point!.Value.Z < 15.0f,
            $"expected ~15 (roof, minus 0.27 pull-back), got {result.Point.Value.Z}");
    }

    [Fact]
    public void TryDash_AimHitOnWall_PulledBackNotEmbedded()
    {
        var player = new FakePlayer { Position = new(0, 0, 0), IsAiming = true };
        player.AimDirection = new Vector3(1, 0, 0);      // aiming east at a wall
        var probe = new FakeProbe
        {
            RaycastResult = new RaycastSample(new(0, 0, 0), new(20, 0, 0), true, new(20, 0, 0))
        };
        var service = Create(player, probe, new FakeNotifier(), new FakeLog());

        var result = service.TryDash();

        Assert.Equal(TeleportOutcome.Success, result.Outcome);
        Assert.Equal(19.4f, result.Point!.Value.X, 1);   // 20 - 0.6 pull-back
    }

    [Fact]
    public void GetAimTarget_ReticleVisibleWhileAiming()
    {
        var player = new FakePlayer { Position = new(0, 0, 0), IsAiming = true };
        player.AimDirection = new Vector3(0, 1, 0);
        var probe = new FakeProbe();                      // no hit
        var service = Create(player, probe, new FakeNotifier(), new FakeLog());

        var target = service.GetAimTarget();

        Assert.NotNull(target);
        Assert.True(target!.Value.Y > 0f);
    }

    [Fact]
    public void GetAimTarget_NullWhenNotAiming()
    {
        var player = new FakePlayer { Position = new(0, 0, 0), IsAiming = false };
        var service = Create(player, new FakeProbe(), new FakeNotifier(), new FakeLog());

        Assert.Null(service.GetAimTarget());
    }

    [Fact]
    public void TryDash_OutsideWorldBounds_Refused()
    {
        // Player near the map edge dashing outward — must NOT leave the map
        var player = new FakePlayer { Position = new(3880, 0, 10), Heading = 0f };
        var probe = new FakeProbe { GroundHeight = 10f };
        var notifier = new FakeNotifier();
        var service = Create(player, probe, notifier, new FakeLog());

        var result = service.TryDash();   // 12m north from x=3880 — still in bounds (3900 limit)

        // x=3880 + north 12m → (3880, 12) is INSIDE bounds → success expected
        Assert.Equal(TeleportOutcome.Success, result.Outcome);

        // Now aim EAST (out of the map) at max range
        player.Heading = 90f;
        player.IsAiming = true;
        player.AimDirection = new(1, 0, 0);
        result = service.TryDash();       // 30m east → x=3910 → OUTSIDE

        Assert.Equal(TeleportOutcome.NoClearPath, result.Outcome);
        Assert.Contains(notifier.Messages, m => m == UiStrings.MapEdge);
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
