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
}
