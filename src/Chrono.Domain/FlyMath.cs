using System;
using System.Numerics;

namespace Chrono.Domain;

/// <summary>Pure flight velocity math (Dragon Ball style, camera-relative). Testable.</summary>
public static class FlyMath
{
    /// <summary>
    /// Compute flight velocity from camera basis and input flags.
    /// Horizontal input is normalized (no diagonal speed boost). Vertical axis is absolute.
    /// </summary>
    public static Vector3 CalculateVelocity(
        Vector3 forward,          // camera forward (horizontal component expected)
        Vector3 right,            // camera right = cross(forward, up)
        float speed,
        bool moveForward, bool moveBack, bool moveLeft, bool moveRight,
        bool ascend, bool descend)
    {
        float f = (moveForward ? 1f : 0f) - (moveBack ? 1f : 0f);
        float r = (moveRight ? 1f : 0f) - (moveLeft ? 1f : 0f);
        float v = (ascend ? 1f : 0f) - (descend ? 1f : 0f);

        var horizontal = forward * f + right * r;
        if (horizontal.Length() > 1f)
            horizontal = Vector3.Normalize(horizontal);

        return horizontal * speed + Vector3.UnitZ * (v * speed);
    }

    /// <summary>Exponential approach toward a target velocity (natural inertia, v0.8.0).
    /// dt=0 returns the current velocity unchanged; higher accel = snappier.</summary>
    public static Vector3 SmoothVelocity(Vector3 current, Vector3 target, float dtSeconds, float acceleration)
    {
        if (dtSeconds <= 0f || acceleration <= 0f) return current;
        float t = 1f - (float)Math.Exp(-acceleration * dtSeconds);
        return current + (target - current) * t;
    }

    /// <summary>Smooth heading rotation toward a target, taking the short way around
    /// (350° → 10° goes through 0°, not 180°).</summary>
    public static float SmoothHeading(float currentDeg, float targetDeg, float dtSeconds, float rate)
    {
        if (dtSeconds <= 0f || rate <= 0f) return currentDeg;
        float delta = NormalizeAngle(targetDeg - currentDeg);
        float t = 1f - (float)Math.Exp(-rate * dtSeconds);
        return NormalizeAngle(currentDeg + delta * t);
    }

    private static float NormalizeAngle(float deg)
    {
        while (deg > 180f) deg -= 360f;
        while (deg < -180f) deg += 360f;
        return deg;
    }
}
