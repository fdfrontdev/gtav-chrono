using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>
/// Food/drink world interactions (v0.10, SRS FR-C8..C11) — props, eat anims,
/// vending-machine detection. Game-touching; the application stays pure.
/// </summary>
public interface IFoodBoundary
{
    /// <summary>Spawn a food prop at a world position (delivery arrival, FR-C10).</summary>
    void SpawnFoodProp(Vector3 position, string model);

    /// <summary>Play the eat animation (verified dict; missing anim = still consumed).</summary>
    void PlayEatAnim();

    /// <summary>Play the drink animation (v0.12 phone drinks; missing dict = still consumed).</summary>
    void PlayDrinkAnim();

    /// <summary>Nearest vending machine within radius (FR-C9), or null.</summary>
    Vector3? FindVendingMachine(Vector3 center, float radiusM);

    /// <summary>Nearest fixed eatery within radius (FR-C8), or false.</summary>
    bool TryFindEatery(Vector3 center, float radiusM, out Vector3 spot);
}
