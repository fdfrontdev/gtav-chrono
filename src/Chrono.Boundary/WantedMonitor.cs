using Chrono.Application.Ports;
using GTA;

namespace Chrono.Boundary;

/// <summary>Wanted-level reader (star edges are detected by JusticeService — pure logic).</summary>
public sealed class WantedMonitor : IWantedMonitor
{
    // SHVDN 3.9: Player.WantedLevel is obsolete — use the Wanted API
    public int CurrentStars => Game.Player.Wanted.WantedLevel;

    public void SetStars(int stars) => Game.Player.Wanted.SetWantedLevel(stars, false);
}
