using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>Snapshot / freeze / restore primitives via SHVDN Entity API (ADR-02 §2.1).</summary>
public sealed class EntityFreezer : IEntityFreezer
{
    public bool Exists(GameEntity entity)
        => Entity.FromHandle(entity.Handle)?.Exists() ?? false;

    public FreezeSnapshot Snapshot(GameEntity entity)
    {
        var e = Entity.FromHandle(entity.Handle);
        return new FreezeSnapshot(
            entity.Handle,
            entity.Kind,
            ToNumerics(e.Position),
            ToNumerics(e.Rotation),
            ToNumerics(e.Velocity),
            e.IsPositionFrozen);
    }

    public void Freeze(GameEntity entity, FreezeSnapshot snapshot)
    {
        var e = Entity.FromHandle(entity.Handle);
        if (e == null || !e.Exists()) return;
        e.IsPositionFrozen = true;
        e.Velocity = ToGta(Vector3.Zero);

        if (entity.Kind == EntityKind.Ped)
        {
            // TASK-PRESERVING freeze: move-rate 0 keeps the ped's current AI task alive
            // (walking, bird flying, driving) so it RESUMES on restore — the missing piece
            // for "NPCs resume their usual activities" (v0.4.0/v0.5.0). Pin prevents
            // streaming despawn. Props/vehicles are NEVER pinned (v0.5.1 crash lesson).
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, entity.Handle, 0f);
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, entity.Handle, true, true);
        }
    }

    public void Restore(GameEntity entity, FreezeSnapshot snapshot)
    {
        var e = Entity.FromHandle(entity.Handle);
        if (e == null || !e.Exists()) return;
        e.Position = ToGta(snapshot.Position);
        e.Rotation = ToGta(snapshot.Rotation);
        e.Velocity = ToGta(snapshot.Velocity);
        e.IsPositionFrozen = snapshot.WasFrozen;

        if (entity.Kind == EntityKind.Ped)
        {
            // Resume the ORIGINAL task (move rate back to 1) — NO task clearing:
            // CLEAR_PED_TASKS left NPCs standing (v0.4.0/v0.5.0) and ejected vehicle
            // drivers (v0.5.0 user report).
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, entity.Handle, 1f);
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, entity.Handle, false, true);
        }
        else if (entity.Kind == EntityKind.Vehicle)
        {
            // Driver stays SEATED (no task clear). If the vehicle was moving when
            // frozen, nudge the driver to drive off — guarantees "cars resume driving".
            int driver = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, entity.Handle, -1);
            if (driver != 0 && snapshot.Velocity.LengthSquared() > 25f)   // was moving (>5 m/s)
                Function.Call(Hash.TASK_VEHICLE_DRIVE_WANDER, driver, entity.Handle, 20f, 786603);
        }
    }

    internal static Vector3 ToNumerics(GTA.Math.Vector3 v) => new(v.X, v.Y, v.Z);
    internal static GTA.Math.Vector3 ToGta(Vector3 v) => new(v.X, v.Y, v.Z);
}
