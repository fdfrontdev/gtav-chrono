using System;
using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;
using GTA;

namespace Chrono.Boundary;

/// <summary>Raycast + ground height probing (ADR-02 §2.2).</summary>
public sealed class WorldProbe : IWorldProbe
{
    public RaycastSample Raycast(Vector3 origin, Vector3 direction, float maxDistance)
    {
        var result = World.Raycast(
            EntityFreezer.ToGta(origin),
            EntityFreezer.ToGta(direction),
            maxDistance,
            IntersectFlags.Everything,
            Game.Player.Character);

        return new RaycastSample(
            origin,
            origin + direction * maxDistance,
            result.DidHit,
            result.DidHit ? EntityFreezer.ToNumerics(result.HitPosition) : Vector3.Zero);
    }

    public float? GetGroundHeight(Vector3 position)
    {
        var gtaPos = EntityFreezer.ToGta(position);
        return World.GetGroundHeightAndNormal(gtaPos, out float height, out _)
            ? height
            : null;
    }
}
