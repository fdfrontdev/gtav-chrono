using System.Numerics;

namespace Chrono.Domain;

/// <summary>Game-boundary-neutral entity reference (pure data — no SHVDN types).</summary>
public sealed record GameEntity(int Handle, EntityKind Kind, Vector3 Position = default)
{
    public bool IsWithinRadius(Vector3 center, float radius)
    {
        if (radius <= 0f) return true;                     // 0 = no radius filter
        var dx = Position.X - center.X;
        var dy = Position.Y - center.Y;
        return dx * dx + dy * dy <= radius * radius;
    }
}
