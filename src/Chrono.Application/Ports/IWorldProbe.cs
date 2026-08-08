using System.Numerics;
using Chrono.Domain;

namespace Chrono.Application.Ports;

/// <summary>World probing: raycasts and ground height (implemented by the SHVDN boundary).</summary>
public interface IWorldProbe
{
    /// <summary>Raycast from origin along direction; returns the first hit (or none).</summary>
    RaycastSample Raycast(Vector3 origin, Vector3 direction, float maxDistance);

    /// <summary>Ground height directly below the position; null when no ground found.</summary>
    float? GetGroundHeight(Vector3 position);

    /// <summary>Nearby civilian count (warrant reports, S9).</summary>
    int CountNearbyCivilians(Vector3 position, float radius);

    /// <summary>Nearby civilians flee in fear (notoriety crowd reaction, S9).</summary>
    void MakeNearbyCiviliansFlee(Vector3 position, float radius);
}
