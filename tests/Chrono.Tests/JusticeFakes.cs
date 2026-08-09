using Chrono.Application;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>In-memory IRecordStore for justice tests (no disk I/O).</summary>
public sealed class FakeRecordStore : IRecordStore
{
    public CriminalRecord Record { get; set; } = new();
    public CharacterProfile Profile { get; set; } = new();
    public JusticeStatus Status { get; set; } = new();
    public int SaveCount { get; private set; }

    public CriminalRecord Load() => Record;
    public void SaveAtomic(CriminalRecord record) { Record = record; SaveCount++; }
    public CharacterProfile LoadProfile() => Profile;
    public void SaveProfileAtomic(CharacterProfile profile) { Profile = profile; SaveCount++; }
    public JusticeStatus LoadStatus() => Status;
    public void SaveStatusAtomic(JusticeStatus status) { Status = status; SaveCount++; }
}

public sealed class FakeWantedMonitor : IWantedMonitor
{
    public int CurrentStars { get; set; }
    public List<int> StarSets { get; } = new();
    public void SetStars(int stars)
    {
        CurrentStars = stars;
        StarSets.Add(stars);
    }
}

/// <summary>S21 — shared capture flow for justice tests: a cop REACHES the player
/// (≤3 m) while the player is stopped → cuffed (physical capture, user UAT r15).
/// Replaces the old S19 confrontation-timer helper.</summary>
public static class JusticeTestFlow
{
    public static void CaptureByProximity(JusticeService service, FakeWantedMonitor wanted, FakeCrimeProbe probe)
    {
        wanted.CurrentStars = 4;
        service.Tick();                 // Wanted state
        probe.NearestPoliceDistance = 2f;   // a cop closes in
        service.Tick();                 // stopped + cop within 3m → OnCaptured
    }

    public static void Surrender(JusticeService service, FakeWantedMonitor wanted, FakeCrimeProbe probe, FakeInput input)
    {
        wanted.CurrentStars = 4;
        service.Tick();                 // Wanted state
        probe.NearestPoliceDistance = 8f;   // a cop is near (≤12m)
        input.InteractHotkey = true;        // G pressed (level → Update computes the edge)
        input.Update();
        service.Tick();                 // surrender → custody
        input.InteractHotkey = false;
        input.Update();
    }
}
