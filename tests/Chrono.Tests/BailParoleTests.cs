using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S15 realism (SB-grounded: IDEO control anchor → bail; Bard Prison
/// Initiative recidivism anchor → parole + escalating-but-bounded multipliers):
/// bail out of custody with charges pending; bail revocation on a new crime;
/// supervised parole after a prison term.</summary>
public class BailParoleTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeNotifier notifier, FakeRecordStore store, FakeClock clock, FakeMediaNotifier media) Build(int money = 100000, double roll = 0.99)
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = money, DistrictName = "Vinewood" };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var clock = new FakeClock();
        var media = new FakeMediaNotifier();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(), new JusticeConfig(), clock,
            new MediaService(media, new FakeLog(), new JusticeConfig()),
            random: () => roll);
        return (service, wanted, player, notifier, store, clock, media);
    }

    private static void Capture(JusticeService service, FakeWantedMonitor wanted)
    {
        wanted.CurrentStars = 4;
        service.Tick();                    // Moderate crime + arrest (stars cleared)
    }

    [Fact]
    public void Bail_PostsAndReleases_ChargesPending()
    {
        var (service, wanted, player, notifier, store, _, _) = Build();
        Capture(service, wanted);          // Moderate: projected fine 8000 → bail 4000
        int cost = service.BailCost();

        service.PostBail();

        Assert.Equal(JusticeState.Free, service.State);
        Assert.True(service.IsOnBail);
        Assert.Equal(4000, cost);
        Assert.Contains(player.MoneyCalls, m => m == -4000);
        Assert.False(service.Warrant.IsActive);            // on bail = not a fugitive
        Assert.All(store.Record.Events, e => Assert.False(e.Charged));   // charges pending
    }

    [Fact]
    public void Bail_TooPoor_StaysInCustody()
    {
        var (service, wanted, _, notifier, _, _, _) = Build(money: 100);
        Capture(service, wanted);

        service.PostBail();

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Contains(notifier.Messages, m => m.Contains("Not enough cash"));
    }

    [Fact]
    public void BailRevoked_OnNewCrime_WarrantBack()
    {
        var (service, wanted, _, notifier, _, _, media) = Build();
        Capture(service, wanted);
        service.Tick();                    // stars already cleared → sync _lastStars
        service.PostBail();

        wanted.CurrentStars = 2;           // new crime while out on bail
        service.Tick();

        Assert.False(service.IsOnBail);
        Assert.True(service.Warrant.IsActive);
        Assert.Contains(notifier.Messages, m => m.Contains("BAIL REVOKED"));
        Assert.Contains(media.Headlines, h => h.Contains("BAIL REVOKED"));
    }

    [Fact]
    public void BailFlow_NextArrest_ChargesAllPending()
    {
        var (service, wanted, player, _, store, _, _) = Build();
        wanted.CurrentStars = 2;
        service.Tick();                    // Minor crime (2000)
        wanted.CurrentStars = 0;
        service.Tick();
        wanted.CurrentStars = 4;
        service.Tick();                    // arrest
        service.Tick();                    // sync _lastStars after the star clear
        service.PostBail();                // out on bail — charges pending

        wanted.CurrentStars = 2;           // second episode
        service.Tick();
        wanted.CurrentStars = 0;
        service.Tick();
        wanted.CurrentStars = 4;
        service.Tick();                    // next arrest → court bundles EVERYTHING
        service.AdvanceTrialTime(45.0);
        service.Tick();

        // charges: Minor(2000) + the escalation Moderate(8000) + new Minor(2000) + new Moderate(8000)
        Assert.Equal(JusticeState.Prison, service.State);
        Assert.All(store.Record.Events, e => Assert.True(e.Charged, "all events charged at the gavel"));
    }

    [Fact]
    public void PrisonRelease_StartsParole_ViolationEscalates()
    {
        var (service, wanted, player, notifier, _, clock, media) = Build();
        wanted.CurrentStars = 5;
        service.Tick();                    // Severe → prison
        service.AdvanceTrialTime(45.0);
        service.Tick();
        for (int i = 0; i < 40 && service.State == JusticeState.Prison; i++)
        {
            service.AdvancePrisonTime(30.0);
            service.Tick();
        }
        Assert.Equal(JusticeState.Free, service.State);
        Assert.True(service.ParoleDaysLeft > 0, "parole should be active after a prison term");

        wanted.CurrentStars = 2;           // new crime during parole
        service.Tick();

        Assert.Contains(wanted.StarSets, s => s >= 3);   // instant 3★+ — the state is watching
        Assert.Contains(notifier.Messages, m => m.Contains("PAROLE VIOLATION"));
        Assert.Contains(media.Headlines, h => h.Contains("PAROLE VIOLATION"));
    }

    [Fact]
    public void Parole_ExpiresAfterDays_Clean()
    {
        var (service, wanted, _, notifier, _, clock, _) = Build();
        wanted.CurrentStars = 5;
        service.Tick();
        service.AdvanceTrialTime(45.0);
        service.Tick();
        for (int i = 0; i < 40 && service.State == JusticeState.Prison; i++)
        {
            service.AdvancePrisonTime(30.0);
            service.Tick();
        }

        clock.CurrentGameDay += 5;         // parole window (3 days) passes
        service.Tick();

        Assert.Equal(0, service.ParoleDaysLeft);
        Assert.Contains(notifier.Messages, m => m.Contains("Parole complete"));
    }

    [Fact]
    public void RecidivismMultiplier_CappedAtThree()
    {
        Assert.Equal(6000, SentencingPolicy.SentenceWith(CrimeSeverity.Minor, 10).Fine);   // 2000 × 3.0 cap
        Assert.Equal(24000, SentencingPolicy.SentenceWith(CrimeSeverity.Moderate, 10).Fine); // 8000 × 3.0
    }
}
