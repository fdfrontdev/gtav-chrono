using System;
using System.Numerics;

namespace Chrono.Domain;

/// <summary>
/// Pure teleport math (DLD §2.3). All methods deterministic and unit-testable.
/// Coordinate convention: GTA — X east, Y north, Z up; heading 0 = north, clockwise.
/// </summary>
public static class TeleportMath
{
    /// <summary>Forward target: origin + facing direction * range (no aiming case).</summary>
    public static Vector3 CalculateForwardTarget(Vector3 origin, float headingDegrees, float range)
    {
        float rad = DegToRad(headingDegrees);
        var dir = new Vector3(-(float)Math.Sin(rad), (float)Math.Cos(rad), 0f); // heading → world dir (GTA convention)
        return origin + dir * range;
    }

    /// <summary>Clamp an arbitrary aim point to the allowed range band.</summary>
    public static Vector3? ClampToRange(Vector3 origin, Vector3 aimPoint, float minRange, float maxRange)
    {
        var delta = aimPoint - origin;
        delta.Z = 0f;
        float dist = delta.Length();
        if (dist < 0.01f) return null;
        if (dist > maxRange) return origin + Vector3.Normalize(delta) * maxRange;
        if (dist < minRange) return origin + Vector3.Normalize(delta) * minRange;
        return aimPoint;
    }

    /// <summary>
    /// True when the ray did NOT hit anything before the target (path clear).
    /// </summary>
    public static bool IsPathClear(RaycastSample sample, float requiredClearance)
    {
        if (!sample.Hit) return true;                       // nothing in the way
        float hitDist = Vector3.Distance(sample.Origin, sample.HitPosition);
        float targetDist = Vector3.Distance(sample.Origin, sample.Target);
        return hitDist > targetDist - requiredClearance;    // hit at/behind target → clear
    }

    /// <summary>
    /// Compute the landing point: use the probe hit if sane, else the fallback.
    /// </summary>
    public static Vector3 SnapToGround(Vector3 probeStart, Vector3? hitPoint, float probeDistance, Vector3 fallback)
    {
        if (hitPoint.HasValue)
        {
            var hit = hitPoint.Value;
            float delta = Math.Abs(hit.Z - probeStart.Z);
            if (delta <= probeDistance) return hit;         // hit within probe distance below start
        }
        return fallback;
    }

    /// <summary>Linear interpolation for VFX trail points.</summary>
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        t = t < 0f ? 0f : (t > 1f ? 1f : t);
        return a + (b - a) * t;
    }

    private static float DegToRad(float deg) => deg * (float)Math.PI / 180f;
}

/// <summary>Result of a single raycast probe (pure data).</summary>
public sealed record RaycastSample(Vector3 Origin, Vector3 Target, bool Hit, Vector3 HitPosition);
