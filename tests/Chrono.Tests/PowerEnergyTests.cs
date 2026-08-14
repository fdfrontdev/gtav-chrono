using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>v0.10 energy pool math (SRS FR-B1/B2) — pure PowerEnergy.</summary>
public class PowerEnergyTests
{
    [Fact]
    public void Create_StartsFull()
    {
        var e = PowerEnergy.Create(100, 8);
        Assert.Equal(100, e.Current);
        Assert.Equal(100, e.Max);
    }

    [Fact]
    public void Tick_Regens_ButNeverExceedsMax()
    {
        var e = PowerEnergy.Create(100, 8).Spend(50);
        var after = e.Tick(5).Tick(5);   // 10s × 8 = 80 → capped at 100
        Assert.Equal(100, after.Current);
    }

    [Fact]
    public void Tick_PartialRegen_IntegerTruncation()
    {
        var e = PowerEnergy.Create(100, 8).Spend(90);   // 10 left
        var after = e.Tick(1);                          // +8 → 18
        Assert.Equal(18, after.Current);
        Assert.Equal(26, after.Tick(1).Current);        // +8 → 26
    }

    [Fact]
    public void Spend_NeverBelowZero()
    {
        var e = PowerEnergy.Create(100, 8).Spend(150);
        Assert.Equal(0, e.Current);
    }

    [Fact]
    public void CanAfford_ExactAndOver()
    {
        var e = PowerEnergy.Create(100, 8);
        Assert.True(e.CanAfford(100));
        Assert.False(e.CanAfford(101));
        Assert.True(e.Spend(40).CanAfford(60));
    }
}
