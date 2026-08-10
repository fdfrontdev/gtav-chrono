using System;
using System.Numerics;
using Chrono.Application.Ports;

namespace Chrono.Application;

/// <summary>
/// S22 v8 — police escort ride orchestration (reverse-engineered from the
/// Prison Mod's "full ride"): when custody begins, the cuffed player rides to
/// Bolingbroke in a police cruiser instead of a loading-screen teleport.
/// The ride is skippable (press E). Arrival (or skip) hands off to the intake.
/// </summary>
public sealed class EscortService
{
    private readonly IEscortBoundary _boundary;
    private readonly IPlayerContext _player;
    private readonly IGameInput _input;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;

    private bool _active;
    private Vector3 _destination;
    private bool _arrivalAnnounced;

    /// <summary>Bolingbroke Penitentiary front gate — where the ride ends.</summary>
    public static readonly Vector3 BolingbrokeGate = new(1844.95f, 2607.35f, 45.57f);

    public EscortService(
        IEscortBoundary boundary,
        IPlayerContext player,
        IGameInput input,
        INotifier notifier,
        ILogSink log)
    {
        _boundary = boundary;
        _player = player;
        _input = input;
        _notifier = notifier;
        _log = log;
    }

    /// <summary>True while the escort ride is running (widget/feed can announce it).</summary>
    public bool IsActive => _active;

    /// <summary>Start the ride: warp to the back seat, drive to Bolingbroke.</summary>
    public void Begin(Vector3 destination)
    {
        if (_active) return;
        _destination = destination;
        _arrivalAnnounced = false;
        _boundary.Begin(_player.Position, destination);
        _active = _boundary.IsRiding;
        if (_active)
        {
            _notifier.Show("TRANSPORT — escorted to Bolingbroke (press E to skip)");
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

        // Skip: E during the ride → cut to intake
        if (_input != null && _input.IsInteractKeyJustPressed)
        {
            _boundary.Skip();
            _notifier.Show("Arrived at Bolingbroke (skipped the ride)");
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
}
