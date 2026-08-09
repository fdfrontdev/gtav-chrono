using Chrono.Application;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S21 — regression tests for the prison instant-release bug (user UAT r15 #3):
/// a 14-day sentence released ~1 second after the intake cutscene because
/// State=Prison was set BEFORE the cutscene and PrisonTick served days using the
/// service-construction Stopwatch. Fix: prison time only serves after the intake
/// cutscene calls BeginConfinement (_confinementStarted gate).
/// </summary>
public class PrisonConfinementGateTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeClock clock, FakeCrimeProbe crimeProbe) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000, DistrictName = "Bolingbroke" };
        var store = new FakeRecordStore();
        var clock = new FakeClock();
        var crimeProbe = new FakeCrimeProbe();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            new FakeNotifier(), new FakeLog(),
            new JusticeConfig { PrisonDayRealSeconds = 30 }, clock,
            input: new FakeInput(), crimeProbe: crimeProbe);
        return (service, wanted, player, clock, crimeProbe);
    }

    private static void ConfineWithIntake(JusticeService service, FakeWantedMonitor wanted, FakeCrimeProbe crimeProbe)
    {
        wanted.CurrentStars = 5;
        service.Tick();                  // Severe crime → Wanted
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                  // S21: cop reaches you → cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // verdict → prison (intake cutscene pending)
        service.Tick();                  // intake cutscene completes → BeginConfinement
    }

    [Fact]
    public void SentenceServed_OnlyAfterIntakeCutscene_NoInstantRelease()
    {
        var (service, wanted, _, _, crimeProbe) = Build();
        wanted.CurrentStars = 5;
        service.Tick();                  // Wanted
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                  // captured
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // verdict → 14 days, State = Prison (intake pending)

        Assert.Equal(JusticeState.Prison, service.State);
        Assert.Equal(0, service.ServedDays);   // NOTHING served yet — intake cutscene still active
    }

    [Fact]
    public void PrisonTime_Advances_OnlyAfterConfinementBegins()
    {
        var (service, wanted, _, _, crimeProbe) = Build();
        ConfineWithIntake(service, wanted, crimeProbe);   // intake done → BeginConfinement

        service.AdvancePrisonTime(29.0);
        service.Tick();
        Assert.Equal(0, service.ServedDays);   // still inside day 1 (30s/day)

        service.AdvancePrisonTime(1.0);
        service.Tick();
        Assert.Equal(1, service.ServedDays);   // day 1 complete → day 2 starts
    }

    [Fact]
    public void FourteenDaySentence_ServesInRealTime_NotInstant()
    {
        var (service, wanted, _, _, crimeProbe) = Build();
        ConfineWithIntake(service, wanted, crimeProbe);
        int sentenceDays = service.SentenceDays;   // Severe 5★ → 30 days (SentencingPolicy)

        // Serve all but the last day — STILL inside, NOT released early
        for (int i = 0; i < sentenceDays - 1; i++)
        {
            service.AdvancePrisonTime(30.0);
            service.Tick();
        }
        Assert.Equal(sentenceDays - 1, service.ServedDays);
        Assert.Equal(JusticeState.Prison, service.State);   // still serving — no early release

        // The final day → release (after ALL days served, never before)
        service.AdvancePrisonTime(30.0);
        service.Tick();
        Assert.Equal(JusticeState.Free, service.State);   // released AFTER serving, never before
    }

    [Fact]
    public void PrisonWidget_DayCounter_VisibleProgress()
    {
        var (service, wanted, _, _, crimeProbe) = Build();
        ConfineWithIntake(service, wanted, crimeProbe);
        int sentenceDays = service.SentenceDays;

        service.AdvancePrisonTime(15.0);   // halfway through day 1
        service.Tick();

        Assert.True(service.PrisonDayProgress > 0.4 && service.PrisonDayProgress < 0.6,
            $"day progress should be ~0.5, got {service.PrisonDayProgress}");
        Assert.True(service.PrisonDaySecondsLeft > 10 && service.PrisonDaySecondsLeft < 20,
            $"seconds left should be ~15, got {service.PrisonDaySecondsLeft}");
        Assert.Equal(sentenceDays, service.SentenceDays);
    }
}
