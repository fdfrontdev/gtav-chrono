using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>Game clock pause/resume via the PAUSE_CLOCK native (SRS FR-2.1).</summary>
public sealed class GameClockAdapter : IGameClock
{
    public bool IsPaused { get; private set; }

    public void Pause()
    {
        Function.Call(Hash.PAUSE_CLOCK, true);
        IsPaused = true;
    }

    public void Resume()
    {
        Function.Call(Hash.PAUSE_CLOCK, false);
        IsPaused = false;
    }

    public int CurrentGameDay
    {
        get
        {
            // SHVDN 3.9 has no Game.Clock property — clock access is via natives
            int year = Function.Call<int>(Hash.GET_CLOCK_YEAR);
            int month = Function.Call<int>(Hash.GET_CLOCK_MONTH);
            int day = Function.Call<int>(Hash.GET_CLOCK_DAY_OF_MONTH);
            return year * 372 + month * 31 + day;
        }
    }
}
