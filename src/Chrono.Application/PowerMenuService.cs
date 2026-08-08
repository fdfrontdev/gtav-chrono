using System;
using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Orchestrates the power menu and all power execution (FR-1..FR-4).
/// EntryPoint forwards ticks + input here; services stay decoupled from the game.
/// </summary>
public sealed class PowerMenuService
{
    private readonly MenuFramework _menu;
    private readonly TimeStopService _timeStop;
    private readonly TeleportService _teleport;
    private readonly VfxService _vfx;
    private readonly IGameInput _input;
    private readonly IPlayerContext _player;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly ChronoConfig _config;
    private readonly IConfigStore _configStore;

    private MenuScreen? _rootScreen;
    private MenuItem? _timeStopItem;

    public PowerMenuService(
        MenuFramework menu,
        TimeStopService timeStop,
        TeleportService teleport,
        VfxService vfx,
        IGameInput input,
        IPlayerContext player,
        INotifier notifier,
        ILogSink log,
        ChronoConfig config,
        IConfigStore configStore)
    {
        _menu = menu;
        _timeStop = timeStop;
        _teleport = teleport;
        _vfx = vfx;
        _input = input;
        _player = player;
        _notifier = notifier;
        _log = log;
        _config = config;
        _configStore = configStore;
    }

    /// <summary>Build the menu tree (called once at startup after config load).</summary>
    public void BuildMenu()
    {
        var settings = BuildSettingsScreen();
        _timeStopItem = new MenuItem
        {
            Title = UiStrings.ItemTimeStop,
            OnActivate = ToggleTimeStop
        };

        _rootScreen = new MenuScreen
        {
            Title = UiStrings.MenuTitle,
            Items = new[]
            {
                _timeStopItem,
                new MenuItem { Title = UiStrings.ItemDash, OnActivate = ExecuteDash },
                new MenuItem { Title = UiStrings.ItemMapTeleport, OnActivate = ExecuteMapTeleport },
                new MenuItem { Title = UiStrings.ItemSettings, Submenu = settings }
            }
        };

        RefreshTimeStopLabel();
    }

    public void ToggleMenu()
    {
        if (_menu.IsOpen) _menu.Close();
        else if (_rootScreen != null) _menu.Open(_rootScreen);
    }

    /// <summary>Menu visibility — exposed for tests and diagnostics.</summary>
    public bool IsMenuOpen => _menu.IsOpen;

    /// <summary>Per-frame update: warp progression, menu input, time-stop maintenance.</summary>
    public void Tick(long nowMs)
    {
        _input.Update();

        if (_vfx.IsWarping)
        {
            if (_input.IsMenuKeyJustPressed) { _vfx.CancelWarp(); _notifier.Show(UiStrings.WarpCancelled); }
            else if (_vfx.TickWarp(nowMs))
            {
                var result = _teleport.TryMapTeleport();
                if (result.Outcome == TeleportOutcome.Success)
                    _notifier.Show(UiStrings.WarpArrived);
            }
            return; // no menu while warping
        }

        if (_menu.IsOpen)
        {
            if (_input.IsMenuUpJustPressed) _menu.NavigateUp();
            else if (_input.IsMenuDownJustPressed) _menu.NavigateDown();
            else if (_input.IsMenuAcceptJustPressed) _menu.Accept();
            else if (_input.IsMenuCancelJustPressed) _menu.NavigateBack();
            else if (_input.IsMenuKeyJustPressed) _menu.Close();
            _menu.Render();
        }
        else
        {
            if (_input.IsMenuKeyJustPressed) ToggleMenu();
            if (_input.IsDashHotkeyPressed) ExecuteDash();
        }

        _timeStop.Tick(nowMs);
    }

