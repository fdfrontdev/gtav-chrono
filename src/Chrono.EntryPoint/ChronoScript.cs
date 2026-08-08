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
    private JusticeService? _justice;
    private ClinicService? _clinic;
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
            IGameInput input = new GameInput(config.MenuKey, config.Dash.Hotkey, config.TimeStop.Hotkey, config.Invisible.Hotkey, config.Justice.InteractKey);
            IVfxBoundary vfx = new VfxBoundary();
            IMenuRenderer renderer = new NativeMenuRenderer();

            // Application services
            _timeStop = new TimeStopService(repo, freezer, clock, player, notifier, _log, config.TimeStop);
            var teleport = new TeleportService(player, probe, notifier, _log, config.Dash, config.Teleport);
            var vfxService = new VfxService(vfx, _log, config.Visual);
            var menuFramework = new MenuFramework(renderer);

            // Justice layer (v0.9.0) — S1 core + S2 media + S5 clinic + S6 hack
            var recordStore = new JsonRecordStore(BaseDirectory, _log);
            var identity = new IdentityService(recordStore, _log);
            var warrant = new WarrantService(recordStore, _log);
            var media = new MediaService(new MediaNotifier(notifier), _log, config.Justice);
            var wantedMonitor = new WantedMonitor();
            _justice = new JusticeService(
                wantedMonitor, player, recordStore,
                identity, warrant, notifier, _log, config.Justice, clock, media, vfxService, input);
            _clinic = new ClinicService(
                player, recordStore, identity, notifier, _log, config.Justice, clock, input, vfxService);
            var hack = new PoliceDbHackService(
                wantedMonitor, recordStore, identity, warrant, _justice,
                notifier, _log, config.Justice, clock, vfxService);

            _menu = new PowerMenuService(
                menuFramework, _timeStop, teleport, vfxService,
                input, player, notifier, _log, config, store, hack: hack);
            _menu.BuildMenu();
            CreateClinicBlip();

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
            _justice?.Tick();
            _clinic?.Tick();
        }
        catch (Exception ex)
        {
            _log?.Error($"Tick error: {ex}");
            GTA.UI.Notification.PostTicker(UiStrings.BugError, false, false);
        }
    }

    private static void CreateClinicBlip()
    {
        try
        {
            var door = ClinicService.ClinicDoor;
            var blip = World.CreateBlip(new GTA.Math.Vector3(door.X, door.Y, door.Z), 1f);
            blip.Color = GTA.BlipColor.Pink;   // clinic pink
            blip.Name = "Chrono Clinic";
        }
        catch
        {
            // blip is flavor — never a crash vector
        }
    }
}
