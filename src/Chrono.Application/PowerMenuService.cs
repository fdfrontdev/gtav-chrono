using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly Func<IReadOnlyList<NewsFeedItem>>? _feedProvider;
    private readonly Func<bool>? _cutsceneActive;   // S16: menu stays closed during cutscenes
    private MenuItem? _webnetItem;
    private bool _menuWasOpen;
    private readonly NpcReactionService _npcReaction;
    private readonly PoliceDbHackService? _hack;
    private readonly JusticeStatsService? _stats;
    private readonly JusticeHudWidget? _hud;   // S21: persistent HUD widget (Settings toggle)

    private MenuScreen? _rootScreen;
    private MenuScreen? _webnetScreen;   // S14: WEBNET lives INSIDE the menu now
    private MenuItem? _timeStopItem;
    private MenuItem? _godModeItem;
    private MenuItem? _invisibleItem;
    private MenuItem? _flyItem;
    private MenuItem? _dashItem;         // S21 v3: powers grouped under SUPER
    private MenuItem? _mapTeleportItem;  // S21 v3
    private MenuItem? _hackItem;
    private MenuItem? _recordItem;
    private MenuScreen? _justiceScreen;
    private MenuScreen? _recordScreen;

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
        FlyService? fly = null,
        NpcReactionService? npcReaction = null,
        PoliceDbHackService? hack = null,
        JusticeStatsService? stats = null,
        Func<IReadOnlyList<NewsFeedItem>>? feedProvider = null,
        Func<bool>? cutsceneActive = null,
        JusticeHudWidget? hud = null)   // S21: persistent HUD widget toggle
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
        _npcReaction = npcReaction ?? new NpcReactionService(player, log, config.Npc);
        _hack = hack;
        _stats = stats;
        _feedProvider = feedProvider;
        _cutsceneActive = cutsceneActive;
        _hud = hud;
    }

    /// <summary>Build the menu tree (called once at startup after config load).</summary>
    public void BuildMenu()
    {
        var settings = BuildSettingsScreen();
        _timeStopItem = new MenuItem
        {
            Title = $"{UiStrings.ItemTimeStop} [{_config.TimeStop.Hotkey}]",
            OnActivate = ToggleTimeStop
        };
        _godModeItem = new MenuItem
        {
            Title = UiStrings.ItemGodMode,
            OnActivate = () => { _godMode.Toggle(); RefreshPowerLabels(); }
        };
        _invisibleItem = new MenuItem
        {
            Title = $"{UiStrings.ItemInvisible} [{_config.Invisible.Hotkey}]",
            OnActivate = ToggleInvisible
        };
        _flyItem = new MenuItem
        {
            Title = UiStrings.ItemFly,
            OnActivate = () => { _fly.Toggle(); RefreshPowerLabels(); }
        };
        _dashItem = new MenuItem
        {
            Title = $"{UiStrings.ItemDash} [{_config.Dash.Hotkey}]",
            OnActivate = ExecuteDash
        };
        _mapTeleportItem = new MenuItem
        {
            Title = UiStrings.ItemMapTeleport,
            OnActivate = ExecuteMapTeleport
        };

        // S21 v3 (user UAT): ALL superpowers live under one "SUPERPOWERS"
        // category — dash / map teleport / fly / invisible / god mode.
        var powers = new MenuScreen
        {
            Title = UiStrings.ItemSuperpowers,
            Items = new[]
            {
                _dashItem,
                _mapTeleportItem,
                _flyItem,
                _invisibleItem,
                _godModeItem,
                _timeStopItem
            }
        };

        _rootScreen = new MenuScreen
        {
            Title = UiStrings.MenuTitle,
            Items = new[]
            {
                new MenuItem { Title = UiStrings.ItemSuperpowers, Submenu = powers },
                new MenuItem { Title = UiStrings.ItemJustice, Submenu = BuildJusticeScreen() },
                _webnetItem = new MenuItem { Title = UiStrings.ItemWebnet, Submenu = RebuildWebnetScreen() },
                new MenuItem { Title = UiStrings.ItemSettings, Submenu = settings }
            }
        };

        RefreshPowerLabels();
        RefreshTimeStopLabel();
    }

    /// <summary>WEBNET feed screen (S14) — the news feed lives INSIDE the cheat
    /// menu now (no more ↑ phone key). Newest first, capped at 14 with a footer.</summary>
    private MenuScreen RebuildWebnetScreen()
    {
        if (_feedProvider == null)
        {
            _webnetScreen = new MenuScreen { Title = UiStrings.ItemWebnet, Items = new[] { new MenuItem { Title = "No feed available" } } };
            return _webnetScreen;
        }

        var feed = _feedProvider();
        var items = new List<MenuItem>();
        int shown = 0;
        for (int i = feed.Count - 1; i >= 0 && shown < 100; i--, shown++)   // S17: viewport scrolls; sanity cap only
        {
            var post = feed[i];
            items.Add(new MenuItem
            {
                Title = ClampTitle(post.Text),
                Value = post.Viral ? "▲ VIRAL" : post.When
            });
        }
        if (shown == 0)
            items.Add(new MenuItem { Title = "No stories yet — go make some" });
        else if (feed.Count > shown)
            items.Add(new MenuItem { Title = $"…{feed.Count - shown} older posts — session feed" });

        _webnetScreen = new MenuScreen { Title = UiStrings.ItemWebnet, Items = items };
        return _webnetScreen;
    }

    private static string ClampTitle(string text)
        => text.Length <= 52 ? text : text.Substring(0, 51) + "…";

    private MenuScreen BuildJusticeScreen()
    {
        _hackItem = new MenuItem
        {
            Title = UiStrings.ItemHackPoliceDb,
            OnActivate = ExecuteHack
        };
        RebuildRecordScreen();
        _recordItem = new MenuItem
        {
            Title = UiStrings.ItemCriminalRecord,
            Submenu = _recordScreen
        };

        var items = new List<MenuItem>();
        if (_hack != null) items.Add(_hackItem);
        if (_stats != null) items.Add(_recordItem);

        _justiceScreen = new MenuScreen
        {
            Title = UiStrings.ItemJustice,
            Items = items
        };
        return _justiceScreen;
    }

    /// <summary>Live snapshot of the criminal record (S7): identity, warrant, age,
    /// convictions + each crime. Rebuilt while the Justice screen is open.</summary>
    private void RebuildRecordScreen()
    {
        if (_stats == null)
        {
            _recordScreen = new MenuScreen { Title = UiStrings.ItemCriminalRecord, Items = Array.Empty<MenuItem>() };
            return;
        }

        var stats = _stats.GetStats();
        var items = new List<MenuItem>
        {
            new() { Title = $"Public image: {stats.PublicImage} (N{stats.Notoriety} · F{stats.Fame})" },
            new() { Title = stats.BailActive ? "Bail: OUT PENDING TRIAL — don't break the law" : "Bail: none" },
            new() { Title = stats.ParoleDaysLeft > 0 ? $"Parole: {stats.ParoleDaysLeft} day(s) left — the state is watching" : "Parole: none" },
            new() { Title = $"Identity: {(stats.Identity == IdentityState.Burned ? "BURNED — face on file" : "Clean")}" },
            new() { Title = $"Warrant: {(stats.WarrantActive ? "ACTIVE — stay out of sight" : "None")}" },
            new() { Title = $"Record: {stats.Crimes.Count + (stats.Crimes.Count == 20 ? "+" : "")} crimes · {stats.ConvictionCount} convictions" },
            new() { Title = $"Fines paid: ${stats.TotalFines:N0}" },
            new() { Title = $"Time served: {stats.DaysServed} days" },
            new() { Title = $"Age: {FormatAge(stats.AgeDays)} · Surgeries: {stats.Surgeries}" },
            new() { Title = $"Clinic: {(stats.ClinicReady ? "READY — stand at the door, press G" : "on cooldown")}" },
            new() { Title = $"Police DB: {(stats.HackReady ? "READY — activate this item" : "on cooldown")}" }
        };

        if (stats.Identity == IdentityState.Burned && stats.WarrantActive)
            items.Add(new MenuItem { Title = "Tip: the clinic or a DB hack clears your warrant" });

        // S17: the renderer's viewport scrolls — the record can list everything
        // (sanity cap 100 protects the per-tick rebuild cost)
        int shown = 0;
        int uncharged = stats.Crimes.Count(c => !c.Charged);
        if (uncharged > 0)
            items.Add(new MenuItem { Title = $"⚠ {uncharged} uncharged crime{(uncharged > 1 ? "s" : "")} — will be sentenced at your next court date" });
        foreach (var crime in stats.Crimes)
        {
            if (shown >= 100) break;
            string time = crime.GameTime.Length >= 16 ? crime.GameTime.Substring(11, 5) : crime.GameTime;
            string charged = crime.Charged ? " ✓" : "";
            items.Add(new MenuItem
            {
                Title = $"{crime.Severity} — {crime.Kind} ({crime.District}, {time}){charged}"
            });
            shown++;
        }

        if (stats.Crimes.Count > shown)
            items.Add(new MenuItem { Title = $"…{stats.Crimes.Count - shown} older crimes — full log in record.json" });

        if (shown == 0)
            items.Add(new MenuItem { Title = "No crimes recorded" });

        _recordScreen = new MenuScreen
        {
            Title = UiStrings.ItemCriminalRecord,
            Items = items
        };
    }

    private static string FormatAge(int ageDays)
        => $"{ageDays / 365}y {ageDays % 365}d";

    private void ExecuteHack()
    {
        if (_hack == null)
        {
            _notifier.Show("No police DB access");
            return;
        }
        _hack.TryHack();
    }

    public void ToggleMenu()
    {
        if (_menu.IsOpen)
        {
            _menu.Close();
            _player.SetControlEnabled(true);
        }
        else if (_rootScreen != null)
        {
            if (_cutsceneActive != null && _cutsceneActive())
            {
                _notifier.Show("Not now — a cutscene is playing");
                return;
            }
            _menu.Open(_rootScreen);
            _player.SetControlEnabled(false);   // freeze the character while the menu is up (S8)
        }
    }

    /// <summary>Menu visibility — exposed for tests and diagnostics.</summary>
    public bool IsMenuOpen => _menu.IsOpen;

    /// <summary>Invisibility state — exposed for tests and diagnostics.</summary>
    public bool IsInvisible => _invisible.IsEnabled;

    /// <summary>Time Stop state — exposed for tests and diagnostics.</summary>
    public bool IsTimeStopActive => _timeStop.IsActive;

    /// <summary>Per-frame update: warp progression, menu input, time-stop maintenance.</summary>
    public void Tick(long nowMs)
    {
        _input.Update();

        // Watchdog (S10): ANY close transition must restore control — covers close
        // paths the code above might miss (e.g. scripted closes)
        if (_menuWasOpen && !_menu.IsOpen)
            _player.SetControlEnabled(true);
        _menuWasOpen = _menu.IsOpen;

        if (_vfx.IsWarping)
        {
            if (_input.IsMenuKeyJustPressed) { _vfx.CancelWarp(); _notifier.Show(UiStrings.WarpCancelled); }
            else if (_vfx.TickWarp(nowMs))
            {
                var from = _player.Position;
                _vfx.BeginGokuTransmission();
                // Grace BEFORE the warp: covers the wind-up + arrival + digestion
                _npcReaction.TriggerGracePeriod();
                var result = _teleport.TryMapTeleport();
                if (result.Outcome == TeleportOutcome.Success && result.Point.HasValue)
                {
                    _vfx.CompleteGokuTransmission(from, result.Point.Value);
                    // Settle on terrain first (kills the falling/parachute pose), then
                    // superhero chest-landing pose (verified anim, DurtyFree dump)
                    _player.PlaceOnGround();
                    _player.PlayAnimationOnce("anim@scripted@heist@ig20_chest_land@male@", "action_chest", 1200);
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
            else if (_input.IsMenuCancelJustPressed)
            {
                _menu.NavigateBack();
                if (!_menu.IsOpen) _player.SetControlEnabled(true);   // Esc closed at root (S10)
            }
            else if (_input.IsMenuKeyJustPressed) { _menu.Close(); _player.SetControlEnabled(true); }
            _menu.Render();
        }
        else
        {
            if (_input.IsMenuKeyJustPressed) ToggleMenu();
            if (_input.IsDashHotkeyPressed) ExecuteDash();
            if (_input.IsTimeStopHotkeyJustPressed) ToggleTimeStop();       // Z
            if (_input.IsInvisibleHotkeyJustPressed) ToggleInvisible();     // B
        }

        _timeStop.Tick(nowMs);
        _vfx.Tick();
        _godMode.Tick();
        _invisible.Tick();
        _fly.Tick();

        // Criminal Record screen refresh while the Justice screen is open (S7)
        if (_menu.IsOpen && _menu.CurrentScreen == _justiceScreen)
        {
            RebuildRecordScreen();
            if (_recordItem != null) _recordItem.Submenu = _recordScreen;
        }

        // WEBNET feed refresh while its screen is open (S14 — the feed is session data)
        if (_menu.IsOpen && _menu.CurrentScreen == _webnetScreen)
        {
            if (_webnetItem != null) _webnetItem.Submenu = RebuildWebnetScreen();
        }

        // Persistent fly-controls hint while flying (user request v0.3.0: "no instructions on screen")
        if (_fly.IsEnabled && !_menu.IsOpen)
            _menu.DrawHint(UiStrings.FlyHint);

        // Invisibility = PERSISTENT perception suppression (reasserted every tick —
        // the game can reset the ignore flags). When not invisible, the timed
        // reaction grace applies instead.
        if (_invisible.IsEnabled)
            _player.SetNpcAwareness(false);
        else
            _npcReaction.Tick();

        // Dash aim reticle: show where the blink will land while aiming (v0.8.0)
        if (!_menu.IsOpen && _player.IsAiming)
        {
            var aimTarget = _teleport.GetAimTarget();
            if (aimTarget.HasValue) _vfx.DrawDashTarget(aimTarget.Value);
        }
    }

    private void ToggleInvisible()
    {
        bool wasOn = _invisible.IsEnabled;
        _invisible.Toggle();
        RefreshPowerLabels();

        if (wasOn)
        {
            // Uncloaking near NPCs: give them the same surprise → digest window
            // (they didn't see you appear — they must process your sudden presence).
            _npcReaction.TriggerGracePeriod();
        }
    }

    private void ToggleTimeStop()
    {
        try
        {
            if (_timeStop.IsActive)
            {
                // Grace BEFORE the unfreeze: NPCs wake up to a player they haven't
                // processed yet (two-stage cognition: orient → comprehend → act).
                _npcReaction.TriggerGracePeriod();
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
            _player.SetControlEnabled(true);
            var from = _player.Position;

            _npcReaction.TriggerGracePeriod();   // BEFORE the blink — peds can't queue a startle reaction
            _vfx.BeginInstantTransmission();
            var result = _teleport.TryDash();
            if (result.Outcome == TeleportOutcome.Success && result.Point.HasValue)
            {
                _vfx.CompleteInstantTransmission(from, result.Point.Value);
            }
            else
            {
                _vfx.AbortInstantTransmission(); // blocked — never leave the player invisible
            }
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
            _player.SetControlEnabled(true);
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
                    Title = UiStrings.ItemHotkeys,
                    Value = $"F9 menu | {_config.Dash.Hotkey} dash | {_config.TimeStop.Hotkey} stop | {_config.Invisible.Hotkey} invis"
                },
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
                new MenuItem
                {
                    Title = UiStrings.ItemShowHud,
                    Value = _hud?.Enabled == true ? "ON" : "OFF",
                    OnActivate = () =>
                    {
                        if (_hud == null) return;
                        _hud.Enabled = !_hud.Enabled;
                        _config.Justice.HudEnabled = _hud.Enabled;
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
