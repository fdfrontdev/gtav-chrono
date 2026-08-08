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
    private readonly GodModeService _godMode;
    private readonly InvisibilityService _invisible;
    private readonly FlyService _fly;

    private MenuScreen? _rootScreen;
    private MenuItem? _timeStopItem;
    private MenuItem? _godModeItem;
    private MenuItem? _invisibleItem;
    private MenuItem? _flyItem;

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
        IConfigStore configStore,
        GodModeService? godMode = null,
        InvisibilityService? invisible = null,
        FlyService? fly = null)
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
        _godMode = godMode ?? new GodModeService(player, log);
        _invisible = invisible ?? new InvisibilityService(player, log);
        _fly = fly ?? new FlyService(player, input, log, config.Fly);
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
        _godModeItem = new MenuItem
        {
            Title = UiStrings.ItemGodMode,
            OnActivate = () => { _godMode.Toggle(); RefreshPowerLabels(); }
        };
        _invisibleItem = new MenuItem
        {
            Title = UiStrings.ItemInvisible,
            OnActivate = () => { _invisible.Toggle(); RefreshPowerLabels(); }
        };
        _flyItem = new MenuItem
        {
            Title = UiStrings.ItemFly,
            OnActivate = () => { _fly.Toggle(); RefreshPowerLabels(); }
        };

        _rootScreen = new MenuScreen
        {
            Title = UiStrings.MenuTitle,
            Items = new[]
            {
                _timeStopItem,
                new MenuItem { Title = UiStrings.ItemDash, OnActivate = ExecuteDash },
                new MenuItem { Title = UiStrings.ItemMapTeleport, OnActivate = ExecuteMapTeleport },
                _godModeItem,
                _invisibleItem,
                _flyItem,
                new MenuItem { Title = UiStrings.ItemSettings, Submenu = settings }
            }
        };

        RefreshPowerLabels();
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
                var from = _player.Position;
                _vfx.BeginGokuTransmission();
                var result = _teleport.TryMapTeleport();
                if (result.Outcome == TeleportOutcome.Success && result.Point.HasValue)
                {
                    _vfx.CompleteGokuTransmission(from, result.Point.Value);
                    _notifier.Show(UiStrings.WarpArrived);
                }
                else
                {
                    _vfx.AbortInstantTransmission();
                }
            }
            _vfx.Tick();
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
        _vfx.Tick();
        _godMode.Tick();
        _invisible.Tick();
        _fly.Tick();
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

            _vfx.BeginInstantTransmission();
            var result = _teleport.TryDash();
            if (result.Outcome == TeleportOutcome.Success && result.Point.HasValue)
                _vfx.CompleteInstantTransmission(from, result.Point.Value);
            else
                _vfx.AbortInstantTransmission(); // blocked — never leave the player invisible
        }
        catch (Exception ex)
        {
            _log.Error($"Dash failed: {ex}");
            _vfx.AbortInstantTransmission();
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
                    Title = UiStrings.ItemFlySpeed,
                    Value = $"{_config.Fly.Speed:0.0}",
                    OnAdjust = AdjustFlySpeed
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

    private void AdjustFlySpeed(int direction)
    {
        float next = _config.Fly.Speed + direction * 5.0f;
        _config.Fly.Speed = next < 5.0f ? 5.0f : (next > 80.0f ? 80.0f : next);
        PersistConfig();
        RefreshSettingsValues();
    }

    private void AdjustDashRange(int direction)
    {
        float next = _config.Dash.Range + direction * 1.0f;
        _config.Dash.Range = next < 5.0f ? 5.0f : (next > 30.0f ? 30.0f : next);
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
            else if (item.Title == UiStrings.ItemFlySpeed) item.Value = $"{_config.Fly.Speed:0.0}";
            else if (item.Title == UiStrings.ItemFreezeProps) item.Value = _config.TimeStop.FreezeProps ? "ON" : "OFF";
            else if (item.Title == UiStrings.ItemPauseClock) item.Value = _config.TimeStop.PauseClock ? "ON" : "OFF";
        }
    }

    private void RefreshPowerLabels()
    {
        if (_godModeItem != null) _godModeItem.Value = _godMode.IsEnabled ? "ON" : "OFF";
        if (_invisibleItem != null) _invisibleItem.Value = _invisible.IsEnabled ? "ON" : "OFF";
        if (_flyItem != null) _flyItem.Value = _fly.IsEnabled ? "ON" : "OFF";
    }

    private void RefreshTimeStopLabel()
    {
        if (_timeStopItem != null) _timeStopItem.Value = _timeStop.IsActive ? "ON" : "OFF";
    }
}
