using System;
using System.Linq;
using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S22 v6 — warrant report realism (user UAT: "when I'm far from people,
/// someone still recognizes me from far and calls police — realistically they
/// should be very near to confirm I'm the criminal before calling").
/// The report gate used a hardcoded 30m scan — anyone within 30m "recognized"
/// the face. Now the scan radius is the configurable WarrantRecognitionRangeM
/// (default 6m): a civilian must be CLOSE enough to confirm the identity.
/// </summary>
public class WarrantRecognitionRangeTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player,
        FakeNotifier notifier, FakeRecordStore store, FakeProbe probe) Build(
        float recognitionRangeM = 6f, double roll = 0.0)
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true };
        var notifier = new FakeNotifier();
        var store = new FakeRecordStore();
        store.Status.Identity = IdentityState.Burned;
        store.Status.WarrantActive = true;
        store.Status.Notoriety = 100;
        var probe = new FakeProbe();
        var crimeProbe = new FakeCrimeProbe();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(),
            new JusticeConfig { WarrantReportSeconds = 0, WarrantRecognitionRangeM = recognitionRangeM },
            new FakeClock(),
            new MediaService(new FakeMediaNotifier(), new FakeLog(), new JusticeConfig()),
            input: null, probe: probe, random: () => roll,
            crimeProbe: crimeProbe);
        return (service, wanted, player, notifier, store, probe);
    }

    [Fact]
    public void Report_ScansConfigRange_NotHardcoded30m()
    {
        var (service, wanted, _, _, _, probe) = Build(recognitionRangeM: 6f);
        probe.NearbyCivilians = 5;       // civilians exist

        wanted.CurrentStars = 0;         // Free + warrant + burned
        service.Tick();                  // report path runs

        Assert.Equal(6f, probe.LastCiviliansRadius);   // THE fix: config range, not 30
    }

    [Fact]
    public void FarCivilians_NoReport()
    {
        var (service, wanted, _, notifier, _, probe) = Build(recognitionRangeM: 6f);
        probe.NearbyCivilians = 0;       // nobody within 6m — the face can't be confirmed

        wanted.CurrentStars = 0;
        service.Tick();
        service.Tick();

        Assert.Equal(0, wanted.CurrentStars);   // no dispatch — nobody close enough
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("recognized"));
    }

    [Fact]
    public void NearCivilian_ReportFires()
    {
        var (service, wanted, _, notifier, _, probe) = Build(recognitionRangeM: 6f, roll: 0.0);
        probe.NearbyCivilians = 2;       // two people right there — they see the face

        wanted.CurrentStars = 0;
        service.Tick();

        Assert.True(wanted.CurrentStars >= 1, "a nearby witness calls the police");
        Assert.Contains(notifier.Messages, m => m.Contains("recognized"));
    }

    // S22 v7 (user UAT: manhunt showed "WANTED 2*" while vanilla was 5★) —
    // a warrant report must NEVER downgrade the heat during a manhunt.

    [Fact]
    public void Report_WhileActivelyWanted_DoesNotFire_DowngradeImpossible()
    {
        // During an active chase (State=Wanted) warrant reports are BLOCKED —
        // they cannot overwrite the 5★ heat down to a report-streak level.
        var (service, wanted, _, notifier, _, probe) = Build(recognitionRangeM: 6f, roll: 0.0);
        probe.NearbyCivilians = 3;
        wanted.CurrentStars = 5;         // manhunt at max heat → State becomes Wanted

        service.Tick();                  // star edge: records the crime, State → Wanted
        Assert.Equal(JusticeState.Wanted, service.State);
        service.Tick();                  // report path runs — but the gate blocks it

        Assert.Equal(5, wanted.CurrentStars);   // heat untouched
        Assert.DoesNotContain(notifier.Messages, m => m.Contains("recognized"));
    }

    [Fact]
    public void Report_EscalatesFromCurrentHeat_NotFromZero()
    {
        // When a report DOES fire (free + warrant + witness within range), the
        // escalation holds or rises from the current heat — never drops it.
        var (service, wanted, _, _, _, probe) = Build(recognitionRangeM: 6f, roll: 0.0);
        probe.NearbyCivilians = 3;
        wanted.CurrentStars = 4;         // mid-manhunt heat already set

        service.Tick();                  // streak #1 would suggest 2★ — must not drop to it

        Assert.True(wanted.CurrentStars >= 4, "escalation holds or rises — never drops");
    }
}
