using Chrono.Domain;

namespace Chrono.Tests;

public class ConfigValidatorTests
{
    [Fact]
    public void Validate_ValidConfig_NoWarnings()
    {
        var result = ConfigValidator.Validate(new ChronoConfig());
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Validate_NullConfig_UsesDefaults()
    {
        var result = ConfigValidator.Validate(null!);
        Assert.NotNull(result.Config);
        Assert.Single(result.Warnings);
        Assert.Equal("Shift+0", result.Config.MenuKey);
    }

    [Fact]
    public void Validate_EmptyMenuKey_DefaultsToShift0()
    {
        var cfg = new ChronoConfig { MenuKey = "" };
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal("Shift+0", result.Config.MenuKey);
        Assert.Contains(result.Warnings, w => w.Contains("menuKey"));
    }

    [Theory]
    [InlineData(4.0f)]
    [InlineData(31.0f)]
    [InlineData(0f)]
    public void Validate_DashRangeOutOfBounds_DefaultsTo12(float range)
    {
        var cfg = new ChronoConfig();
        cfg.Dash.Range = range;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(12.0f, result.Config.Dash.Range);
    }

    [Theory]
    [InlineData(5.0f)]
    [InlineData(12.0f)]
    [InlineData(30.0f)]
    public void Validate_DashRangeInBounds_Kept(float range)
    {
        var cfg = new ChronoConfig();
        cfg.Dash.Range = range;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(range, result.Config.Dash.Range);
    }

    [Fact]
    public void Validate_MaxRangeBelowRange_Clamped()
    {
        var cfg = new ChronoConfig();
        cfg.Dash.Range = 20f;
        cfg.Dash.MaxRange = 10f;
        var result = ConfigValidator.Validate(cfg);
        Assert.True(result.Config.Dash.MaxRange >= result.Config.Dash.Range);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(20000)]
    public void Validate_MaintenanceIntervalOutOfBounds_Defaults(int ms)
    {
        var cfg = new ChronoConfig();
        cfg.TimeStop.MaintenanceIntervalMs = ms;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(2000, result.Config.TimeStop.MaintenanceIntervalMs);
    }

    [Fact]
    public void Validate_MaxFrozenEntitiesOutOfBounds_Defaults()
    {
        var cfg = new ChronoConfig();
        cfg.TimeStop.MaxFrozenEntities = 99999;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(1024, result.Config.TimeStop.MaxFrozenEntities);
    }

    [Fact]
    public void Validate_FreezeRadiusOutOfBounds_Defaults()
    {
        var cfg = new ChronoConfig();
        cfg.TimeStop.FreezeRadius = 999f;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(100f, result.Config.TimeStop.FreezeRadius);
    }

    [Fact]
    public void Validate_FlySpeedOutOfBounds_Defaults()
    {
        var cfg = new ChronoConfig();
        cfg.Fly.Speed = 200f;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(25f, result.Config.Fly.Speed);
    }

    [Fact]
    public void Validate_TintStrengthOutOfBounds_Defaults()
    {
        var cfg = new ChronoConfig();
        cfg.Visual.TimeStop.TintStrength = 2.5f;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(0.4f, result.Config.Visual.TimeStop.TintStrength);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData("WARN")]
    public void Validate_LoggingLevel_InvalidOrCaseInsensitive(string level)
    {
        var cfg = new ChronoConfig();
        cfg.Logging.Level = level;
        var result = ConfigValidator.Validate(cfg);

        if (level == "WARN")
            Assert.Equal("WARN", result.Config.Logging.Level); // case-insensitive valid
        else
            Assert.Equal("info", result.Config.Logging.Level);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    [InlineData(2_000_000)]
    public void Validate_CheatMoneyAmountOutOfBounds_Defaults(int amount)
    {
        var cfg = new ChronoConfig();
        cfg.Cheat.MoneyAmount = amount;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(10000, result.Config.Cheat.MoneyAmount); // FR-A2 fail-soft
        Assert.Contains(result.Warnings, w => w.Contains("cheat.moneyAmount"));
    }

    [Fact]
    public void Validate_CheatMoneyAmountInBounds_Kept()
    {
        var cfg = new ChronoConfig();
        cfg.Cheat.MoneyAmount = 25000;
        var result = ConfigValidator.Validate(cfg);
        Assert.Equal(25000, result.Config.Cheat.MoneyAmount);
    }
}
