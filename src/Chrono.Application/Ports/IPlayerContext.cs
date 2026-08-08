using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>Player state + teleport capability (implemented by the SHVDN boundary).</summary>
public interface IPlayerContext
{
    int PlayerHandle { get; }
    int? PlayerVehicleHandle { get; }
    Vector3 Position { get; }
    float Heading { get; }
    bool IsAiming { get; }
    Vector3 GetAimDirection();
    bool IsWaypointActive();
    Vector3 GetWaypointPosition();
    void Teleport(Vector3 position);
}
