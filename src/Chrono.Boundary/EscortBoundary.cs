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

    public bool IsRiding => _cruiser != null && _cruiser.Exists() && _driver != null && _driver.Exists();

    public void Begin(Vector3 playerPosition, Vector3 destination)
    {
        End();   // idempotent — never two cruisers

        try
        {
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

            // Player into the rear seat (seat index 2 = rear left) — cuffed, no control
            var playerPed = Game.Player.Character;
            if (playerPed != null && playerPed.Exists())
            {
                Function.Call(Hash.SET_PED_INTO_VEHICLE, playerPed.Handle, _cruiser.Handle, 2);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, playerPed.Handle, 0, false);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, playerPed.Handle, 32, false);   // unable to fight
            }
            Game.Player.SetControlState(false, SetPlayerControlFlags.AllowPlayerDamage | SetPlayerControlFlags.DontStopOtherCarsAroundPlayer);

            // Driver: route to Bolingbroke — long-range drive task, max speed, arrive exactly
            Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE,
                _driver.Handle, _cruiser.Handle,
                destination.X, destination.Y, destination.Z,
                18f, 786603, 10f);   // speed 18 m/s (~65 km/h — a lawful prison run), driving flags, radius

            _log?.Info("Escort ride started → Bolingbroke");
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
