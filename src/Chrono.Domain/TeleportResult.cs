using System.Numerics;

namespace Chrono.Domain;

public enum TeleportOutcome
{
    Success,
    NoClearPath,
    NoWaypoint,
    Failed
}

/// <summary>Typed result of a teleport attempt (operational error handling — never exceptions).</summary>
public sealed record TeleportResult(TeleportOutcome Outcome, Vector3? Point)
{
    public static TeleportResult Success(Vector3 point) => new(TeleportOutcome.Success, point);
    public static TeleportResult NoClearPath() => new(TeleportOutcome.NoClearPath, null);
    public static TeleportResult NoWaypoint() => new(TeleportOutcome.NoWaypoint, null);
    public static TeleportResult Failed() => new(TeleportOutcome.Failed, null);
}
