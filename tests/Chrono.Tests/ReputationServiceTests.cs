using Chrono.Application;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>S9 reputation: crimes/escapes/hacks build notoriety; clean days, fines
/// and rehabilitation build fame; media covers milestones; report chance shifts.</summary>
public class ReputationServiceTests
{
    private static (ReputationService rep, FakeRecordStore store, FakeClock clock, FakeMediaNotifier media) Build()
    {
        var store = new FakeRecordStore();
        var clock = new FakeClock { CurrentGameDay = 100 };
        var media = new FakeMediaNotifier();
        var rep = new ReputationService(store, clock, new MediaService(media, new FakeLog(), new JusticeConfig()), new JusticeConfig());
        return (rep, store, clock, media);
    }

    [Fact]
    public void Crime_AddsNotorietyBySeverity()
    {
        var (rep, _, _, _) = Build();
        rep.OnCrime(CrimeSeverity.Minor);
        rep.OnCrime(CrimeSeverity.Moderate);
        rep.OnCrime(CrimeSeverity.Severe);

        Assert.Equal(5 + 10 + 25, rep.Notoriety);
    }

    [Fact]
    public void EscapeAndHack_AddNotoriety()
    {
        var (rep, _, _, _) = Build();
        rep.OnEscape();
        rep.OnHack();

        Assert.Equal(70, rep.Notoriety);
    }

    [Fact]
    public void ConvictionAndRelease_BuildFame()
    {
        var (rep, _, _, _) = Build();
        rep.OnConviction();   // fame +3, notoriety -10 (clamped at 0)
        rep.OnRelease();      // fame +10

        Assert.Equal(13, rep.Fame);
        Assert.Equal(0, rep.Notoriety);
    }

    [Fact]
    public void CleanDay_EarnsFame_AndFeelGoodNews()
    {
        var (rep, store, clock, media) = Build();
        store.Record.Append(new CrimeEvent("e1", CrimeSeverity.Severe, "murder", "2026-08-09T10:00:00", "Vinewood", true));

        clock.CurrentGameDay = 101;
        rep.Tick();   // a clean day (yesterday's crime only)

        Assert.Equal(2, rep.Fame);
        Assert.Contains(media.Headlines, h => h.Contains("FEEL-GOOD"));
    }

    [Fact]
    public void CrimeDay_NoFame()
    {
        var (rep, store, clock, _) = Build();
        string today = "0000-04-09T10:00:00";   // matches game-day 101 (FormatDay)
        store.Record.Append(new CrimeEvent("e1", CrimeSeverity.Moderate, "robbery", today, "Vinewood", true));

        clock.CurrentGameDay = 101;
        rep.Tick();

        Assert.Equal(0, rep.Fame);
    }

    [Fact]
    public void PublicImage_Labels()
    {
        var (rep, store, _, _) = Build();
        Assert.Equal("Unknown", rep.PublicImage);

        store.Status.Notoriety = 80;
        Assert.Equal("Known Criminal", rep.PublicImage);

        store.Status.Notoriety = 0;
        store.Status.Fame = 50;
        Assert.Equal("Local Favorite", rep.PublicImage);
    }

    [Fact]
    public void ReportChanceModifier_InfamyRaises_FameLowers()
    {
        var (rep, store, _, _) = Build();
        Assert.Equal(1.0, rep.ReportChanceModifier, 2);

        store.Status.Notoriety = 100;
        Assert.Equal(2.0, rep.ReportChanceModifier, 2);

        store.Status.Notoriety = 0;
        store.Status.Fame = 400;   // clamp at floor
        Assert.Equal(0.2, rep.ReportChanceModifier, 2);
    }

    [Fact]
    public void NotorietyMilestone_TriggersNews()
    {
        var (rep, _, _, media) = Build();
        for (int i = 0; i < 7; i++) rep.OnCrime(CrimeSeverity.Severe);   // 175 ≥ 150

        Assert.Contains(media.Headlines, h => h.Contains("CITY ON EDGE"));
        Assert.Contains(media.ViralMessages, v => v.Contains("Menace"));
    }
}
