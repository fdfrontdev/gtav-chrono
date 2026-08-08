using System;
using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;
using GTA;
using GTA.Native;

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

    public int CountNearbyCivilians(Vector3 position, float radius)
    {
        try
        {
            var peds = World.GetNearbyPeds(EntityFreezer.ToGta(position), radius, Array.Empty<Model>());
            int playerHandle = Game.Player.Character.Handle;
            int count = 0;
            foreach (var ped in peds)
            {
                if (ped == null || !ped.Exists() || ped.Handle == playerHandle) continue;
                count++;
            }
            return count;
        }
        catch
        {
            return 0;   // probing is flavor — never a crash vector
        }
    }

    public void MakeNearbyCiviliansFlee(Vector3 position, float radius)
    {
        try
        {
            var peds = World.GetNearbyPeds(EntityFreezer.ToGta(position), radius, Array.Empty<Model>());
            int playerHandle = Game.Player.Character.Handle;
            foreach (var ped in peds)
            {
                if (ped == null || !ped.Exists() || ped.Handle == playerHandle) continue;
                Function.Call(GTA.Native.Hash.TASK_SMART_FLEE_PED, ped, Game.Player.Character, 100f, -1, false, false);
            }
        }
        catch
        {
            // crowd reactions are flavor — never a crash vector
        }
    }
}
