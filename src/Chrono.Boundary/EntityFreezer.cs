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

        // Pin ONLY peds as mission entities to prevent streaming despawn during the
        // freeze. Props and vehicles are NOT pinned — mission-flagging hundreds of
        // ambient props caused a hard game crash on release (v0.5.0 incident).
        if (entity.Kind == EntityKind.Ped)
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, entity.Handle, true, true);
    }

    public void Restore(GameEntity entity, FreezeSnapshot snapshot)
    {
        var e = Entity.FromHandle(entity.Handle);
        if (e == null || !e.Exists()) return;
        e.Position = ToGta(snapshot.Position);
        e.Rotation = ToGta(snapshot.Rotation);
        e.Velocity = ToGta(snapshot.Velocity);
        e.IsPositionFrozen = snapshot.WasFrozen;

        // Release the pin (peds only) with p2=true — the correct un-mission call.
        // NOTE: SET_PED_AS_NO_LONGER_NEEDED is deliberately NOT used here — bulk
        // no-longer-needed right after unpinning crashed the game (v0.5.0 incident).
        if (entity.Kind == EntityKind.Ped)
        {
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, entity.Handle, false, true);
            Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, entity.Handle);
        }
        else if (entity.Kind == EntityKind.Vehicle)
        {
            int driver = Function.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, entity.Handle, -1);
            if (driver != 0) Function.Call(Hash.CLEAR_PED_TASKS_IMMEDIATELY, driver);
        }
    }

    internal static Vector3 ToNumerics(GTA.Math.Vector3 v) => new(v.X, v.Y, v.Z);
    internal static GTA.Math.Vector3 ToGta(Vector3 v) => new(v.X, v.Y, v.Z);
}
