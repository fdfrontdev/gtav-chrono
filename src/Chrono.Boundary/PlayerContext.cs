using System;
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
    public bool HasWeapon => Function.Call<bool>(Hash.IS_PED_ARMED, Game.Player.Character.Handle, 7);
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

    // S23 (user UAT 2026-08-13): custody/prison — the street chase is over.
    // Police stop targeting the player and civilians stop calling it in.
    // Restored on release/escape so the manhunt can engage normally.
    public void SetLawEnforcementIgnore(bool enabled)
    {
        Function.Call(Hash.SET_POLICE_IGNORE_PLAYER, Game.Player.Handle, enabled);
        Function.Call(Hash.SET_EVERYONE_IGNORE_PLAYER, Game.Player.Handle, enabled);
    }

    public bool IsVisible
    {
        get
        {
            var ped = Game.Player.Character;
            return ped != null && ped.Exists() && ped.IsVisible;
        }
    }

    public string GetCharacterName()
    {
        try
        {
            // Game.Player.Name = the current character's name (Franklin/Michael/Trevor)
            string name = Game.Player.Name;
            return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
        }
        catch
        {
            return "Unknown";   // flavor — never a crash vector
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

    public void AddMoney(int delta) => Game.Player.Money += delta;

    public int GetMoney() => Game.Player.Money;

    public bool IsDead => Game.Player.IsDead;

    public void SetControlEnabled(bool enabled)
        => Game.Player.SetControlState(enabled, SetPlayerControlFlags.None);

    // ── v0.10 survivor needs (SRS FR-C4..C7) — cosmetic/balance natives,
    // all guarded: a missing native must never crash the mod. ──

    public void SetHealthRechargeMultiplier(float multiplier)
    {
        try { Function.Call(Hash.SET_PLAYER_HEALTH_RECHARGE_MULTIPLIER, Game.Player.Handle, multiplier); }
        catch { /* cosmetic — ignore */ }
    }

    public void SetRunSpeedMultiplier(float multiplier)
    {
        try
        {
            // SET_PED_MOVE_RATE_OVERRIDE — the real movement-speed multiplier native.
            // (FIX 2026-08-16: old code hardcoded 0x3B3CAD6166916D87 labelled
            // "SET_RUN_SPEED_MULTIPLIER" — that native does NOT exist in GTA V; the hash
            // actually maps to PRELOAD_SCRIPT_CONVERSATION (AUDIO), so the speed effect
            // silently never applied. Verified: enum 0x085BF80FA50A39D1 == legacy == gen9.)
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, Game.Player.Handle, multiplier);
        }
        catch { /* cosmetic — ignore */ }
    }

    public void SetDrunkVisual(bool enabled)
    {
        try
        {
            var ped = Game.Player.Character;
            if (ped != null && ped.Exists())
                Function.Call(Hash.SET_PED_IS_DRUNK, ped.Handle, enabled);
        }
        catch { /* cosmetic — ignore */ }
    }

    public void ApplyHealthDamage(float amount)
    {
        try
        {
            var ped = Game.Player.Character;
            if (ped == null || !ped.Exists() || ped.IsDead) return;
            ped.Health = (int)Math.Max(1, ped.Health - amount);
        }
        catch { /* never a crash vector */ }
    }

    public bool IsOutdoors()
    {
        try
        {
            var pos = Game.Player.Character?.Position;
            if (pos == null) return true;
            // GET_INTERIOR_AT_COORDS 0xB0F7F8663821D9C3 — verified in nativedb legacy AND gen9
            // (FIX 2026-08-16: old code used 0xB0F7A866A4B0E1E4 which exists in NO native table —
            // SHV "FATAL: Can't find native" → critical error popup every launch since v0.13.)
            return Function.Call<int>((Hash)0xB0F7F8663821D9C3, pos.Value.X, pos.Value.Y, pos.Value.Z) == 0;
        }
        catch { return true; }   // fail-open: no interior data → treat as outdoors
    }
}
