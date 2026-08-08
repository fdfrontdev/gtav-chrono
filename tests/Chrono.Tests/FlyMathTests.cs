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
}
