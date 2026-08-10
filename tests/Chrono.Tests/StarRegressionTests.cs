using System;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S22 v8 r2 (user UAT r38: "I already have > 1 star, when I keep doing
/// crime the star regress. example from 5 to 1 — didn't make any sense") —
/// the wanted level must NEVER regress from a new act: a Minor crime during
/// a 5★ chase holds the heat; the act only raises or holds, never lowers.
/// </summary>
public class StarRegressionTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakeCrimeProbe crimeProbe) Build()
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = 100000 };
        var store = new FakeRecordStore();
        var probe = new FakeProbe { NearbyCivilians = 5 };
        var crimeProbe = new FakeCrimeProbe();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            new FakeNotifier(), new FakeLog(),
            new JusticeConfig { WarrantReportSeconds = 0 }, new FakeClock(),
            probe: probe, random: () => 0.0, crimeProbe: crimeProbe);
        return (service, wanted, crimeProbe);
    }

    private static ClassifiedCrime MinorAct()
        => new(CrimeKind.Assault, CrimeSeverity.Minor, 1, "SHOVE");

    [Fact]
    public void MinorAct_DuringFiveStarChase_DoesNotRegressStars()
    {
        var (service, wanted, _) = Build();
        wanted.CurrentStars = 5;              // mid-manhunt heat
        service.Tick();                       // Wanted state
        Assert.Equal(5, wanted.CurrentStars);

        service.RecordDetectedCrime(MinorAct());   // a shove while at 5★

        Assert.Equal(5, wanted.CurrentStars);      // 5★ holds — never 1★
    }

    [Fact]
    public void MinorAct_AtLowStars_RaisesToActLevel()
    {
        var (service, wanted, _) = Build();
        wanted.CurrentStars = 0;
        service.Tick();

        service.RecordDetectedCrime(MinorAct());

        Assert.Equal(1, wanted.CurrentStars);      // act sets its own level when higher
    }

    [Fact]
    public void MajorAct_DuringLowStars_EscalatesToActLevel()
    {
        var (service, wanted, _) = Build();
        wanted.CurrentStars = 1;
        service.Tick();

        service.RecordDetectedCrime(new(CrimeKind.Murder, CrimeSeverity.Severe, 5, "MURDER"));

        Assert.Equal(5, wanted.CurrentStars);      // escalation still works
    }

    [Fact]
    public void ConsecutiveMinorActs_StarsNeverDecrease()
    {
        var (service, wanted, _) = Build();
        wanted.CurrentStars = 3;
        service.Tick();

        service.RecordDetectedCrime(MinorAct());
        Assert.Equal(3, wanted.CurrentStars);
        service.RecordDetectedCrime(MinorAct());
        Assert.Equal(3, wanted.CurrentStars);
        service.RecordDetectedCrime(MinorAct());
        Assert.Equal(3, wanted.CurrentStars);
    }

    [Fact]
    public void NewAct_StillRecordsEvidence_AndSetsWanted()
    {
        var (service, wanted, _) = Build();
        wanted.CurrentStars = 5;
        service.Tick();
        int before = service.Record.Events.Count;

        service.RecordDetectedCrime(MinorAct());   // held at 5★, but still a crime

        Assert.Equal(before + 1, service.Record.Events.Count);   // evidence recorded
        Assert.Equal(JusticeState.Wanted, service.State);
    }
}
