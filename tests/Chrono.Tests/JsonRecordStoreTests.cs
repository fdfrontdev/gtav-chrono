using Chrono.Boundary;
using Chrono.Domain;

namespace Chrono.Tests;

/// <summary>JsonRecordStore persistence tests: round-trip, atomicity, corruption fallback.</summary>
public class JsonRecordStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "chrono-test-" + Guid.NewGuid().ToString("N"));

    public JsonRecordStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private JsonRecordStore CreateStore() => new(_dir, new FakeLog());

    [Fact]
    public void Load_MissingFiles_ReturnsDefaults()
    {
        var store = CreateStore();

        var record = store.Load();
        var profile = store.LoadProfile();

        Assert.Empty(record.Events);
        Assert.Equal(27, profile.AgeYears);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = CreateStore();
        var record = new CriminalRecord();
        record.Append(new CrimeEvent("e1", CrimeSeverity.Severe, "murder", "2026-08-08T12:00:00", "Vinewood", true));
        record.AddConviction(new Conviction("c" + 25000, 25000, 30, "2026-08-08"));
        store.SaveAtomic(record);

        var profile = new CharacterProfile();
        profile.AddDays(30);
        store.SaveProfileAtomic(profile);

        var loaded = store.Load();
        var loadedProfile = store.LoadProfile();

        Assert.Single(loaded.Events);
        Assert.Equal(CrimeSeverity.Severe, loaded.Events[0].Severity);
        Assert.True(loaded.Events[0].Burned);
        Assert.Single(loaded.Convictions);
        Assert.Equal(27 * 365 + 30, loadedProfile.AgeDays);
    }

    [Fact]
    public void CorruptRecordFile_ReturnsFreshAndBacksUp()
    {
        File.WriteAllText(Path.Combine(_dir, "record.json"), "{not valid json!!");
        var store = CreateStore();

        var record = store.Load();

        Assert.Empty(record.Events);
        Assert.True(File.Exists(Path.Combine(_dir, "record.json.bak")), "corrupt file should be backed up");
    }

    [Fact]
    public void CorruptProfileFile_ReturnsDefaultProfile()
    {
        File.WriteAllText(Path.Combine(_dir, "profile.json"), "garbage");
        var store = CreateStore();

        Assert.Equal(27, store.LoadProfile().AgeYears);
    }

    [Fact]
    public void SaveAtomic_NeverLeavesTempFileBehind()
    {
        var store = CreateStore();
        store.SaveAtomic(new CriminalRecord());

        Assert.False(File.Exists(Path.Combine(_dir, "record.json.tmp")), "temp file must be cleaned up");
        Assert.True(File.Exists(Path.Combine(_dir, "record.json")));
    }
}
