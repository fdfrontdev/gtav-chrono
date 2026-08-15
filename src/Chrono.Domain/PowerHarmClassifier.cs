namespace Chrono.Domain;

/// <summary>Kinds of harm a combat power can inflict (SRS FR-B8, ADR D3).</summary>
public enum PowerHarmKind
{
    /// <summary>Force Push threw a ped into a wall/ground hard enough to injure.</summary>
    PushImpact,

    /// <summary>Energy Blast killed a ped (inner blast radius).</summary>
    BlastPedKill,

    /// <summary>Energy Blast injured a ped (outer blast radius).</summary>
    BlastPedHurt,

    /// <summary>Energy Blast damaged a vehicle.</summary>
    BlastVehicleDamage,

    /// <summary>Energy Blast damaged a prop.</summary>
    BlastPropDamage,
}

/// <summary>
/// Maps power harm to classified crimes (SRS FR-B8) — powers self-report so
/// severity + stars are always right (ADR D3). The generic crime probe stays
/// as a backstop. Pure mapping, tested.
/// </summary>
public static class PowerHarmClassifier
{
    public static ClassifiedCrime Classify(PowerHarmKind kind) => kind switch
    {
        PowerHarmKind.PushImpact => new ClassifiedCrime(CrimeKind.Assault, CrimeSeverity.Moderate, 3, "TELEKINETIC ASSAULT"),
        PowerHarmKind.BlastPedKill => new ClassifiedCrime(CrimeKind.Murder, CrimeSeverity.Severe, 5, "ENERGY BLAST"),
        PowerHarmKind.BlastPedHurt => new ClassifiedCrime(CrimeKind.Assault, CrimeSeverity.Moderate, 3, "ENERGY BLAST ASSAULT"),
        PowerHarmKind.BlastVehicleDamage => new ClassifiedCrime(CrimeKind.PropertyDamage, CrimeSeverity.Moderate, 3, "SUPER-POWERED VANDALISM"),
        _ => new ClassifiedCrime(CrimeKind.PropertyDamage, CrimeSeverity.Minor, 1, "SUPER-POWERED VANDALISM"),
    };
}
