using Chrono.Domain;

namespace Chrono.Application.Ports;

/// <summary>
/// S20 — act sampling for crime detection (ADR-04 D1). The Boundary polls the
/// game (SHVDN 3.9 exposes no C# events) and reports game-neutral facts; the
/// Domain's <see cref="CrimeClassifier"/> turns them into crimes. All "since
/// last poll" facts are CONSUMING — each event reports once.
/// </summary>
public interface ICrimeProbe
{
    /// <summary>Player context sampled now: weapon held, aiming, driving, speed.</summary>
    PlayerActContext SampleContext();

    /// <summary>
    /// True when a ped near the player DIED since the last poll AND the player
    /// (or the player's vehicle) is the source of death. Returns the death cause
    /// class (None = no kill). Consuming.
    /// </summary>
    DeathCauseKind PollKillSinceLastPoll();

    /// <summary>True when a ped near the player took non-lethal damage from the
    /// player since the last poll (assault detection). Consuming.</summary>
    bool PollPedDamageSinceLastPoll();

    /// <summary>True when a nearby vehicle/prop took damage from the player since
    /// the last poll (property damage). Consuming.</summary>
    bool PollVehicleDamageSinceLastPoll();

    /// <summary>Distance (m) to the ped under the crosshair; float.MaxValue when
    /// the crosshair is not on a ped (attempted-robbery detection).</summary>
    float CrosshairPedDistanceM { get; }

    /// <summary>Nearby police peds (witness count + hold-fire targeting).</summary>
    int CountNearbyPolice(float radius);

    /// <summary>S21: distance (m) to the NEAREST police ped; float.MaxValue when none
    /// (physical capture — cops must REACH you to cuff you, user UAT r15).</summary>
    float NearestPoliceDistanceM { get; }

    /// <summary>
    /// Hold-fire: nearby police aim but do NOT shoot (use-of-force continuum,
    /// ADR-04 D2). false lifts the hold (vanilla AI re-engages).
    /// </summary>
    void SetPoliceHoldFire(bool hold);
}
