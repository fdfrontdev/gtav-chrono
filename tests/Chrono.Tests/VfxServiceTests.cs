using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>
/// Instant-Transmission contract tests: Begin vanishes (alpha 0 + fade out),
/// Complete rematerializes with bursts + flash + shake, Abort ALWAYS restores
/// visibility (regression: player must never be left invisible).
/// </summary>
public class VfxServiceTests
{
    private static (VfxService service, FakeVfx vfx) Build(VisualConfig? visual = null)
    {
        var vfx = new FakeVfx();
        var service = new VfxService(vfx, new FakeLog(), visual ?? new ChronoConfig().Visual);
        return (service, vfx);
    }

    [Fact]
    public void Begin_HidesPlayer()
    {
        var (service, vfx) = Build();

        service.BeginInstantTransmission();

        Assert.Contains(vfx.Calls, c => c == "alpha:0");
        Assert.Contains(vfx.Calls, c => c == "fadeout:0");
    }

    [Fact]
    public void SingleFlash_NotDouble()
    {
        // User report v0.4.0: "double light animation, expect only show once".
        // Begin must NOT flash; Complete produces exactly ONE color flash.
        var (service, vfx) = Build();

        service.BeginInstantTransmission();
        Assert.DoesNotContain(vfx.Calls, c => c.StartsWith("flashcolor:"));

        vfx.Calls.Clear();
        service.CompleteInstantTransmission(new(0, 0, 0), new(0, 12, 0));
        Assert.Single(vfx.Calls, c => c.StartsWith("flashcolor:"));
    }

    [Fact]
    public void Complete_RematerializesWithBurstsAndFlash()
    {
        var (service, vfx) = Build();
        service.BeginInstantTransmission();
        vfx.Calls.Clear();

        service.CompleteInstantTransmission(new(0, 0, 0), new(0, 12, 0));

        Assert.Contains(vfx.Calls, c => c == "alpha:reset");              // visible again
        Assert.Contains(vfx.Calls, c => c == "flash:180");                // anime flash-in
        Assert.Contains(vfx.Calls, c => c.StartsWith("particle:"));       // bursts + trail
        Assert.Contains(vfx.Calls, c => c == "shake");                    // impact shake
    }

    [Fact]
    public void Abort_AlwaysRestoresVisibility()
    {
        var (service, vfx) = Build();
        service.BeginInstantTransmission();
        vfx.Calls.Clear();

        service.AbortInstantTransmission();

        Assert.Contains(vfx.Calls, c => c == "alpha:reset");
        Assert.Contains(vfx.Calls, c => c == "flash:150");
        Assert.DoesNotContain(vfx.Calls, c => c.StartsWith("particle:")); // no teleport → no bursts
    }

    [Fact]
    public void Abort_WithoutBegin_IsSafe()
    {
        var (service, vfx) = Build();

        service.AbortInstantTransmission(); // must not throw

        Assert.Empty(vfx.Calls);
    }

    [Fact]
    public void Complete_WithoutBegin_StillRematerializes()
    {
        var (service, vfx) = Build();

        service.CompleteInstantTransmission(new(0, 0, 0), new(0, 5, 0)); // defensive: no exception

        Assert.Contains(vfx.Calls, c => c == "flash:180");
    }

    [Fact]
    public void TimeStopCue_AppliesAndClearsTint()
    {
        var (service, vfx) = Build();

        service.SetTimeStopCue(true);
        Assert.Contains(vfx.Calls, c => c.StartsWith("timecycle:hud_def_desat:0.4"));

        vfx.Calls.Clear();
        service.SetTimeStopCue(false);
        Assert.Contains(vfx.Calls, c => c == "timecycle:clear");
    }

    [Fact]
    public void TimeStopCue_WithZeroTint_DoesNothing()
    {
        var visual = new ChronoConfig().Visual;
        visual.TimeStop.TintStrength = 0f;
        var (service, vfx) = Build(visual);

        service.SetTimeStopCue(true);

        Assert.DoesNotContain(vfx.Calls, c => c.StartsWith("timecycle:"));
    }

    [Fact]
    public void Warp_FullCycle_StartsTintAndCompletes()
    {
        var (service, vfx) = Build();
        service.StartWarp(new(0, 0, 0), new(100, 100, 0));
        Assert.Contains(vfx.Calls, c => c.StartsWith("timecycle:hud_def_desat:0.2"));

        Assert.False(service.TickWarp(500));   // wind-up in progress
        Assert.True(service.IsWarping);
        Assert.True(service.TickWarp(1700));   // completed (1200ms wind-up from first tick)
        Assert.False(service.IsWarping);
    }

    [Fact]
    public void CancelWarp_ClearsTintAndRestoresEverything()
    {
        var (service, vfx) = Build();
        service.StartWarp(new(0, 0, 0), new(100, 100, 0));
        service.BeginInstantTransmission(); // hidden mid-sequence
        vfx.Calls.Clear();

        service.CancelWarp();

        Assert.Contains(vfx.Calls, c => c == "timecycle:clear");
        Assert.Contains(vfx.Calls, c => c == "alpha:reset");
        Assert.Contains(vfx.Calls, c => c == "shake:stop");
        Assert.False(service.IsWarping);
    }
}
