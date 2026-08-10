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
        Assert.True(service.IsManhunt, "escape must put the state into a MANHUNT");
        Assert.Equal(101, service.ManhuntUntilDay);   // FakeClock day 100 + 1 → heat until 101
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
    public void Recapture_ServesRemainingDays_PlusEscapeCharge()   // S21 v3 (user UAT)
    {
        var (service, wanted, player, notifier, _, _, crimeProbe) = Build(roll: 0.0, money: 1000000);
        PrisonTerm(service, wanted, crimeProbe);       // 5★ severe → 30d sentence
        service.AdvancePrisonTime(30.0);               // serve ONE full day (day = 30s)
        service.Tick();
        service.AdvancePrisonTime(20.0);               // yard opens (yard at 20s of the next day)
        service.Tick();
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Dash);         // escape with 29 days remaining
        Assert.True(service.IsManhunt);
        Assert.Equal(JusticeState.Free, service.State);

        // Manhunt: commit ANOTHER severe crime while on the run
        wanted.CurrentStars = 5;
        service.Tick();
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                                // S21: physical capture
        Assert.Equal(JusticeState.Captured, service.State);
        Assert.False(service.IsManhunt);

        // Trial: (severe 30d + escape 7d) × recidivism 1.5 = 56 + remaining 29 = 85
        service.AdvanceTrialTime(60.0);
        service.Tick();                                // verdict → prison
        service.Tick();                                // intake done → confinement

        Assert.Equal(JusticeState.Prison, service.State);
        int expected = (int)Math.Round((30 + 7) * 1.5) + 29;   // 56 + 29 = 85
        Assert.Equal(expected, service.SentenceDays);
        Assert.Contains(notifier.Messages, m => m.Contains($"{expected} days"));   // SENTENCED line totals all three
    }

    [Fact]
    public void WastedDuringManhunt_Recaptured_BackToPrison()   // S21 v3: "busted, wasted, captured → back to prison"
    {
        var (service, wanted, player, notifier, _, _, crimeProbe) = Build(roll: 0.0, money: 1000000);
        PrisonTerm(service, wanted, crimeProbe);       // 30d sentence
        service.AdvancePrisonTime(30.0);               // serve 1 day
        service.Tick();
        service.AdvancePrisonTime(20.0);               // yard opens
        service.Tick();
        service.TryOpenEscapeChoice();
        service.ChooseEscape(EscapeKind.Dash);         // escape → manhunt
        Assert.True(service.IsManhunt);

        // WASTED: die during the manhunt (wanted episode) → custody on respawn
        wanted.CurrentStars = 4;                       // manhunt stars
        service.Tick();
        player.IsDead = true;
        service.Tick();                                // death edge
        player.IsDead = false;
        service.Tick();                                // respawn → custody

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.False(service.IsManhunt);
        Assert.Contains(notifier.Messages, m => m.Contains("RECAPTURED"));

        // trial → back to prison with remaining 29d + escape charge (7d ×1.5)
        service.AdvanceTrialTime(60.0);
        service.Tick();
        service.Tick();
        Assert.Equal(JusticeState.Prison, service.State);
        Assert.True(service.SentenceDays >= 29 + 7, "wasted recapture must still serve remaining days + escape charge");
    }

    [Fact]
    public void DeathInsidePrison_RespawnsInCell_NotHospital()   // S21 v3 (user UAT)
    {
        var (service, wanted, player, notifier, _, _, crimeProbe) = Build(roll: 0.0, money: 1000000);
        PrisonTerm(service, wanted, crimeProbe);       // 30d sentence
        service.Tick();                                // confinement starts

        // die INSIDE the prison (yard / escape attempt — stars are 0 in prison)
        player.IsDead = true;
        service.Tick();                                // death edge
        player.IsDead = false;
        service.Tick();                                // respawn

        Assert.Equal(JusticeState.Prison, service.State);   // sentence CONTINUES — still serving
        Assert.Contains(notifier.Messages, m => m.Contains("back to your cell"));
        Assert.Equal(new System.Numerics.Vector3(1826f, 2635f, 46f), player.TeleportCalls.Last());   // PrisonCenter
    }

    [Fact]
    public void DeathCapture_RespawnsAtPrisonHolding()   // S21 v3 (user UAT: "respawn at hospital — expected prison")
    {
        var (service, wanted, player, _, _, _, crimeProbe) = Build(roll: 0.0, money: 1000000);
        wanted.CurrentStars = 5;
        service.Tick();                    // wanted episode (manhunt-style)
        crimeProbe.NearestPoliceDistance = 2f;
        service.Tick();                    // busted → custody (stars cleared)

        // die during a wanted episode → death capture on respawn
        wanted.CurrentStars = 5;
        service.Tick();
        player.IsDead = true;
        service.Tick();                    // death edge (wanted)
        player.IsDead = false;
        service.Tick();                    // respawn → death capture

        Assert.Equal(JusticeState.Captured, service.State);
        Assert.Equal(new System.Numerics.Vector3(1826f, 2635f, 46f), player.TeleportCalls.Last());   // PrisonCenter holding
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
