using System;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// v0.11 cheat menu (SRS FR-B1..B4): GiveMoney / RefillHealth / FillNeeds.
/// Every action is manual, menu-only, and visible (notifier). Survivor loop
/// untouched unless explicitly invoked. All side effects go through existing
/// ports — Boundary has zero changes for this feature.
/// </summary>
public sealed class CheatService
{
    private readonly IPlayerContext _player;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly CheatConfig _config;
    private readonly NeedsService? _needs;

    public CheatService(
        IPlayerContext player,
        INotifier notifier,
        ILogSink log,
        ChronoConfig config,
        NeedsService? needs = null)
    {
        _player = player;
        _notifier = notifier;
        _log = log;
        _config = config.Cheat;
        _needs = needs;
    }

    public void GiveMoney()
    {
        _player.AddMoney(_config.MoneyAmount);
        _notifier.Show($"CHEAT: +${_config.MoneyAmount:N0}");
        _log.Info($"Cheat: gave ${_config.MoneyAmount:N0}");
    }

    public void RefillHealth()
    {
        _player.RefillHealth();
        _notifier.Show("CHEAT: health restored");
        _log.Info("Cheat: health restored");
    }

    public void FillNeeds()
    {
        if (_needs is null) return;   // needs disabled → safe no-op
        _needs.FillAll();
        _notifier.Show("CHEAT: needs filled");
        _log.Info("Cheat: needs filled");
    }
}
