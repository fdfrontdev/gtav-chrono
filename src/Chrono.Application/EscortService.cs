using System;
using System.Diagnostics;
using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// S22 v8 — police escort ride orchestration (reverse-engineered from the
/// Prison Mod's "full ride"): when custody begins, the cuffed player rides to
/// Bolingbroke in a police cruiser instead of a loading-screen teleport.
/// The ride is skippable (press E). Arrival (or skip, or TIMEOUT — S22 v8 r2)
/// hands off to the intake.
/// </summary>
public sealed class EscortService
{
    private readonly IEscortBoundary _boundary;
    private readonly IPlayerContext _player;
    private readonly IGameInput _input;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly Stopwatch _clock = Stopwatch.StartNew();   // S22 v8 r2: timeout watchdog

    private bool _active;
    private Vector3 _destination;
    private bool _arrivalAnnounced;
    private long _rideStartMs;   // S22 v8 r2: timeout watchdog
    private readonly JusticeConfig _config;

    /// <summary>Bolingbroke Penitentiary front gate — where the ride ends.</summary>
    public static readonly Vector3 BolingbrokeGate = new(1844.95f, 2607.35f, 45.57f);

    public EscortService(
        IEscortBoundary boundary,
        IPlayerContext player,
        IGameInput input,
        INotifier notifier,
        ILogSink log,
        JusticeConfig? config = null)   // S22 v8 r2: timeout seconds
    {
        _boundary = boundary;
        _player = player;
        _input = input;
        _notifier = notifier;
        _log = log;
        _config = config ?? new JusticeConfig();
    }

    /// <summary>True while the escort ride is running (widget/feed can announce it).</summary>
    public bool IsActive => _active;

    /// <summary>Start the ride: warp to the back seat, drive to Bolingbroke.</summary>
    public void Begin(Vector3 destination)
    {
        if (_active) return;
        _destination = destination;
        _arrivalAnnounced = false;
        _rideStartMs = _clock.ElapsedMilliseconds;   // S22 v8 r2: timeout watchdog
        _boundary.Begin(_player.Position, destination);
        _active = _boundary.IsRiding;
        if (_active)
        {
            _notifier.Show($"TRANSPORT — escorted to Bolingbroke (press {_config.InteractKey} to skip)");
            _log.Info("Escort ride begun");
        }
        else
        {
            _notifier.Show("TRANSPORT — police cruiser unavailable, direct to booking");
        }
    }

    /// <summary>Drive the ride — call every tick while <see cref="IsActive"/>.</summary>
    public void Tick()
    {
        if (!_active) return;

        // S23: the boundary watchdog reasserts the custody suppression and
        // re-seats a bailed driver every tick (user UAT: officer got out).
        _boundary.Tick();

        // Skip: the interact key during the ride → cut to intake NOW
        // (S22 v8 r2: skip must END the ride, not just flag it — otherwise
        // the player waits for arrival/timeout anyway).
        if (_input != null && _input.IsInteractKeyJustPressed)
        {
            _boundary.Skip();
            _notifier.Show("Arrived at Bolingbroke (skipped the ride)");
            _active = false;
            _boundary.End();
            return;
        }

        // S22 v8 r2 (user UAT r39: "court reached 0:00, nothing happened"):
        // TIMEOUT watchdog — if the ride hasn't finished (broken driver AI,
        // stuck cruiser), force-complete it so the booking + verdict ALWAYS
        // proceed. The verdict can never be held forever.
        if (_config.EscortTimeoutSeconds > 0
            && _clock.ElapsedMilliseconds - _rideStartMs > _config.EscortTimeoutSeconds * 1000L)
        {
            _log.Info("Escort ride timed out — proceeding to booking");
            _notifier.Show("ARRIVED — Bolingbroke Penitentiary");
            _active = false;
            _boundary.End();
            return;
        }

        if (_boundary.IsRiding && _boundary.HasArrived(_destination))
        {
            if (!_arrivalAnnounced)
            {
                _arrivalAnnounced = true;
                _notifier.Show("ARRIVED — Bolingbroke Penitentiary");
            }
            _active = false;
            _boundary.End();
            return;
        }

        // Fallback: the ride broke (cruiser/driver despawned) — finish anyway
        if (!_boundary.IsRiding)
        {
            _log.Info("Escort ride ended unexpectedly — proceeding to intake");
            _active = false;
        }
    }

    /// <summary>Force-finish (e.g. mission standby aborted it). Idempotent.</summary>
    public void Abort()
    {
        if (!_active) return;
        _active = false;
        _boundary.End();
        _log.Info("Escort ride aborted");
    }

    /// <summary>
    /// S22 v8 r2 (user UAT r40: "court still stuck"): the COURT DATE is the
    /// hard deadline — when the clock hits 0:00 the ride is over, cut to
    /// booking. Idempotent.
    /// </summary>
    public void ForceComplete()
    {
        if (!_active) return;
        _active = false;
        _boundary.End();
        _notifier.Show("ARRIVED — Bolingbroke Penitentiary");
        _log.Info("Escort ride force-completed (court date due)");
    }
}
