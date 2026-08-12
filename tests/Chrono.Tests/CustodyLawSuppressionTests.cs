using System.Linq;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S23 (user UAT 2026-08-13 screenshot: prison panel showed WANTED 2★ at
/// Day 10/56, civilians still calling police, cops shooting during the
/// custody ride): while CAPTURED or in PRISON the street chase is OVER —
/// the wanted level is forced to 0 every tick (the game's crime memory
/// re-raises it), law enforcement + civilians ignore the player, and new
/// acts add to the case WITHOUT driving stars or changing state. The
/// suppression lifts on release/escape so the manhunt can engage.
/// </summary>
public class CustodyLawSuppressionTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeRecordStore store, FakeCrimeProbe probe) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000 };
        var store = new FakeRecordStore();
        var probe = new FakeCrimeProbe();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            new FakeNotifier(), new FakeLog(),
            new JusticeConfig { PrisonDayRealSeconds = 30, TrialDelaySeconds = 45 },
            new FakeClock(),
            input: new FakeInput(),
            probe: new FakeProbe(),
            crimeProbe: probe);
        return (service, wanted, player, store, probe);
    }

    private static void Capture(JusticeService service, FakeWantedMonitor wanted, FakeCrimeProbe probe)
    {
        wanted.CurrentStars = 4;
        service.Tick();                  // Wanted state (crime recorded)
        probe.NearestPoliceDistance = 2f;
        service.Tick();                  // cop in reach, suspect stopped → OnCaptured
    }

    private static void ReachPrison(JusticeService service, FakeWantedMonitor wanted, FakeCrimeProbe probe)
    {
        Capture(service, wanted, probe);
        service.AdvanceTrialTime(45.0);
        service.Tick();                  // verdict → ApplySentence → BeginConfinement
    }

    [Fact]
    public void Capture_EnablesLawEnforcementIgnore()
    {
        var (service, wanted, player, _, probe) = Build();
        Capture(service, wanted, probe);
        service.Tick();                  // suppression edge fires

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.True(player.LawIgnoreCalls.Last());
    }

    [Fact]
    public void Captured_GameReraisesStars_ForcedBackToZero()
    {
        var (service, wanted, _, _, probe) = Build();
        Capture(service, wanted, probe);
        service.Tick();

        wanted.CurrentStars = 2;         // the game's crime memory re-raises
        service.Tick();

        Assert.Equal(0, wanted.CurrentStars);
        Assert.Contains(0, wanted.StarSets);   // the reassert happened
    }

    [Fact]
    public void Prison_GameReraisesStars_ForcedBackToZero()
    {
        var (service, wanted, player, _, probe) = Build();
        ReachPrison(service, wanted, probe);
        service.Tick();                  // suppression edge fires

        Assert.Equal(JusticeState.Prison, service.State);
        Assert.True(player.LawIgnoreCalls.Last());

        wanted.CurrentStars = 3;         // e.g. a punch on a guard re-raised it
        service.Tick();

        Assert.Equal(0, wanted.CurrentStars);
    }

    [Fact]
    public void CrimeWhileInCustody_AddsToCase_ButNeverRaisesStars()
    {
        var (service, wanted, _, store, probe) = Build();
        Capture(service, wanted, probe);
        service.Tick();
        int before = store.Record.Events.Count;

        service.RecordDetectedCrime(new(CrimeKind.Assault, CrimeSeverity.Minor, 1, "SHOVE"));

        Assert.Equal(before + 1, store.Record.Events.Count);   // added to the case
        Assert.Equal(0, wanted.CurrentStars);                  // no star-driving in custody
        Assert.Equal(JusticeState.Captured, service.State);    // the state holds
    }

    [Fact]
    public void CrimeWhileInPrison_AddsToCase_StateHoldsAsPrison()
    {
        var (service, wanted, _, store, probe) = Build();
        ReachPrison(service, wanted, probe);
        service.Tick();
        int before = store.Record.Events.Count;

        service.RecordDetectedCrime(new(CrimeKind.Assault, CrimeSeverity.Minor, 1, "SHOVE"));

        Assert.Equal(before + 1, store.Record.Events.Count);
        Assert.Equal(JusticeState.Prison, service.State);      // no state escape via crime
        Assert.Equal(0, wanted.CurrentStars);
    }

    [Fact]
    public void Escape_LiftsLawEnforcementIgnore_ManhuntStarsSurvive()
    {
        var (service, wanted, player, _, probe) = Build();
        ReachPrison(service, wanted, probe);
        service.Tick();                  // suppression ON

        service.AdvancePrisonTime(20.0); // yard window reached
        service.Tick();                  // yard opens
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Dash);

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Equal(4, wanted.CurrentStars);   // the escape set the manhunt heat

        service.Tick();                  // suppression edge → OFF

        Assert.False(player.LawIgnoreCalls.Last());
        Assert.Equal(4, wanted.CurrentStars);   // the manhunt is NOT zeroed
    }

    [Fact]
    public void Escape_DoesNotDoubleRecordTheManhuntAsANewCrime()
    {
        var (service, wanted, _, store, probe) = Build();
        ReachPrison(service, wanted, probe);
        service.Tick();

        service.AdvancePrisonTime(20.0);
        service.Tick();                  // yard
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Dash);
        int afterEscape = store.Record.Events.Count;   // 4★ crime + prison_escape

        service.Tick();                  // post-escape tick — the 4★ edge must be suppressed

        Assert.Equal(afterEscape, store.Record.Events.Count);   // no phantom public_offense
    }

    [Fact]
    public void Release_LiftsLawEnforcementIgnore()
    {
        var (service, wanted, player, _, probe) = Build();
        ReachPrison(service, wanted, probe);
        service.Tick();                  // suppression ON
        Assert.True(player.LawIgnoreCalls.Last());

        for (int i = 0; i < 7; i++) service.AdvancePrisonTime(30.0);   // serve the 7-day term
        Assert.Equal(JusticeState.Free, service.State);                 // released
        service.Tick();

        Assert.False(player.LawIgnoreCalls.Last());
    }
}
