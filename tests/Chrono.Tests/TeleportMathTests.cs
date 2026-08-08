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
        // heading 90 = east = +X (GTA convention)
        var target = TeleportMath.CalculateForwardTarget(Vector3.Zero, 90f, 7f);
        Assert.Equal(7f, target.X, 3);
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

    [Theory]
    [InlineData(0f, 0f, true)]            // city center
    [InlineData(3000f, 5000f, true)]      // northern map
    [InlineData(3950f, 0f, false)]        // off the east edge
    [InlineData(-3950f, 0f, false)]       // off the west edge
    [InlineData(0f, -4500f, false)]       // off the south edge
    [InlineData(0f, 8000f, false)]        // off the north edge
    public void IsInsideWorldBounds_Various(float x, float y, bool expected)
    {
        Assert.Equal(expected, TeleportMath.IsInsideWorldBounds(new Vector3(x, y, 0)));
    }

    [Fact]
    public void HeadingFromVelocity_North_IsZero()
    {
        Assert.Equal(0f, TeleportMath.HeadingFromVelocity(new Vector3(0, 25, 0)), 2);
    }

    [Fact]
    public void HeadingFromVelocity_East_Is90()
    {
        Assert.Equal(90f, TeleportMath.HeadingFromVelocity(new Vector3(25, 0, 0)), 2);
    }

    [Fact]
    public void HeadingFromVelocity_Zero_IsZero()
    {
        Assert.Equal(0f, TeleportMath.HeadingFromVelocity(Vector3.Zero), 2);
    }

    [Fact]
    public void HeadingFromVelocity_NorthWest_Is315()
    {
        Assert.Equal(315f, TeleportMath.HeadingFromVelocity(new Vector3(-25, 25, 0)), 2);
    }

    [Fact]
    public void Lerp_Midpoint_IsHalfway()
    {
        var result = TeleportMath.Lerp(Vector3.Zero, new Vector3(10, 0, 0), 0.5f);
        Assert.Equal(5f, result.X, 3);
    }
}
