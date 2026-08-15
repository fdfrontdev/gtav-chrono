using System;
using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// S22 v8 — police escort ride boundary (reverse-engineered from the Prison
/// Mod's "full ride": arrest → police car → drive to Bolingbroke → intake).
/// Spawns a police cruiser + driver, warps the cuffed player into the back
/// seat, tasks the driver to route to the prison, and supports skipping.
/// </summary>
public sealed class EscortBoundary : IEscortBoundary
{
    private const string EscortDriverModel = "s_m_y_cop_01";
    private const string EscortVehicleModel = "police";
    private const float SpawnDistanceM = 15f;

    private Vehicle? _cruiser;
    private Ped? _driver;
    private bool _skipped;
    private Vector3 _destination;   // S23: kept for the driver re-task watchdog

    public bool IsRiding => _cruiser != null && _cruiser.Exists() && _driver != null && _driver.Exists();

    public void Begin(Vector3 playerPosition, Vector3 destination)
    {
        End();   // idempotent — never two cruisers

        try
        {
            _destination = destination;   // S23: watchdog needs the route

            // S23 (user UAT 2026-08-13: "police still shooting me even though
            // I'm already in custody"): the arrest chase must END here — flush
            // the wanted level (pending crime memory included) and make police
            // + civilians ignore the cuffed suspect for the ride's duration.
            Game.Player.Wanted.SetWantedLevel(0, false);
            Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);
            Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, true);
            Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, Game.Player.Handle, true);

            // Spawn the cruiser just ahead of the arrest point (off the player's nose)
            var heading = Game.Player.Character?.Heading ?? 0f;
            var spawn = playerPosition + new Vector3(
                (float)Math.Sin(heading * Math.PI / 180f) * SpawnDistanceM,
                (float)Math.Cos(heading * Math.PI / 180f) * SpawnDistanceM, 0f);

            _cruiser = World.CreateVehicle(new Model(EscortVehicleModel), EntityFreezer.ToGta(spawn), heading);
            if (_cruiser == null || !_cruiser.Exists()) { _log?.Info("Escort: cruiser spawn failed"); return; }
            _cruiser.Mods.PrimaryColor = VehicleColor.MetallicBlack;
            _cruiser.Mods.SecondaryColor = VehicleColor.MetallicWhite;

            _driver = World.CreatePed(new Model(EscortDriverModel), EntityFreezer.ToGta(spawn + new Vector3(2f, 0f, 0f)), heading);
            if (_driver == null || !_driver.Exists()) { _log?.Info("Escort: driver spawn failed"); End(); return; }
            _driver.IsPersistent = true;

            _skipped = false;

            // Driver into the DRIVER seat FIRST (seat -1) — the S22 v8 r2 bug
            // (user UAT r39 screenshot: "police go out, not drive"): the drive
            // task was issued to a ped standing BESIDE the car, which GTA's AI
            // can't honor — the officer walked away and the ride never moved.
            Function.Call(Hash.SET_PED_INTO_VEHICLE, _driver.Handle, _cruiser.Handle, -1);
            // S23: the driver must not drop the route for ambient events (e.g.
            // a nearby gunfight from the leftover chase) — block non-temporary
            // event interruption so the escort task sticks.
            Function.Call(Hash.TASK_SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _driver.Handle, true);
            // Then the long-range drive task: ~108 km/h — an escort runs hot
            // (the lawful 65 km/h made the cross-city trip an eternity).
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                _driver.Handle, _cruiser.Handle,
                destination.X, destination.Y, destination.Z,
                30f, 786603, 10f);   // speed 30 m/s, driving flags, arrival radius

            // Player into the rear seat (seat index 2 = rear left) — cuffed, no control
            var playerPed = Game.Player.Character;
            if (playerPed != null && playerPed.Exists())
            {
                Function.Call(Hash.SET_PED_INTO_VEHICLE, playerPed.Handle, _cruiser.Handle, 2);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, playerPed.Handle, 0, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, playerPed.Handle, 32, false);   // unable to fight
            }
            Game.Player.SetControlState(false, SetPlayerControlFlags.AllowPlayerDamage | SetPlayerControlFlags.DontStopOtherCarsAroundPlayer);

            _log?.Info("Escort ride started → Bolingbroke (driver seated, en route)");
        }
        catch (Exception ex)
        {
            _log?.Error($"Escort begin failed: {ex.Message}");
            End();
        }
    }

    /// <summary>True when the cruiser has ARRIVED (driver finished the route or got close).</summary>
    public bool HasArrived(Vector3 destination, float arrivalRadiusM = 20f)
    {
        if (!IsRiding) return false;
        var pos = _cruiser!.Position;
        return Vector3.Distance(EntityFreezer.ToNumerics(pos), destination) <= arrivalRadiusM;
    }

    /// <summary>
    /// S23 — per-tick watchdog (user UAT 2026-08-13: "the officer that should
    /// drive got out of the car, the others still shoot me"): reassert the
    /// custody suppression and, if the driver AI bailed (left the cruiser),
    /// re-seat him and re-issue the drive task so the ride always completes.
    /// </summary>
    public void Tick()
    {
        if (!IsRiding) return;

        Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, true);
        Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, Game.Player.Handle, true);
        Game.Player.Wanted.SetWantedLevel(0, false);
        Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);

        // Driver bailed (walked away from the car)? Put him back + re-issue the route.
        if (!Function.Call<bool>(Hash.IS_PED_IN_VEHICLE, _driver!.Handle, _cruiser!.Handle, false))
        {
            Function.Call(Hash.SET_PED_INTO_VEHICLE, _driver.Handle, _cruiser.Handle, -1);
            Function.Call(Hash.TASK_SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, _driver.Handle, true);
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                _driver.Handle, _cruiser.Handle,
                _destination.X, _destination.Y, _destination.Z,
                30f, 786603, 10f);
            _log?.Info("Escort watchdog: driver re-seated + re-tasked to Bolingbroke");
        }
    }

    public void Skip()
    {
        if (!IsRiding) return;
        _skipped = true;
        _log?.Info("Escort ride skipped by player");
    }

    public bool WasSkipped => _skipped;

    public void End()
    {
        try
        {
            Game.Player.SetControlState(true, SetPlayerControlFlags.AllowPlayerDamage | SetPlayerControlFlags.DontStopOtherCarsAroundPlayer);
            if (_driver != null && _driver.Exists()) Function.Call(Hash.CLEAR_PED_TASKS, _driver.Handle);
            if (_cruiser != null && _cruiser.Exists()) _cruiser.Delete();
            if (_driver != null && _driver.Exists()) _driver.Delete();
        }
        catch (Exception ex)
        {
            _log?.Error($"Escort end failed: {ex.Message}");
        }
        finally
        {
            _cruiser = null;
            _driver = null;
        }
    }

    private readonly ILogSink? _log;

    public EscortBoundary(ILogSink? log = null) => _log = log;
}
