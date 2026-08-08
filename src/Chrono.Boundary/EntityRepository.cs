using System;
using System.Collections.Generic;
using Chrono.Application.Ports;
using Chrono.Domain;
using GTA;

namespace Chrono.Boundary;

/// <summary>World entity enumeration mapped to game-neutral <see cref="GameEntity"/> (DLD §3).</summary>
public sealed class EntityRepository : IEntityRepository
{
    // NOTE: SHVDN 3.9's GetAllXxx(Model[]) throws ArgumentNullException on null —
    // an EMPTY array means "no filter" (verified via chrono.log stack trace, v0.1.1).
    private static readonly Model[] NoFilter = Array.Empty<Model>();

    public IReadOnlyList<GameEntity> GetAllPeds()
        => ToEntities(World.GetAllPeds(NoFilter), EntityKind.Ped);

    public IReadOnlyList<GameEntity> GetAllVehicles()
        => ToEntities(World.GetAllVehicles(NoFilter), EntityKind.Vehicle);

    public IReadOnlyList<GameEntity> GetAllProps()
        => ToEntities(World.GetAllProps(NoFilter), EntityKind.Prop);

    private static List<GameEntity> ToEntities(Entity[] entities, EntityKind kind)
    {
        var result = new List<GameEntity>(entities.Length);
        foreach (var e in entities)
            if (e.Exists())
                result.Add(new GameEntity(e.Handle, kind, EntityFreezer.ToNumerics(e.Position)));
        return result;
    }
}
