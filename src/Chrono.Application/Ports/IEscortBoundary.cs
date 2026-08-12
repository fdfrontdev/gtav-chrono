using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>
/// S22 v8 — police escort ride (reverse-engineered from the Prison Mod's
/// "full ride"): after arrest, the cuffed player rides to Bolingbroke in a
/// police cruiser driven by AI. Boundary owns all GTA-side work.
/// </summary>
public interface IEscortBoundary
{
    /// <summary>True while the cruiser + driver exist (the ride is live).</summary>
    bool IsRiding { get; }

    /// <summary>Spawn the cruiser near the player, warp them into the back seat,
    /// task the AI driver to the destination. Idempotent.</summary>
    void Begin(Vector3 playerPosition, Vector3 destination);

    /// <summary>
    /// S23 — per-tick watchdog: re-seat/re-task the driver if the AI bailed
    /// (user UAT: \"the officer got out of the car\") and reassert the custody
    /// suppression (police ignore / wanted 0) for the ride's duration.
    /// </summary>
    void Tick();

    /// <summary>True when the cruiser reached the destination (arrival radius).</summary>
    bool HasArrived(Vector3 destination, float arrivalRadiusM = 20f);

    /// <summary>Player pressed the skip key — end the ride early (still teleport to intake).</summary>
    void Skip();

    /// <summary>True when the ride was skipped by the player.</summary>
    bool WasSkipped { get; }

    /// <summary>Restore control, delete the cruiser + driver. Idempotent.</summary>
    void End();
}
