using System;
using System.IO;
using Chrono.Application.Ports;
using Chrono.Boundary;
using Chrono.Domain;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S22 — SQLite record store (user UAT: "use a relational database like sqlite
/// where we can implement idempotency and other non-functional aspects").
/// Every test runs against its own temp directory (fresh chrono.db per test).
/// </summary>
public class SqliteRecordStoreTests : IDisposable
{
    private readonly string _dir;

    public SqliteRecordStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "chrono-sqlite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private SqliteRecordStore NewStore() => new(_dir, new FakeLog());

    // ── idempotency ──

    [Fact]
    public void Append_SameEventIdTwice_StoresOnce()   // the "loose math" killer: no dupes
    {
        var store = NewStore();
        var ev = new CrimeEvent("e1", CrimeSeverity.Severe, "murder", "2026-08-09T10:00:00", "Davis", true, false);

        var r1 = new CriminalRecord();
        r1.Append(ev);
        store.SaveAtomic(r1);

        var r2 = new CriminalRecord();           // same event appended AGAIN (double-tick edge)
        r2.Append(ev);
        store.SaveAtomic(r2);

        var loaded = store.Load();
        Assert.Single(loaded.Events);
        Assert.Equal("e1", loaded.Events[0].Id);
    }

    [Fact]
    public void SaveSameConvictionTwice_StoresOnce()
    {
        var store = NewStore();
        var r = new CriminalRecord();
        r.AddConviction(new Conviction("c1", 25000, 30, "2026-08-09T10:00:00"));
        store.SaveAtomic(r);
        store.SaveAtomic(r);   // idempotent re-save

        Assert.Single(store.Load().Convictions);
    }

    // ── junction: sequence of what each verdict sentenced ──

    [Fact]
    public void Conviction_IsLinkedToTheEventsItCharged()
    {
        var store = NewStore();
        var r = new CriminalRecord();
        r.Append(new CrimeEvent("e1", CrimeSeverity.Severe, "murder", "2026-08-09T10:00:00", "Davis", true));
        r.Append(new CrimeEvent("e2", CrimeSeverity.Moderate, "prison_escape", "2026-08-09T11:00:00", "Bolingbroke", true));
        // verdict: both events charged, one conviction
        r.Events[0] = r.Events[0] with { Charged = true };
        r.Events[1] = r.Events[1] with { Charged = true };
        r.AddConviction(new Conviction("c1", 33000, 37, "2026-08-09T12:00:00"));
        store.SaveAtomic(r);

        var loaded = store.Load();
        Assert.Equal(2, loaded.Events.Count(e => e.Charged));
        Assert.Single(loaded.Convictions);
        Assert.True(loaded.Convictions[0].Id == "c1");
    }

    // ── round-trip fidelity ──

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var store = NewStore();
        var r = new CriminalRecord();
        r.Append(new CrimeEvent("e1", CrimeSeverity.Moderate, "assault", "2026-08-09T09:00:00", "Vinewood", false, true));
        r.AddConviction(new Conviction("c1", 8000, 7, "2026-08-09T09:30:00"));
        store.SaveAtomic(r);

        var loaded = store.Load();
        var ev = Assert.Single(loaded.Events);
        Assert.Equal(CrimeSeverity.Moderate, ev.Severity);
        Assert.Equal("assault", ev.Kind);
        Assert.True(ev.Charged);
        Assert.False(ev.Burned);
        var c = Assert.Single(loaded.Convictions);
        Assert.Equal(8000, c.Fine);
        Assert.Equal(7, c.PrisonDays);
    }

    // ── profile + status ──

    [Fact]
    public void Profile_RoundTrips()
    {
        var store = NewStore();
        var p = new CharacterProfile { AgeDays = 27 * 365, Surgeries = 2, DaysServed = 14 };
        store.SaveProfileAtomic(p);
        var loaded = store.LoadProfile();
        Assert.Equal(p.AgeDays, loaded.AgeDays);
        Assert.Equal(2, loaded.Surgeries);
        Assert.Equal(14, loaded.DaysServed);
    }

    [Fact]
    public void Status_RoundTrips()
    {
        var store = NewStore();
        var s = new JusticeStatus { Identity = IdentityState.Burned, WarrantActive = true,
            WarrantSinceGameTime = "2026-08-09", LastHackDay = 3, Notoriety = 42, Fame = 7 };
        store.SaveStatusAtomic(s);
        var loaded = store.LoadStatus();
        Assert.Equal(IdentityState.Burned, loaded.Identity);
        Assert.True(loaded.WarrantActive);
        Assert.Equal(42, loaded.Notoriety);
        Assert.Equal(7, loaded.Fame);
    }

    // ── legacy JSON migration ──

    [Fact]
    public void LegacyJson_MigratesIntoDb_AndRenamesToBak()
    {
        // seed a legacy record.json + status.json
        var recordJson = Path.Combine(_dir, "record.json");
        File.WriteAllText(recordJson, """
            {"Events":[{"Id":"e1","Severity":2,"Kind":"murder","GameTime":"2026-08-08T12:00:00",
            "District":"Davis","Burned":true,"Charged":false}],
            "Convictions":[]}
            """);
        var statusJson = Path.Combine(_dir, "status.json");
        File.WriteAllText(statusJson,
            "{\"Identity\":1,\"WarrantActive\":false,\"WarrantSinceGameTime\":null," +
            "\"LastSurgeryDay\":0,\"LastHackDay\":0,\"Notoriety\":5,\"Fame\":2}");

        var store = NewStore();   // ctor runs the migration

        var record = store.Load();
        var ev = Assert.Single(record.Events);
        Assert.Equal(CrimeSeverity.Severe, ev.Severity);
        Assert.Equal("murder", ev.Kind);
        Assert.False(File.Exists(recordJson), "record.json must be renamed to .bak after migration");
        Assert.True(File.Exists(recordJson + ".bak"));
        Assert.Equal(5, store.LoadStatus().Notoriety);
    }
}
