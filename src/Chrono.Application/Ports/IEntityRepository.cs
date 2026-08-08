using System.Collections.Generic;
using Chrono.Domain;

namespace Chrono.Application.Ports;

/// <summary>Game-boundary-neutral access to world entities (never exposes SHVDN types).</summary>
public interface IEntityRepository
{
    IReadOnlyList<GameEntity> GetAllPeds();
    IReadOnlyList<GameEntity> GetAllVehicles();
    IReadOnlyList<GameEntity> GetAllProps();
}
