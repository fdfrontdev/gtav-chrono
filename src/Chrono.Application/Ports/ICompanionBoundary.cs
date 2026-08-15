using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>
/// v0.12 phone escort companion (FR-D2) — a ped walks to the player, then
/// the application fades + applies the mood/energy payoff.
/// Game-touching; the application stays pure.
/// NOTE: distinct from IEscortBoundary (S23 police custody ride).
/// </summary>
public interface ICompanionBoundary
{
    /// <summary>Spawn the companion near the player and walk her to them.</summary>
    void SendCompanion(Vector3 playerPosition, string model);

    /// <summary>True once she is close enough to the player to "service" them.</summary>
    bool IsCompanionNear(Vector3 playerPosition);

    /// <summary>Remove the companion from the world (after the fade).</summary>
    void DismissCompanion();
}
