using System.Numerics;
using Chrono.Domain;

namespace Chrono.Tests;

public class TeleportMathTests
{
    [Fact]
    public void CalculateForwardTarget_Heading0_GoesNorth()
    {
        // heading 0 = north = +Y
        var target = TeleportMath.CalculateForwardTarget(Vector3.Zero, 0f, 7f);
        Assert.Equal(0f, target.X, 3);
        Assert.Equal(7f, target.Y, 3);
        Assert.Equal(0f, target.Z, 3);
    }

    [Fact]
    public void CalculateForwardTarget_Heading90_GoesEast()
    {
        // heading 90 = east = +X
        var target = TeleportMath.CalculateForwardTarget(Vector3.Zero, 90f, 7f);
        Assert.Equal(-7f, target.X, 3); // GTA convention: sin(90)=1 → -X... see note
        Assert.Equal(0f, target.Y, 3);
    }

    [Fact]
    public void CalculateForwardTarget_RespectsRange()
    {
        var origin = new Vector3(10, 20, 5);
        var target = TeleportMath.CalculateForwardTarget(origin, 0f, 7f);
        Assert.Equal(7f, Vector3.Distance(new Vector3(origin.X, origin.Y, 0), new Vector3(target.X, target.Y, 0)), 3);
        Assert.Equal(5f, target.Z);
    }

    [Fact]
    public void ClampToRange_TooFar_ClampedToMax()
    {
        var origin = Vector3.Zero;
        var far = new Vector3(100, 0, 0);
        var clamped = TeleportMath.ClampToRange(origin, far, 3f, 15f);
        Assert.NotNull(clamped);
        Assert.Equal(15f, Vector3.Distance(origin, clamped!.Value), 3);
    }

    [Fact]
    public void ClampToRange_TooClose_ClampedToMin()
    {
        var origin = Vector3.Zero;
        var close = new Vector3(1, 0, 0);
        var clamped = TeleportMath.ClampToRange(origin, close, 3f, 15f);
        Assert.NotNull(clamped);
        Assert.Equal(3f, Vector3.Distance(origin, clamped!.Value), 3);
    }

    [Fact]
    public void ClampToRange_AtOrigin_ReturnsNull()
    {
        Assert.Null(TeleportMath.ClampToRange(Vector3.Zero, Vector3.Zero, 3f, 15f));
    }

    [Fact]
    public void IsPathClear_NoHit_True()
    {
        var sample = new RaycastSample(Vector3.Zero, new Vector3(7, 0, 0), false, Vector3.Zero);
        Assert.True(TeleportMath.IsPathClear(sample, 1f));
    }

    [Fact]
    public void IsPathClear_HitBeforeTarget_False()
    {
        var sample = new RaycastSample(Vector3.Zero, new Vector3(7, 0, 0), true, new Vector3(3, 0, 0));
        Assert.False(TeleportMath.IsPathClear(sample, 1f));
    }

    [Fact]
    public void IsPathClear_HitAtTarget_True()
    {
        var sample = new RaycastSample(Vector3.Zero, new Vector3(7, 0, 0), true, new Vector3(7, 0, 0));
        Assert.True(TeleportMath.IsPathClear(sample, 1f));
    }

    [Fact]
    public void SnapToGround_HitWithinProbe_UsesHit()
    {
        var probeStart = new Vector3(10, 10, 100);
        var hit = new Vector3(10, 10, 5);
        var result = TeleportMath.SnapToGround(probeStart, hit, 100f, probeStart);
        Assert.Equal(5f, result.Z);
    }

    [Fact]
    public void SnapToGround_NoHit_UsesFallback()
    {
        var probeStart = new Vector3(10, 10, 100);
        var result = TeleportMath.SnapToGround(probeStart, null, 100f, probeStart);
        Assert.Equal(probeStart, result);
    }

    [Fact]
    public void SnapToGround_HitTooFar_UsesFallback()
    {
        var probeStart = new Vector3(10, 10, 100);
        var hit = new Vector3(10, 10, -500);
        var result = TeleportMath.SnapToGround(probeStart, hit, 100f, probeStart);
        Assert.Equal(probeStart, result);
    }

    [Fact]
    public void Lerp_Midpoint_IsHalfway()
    {
        var result = TeleportMath.Lerp(Vector3.Zero, new Vector3(10, 0, 0), 0.5f);
        Assert.Equal(5f, result.X, 3);
    }
}
