using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>Wanted-level reader (star edges are detected by JusticeService — pure logic).</summary>
public sealed class WantedMonitor : IWantedMonitor
{
    // SHVDN 3.9: Player.WantedLevel is obsolete — use the Wanted API
    public int CurrentStars => Game.Player.Wanted.WantedLevel;

    public void SetStars(int stars)
    {
        Game.Player.Wanted.SetWantedLevel(stars, false);
        // S23: SET_PLAYER_WANTED_LEVEL alone leaves the game's crime memory
        // armed — the chase re-ignites seconds later (the UAT screenshot:
        // wanted 2★ while sitting in prison). Flush the pending level NOW.
        Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, Game.Player.Handle, false);
    }
}
