using Chrono.Domain;

namespace Chrono.Tests;

public class FreezePolicyTests
{
    [Fact]
    public void CanFreeze_Player_Never()
    {
        var cfg = new ChronoConfig();
        Assert.False(FreezePolicy.CanFreeze(EntityKind.Player, cfg));
    }

    [Fact]
    public void CanFreeze_Ped_Always()
    {
        var cfg = new ChronoConfig();
        Assert.True(FreezePolicy.CanFreeze(EntityKind.Ped, cfg));
    }

    [Fact]
    public void CanFreeze_Vehicle_Always()
    {
        var cfg = new ChronoConfig();
        Assert.True(FreezePolicy.CanFreeze(EntityKind.Vehicle, cfg));
    }

    [Fact]
    public void CanFreeze_Prop_OnlyWhenEnabled()
    {
        var cfgOn = new ChronoConfig();
        cfgOn.TimeStop.FreezeProps = true;
        Assert.True(FreezePolicy.CanFreeze(EntityKind.Prop, cfgOn));

        var cfgOff = new ChronoConfig();
        cfgOff.TimeStop.FreezeProps = false;
        Assert.False(FreezePolicy.CanFreeze(EntityKind.Prop, cfgOff));
    }
}
