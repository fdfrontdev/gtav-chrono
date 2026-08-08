using Chrono.Domain;

namespace Chrono.Tests;

public class CriminalRecordTests
{
    private static CrimeEvent Event(CrimeSeverity s) => new($"e-{Guid.NewGuid():N}", s, "assault", "2026-08-08T12:00:00", "Vinewood", true);

    [Fact]
    public void Append_AddsEvent()
    {
        var record = new CriminalRecord();
        record.Append(Event(CrimeSeverity.Minor));

        Assert.Single(record.Events);
        Assert.Equal(1, record.Count);
    }

    [Fact]
    public void Append_CapsAtMaxEvents()
    {
        var record = new CriminalRecord();
        for (int i = 0; i < CriminalRecord.MaxEvents + 50; i++) record.Append(Event(CrimeSeverity.Minor));

        Assert.Equal(CriminalRecord.MaxEvents, record.Count);
        Assert.Equal("e-", record.Events[0].Id.Substring(0, 2)); // oldest dropped, newest kept
    }

    [Fact]
    public void Purge_ClearsEverything()
    {
        var record = new CriminalRecord();
        record.Append(Event(CrimeSeverity.Severe));
        record.AddConviction(new Conviction(1000, 7, "2026-08-08"));

        record.Purge();

        Assert.Empty(record.Events);
        Assert.Empty(record.Convictions);
    }

    [Fact]
    public void HasSeverity_DetectsBySeverity()
    {
        var record = new CriminalRecord();
        record.Append(Event(CrimeSeverity.Severe));

        Assert.True(record.HasSeverity(CrimeSeverity.Severe));
        Assert.False(record.HasSeverity(CrimeSeverity.Minor));
    }
}

public class SentencingPolicyTests
{
    [Theory]
    [InlineData(CrimeSeverity.Minor, 2000, 0)]
    [InlineData(CrimeSeverity.Moderate, 8000, 7)]
    [InlineData(CrimeSeverity.Severe, 25000, 30)]
    public void BaseSentence_MatchesTable(CrimeSeverity severity, int fine, int days)
    {
        var s = SentencingPolicy.BaseSentence(severity);
        Assert.Equal(fine, s.Fine);
        Assert.Equal(days, s.PrisonDays);
    }

    [Fact]
    public void SentenceWith_FirstOffense_NoMultiplier()
    {
        var s = SentencingPolicy.SentenceWith(CrimeSeverity.Moderate, 0);
        Assert.Equal(8000, s.Fine);
        Assert.Equal(7, s.PrisonDays);
    }

    [Fact]
    public void SentenceWith_Recidivism_ScalesSentence()
    {
        var s = SentencingPolicy.SentenceWith(CrimeSeverity.Moderate, 2);   // ×2.0
        Assert.Equal(16000, s.Fine);
        Assert.Equal(14, s.PrisonDays);
    }

    [Fact]
    public void SentenceWith_RepeatSevere_IsHeavier()
    {
        var s = SentencingPolicy.SentenceWith(CrimeSeverity.Severe, 4);    // ×3.0
        Assert.Equal(75000, s.Fine);
        Assert.Equal(90, s.PrisonDays);
    }
}

public class PrisonCalendarTests
{
    [Fact]
    public void Advance_NoDay_NoBoundary()
    {
        var cal = new PrisonCalendar(30);
        Assert.False(cal.Advance(10));
        Assert.Equal(0, cal.DayIndex);
    }

    [Fact]
    public void Advance_CrossingBoundary_FiresOnce()
    {
        var cal = new PrisonCalendar(30);
        Assert.True(cal.Advance(30));
        Assert.Equal(1, cal.DayIndex);
        Assert.Equal(0.0, cal.DayProgressSeconds, 3);
    }

    [Fact]
    public void Advance_AccumulatesAcrossTicks()
    {
        var cal = new PrisonCalendar(30);
        cal.Advance(10);
        Assert.True(cal.Advance(25));   // 35 total → 1 day + 5s
        Assert.Equal(1, cal.DayIndex);
        Assert.Equal(5.0, cal.DayProgressSeconds, 3);
    }

    [Fact]
    public void Advance_ZeroOrNegativeDt_IsSafe()
    {
        var cal = new PrisonCalendar(30);
        Assert.False(cal.Advance(0));
        Assert.False(cal.Advance(-5));
        Assert.Equal(0, cal.DayIndex);
    }

    [Fact]
    public void Advance_MultipleDaysInOneTick()
    {
        var cal = new PrisonCalendar(30);
        bool boundary = false;
        for (int i = 0; i < 3; i++) boundary = cal.Advance(30);
        Assert.True(boundary);
        Assert.Equal(3, cal.DayIndex);
    }
}

public class CharacterProfileTests
{
    [Fact]
    public void DefaultProfile_Is27YearsOld()
    {
        var profile = new CharacterProfile();
        Assert.Equal(27, profile.AgeYears);
    }

    [Fact]
    public void AddDays_AgesCharacter()
    {
        var profile = new CharacterProfile();
        profile.AddDays(30);   // one month inside

        Assert.Equal(27 * 365 + 30, profile.AgeDays);
    }

    [Fact]
    public void AddDays_Negative_IsIgnored()
    {
        var profile = new CharacterProfile();
        profile.AddDays(-10);

        Assert.Equal(27 * 365, profile.AgeDays);
    }

    [Fact]
    public void AddDays_PastBirthday_AdvancesYear()
    {
        var profile = new CharacterProfile();
        profile.AddDays(365);
        Assert.Equal(28, profile.AgeYears);
    }
}
