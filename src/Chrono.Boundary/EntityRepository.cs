using System.Collections.Generic;
using Chrono.Application.Ports;
using Chrono.Domain;
using GTA;

namespace Chrono.Boundary;

/// <summary>World entity enumeration mapped to game-neutral <see cref="GameEntity"/> (DLD §3).</summary>
public sealed class EntityRepository : IEntityRepository
{
    public IReadOnlyList<GameEntity> GetAllPeds()
        => ToEntities(World.GetAllPeds(null), EntityKind.Ped);

    public IReadOnlyList<GameEntity> GetAllVehicles()
        => ToEntities(World.GetAllVehicles(null), EntityKind.Vehicle);

    public IReadOnlyList<GameEntity> GetAllProps()
        => ToEntities(World.GetAllProps(null), EntityKind.Prop);

    private static List<GameEntity> ToEntities(Entity[] entities, EntityKind kind)
    {
        var result = new List<GameEntity>(entities.Length);
        foreach (var e in entities)
            if (e.Exists()) result.Add(new GameEntity(e.Handle, kind));
        return result;
    }
}
