using System;
using System.Collections.Generic;
using System.Linq;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Time Stop use case (SRS FR-2, ADR-02). Snapshot+freeze with per-tick batching,
/// maintenance sweep for late spawns, exact restore. Player and player vehicle excluded.
/// </summary>
public sealed class TimeStopService
{
    private const int BatchSize = 100;

    private readonly IEntityRepository _repo;
    private readonly IEntityFreezer _freezer;
    private readonly IGameClock _clock;
    private readonly IPlayerContext _player;
    private readonly INotifier _notifier;
    private readonly ILogSink _log;
    private readonly TimeStopConfig _config;

    private readonly List<FreezeSnapshot> _frozen = new();
    private readonly Queue<GameEntity> _pending = new();
    private readonly HashSet<int> _knownHandles = new();
    private long _lastSweepMs;
    private bool _capNotified;

    public TimeStopService(
        IEntityRepository repo,
        IEntityFreezer freezer,
        IGameClock clock,
        IPlayerContext player,
        INotifier notifier,
        ILogSink log,
        TimeStopConfig config)
    {
        _repo = repo;
        _freezer = freezer;
        _clock = clock;
        _player = player;
        _notifier = notifier;
        _log = log;
        _config = config;
    }

    public bool IsActive { get; private set; }
    public bool IsFreezingInProgress => _pending.Count > 0;
    public int FrozenCount => _frozen.Count;

    /// <summary>Activate Time Stop: collect eligible entities, queue for batched freeze, pause clock.</summary>
    public void Activate()
    {
        if (IsActive) return;

        _frozen.Clear();
        _pending.Clear();
        _knownHandles.Clear();

        foreach (var entity in CollectEligibleEntities())
        {
            _knownHandles.Add(entity.Handle);
            _pending.Enqueue(entity);
        }

        IsActive = true;
        _lastSweepMs = 0;
        _capNotified = false;

        if (_config.PauseClock) _clock.Pause();
        _log.Info($"TimeStop activated — {_pending.Count} entities queued");
    }

    /// <summary>Deactivate: restore all frozen entities in batches, resume clock.</summary>
    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        _pending.Clear();

        foreach (var snapshot in _frozen)
        {
            RestoreEntity(snapshot);
        }
        _frozen.Clear();
        _knownHandles.Clear();

        if (_config.PauseClock) _clock.Resume();
        _log.Info("TimeStop deactivated");
    }

    /// <summary>Per-tick maintenance: drain freeze queue; sweep for late-spawning entities.</summary>
    public void Tick(long nowMs)
    {
        if (!IsActive) return;

        DrainFreezeQueue();
        RunMaintenanceSweep(nowMs);
    }

    private void DrainFreezeQueue()
    {
        int processed = 0;
        while (_pending.Count > 0 && processed < BatchSize)
        {
            var entity = _pending.Dequeue();
            processed++;

            if (!_freezer.Exists(entity)) continue;          // entity died before freezing
            if (entity.Handle == _player.PlayerHandle) continue;
            if (entity.Handle == _player.PlayerVehicleHandle) continue;

            try
            {
                var snapshot = _freezer.Snapshot(entity);
                _freezer.Freeze(entity, snapshot);
                _frozen.Add(snapshot);
            }
            catch (Exception ex)
            {
                _log.Warn($"Freeze failed for entity {entity.Handle}: {ex.Message}");
            }
        }
    }

    private void RunMaintenanceSweep(long nowMs)
    {
        if (_config.MaintenanceIntervalMs <= 0) return;
        if (nowMs - _lastSweepMs < _config.MaintenanceIntervalMs) return;
        _lastSweepMs = nowMs;

        if (_frozen.Count >= _config.MaxFrozenEntities)
        {
            if (!_capNotified)
            {
                _capNotified = true;
                _notifier.Show(UiStrings.TimeStopCapped);
            }
            _log.Warn("Freeze cap reached — skipping new entities");
            return;
        }

        int added = 0;
        foreach (var entity in CollectEligibleEntities())
        {
            if (_knownHandles.Contains(entity.Handle)) continue;
            if (_frozen.Count + added >= _config.MaxFrozenEntities) break;

            _knownHandles.Add(entity.Handle);
            _pending.Enqueue(entity);
            added++;
        }

        if (added > 0) _log.Debug($"Maintenance sweep captured {added} new entities");
    }

    private IEnumerable<GameEntity> CollectEligibleEntities()
    {
        var config = new ChronoConfig { TimeStop = _config }; // reuse policy with same settings
        var result = new List<GameEntity>();
        var center = _player.Position;
        float radius = _config.FreezeRadius;

        foreach (var e in _repo.GetAllPeds())
            if (FreezePolicy.CanFreeze(e.Kind, config) && e.Handle != _player.PlayerHandle
                && e.IsWithinRadius(center, radius)) result.Add(e);
        foreach (var e in _repo.GetAllVehicles())
            if (FreezePolicy.CanFreeze(e.Kind, config) && e.Handle != _player.PlayerVehicleHandle
                && e.IsWithinRadius(center, radius)
                && !e.IsAirborne)   // airborne vehicles (planes/heli) crash on freeze-restore (v0.4.0)
                result.Add(e);
        foreach (var e in _repo.GetAllProps())
            if (FreezePolicy.CanFreeze(e.Kind, config) && e.IsWithinRadius(center, radius)) result.Add(e);

        return result;
    }

    private void RestoreEntity(FreezeSnapshot snapshot)
    {
        var entity = new GameEntity(snapshot.Handle, snapshot.Kind);
        if (!_freezer.Exists(entity))
        {
            _log.Warn($"Restore skipped — entity {snapshot.Handle} no longer exists");
            return;
        }

        try
        {
            _freezer.Restore(entity, snapshot);
        }
        catch (Exception ex)
        {
            _log.Warn($"Restore failed for entity {snapshot.Handle}: {ex.Message}");
        }
    }
}
