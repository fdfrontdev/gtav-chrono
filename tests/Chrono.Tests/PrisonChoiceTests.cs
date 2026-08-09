using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S13: prison escape is the PLAYER's choice — no auto-escape; the yard
/// window opens an escape plan (X powers / Z stealth / B fight); failure = solitary
/// confinement; the prison outfit swaps on confinement and restores on release.</summary>
public class PrisonChoiceTests
{
    private static (JusticeService service, FakeWantedMonitor wanted, FakePlayer player, FakeNotifier notifier, FakeRecordStore store, FakePrisonOutfit outfit, FakeCrimeProbe crimeProbe) Build(double roll = 0.99, int money = 100000)
    {
        var wanted = new FakeWantedMonitor();
        var player = new FakePlayer { IsVisible = true, Money = money, DistrictName = "Bolingbroke" };
        var store = new FakeRecordStore();
        var notifier = new FakeNotifier();
        var outfit = new FakePrisonOutfit();
        var input = new FakeInput();
        var crimeProbe = new FakeCrimeProbe();
        var service = new JusticeService(
            wanted, player, store,
            new IdentityService(store, new FakeLog()),
            new WarrantService(store, new FakeLog()),
            notifier, new FakeLog(), new JusticeConfig { PrisonDayRealSeconds = 30 }, new FakeClock(),
            input: input, random: () => roll, outfit: outfit, crimeProbe: crimeProbe);
        return (service, wanted, player, notifier, store, outfit, crimeProbe);
    }

    /// S12/S13: commit a crime → prison sentence via direct 5★ (Severe) capture
    private static JusticeService PrisonTerm(JusticeService service, FakeWantedMonitor wanted, FakeCrimeProbe crimeProbe)
    {
        wanted.CurrentStars = 5;
        service.Tick();                    // Severe crime → Wanted
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // S21: cop reaches you -> cuffed
        service.AdvanceTrialTime(45.0);
        service.Tick();                    // verdict → prison
        return service;
    }

    [Fact]
    public void Confinement_AppliesPrisonOutfit()
    {
        var (service, wanted, _, _, _, outfit, crimeProbe) = Build();
        PrisonTerm(service, wanted, crimeProbe);

        Assert.Equal(JusticeState.Prison, service.State);
        Assert.Equal(1, outfit.ApplyCount);
    }

    [Fact]
    public void Release_RestoresOutfit()
    {
        var (service, wanted, _, _, _, outfit, crimeProbe) = Build();
        PrisonTerm(service, wanted, crimeProbe);
        for (int i = 0; i < 40 && service.State == JusticeState.Prison; i++)
        {
            service.AdvancePrisonTime(30.0);   // one day per advance
            service.Tick();
        }

        Assert.Equal(JusticeState.Free, service.State);
        Assert.Equal(1, outfit.RestoreCount);
    }

    [Fact]
    public void OutsideRadius_NoAutoEscape_GuardEscortsBack()
    {
        // S13 fix: wandering past the yard radius is NOT an escape — the guard
        // teleports you back to the cell
        var (service, wanted, player, notifier, _, _, crimeProbe) = Build();
        PrisonTerm(service, wanted, crimeProbe);

        player.Position = new System.Numerics.Vector3(2000f, 2700f, 46f);   // way outside
        service.Tick();

        Assert.Equal(JusticeState.Prison, service.State);                   // still confined
        Assert.Contains(player.TeleportCalls, t => t == new System.Numerics.Vector3(1826f, 2635f, 46f));   // back in the cell
        Assert.Contains(notifier.Messages, m => m.Contains("escorts you back"));
    }

    [Fact]
    public void YardG_OpensEscapeChoice_AndExpires()
    {
        var (service, wanted, player, notifier, _, _, crimeProbe) = Build();
        PrisonTerm(service, wanted, crimeProbe);
        service.AdvancePrisonTime(25.0);     // yard window opens (day 1, 25s of 30s)
        service.Tick();
        service.TryOpenEscapeChoice();

        Assert.True(service.IsEscapeChoiceOpen);
        Assert.Contains(notifier.Messages, m => m.Contains("ESCAPE PLAN"));

        service.AdvanceEscapeTime(11.0);     // 10s window expires
        service.Tick();
        Assert.False(service.IsEscapeChoiceOpen);
        Assert.Equal(JusticeState.Prison, service.State);   // no auto-escape
    }

    [Fact]
    public void PowersChoice_AlwaysEscapes()
    {
        var (service, wanted, player, _, _, _, crimeProbe) = Build(roll: 0.0);   // even bad luck
        PrisonTerm(service, wanted, crimeProbe);
        service.AdvancePrisonTime(25.0);
        service.Tick();
        service.TryOpenEscapeChoice();

        service.ChooseEscape(EscapeKind.Dash);   // X = powers

        Assert.Equal(JusticeState.Free, service.State);
        Assert.NotEqual(new System.Numerics.Vector3(1826f, 2635f, 46f), player.TeleportCalls.Last());   // out of prison
        Assert.Equal(4, wanted.CurrentStars);    // manhunt (ManhuntStars)
    }

    [Fact]
    public void StealthFailure_SolitaryConfinement()
    {
        var (service, wanted, _, notifier, _, _, crimeProbe) = Build(roll: 0.99);   // > 0.5 → fail
        PrisonTerm(service, wanted, crimeProbe);
        int daysBefore = service.SentenceDays;
        service.AdvancePrisonTime(25.0);
        service.Tick();
        service.TryOpenEscapeChoice();

        service.ChooseEscape(EscapeKind.Stealth);   // Z = stealth, 50% — fails here

        Assert.Equal(JusticeState.Prison, service.State);
        Assert.Equal(daysBefore + 3, service.SentenceDays);   // solitary +3
        Assert.Contains(notifier.Messages, m => m.Contains("SOLITARY"));
    }

    [Fact]
    public void FightSuccess_Escapes()
    {
        var (service, wanted, player, _, _, _, crimeProbe) = Build(roll: 0.1);   // < 0.7 → success
        PrisonTerm(service, wanted, crimeProbe);
        service.AdvancePrisonTime(25.0);
        service.Tick();
        service.TryOpenEscapeChoice();

        service.ChooseEscape(EscapeKind.Fight);   // B = fight, 70%

        Assert.Equal(JusticeState.Free, service.State);
    }

    [Fact]
    public void Escape_RestoresOutfit()
    {
        var (service, wanted, _, _, _, outfit, crimeProbe) = Build(roll: 0.0);
        PrisonTerm(service, wanted, crimeProbe);
        service.AdvancePrisonTime(25.0);
        service.Tick();
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Dash);

        Assert.Equal(1, outfit.RestoreCount);
    }
}
