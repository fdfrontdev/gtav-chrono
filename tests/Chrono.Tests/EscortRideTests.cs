using System;
using System.Numerics;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S22 v8 — police escort ride (reverse-engineered from the Prison Mod's
/// "full ride"): capture → cuffed in a police cruiser → AI drive to
/// Bolingbroke → booking. The trial verdict waits for arrival; E skips.
/// </summary>
public class EscortRideTests
{
    internal sealed class FakeEscortBoundary : IEscortBoundary
    {
        public bool IsRiding { get; private set; }
        public bool WasSkipped { get; private set; }
        public int BeginCalls { get; private set; }
        public int EndCalls { get; private set; }
        public bool ArriveNextTick { get; set; }
        public Vector3? LastDestination { get; private set; }

        public void Begin(Vector3 playerPosition, Vector3 destination)
        {
            BeginCalls++;
            LastDestination = destination;
            IsRiding = true;
        }

        public bool HasArrived(Vector3 destination, float arrivalRadiusM = 20f)
            => ArriveNextTick;

        public void Skip() => WasSkipped = true;
        public void End() { IsRiding = false; EndCalls++; }
    }

    private static (EscortService service, FakeEscortBoundary boundary, FakePlayer player, FakeNotifier notifier) Build(int timeoutSeconds = 120)
    {
        var boundary = new FakeEscortBoundary();
        var player = new FakePlayer { IsVisible = true };
        var notifier = new FakeNotifier();
        var service = new EscortService(boundary, player, new FakeInput(), notifier, new FakeLog(),
            new JusticeConfig { EscortTimeoutSeconds = timeoutSeconds });
        return (service, boundary, player, notifier);
    }

    [Fact]
    public void Begin_StartsRide_ToBolingbroke()
    {
        var (service, boundary, _, notifier) = Build();

        service.Begin(EscortService.BolingbrokeGate);

        Assert.True(service.IsActive);
        Assert.Equal(1, boundary.BeginCalls);
        Assert.Equal(EscortService.BolingbrokeGate, boundary.LastDestination!.Value);
        Assert.Contains(notifier.Messages, m => m.Contains("TRANSPORT"));
    }

    [Fact]
    public void Tick_Arrival_EndsRide()
    {
        var (service, boundary, _, notifier) = Build();
        service.Begin(EscortService.BolingbrokeGate);
        boundary.ArriveNextTick = true;

        service.Tick();

        Assert.False(service.IsActive);
        Assert.Equal(1, boundary.EndCalls);
        Assert.Contains(notifier.Messages, m => m.Contains("ARRIVED"));
    }

    [Fact]
    public void Tick_StillRiding_NoEnd()
    {
        var (service, boundary, _, _) = Build();
        service.Begin(EscortService.BolingbrokeGate);
        boundary.ArriveNextTick = false;

        service.Tick();

        Assert.True(service.IsActive);
        Assert.Equal(0, boundary.EndCalls);
    }

    [Fact]
    public void Skip_EndsRide_WithSkipNotice()
    {
        var (service, boundary, _, notifier) = Build();
        service.Begin(EscortService.BolingbrokeGate);
        var input = (FakeInput)typeof(EscortService)
            .GetField("_input", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(service)!;
        input.InteractHotkey = true;
        input.Update();                  // edge detection: InteractHotkey → IsInteractKeyJustPressed

        service.Tick();   // skip → boundary.Skip → next tick sees IsRiding false → ends

        Assert.True(boundary.WasSkipped);
    }

    [Fact]
    public void Abort_EndsRide_Immediately()
    {
        var (service, boundary, _, _) = Build();
        service.Begin(EscortService.BolingbrokeGate);

        service.Abort();

        Assert.False(service.IsActive);
        Assert.Equal(1, boundary.EndCalls);
    }

    // S22 v8 r2 (user UAT r39: "court reached 0:00, nothing happened"): the
    // ride TIMES OUT when the driver never moves — the verdict can never be
    // held forever.

    [Fact]
    public void StuckRide_TimesOut_AndCompletes()
    {
        var (service, boundary, _, notifier) = Build(timeoutSeconds: 1);
        service.Begin(EscortService.BolingbrokeGate);
        boundary.ArriveNextTick = false;      // the cruiser never arrives

        System.Threading.Thread.Sleep(1100);  // past the 1s timeout
        service.Tick();

        Assert.False(service.IsActive);
        Assert.Equal(1, boundary.EndCalls);
        Assert.Contains(notifier.Messages, m => m.Contains("ARRIVED"));
    }

    [Fact]
    public void Timeout_ThenVerdictFires_InJusticePipeline()
    {
        var (service, wanted, player, _, store, probe, crimeProbe, boundary) =
            BuildJustice(withEscort: true, timeoutSeconds: 1);
        wanted.CurrentStars = 4;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                  // captured → escort begins

        Assert.True(service.IsEscortRiding);
        service.AdvanceTrialTime(120.0); // court is DUE
        System.Threading.Thread.Sleep(1100);
        service.Tick();                  // timeout → ride ends
        service.Tick();                  // verdict fires

        Assert.Equal(JusticeState.Prison, service.State);
    }

    // ── justice integration: capture starts the ride; verdict waits for arrival ──

    [Fact]
    public void Capture_StartsEscort_VerdictWaitsForArrival()
    {
        var (service, wanted, player, _, store, probe, crimeProbe, boundary) = BuildJustice(withEscort: true);
        wanted.CurrentStars = 4;
        service.Tick();                  // Wanted
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                  // physical capture → escort begins

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.True(boundary.BeginCalls >= 1, "capture must start the escort ride");

        // trial time passes DURING the ride — the CLOCK keeps ticking (UAT r37:
        // "the court timer didn't count down" — frozen 0:45 reads as a bug),
        // but the verdict must NOT fire yet (court scene plays at Bolingbroke).
        double before = service.TrialSecondsLeft;
        service.AdvanceTrialTime(120.0);
        service.Tick();
        Assert.Equal(JusticeState.Captured, service.State);
        Assert.True(service.TrialSecondsLeft < before, "court clock must count DOWN during the ride");

        // arrival → ride ends → verdict fires on the next tick
        boundary.ArriveNextTick = true;
        service.Tick();                  // escort completes → booking
        service.AdvanceTrialTime(120.0);
        service.Tick();                  // now the verdict can run
        Assert.Equal(JusticeState.Prison, service.State);
    }

    [Fact]
    public void Capture_WithoutEscort_BookingPlaysLocally()
    {
        var (service, wanted, player, _, store, probe, crimeProbe, boundary) = BuildJustice(withEscort: false);
        wanted.CurrentStars = 4;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();

        Assert.Equal(0, boundary.BeginCalls);
        Assert.Equal(JusticeState.Captured, service.State);
    }

    private static (JusticeService, FakeWantedMonitor, FakePlayer, FakeNotifier, FakeRecordStore,
        FakeProbe, FakeCrimeProbe, FakeEscortBoundary) BuildJustice(bool withEscort, int timeoutSeconds = 120)
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true };
        var notifier = new FakeNotifier();
        var store = new FakeRecordStore();
        var probe = new FakeProbe { NearbyCivilians = 5 };
        var crimeProbe = new FakeCrimeProbe();
        var boundary = new FakeEscortBoundary();
        EscortService? escort = null;
        if (withEscort)
            escort = new EscortService(boundary, player, new FakeInput(), notifier, new FakeLog(),
                new JusticeConfig { EscortTimeoutSeconds = timeoutSeconds });
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(),
            new JusticeConfig { WarrantReportSeconds = 0, PrisonDayRealSeconds = 30, EscortTimeoutSeconds = timeoutSeconds }, new FakeClock(),
            cutscene: null, probe: probe, random: () => 0.0, crimeProbe: crimeProbe,
            escort: escort);
        return (service, wanted, player, notifier, store, probe, crimeProbe, boundary);
    }
}
