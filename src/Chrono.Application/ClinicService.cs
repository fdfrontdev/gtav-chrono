using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Plastic surgery clinic (FR-5): a physical location at Mount Zonah (Pillbox Hill).
/// Standing at the door prompts "press G"; the surgery gives a NEW FACE → identity
/// Clean — the criminal RECORD is untouched (surgery changes appearance, not history).
/// Cost scales with the record; 1 in-game day cooldown (config).
/// </summary>
public sealed class ClinicService
{
    // Mount Zonah Medical Center (Pillbox Hill) — clinic door
    public static readonly Vector3 ClinicDoor = new(294f, -582f, 26f);
    private const float InteractRadiusM = 4f;

    private readonly IPlayerContext _player;
    private readonly IRecordStore _store;
    private readonly IdentityService _identity;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly JusticeConfig _config;
    private readonly IGameClock _clock;
    private readonly IGameInput _input;
    private readonly VfxService? _vfx;
    private bool _promptShown;

    public ClinicService(
        IPlayerContext player,
        IRecordStore store,
        IdentityService identity,
        INotifier notifier,
        ILogSink log,
        JusticeConfig config,
        IGameClock clock,
        IGameInput input,
        VfxService? vfx = null)
    {
        _player = player;
        _store = store;
        _identity = identity;
        _notifier = notifier;
        _log = log;
        _config = config;
        _clock = clock;
        _input = input;
        _vfx = vfx;
    }

    /// <summary>Per-tick: door proximity prompt + interact edge → surgery.</summary>
    public void Tick()
    {
        bool atDoor = (_player.Position - ClinicDoor).LengthSquared() <= InteractRadiusM * InteractRadiusM;

        if (!atDoor)
        {
            _promptShown = false;
            return;
        }

        if (_input.IsInteractKeyJustPressed)
        {
            TrySurgery();
            _promptShown = false;
        }
        else if (!_promptShown)
        {
            _promptShown = true;
            _notifier.Show("Chrono Clinic — press G for plastic surgery");
        }
    }

    /// <summary>Run the surgery. Pure flow (no location check — menu can trigger too).</summary>
    public bool TrySurgery()
    {
        var status = _store.LoadStatus();

        // Cooldown (FR-5.5)
        if (status.LastSurgeryDay > 0
            && status.LastSurgeryDay + _config.SurgeryCooldownDays > _clock.CurrentGameDay)
        {
            int wait = status.LastSurgeryDay + _config.SurgeryCooldownDays - _clock.CurrentGameDay;
            _notifier.Show($"Clinic is booked — come back in {wait} day(s)");
            return false;
        }

        // Cost scales with the record (FR-5.4): base + per-event
        int recordCount = _store.Load().Count;
        int cost = _config.ClinicBaseCost + _config.PerEventCost * recordCount;
        if (_player.GetMoney() < cost)
        {
            _notifier.Show($"Surgery costs ${cost} — you can't afford it");
            return false;
        }

        // Surgery
        _player.AddMoney(-cost);
        var profile = _store.LoadProfile();
        profile.RecordSurgery();
        _store.SaveProfileAtomic(profile);
        status.LastSurgeryDay = _clock.CurrentGameDay;
        _store.SaveStatusAtomic(status);
        _identity.SetClean();   // new face — record INTACT (FR-5.3)

        _vfx?.ScreenFadeOut(300);
        _vfx?.ScreenFlash(300);
        _vfx?.ScreenFadeIn(300);
        _notifier.Show("SURGERY COMPLETE — a new face. The record stays...");
        _log.Info($"Plastic surgery done (${cost}) — identity Clean, {recordCount} events on record");
        return true;
    }
}
