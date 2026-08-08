using System;
using System.Diagnostics;
using System.IO;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Boundary;
using Chrono.Domain;
using GTA;

namespace Chrono.EntryPoint;

/// <summary>
/// Composition root (HLD §2, DLD §6). Wires Domain → Application → Boundary.
/// No business logic here — only construction and the tick pipeline.
/// </summary>
public class ChronoScript : Script
{
    private PowerMenuService? _menu;
    private TimeStopService? _timeStop;
    private ChronoLogger? _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public ChronoScript()
    {
        try
        {
            // SHVDN sets Script.BaseDirectory to the folder containing this script
            // (scripts\Chrono for subfolder installs) — config and log live next to the DLL.
            var modDir = BaseDirectory;
            var configPath = Path.Combine(modDir, "config.json");
            var logPath = Path.Combine(modDir, "chrono.log");

            // Config: load → validate → fail-soft (FR-5, SRS §7)
            var store = new JsonConfigStore(configPath);
            var validation = ConfigValidator.Validate(store.Load());
            var config = validation.Config;

            _log = new ChronoLogger(logPath, config.Logging.Level);
            foreach (var warning in validation.Warnings) _log.Warn($"Config: {warning}");
            if (validation.Warnings.Count > 0)
                GTA.UI.Notification.PostTicker(UiStrings.ConfigError, false, false);

            // Boundary adapters
            IGameClock clock = new GameClockAdapter();
            IEntityRepository repo = new EntityRepository();
            IEntityFreezer freezer = new EntityFreezer();
            IPlayerContext player = new PlayerContext();
            IWorldProbe probe = new WorldProbe();
            INotifier notifier = new Notifier();
            IGameInput input = new GameInput(config.MenuKey, config.Dash.Hotkey);
            IVfxBoundary vfx = new VfxBoundary();
            IMenuRenderer renderer = new NativeMenuRenderer();

            // Application services
            _timeStop = new TimeStopService(repo, freezer, clock, player, notifier, _log, config.TimeStop);
            var teleport = new TeleportService(player, probe, notifier, _log, config.Dash, config.Teleport);
            var vfxService = new VfxService(vfx, _log, config.Visual);
            var menuFramework = new MenuFramework(renderer);
            _menu = new PowerMenuService(
                menuFramework, _timeStop, teleport, vfxService,
                input, player, notifier, _log, config, store);
            _menu.BuildMenu();

            Tick += OnTick;
            _log.Info($"Chrono initialized — menu key {config.MenuKey}");
            notifier.Show(UiStrings.FirstRun);
        }
        catch (Exception ex)
        {
            // Logging may not exist yet — write a minimal error file (never crash the game)
            try
            {
                Directory.CreateDirectory(BaseDirectory);
                File.AppendAllText(Path.Combine(BaseDirectory, "chrono.log"),
                    $"[{DateTime.Now:HH:mm:ss}] ERROR: Chrono init failed: {ex}{Environment.NewLine}");
            }
            catch { /* last resort: silent */ }
            GTA.UI.Notification.PostTicker(UiStrings.BugError, false, false);
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        try
        {
            _menu?.Tick(_clock.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _log?.Error($"Tick error: {ex}");
            GTA.UI.Notification.PostTicker(UiStrings.BugError, false, false);
        }
    }
}
