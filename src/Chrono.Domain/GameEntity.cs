namespace Chrono.Domain;

/// <summary>Game-boundary-neutral entity reference (pure data — no SHVDN types).</summary>
public sealed record GameEntity(int Handle, EntityKind Kind);
