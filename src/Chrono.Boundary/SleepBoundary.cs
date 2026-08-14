using System;
using System.Numerics;
using Chrono.Application.Ports;
using GTA;

namespace Chrono.Boundary;

/// <summary>
/// v0.10 sleep spots (SRS FR-C12): fixed safehouse/motel coordinates first,
/// then a bed-prop scan. Coordinates are UAT-tunable.
/// </summary>
public sealed class SleepBoundary : ISleepBoundary
{
    // Fixed sleep spots — protagonist homes + motel. Approximate exterior
    // positions; the fade-sleep works on arrival (interiors not required).
    private static readonly (float X, float Y, float Z)[] FixedSpots =
    {
        (9.5f, -1414.0f, 29.2f),     // Franklin's house (Forum Drive)
        (-851.0f, 168.0f, 75.0f),    // Michael's house (Rockford Hills)
        (1972.0f, 3810.0f, 33.0f),   // Trevor's trailer (Sandy Shores)
        (330.0f, 175.0f, 103.0f),    // Vespucci motel
    };

    public bool TryFindSleepSpot(Vector3 center, float radiusM, out Vector3 spot)
    {
        foreach (var (x, y, z) in FixedSpots)
        {
            var p = new Vector3(x, y, z);
            if (Vector3.Distance(center, p) <= radiusM)
            {
                spot = p;
                return true;
            }
        }

        // Bed-prop scan fallback (hospital beds, interior beds — any model with "bed")
        try
        {
            var gta = EntityFreezer.ToGta(center);
            foreach (var prop in World.GetNearbyProps(gta, radiusM))
            {
                if (prop == null || !prop.Exists()) continue;
                string model = prop.Model.ToString() ?? "";
                if (model.Contains("bed", StringComparison.OrdinalIgnoreCase))
                {
                    spot = EntityFreezer.ToNumerics(prop.Position);
                    return true;
                }
            }
        }
        catch { /* scan is flavor — never a crash */ }

        spot = default;
        return false;
    }
}
