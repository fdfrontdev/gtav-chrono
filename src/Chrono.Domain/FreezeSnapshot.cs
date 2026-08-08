using System.Numerics;

namespace Chrono.Domain;

/// <summary>Captured pose of a frozen entity — used to restore the world exactly.</summary>
public sealed record FreezeSnapshot(
    int Handle,
    EntityKind Kind,
    Vector3 Position,
    Vector3 Rotation,
    Vector3 Velocity,
    bool WasFrozen);
