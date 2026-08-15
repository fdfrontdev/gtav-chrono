using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>
/// Sleep interactions (v0.10, SRS FR-C12) — fixed sleep spots + bed-prop scan.
/// v0.13 (ADR 09): TV detection added — watching TV is a downtime mood booster.
/// Game-touching; the application stays pure.
/// </summary>
public interface ISleepBoundary
{
    /// <summary>Try to find a sleep spot within radius (fixed spots first, then bed props).</summary>
    bool TryFindSleepSpot(Vector3 center, float radiusM, out Vector3 spot);

    /// <summary>v0.13: nearest TV prop within radius (ADR 09), or false.</summary>
    bool TryFindTv(Vector3 center, float radiusM, out Vector3 spot);
}
