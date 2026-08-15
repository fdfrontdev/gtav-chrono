using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>
/// Harm report from a combat power (SRS FR-B8). The boundary counts what the
/// effect actually hit; the application classifies + records via the justice
/// pipeline. Estimates are acceptable — the generic crime probe is the backstop.
/// </summary>
public sealed record PowerHitReport(int PedsInjured, int PedsKilled, int VehiclesDamaged, int PropsDamaged)
{
    public bool HasHarm => PedsInjured > 0 || PedsKilled > 0 || VehiclesDamaged > 0 || PropsDamaged > 0;
}

/// <summary>
/// Combat-power effects (v0.10, SRS FR-B4..B7) — impulse, explosion, world
/// slow-mo, regeneration. All game-touching; the application stays pure.
/// </summary>
public interface IPowerFxBoundary
{
    /// <summary>Force Push: impulse on peds + light vehicles in a forward cone (FR-B4).</summary>
    PowerHitReport Push(Vector3 origin, Vector3 direction, float rangeM, float coneDeg, float vehicleImpulse);

    /// <summary>Energy Blast: explosion at a world point (FR-B5).</summary>
    PowerHitReport Blast(Vector3 target, float radiusM, float damageScale);

    /// <summary>Bullet Time: world time-scale (1.0 = normal, FR-B6).</summary>
    void SetWorldTimeScale(float scale);

    /// <summary>Regeneration: heal-over-time + damage resistance window (FR-B7).</summary>
    void HealOverTime(int totalSeconds, float damageResist);

    void CancelHeal();
}
