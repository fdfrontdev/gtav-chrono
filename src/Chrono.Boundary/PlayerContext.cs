using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>Player state, aim direction, waypoint, teleport and v0.3.0 powers (DLD §3).</summary>
public sealed class PlayerContext : IPlayerContext
{
    public int PlayerHandle => Game.Player.Character?.Handle ?? 0;
    public int? PlayerVehicleHandle => Game.Player.Character?.CurrentVehicle?.Handle;
    public Vector3 Position => EntityFreezer.ToNumerics(Game.Player.Character.Position);
    public float Heading => Game.Player.Character.Heading;
    public bool IsAiming => Game.Player.IsAiming;
    public bool IsInVehicle => Game.Player.Character?.CurrentVehicle != null;

    public Vector3 GetAimDirection()
        => EntityFreezer.ToNumerics(GameplayCamera.Direction);

    public bool IsWaypointActive()
        => Game.IsWaypointActive;

    public Vector3 GetWaypointPosition()
        => EntityFreezer.ToNumerics(World.WaypointPosition);

    public void Teleport(Vector3 position)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;

        var vehicle = ped.CurrentVehicle;
        if (vehicle != null && vehicle.Exists())
        {
            vehicle.Position = EntityFreezer.ToGta(position);   // teleport vehicle with player (FR-3.4)
            Function.Call(Hash.SET_VEHICLE_ON_GROUND_PROPERLY, vehicle.Handle);
        }
        else
        {
            ped.Position = EntityFreezer.ToGta(position);
        }

        // Kill the default "falling/parachute → land" teleport pose (user report v0.3.0)
        Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, ped.Handle);
    }

    // --- v0.3.0 powers ---

    public void SetVelocity(Vector3 velocity)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        ped.Velocity = EntityFreezer.ToGta(velocity);
    }

    public void SetGravityEnabled(bool enabled)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        Function.Call(Hash.SET_ENTITY_HAS_GRAVITY, ped.Handle, enabled);
    }

    public void SetRagdollEnabled(bool enabled)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        ped.CanRagdoll = enabled;
    }

    public void SetInvincible(bool enabled)
    {
        var ped = Game.Player.Character;
        if (ped != null && ped.Exists()) ped.IsInvincible = enabled;
        Game.Player.IsInvincible = enabled;
    }

    public void RefillHealth()
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        ped.Health = ped.MaxHealth;
    }

    public void SetVisible(bool visible)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;

        ped.IsVisible = visible;
        if (visible) Function.Call(Hash.RESET_ENTITY_ALPHA, ped.Handle);
        else Function.Call(Hash.SET_ENTITY_ALPHA, ped.Handle, 0, false);
    }
}
