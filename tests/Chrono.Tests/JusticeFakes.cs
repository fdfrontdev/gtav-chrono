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
}
