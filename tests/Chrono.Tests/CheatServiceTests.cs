using System;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// v0.11 cheat menu (SRS FR-B1..B4, FR-D1..D3): money grant, health refill,
/// needs fill — all through existing ports, all visible (notifier) actions.
/// </summary>
public class CheatServiceTests
{
    private static (CheatService cheat, FakePlayer player, FakeNotifier notifier, NeedsService? needs) Build(
        bool withNeeds = true, int moneyAmount = 10000)
    {
        var player = new FakePlayer { Money = 500 };
        var notifier = new FakeNotifier();
        var config = new ChronoConfig { Cheat = new CheatConfig { MoneyAmount = moneyAmount } };
        var needs = withNeeds
            ? new NeedsService(player, new FakeRecordStore(), new NeedsConfig(),
                notifier, new FakeLog())
            : null;
        if (withNeeds) needs!.Load();
        var cheat = new CheatService(player, notifier, new FakeLog(), config, needs);
        return (cheat, player, notifier, needs);
    }

    [Fact]
    public void GiveMoney_AddsExactAmount()
    {
        var (cheat, player, _, _) = Build();
        cheat.GiveMoney();
        Assert.Equal(10500, player.Money);
    }

    [Fact]
    public void GiveMoney_NotifiesWithAmount()
    {
        var (cheat, _, notifier, _) = Build(moneyAmount: 25000);
        cheat.GiveMoney();
        Assert.Contains(notifier.Messages, m => m.Contains("25,000"));
    }

    [Fact]
    public void GiveMoney_AmountFromConfig()
    {
        var (cheat, player, _, _) = Build(moneyAmount: 999);
        cheat.GiveMoney();
        Assert.Equal(500 + 999, player.Money);
    }

    [Fact]
    public void RefillHealth_CallsPlayerRefill()
    {
        var (cheat, player, _, _) = Build();
        cheat.RefillHealth();
        Assert.Equal(1, player.RefillCount);
    }

    [Fact]
    public void RefillHealth_Notifies()
    {
        var (cheat, _, notifier, _) = Build();
        cheat.RefillHealth();
        Assert.Contains(notifier.Messages, m => m.Contains("health"));
    }

    [Fact]
    public void FillNeeds_SetsAllFourToFull()
    {
        var (cheat, _, _, needs) = Build();
        needs!.State.Hunger = 10; needs.State.Thirst = 5; needs.State.Energy = 1; needs.State.Mood = 30;

        cheat.FillNeeds();

        Assert.Equal(100, needs.State.Hunger);
        Assert.Equal(100, needs.State.Thirst);
        Assert.Equal(100, needs.State.Energy);
        Assert.Equal(100, needs.State.Mood);
    }

    [Fact]
    public void FillNeeds_NotifiesOnce()
    {
        var (cheat, _, notifier, _) = Build();
        cheat.FillNeeds();
        Assert.Contains(notifier.Messages, m => m.Contains("needs"));
    }

    [Fact]
    public void FillNeeds_WithoutNeedsService_IsSafeNoOp()
    {
        var (cheat, _, notifier, _) = Build(withNeeds: false);
        cheat.FillNeeds(); // must not throw
        Assert.Empty(notifier.Messages);
    }
}
