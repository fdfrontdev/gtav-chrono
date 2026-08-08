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

        // NPCs cannot target/lock an invisible player without direct line of sight
        // (v0.8.0 — stronger than alpha+ignore; SHVDN has no SET_PED_CAN_BE_TARGETED)
        Function.Call(Hash.SET_PED_CAN_BE_TARGETED_WITHOUT_LOS, ped.Handle, visible);
    }

    // --- animation (v0.4.0) ---

    public void SetHeading(float headingDegrees)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        ped.Heading = headingDegrees;
    }

    public void PlayLoopedAnimation(string dict, string anim)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        ped.Task.PlayAnimation(dict, anim, 8f, -1, AnimationFlags.Loop);
    }

    public void PlayAnimationOnce(string dict, string anim, int durationMs)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        ped.Task.PlayAnimation(dict, anim, 8f, durationMs, AnimationFlags.None);
    }

    public void ClearCurrentAnimation()
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        ped.Task.ClearAllImmediately();
    }

    public void PlaceOnGround()
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        var pos = ped.Position;
        if (World.GetGroundHeightAndNormal(pos, out float ground, out _))
        {
            // Snap down to terrain if hovering above it (kills the falling/parachute pose)
            if (pos.Z > ground + 0.5f)
                ped.Position = new GTA.Math.Vector3(pos.X, pos.Y, ground);
        }
    }

    public void SetNpcAwareness(bool enabled)
    {
        // Realistic reactions: while disabled, NPCs/police cannot perceive or track
        // the player (no instant "superpower instinct" tracking after a teleport).
        Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, !enabled);
        Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, Game.Player.Handle, !enabled);
    }

    public bool IsVisible
    {
        get
        {
            var ped = Game.Player.Character;
            return ped != null && ped.Exists() && ped.IsVisible;
        }
    }

    public string GetDistrictName()
    {
        try
        {
            var ped = Game.Player.Character;
            if (ped == null || !ped.Exists()) return "San Andreas";
            var pos = ped.Position;
            var zone = Function.Call<string>(Hash.GET_NAME_OF_ZONE, pos.X, pos.Y, pos.Z);
            return string.IsNullOrWhiteSpace(zone) ? "San Andreas" : zone;
        }
        catch
        {
            return "San Andreas";
        }
    }
}