    private void ToggleTimeStop()
    {
        try
        {
            if (_timeStop.IsActive)
            {
                _timeStop.Deactivate();
                _vfx.SetTimeStopCue(false);
                _notifier.Show(UiStrings.TimeStopOff);
            }
            else
            {
                _timeStop.Activate();
                _vfx.SetTimeStopCue(true);
                _notifier.Show(UiStrings.TimeStopOn);
            }
            RefreshTimeStopLabel();
        }
        catch (Exception ex)
        {
            _log.Error($"TimeStop toggle failed: {ex}");
            _notifier.Show(UiStrings.BugError);
        }
    }

    private void ExecuteDash()
    {
        try
        {
            _menu.Close();
            var from = _player.Position;
            var result = _teleport.TryDash();
            if (result.Outcome == TeleportOutcome.Success && result.Point.HasValue)
                _vfx.PlayDashBlink(from, result.Point.Value);
        }
        catch (Exception ex)
        {
            _log.Error($"Dash failed: {ex}");
            _notifier.Show(UiStrings.BugError);
        }
    }

    private void ExecuteMapTeleport()
    {
        try
        {
            _menu.Close();
            if (!_player.IsWaypointActive())
            {
                _notifier.Show(UiStrings.NoWaypoint);
                return;
            }
            _notifier.Show(UiStrings.WarpStart);
            _vfx.StartWarp(_player.Position, _player.GetWaypointPosition());
        }
        catch (Exception ex)
        {
            _log.Error($"Map teleport failed: {ex}");
            _notifier.Show(UiStrings.BugError);
        }
    }

    private MenuScreen BuildSettingsScreen()
    {
        return new MenuScreen
        {
            Title = UiStrings.ItemSettings,
            Items = new[]
            {
                new MenuItem
                {
                    Title = UiStrings.ItemDashRange,
                    Value = $"{_config.Dash.Range:0.0} m",
                    OnAdjust = AdjustDashRange
                },
                new MenuItem
                {
                    Title = UiStrings.ItemFreezeProps,
                    Value = _config.TimeStop.FreezeProps ? "ON" : "OFF",
                    OnActivate = () =>
                    {
                        _config.TimeStop.FreezeProps = !_config.TimeStop.FreezeProps;
                        PersistConfig();
                    }
                },
                new MenuItem
                {
                    Title = UiStrings.ItemPauseClock,
                    Value = _config.TimeStop.PauseClock ? "ON" : "OFF",
                    OnActivate = () =>
                    {
                        _config.TimeStop.PauseClock = !_config.TimeStop.PauseClock;
                        PersistConfig();
                    }
                },
                new MenuItem { Title = UiStrings.ItemBack, OnActivate = () => _menu.NavigateBack() }
            }
        };
    }

    private void AdjustDashRange(int direction)
    {
        float next = _config.Dash.Range + direction * 0.5f;
        _config.Dash.Range = next < 3.0f ? 3.0f : (next > 15.0f ? 15.0f : next);
        PersistConfig();
        RefreshSettingsValues();
    }

    private void PersistConfig()
    {
        _configStore.Save(_config);
        _log.Info("Settings persisted");
        RefreshSettingsValues();
    }

    private void RefreshSettingsValues()
    {
        if (_rootScreen == null) return;
        var settings = (MenuScreen?)_rootScreen.Items[_rootScreen.Items.Count - 1].Submenu;
        if (settings == null) return;
        foreach (var item in settings.Items)
        {
            if (item.Title == UiStrings.ItemDashRange) item.Value = $"{_config.Dash.Range:0.0} m";
            else if (item.Title == UiStrings.ItemFreezeProps) item.Value = _config.TimeStop.FreezeProps ? "ON" : "OFF";
            else if (item.Title == UiStrings.ItemPauseClock) item.Value = _config.TimeStop.PauseClock ? "ON" : "OFF";
        }
    }

    private void RefreshTimeStopLabel()
    {
        if (_timeStopItem != null) _timeStopItem.Value = _timeStop.IsActive ? "ON" : "OFF";
    }
}
