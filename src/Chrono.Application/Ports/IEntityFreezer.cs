using Chrono.Domain;

namespace Chrono.Application.Ports;

/// <summary>Freeze/restore primitives for a single entity (implemented by the SHVDN boundary).</summary>
public interface IEntityFreezer
{
    bool Exists(GameEntity entity);
    FreezeSnapshot Snapshot(GameEntity entity);
    void Freeze(GameEntity entity, FreezeSnapshot snapshot);
    void Restore(GameEntity entity, FreezeSnapshot snapshot);
}
