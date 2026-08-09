using System;

namespace Chrono.Domain;

/// <summary>
/// S20 — classified criminal acts (ADR-04). The mod detects the ACT itself and
/// drives the wanted level from it (murder → instant 5★), instead of inheriting
/// the game's coarse star mapping (vanilla: murder/assault/damage all = 1★).
/// </summary>
public enum CrimeKind
{
    /// <summary>Unclassified fallback — the vanilla star-proxy path (FR-1.2 legacy).</summary>
    PublicOffense,

    /// <summary>Kill with a firearm/explosive (5★) or melee (4★).</summary>
    Murder,

    /// <summary>Kill while driving a vehicle at speed (3★).</summary>
    VehicularManslaughter,

    /// <summary>Non-lethal weapon damage to a ped (2★).</summary>
    Assault,

    /// <summary>Aiming a firearm at civilians with witnesses (1★).</summary>
    Brandishing,

    /// <summary>Vehicle/prop damage caused by the player (1★).</summary>
    PropertyDamage,

    /// <summary>Aiming a weapon at a civilian within 6 m (3★).</summary>
    AttemptedRobbery
}

/// <summary>
/// Game-neutral classification of a kill cause / held weapon. The Boundary maps
/// SHVDN's WeaponHash → this, so the Domain never touches game types.
/// </summary>
public enum DeathCauseKind
{
    None,
    Gun,
    Melee,
    Explosive,
    Vehicle,
    Other
}

/// <summary>
/// Player act context sampled by the Boundary (S20). Game-neutral — the Domain
/// never sees SHVDN types.
/// </summary>
public sealed record PlayerActContext(
    DeathCauseKind WeaponOut,      // what the player currently holds (None = unarmed)
    bool IsAiming,                 // player aiming a weapon
    bool InVehicle,                // player is driving
    float VehicleSpeedMps);        // current vehicle speed (0 when on foot)

/// <summary>
/// One polling sample of the player's recent acts (game-neutral; SHVDN types never leak).
/// Produced by the Boundary's ICrimeProbe, consumed by <see cref="CrimeClassifier"/>.
/// </summary>
public sealed record ActSample(
    DeathCauseKind WeaponOut,           // what the player currently holds (None = unarmed)
    bool IsAiming,                      // player aiming a weapon
    bool InVehicle,                     // player is driving
    float VehicleSpeedMps,              // current vehicle speed (0 when on foot)
    DeathCauseKind LastKillCause,       // most recent kill attributed to the player (None = none)
    bool PedDamagedSinceLastTick,       // a ped took weapon damage (non-lethal) near the player
    bool VehicleDamagedSinceLastTick,   // a nearby vehicle/prop was damaged by the player
    float CrosshairPedDistanceM,        // distance to the ped under the crosshair (float.MaxValue = none)
    int WitnessCount);                  // nearby civilians + police (witness gating, FR-1.4)

/// <summary>Classified crime: what the act is, its severity, and the wanted stars to force.</summary>
public sealed record ClassifiedCrime(CrimeKind Kind, CrimeSeverity Severity, int Stars, string Name);

/// <summary>
/// Pure act→crime classifier (S20, ADR-04 D1). No game calls — fully unit-testable.
/// Precedence: kill &gt; robbery &gt; brandishing &gt; assault &gt; property damage.
/// Witness gating (FR-1.4) is applied by the Application service, not here.
/// </summary>
public static class CrimeClassifier
{
    public const float RobberyRangeM = 6f;
    public const float VehicularManslaughterSpeedMps = 15f;

    public static ClassifiedCrime? Classify(ActSample sample)
    {
        // 1. Kill — the gravest act wins (vehicular manslaughter only when driving fast)
        if (sample.LastKillCause != DeathCauseKind.None)
        {
            if (sample.LastKillCause == DeathCauseKind.Vehicle
                && sample.InVehicle
                && sample.VehicleSpeedMps >= VehicularManslaughterSpeedMps)
                return new(CrimeKind.VehicularManslaughter, CrimeSeverity.Moderate, 3, "vehicular_manslaughter");

            return sample.LastKillCause switch
            {
                DeathCauseKind.Melee => new(CrimeKind.Murder, CrimeSeverity.Severe, 4, "murder"),
                _ => new(CrimeKind.Murder, CrimeSeverity.Severe, 5, "murder")  // gun/explosive/other
            };
        }

        // 2. Attempted robbery — aiming a weapon at a person up close
        if (sample.IsAiming && sample.WeaponOut != DeathCauseKind.None
            && sample.CrosshairPedDistanceM <= RobberyRangeM)
            return new(CrimeKind.AttemptedRobbery, CrimeSeverity.Moderate, 3, "attempted_robbery");

        // 3. Brandishing — firearm out + aiming, with witnesses
        if (sample.IsAiming && sample.WeaponOut == DeathCauseKind.Gun && sample.WitnessCount > 0)
            return new(CrimeKind.Brandishing, CrimeSeverity.Minor, 1, "brandishing");

        // 4. Assault — non-lethal weapon damage to a ped
        if (sample.PedDamagedSinceLastTick)
            return new(CrimeKind.Assault, CrimeSeverity.Minor, 2, "assault");

        // 5. Property damage — vehicle/prop damage near the player
        if (sample.VehicleDamagedSinceLastTick)
            return new(CrimeKind.PropertyDamage, CrimeSeverity.Minor, 1, "property_damage");

        return null;
    }
}
