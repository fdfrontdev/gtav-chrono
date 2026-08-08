using System.Numerics;
using Chrono.Domain;

namespace Chrono.Tests;

public class FlyMathTests
{
    private static readonly Vector3 North = new(0f, 1f, 0f);
    private static readonly Vector3 East = new(1f, 0f, 0f);

    [Fact]
    public void Forward_AtFullSpeed()
    {
        var v = FlyMath.CalculateVelocity(North, East, 25f, true, false, false, false, false, false);
        Assert.Equal(0f, v.X, 3);
        Assert.Equal(25f, v.Y, 3);
        Assert.Equal(0f, v.Z, 3);
    }

    [Fact]
    public void Diagonal_IsNormalized_NoSpeedBoost()
    {
        // forward + right simultaneously must NOT exceed horizontal speed
        var v = FlyMath.CalculateVelocity(North, East, 25f, true, false, false, true, false, false);
        var horizontal = new Vector2(v.X, v.Y).Length();
        Assert.Equal(25f, horizontal, 2);
    }

    [Fact]
    public void Ascend_AddsVerticalAtFullSpeed()
    {
        var v = FlyMath.CalculateVelocity(North, East, 25f, false, false, false, false, true, false);
        Assert.Equal(25f, v.Z, 3);
        Assert.Equal(0f, v.Y, 3);
    }

    [Fact]
    public void Descend_OpposesAscend()
    {
        var v = FlyMath.CalculateVelocity(North, East, 25f, false, false, false, false, false, true);
        Assert.Equal(-25f, v.Z, 3);
    }

    [Fact]
    public void NoInput_ZeroVelocity()
    {
        var v = FlyMath.CalculateVelocity(North, East, 25f, false, false, false, false, false, false);
        Assert.Equal(Vector3.Zero, v);
    }

    [Fact]
    public void ForwardAndBack_CancelOut()
    {
        var v = FlyMath.CalculateVelocity(North, East, 25f, true, true, false, false, false, false);
        Assert.Equal(0f, v.Y, 3);
    }

    [Fact]
    public void Right_MatchesCameraRight()
    {
        var v = FlyMath.CalculateVelocity(North, East, 25f, false, false, false, true, false, false);
        Assert.Equal(25f, v.X, 3);
    }

    [Fact]
    public void Back_OpposesForward()
    {
        var v = FlyMath.CalculateVelocity(North, East, 25f, false, true, false, false, false, false);
        Assert.Equal(-25f, v.Y, 3);
    }

    [Fact]
    public void SmoothVelocity_ZeroDt_Unchanged()
    {
        var result = FlyMath.SmoothVelocity(new Vector3(10, 0, 0), Vector3.Zero, 0f, 6f);
        Assert.Equal(10f, result.X, 3);
    }

    [Fact]
    public void SmoothVelocity_ApproachesTarget_Asymptotically()
    {
        var v = new Vector3(0, 0, 0);
        var target = new Vector3(25, 0, 0);
        for (int i = 0; i < 20; i++) v = FlyMath.SmoothVelocity(v, target, 0.1f, 6f);

        Assert.True(v.X > 24f && v.X < 25f, $"expected ~25 after 2s, got {v.X}");
        Assert.Equal(0f, v.Y, 3);
    }

    [Fact]
    public void SmoothVelocity_ReleasesInput_DeceleratesToZero()
    {
        var v = new Vector3(25, 0, 0);
        var target = Vector3.Zero;
        for (int i = 0; i < 20; i++) v = FlyMath.SmoothVelocity(v, target, 0.1f, 6f);

        Assert.True(v.X < 0.1f, $"expected ~0 after release, got {v.X}");
    }

    [Fact]
    public void SmoothHeading_WrapsThroughZero_NotThrough180()
    {
        // 350° → 10°: the short way is +20° through 0, not -340°
        float h = 350f;
        for (int i = 0; i < 10; i++) h = FlyMath.SmoothHeading(h, 10f, 0.1f, 6f);

        Assert.True(h < 30f && h > 5f, $"expected ~10 (via 0), got {h}");
    }
}
