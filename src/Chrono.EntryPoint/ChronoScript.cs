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
    private CrowdReactionService? _crowd;
    private JusticeCutsceneService? _cutscene;
    private TimeStopService? _timeStop;
    private ChronoLogger? _log;
    private CrimeDetectionService? _crimeDetection;   // S20: act-based crime detection
    private CrimeProbe? _crimeProbe;                  // S20: concrete — Poll() runs from OnTick
    private JusticeHudWidget? _hud;                   // S21: persistent HUD widget
    private bool _wasMissionActive;                    // S22: mission edge for cutscene abort
    private ChronoConfig? _config;                     // S22: mission gate reads PauseDuringMissions
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
            _config = config;   // S22: OnTick reads the mission gate

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
            _crimeProbe = new CrimeProbe();            // S20: act sampling + hold-fire
            ICrimeProbe crimeProbe = _crimeProbe;
            var hudFeed = new HudFeedBuffer();         // S21 v2: shared widget feed (notifier + WEBNET)
            INotifier notifier = new Notifier(hudFeed);
            IGameInput input = new GameInput(config.MenuKey, config.Dash.Hotkey, config.TimeStop.Hotkey, config.Invisible.Hotkey, config.Justice.InteractKey);
            IVfxBoundary vfx = new VfxBoundary();
            IMenuRenderer renderer = new MaterialMenuRenderer();   // S21: Vuetify-style (was ModernMenuRenderer)
            IHudRenderer hudRenderer = new MaterialHudRenderer();  // S21: persistent justice widget

            // Application services
            _timeStop = new TimeStopService(repo, freezer, clock, player, notifier, _log, config.TimeStop);
            var teleport = new TeleportService(player, probe, notifier, _log, config.Dash, config.Teleport);
            var vfxService = new VfxService(vfx, _log, config.Visual);
            var menuFramework = new MenuFramework(renderer);

            // Justice layer (v0.9.0) — S1..S8 + S9 reputation
                        // S22: relational storage — SQLite (idempotency + integrity). The
                        // legacy JSON files are migrated to chrono.db on first boot.
                        var recordStore = new SqliteRecordStore(BaseDirectory, _log);
            var identity = new IdentityService(recordStore, _log);
            var warrant = new WarrantService(recordStore, _log);
            var media = new MediaService(new MediaNotifier(notifier), _log, config.Justice, hudFeed,
                characterName: player.GetCharacterName);   // S21 v3: real names in headlines
            var reputation = new ReputationService(recordStore, clock, media, config.Justice,
                characterName: player.GetCharacterName);   // S21 v3
            var wantedMonitor = new WantedMonitor();
            var cutscene = new JusticeCutsceneService(new CutsceneRenderer(), player, _log,
                notifier: notifier);   // S21 v3: banners route into the widget feed
            _cutscene = cutscene;
            var prisonOutfit = new PrisonOutfit(msg => _log.Info(msg));
            _justice = new JusticeService(
                wantedMonitor, player, recordStore,
                identity, warrant, notifier, _log, config.Justice, clock, media, vfxService, input,
                reputation, probe, null, cutscene, prisonOutfit, crimeProbe);
            _crimeDetection = new CrimeDetectionService(
                crimeProbe, probe, player, _justice, _log, config.Justice);
            _hud = new JusticeHudWidget(_justice, hudRenderer, config.Justice, hudFeed);   // S21 v2: live feed
            _clinic = new ClinicService(
                player, recordStore, identity, notifier, _log, config.Justice, clock, input, vfxService);
            var hack = new PoliceDbHackService(
                wantedMonitor, recordStore, identity, warrant, _justice,
                notifier, _log, config.Justice, clock, vfxService, reputation, media);
            var stats = new JusticeStatsService(recordStore, identity, warrant, clock, config.Justice, reputation);
            _crowd = new CrowdReactionService(player, probe, identity, reputation, notifier, _log);

            stats.AttachJusticeProbes(() => _justice?.IsOnBail ?? false, () => _justice?.ParoleDaysLeft ?? 0);

            _menu = new PowerMenuService(
                menuFramework, _timeStop, teleport, vfxService,
                input, player, notifier, _log, config, store,
                hack: hack, stats: stats, feedProvider: () => media.Feed,
                cutsceneActive: () => _cutscene?.IsActive ?? false,
                hud: _hud);
            _menu.BuildMenu();

            CreateClinicBlip();

            Tick += OnTick;
            _log.Info($"Chrono initialized — menu key {config.MenuKey}");
            notifier.Show(string.Format(UiStrings.FirstRun, config.MenuKey));
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
            _crimeProbe?.Poll();                 // S20: 200ms world snapshots

            // S22 (user UAT: "this mod makes a mess on main story events"):
            // during scripted missions the justice pipeline FREEZES — story
            // missions own the wanted level. The menu + superpowers keep
            // working; the widget announces the standby.
            // S22 v3 (user UAT: "people run because of me during the story"):
            // the standby signal covers scripted story time — missions AND
            // cutscenes (some walk-ins/intros run with the mission flag unset).
            bool justiceOn = _config?.ModEnabled == true && _config.JusticeEnabled == true;
            bool storyTime = _config != null && _config.Justice.PauseDuringMissions == true
                && (Game.IsMissionActive || Game.IsCutsceneActive);
            if (storyTime && !_wasMissionActive)
            {
                _cutscene?.Abort();              // mission takeover — drop our camera/anim
                _log?.Info("Story sequence active — justice on standby");
            }
            _wasMissionActive = storyTime;
            if (_justice != null) _justice.MissionStandby = storyTime;
            if (_crowd != null) _crowd.Standby = storyTime || !justiceOn;   // crowd = justice world sim

            // S22 v2 (user UAT: "the toggle on/off didn't work"): the widget
            // must reflect the toggles — Mod OFF hides it, Justice OFF suspends.
            if (_hud != null)
            {
                _hud.ModOff = _config?.ModEnabled == false;
                _hud.JusticeOff = !_hud.ModOff && _config?.JusticeEnabled == false;
            }

            if (justiceOn && !storyTime)
            {
                _crimeDetection?.Tick(_clock.ElapsedMilliseconds);
                _justice?.Tick();
                _cutscene?.Tick(_clock.ElapsedMilliseconds);
            }
            _hud?.Tick();                        // S21: persistent justice widget (shows standby)
            _clinic?.Tick();
            _crowd?.Tick(_clock.ElapsedMilliseconds);
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
