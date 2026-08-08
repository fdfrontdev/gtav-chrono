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
        // Pin as mission entity — prevents the game streaming out frozen NPCs/vehicles
        // (root cause of "entities missing after resume", v0.3.0)
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
        // Release the mission pin so entities can be streamed normally again
        Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, entity.Handle, false, false);
    }

    internal static Vector3 ToNumerics(GTA.Math.Vector3 v) => new(v.X, v.Y, v.Z);
    internal static GTA.Math.Vector3 ToGta(Vector3 v) => new(v.X, v.Y, v.Z);
}
