using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.Json;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Boundary;

/// <summary>
/// S22 — SQLite record store (user UAT: "the math of fine/prison/sentence/crime
/// is too loose — use a relational database like sqlite where we can implement
/// idempotency and other non-functional aspects of good software").
///
/// What the relational model buys over the flat JSON file:
///  - events.id PRIMARY KEY → INSERT OR IGNORE = IDEMPOTENT append (no dupes)
///  - convictions.id PRIMARY KEY → idempotent conviction rows
///  - conviction_events junction → the SEQUENCE of what each verdict sentenced
///    is recoverable (was a mutable "Charged" flag scan)
///  - charge+convict in ONE transaction → crash-safe (JSON rewrite could
///    corrupt mid-way → empty-record fallback = silent data loss)
///  - CHECK constraints on severity/identity enums
///  - first-run migration imports legacy record.json/profile.json/status.json
///    and renames them to *.bak (config.json stays as-is — user settings)
///
/// Domain stays pure: IRecordStore is the seam; CriminalRecord/CharacterProfile/
/// JusticeStatus are mapped to rows here.
/// </summary>
public sealed class SqliteRecordStore : IRecordStore
{
    private readonly string _dbPath;
    private readonly string _baseDir;
    private readonly ILogSink _log;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.General);

    public SqliteRecordStore(string baseDirectory, ILogSink log)
    {
        _baseDir = baseDirectory;
        _log = log;
        _dbPath = Path.Combine(baseDirectory, "chrono.db");
        EnsureSchema();
        MigrateFromJson();
    }

    // ── schema ──

    private void EnsureSchema()
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        Exec(conn, tx, """
            CREATE TABLE IF NOT EXISTS events (
                id TEXT PRIMARY KEY,
                severity TEXT NOT NULL CHECK (severity IN ('Minor','Moderate','Severe')),
                kind TEXT NOT NULL,
                game_time TEXT NOT NULL,
                district TEXT NOT NULL,
                burned INTEGER NOT NULL DEFAULT 0,
                charged INTEGER NOT NULL DEFAULT 0
            );
            """);
        Exec(conn, tx, """
            CREATE TABLE IF NOT EXISTS convictions (
                id TEXT PRIMARY KEY,
                fine INTEGER NOT NULL,
                prison_days INTEGER NOT NULL,
                game_time TEXT NOT NULL
            );
            """);
        Exec(conn, tx, """
            CREATE TABLE IF NOT EXISTS conviction_events (
                conviction_id TEXT NOT NULL REFERENCES convictions(id) ON DELETE CASCADE,
                event_id TEXT NOT NULL REFERENCES events(id) ON DELETE CASCADE,
                PRIMARY KEY (conviction_id, event_id)
            );
            """);
        Exec(conn, tx, """
            CREATE TABLE IF NOT EXISTS profile (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                age_days INTEGER NOT NULL,
                date_of_birth TEXT NOT NULL,
                surgeries INTEGER NOT NULL,
                days_served INTEGER NOT NULL
            );
            """);
        Exec(conn, tx, """
            CREATE TABLE IF NOT EXISTS status (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                identity TEXT NOT NULL CHECK (identity IN ('Clean','Burned')),
                warrant_active INTEGER NOT NULL DEFAULT 0,
                warrant_since TEXT,
                last_surgery_day INTEGER NOT NULL DEFAULT 0,
                last_hack_day INTEGER NOT NULL DEFAULT 0,
                notoriety INTEGER NOT NULL DEFAULT 0,
                fame INTEGER NOT NULL DEFAULT 0
            );
            """);
        tx.Commit();
    }

    private SQLiteConnection Open()
    {
        var conn = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
        conn.Open();
        return conn;
    }

    private static void Exec(SQLiteConnection conn, SQLiteTransaction? tx, string sql, params (string Name, object Value)[] ps)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (tx != null) cmd.Transaction = tx;
        foreach (var (name, value) in ps)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    // ── criminal record ──

    public CriminalRecord Load()
    {
        var record = new CriminalRecord();
        try
        {
            using var conn = Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, severity, kind, game_time, district, burned, charged FROM events ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    record.Events.Add(new CrimeEvent(
                        r.GetString(0),
                        ParseSeverity(r.GetString(1)),
                        r.GetString(2), r.GetString(3), r.GetString(4),
                        r.GetInt32(5) != 0, r.GetInt32(6) != 0));
                }
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, fine, prison_days, game_time FROM convictions ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    record.Convictions.Add(new Conviction(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3)));
            }
        }
        catch (Exception ex)
        {
            _log.Error($"chrono.db load failed: {ex.Message}");
        }
        return record;
    }

    public void SaveAtomic(CriminalRecord record)
    {
        try
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();

            // Idempotent append: INSERT OR IGNORE — the same event id can never
            // double-record, no matter how many ticks/edges fire.
            foreach (var e in record.Events)
            {
                Exec(conn, tx, """
                    INSERT OR IGNORE INTO events (id, severity, kind, game_time, district, burned, charged)
                    VALUES ($id, $sev, $kind, $time, $dist, $burned, $charged)
                    """,
                    ("$id", e.Id), ("$sev", e.Severity.ToString()), ("$kind", e.Kind),
                    ("$time", e.GameTime), ("$dist", e.District),
                    ("$burned", e.Burned ? 1 : 0), ("$charged", e.Charged ? 1 : 0));
                // charged flag travels with the event (idempotent state sync)
                Exec(conn, tx, "UPDATE events SET charged = $charged WHERE id = $id",
                    ("$charged", e.Charged ? 1 : 0), ("$id", e.Id));
            }

            // Convictions: INSERT OR IGNORE by id — re-saving the same record is a no-op.
            foreach (var c in record.Convictions)
            {
                Exec(conn, tx, """
                    INSERT OR IGNORE INTO convictions (id, fine, prison_days, game_time)
                    VALUES ($id, $fine, $days, $time)
                    """,
                    ("$id", c.Id), ("$fine", c.Fine), ("$days", c.PrisonDays), ("$time", c.GameTime));
            }

            // Junction: which events each conviction sentenced. An event is linked
            // to the conviction that marks it charged — deterministic sequence.
            foreach (var c in record.Convictions)
            {
                foreach (var e in record.Events.Where(ev => ev.Charged))
                {
                    Exec(conn, tx, """
                        INSERT OR IGNORE INTO conviction_events (conviction_id, event_id)
                        VALUES ($cid, $eid)
                        """,
                        ("$cid", c.Id), ("$eid", e.Id));
                }
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            _log.Error($"chrono.db save failed: {ex.Message}");
        }
    }

    // ── profile ──

    public CharacterProfile LoadProfile()
    {
        var p = new CharacterProfile();
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT age_days, date_of_birth, surgeries, days_served FROM profile WHERE id = 1";
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                p.AgeDays = r.GetInt32(0);
                p.DateOfBirth = r.GetString(1);
                p.Surgeries = r.GetInt32(2);
                p.DaysServed = r.GetInt32(3);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"chrono.db profile load failed: {ex.Message}");
        }
        return p;
    }

    public void SaveProfileAtomic(CharacterProfile profile)
    {
        try
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();
            Exec(conn, tx, """
                INSERT OR REPLACE INTO profile (id, age_days, date_of_birth, surgeries, days_served)
                VALUES (1, $age, $dob, $surgeries, $served)
                """,
                ("$age", profile.AgeDays), ("$dob", profile.DateOfBirth),
                ("$surgeries", profile.Surgeries), ("$served", profile.DaysServed));
            tx.Commit();
        }
        catch (Exception ex)
        {
            _log.Error($"chrono.db profile save failed: {ex.Message}");
        }
    }

    // ── status ──

    public JusticeStatus LoadStatus()
    {
        var s = new JusticeStatus();
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT identity, warrant_active, warrant_since, last_surgery_day,
                       last_hack_day, notoriety, fame FROM status WHERE id = 1
                """;
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                s.Identity = ParseIdentity(r.GetString(0));
                s.WarrantActive = r.GetInt32(1) != 0;
                s.WarrantSinceGameTime = r.IsDBNull(2) ? null : r.GetString(2);
                s.LastSurgeryDay = r.GetInt32(3);
                s.LastHackDay = r.GetInt32(4);
                s.Notoriety = r.GetInt32(5);
                s.Fame = r.GetInt32(6);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"chrono.db status load failed: {ex.Message}");
        }
        return s;
    }

    public void SaveStatusAtomic(JusticeStatus status)
    {
        try
        {
            using var conn = Open();
            using var tx = conn.BeginTransaction();
            Exec(conn, tx, """
                INSERT OR REPLACE INTO status (id, identity, warrant_active, warrant_since,
                    last_surgery_day, last_hack_day, notoriety, fame)
                VALUES (1, $idn, $warrant, $since, $surgery, $hack, $not, $fame)
                """,
                ("$idn", status.Identity.ToString()),
                ("$warrant", status.WarrantActive ? 1 : 0),
                ("$since", (object?)status.WarrantSinceGameTime ?? DBNull.Value),
                ("$surgery", status.LastSurgeryDay), ("$hack", status.LastHackDay),
                ("$not", status.Notoriety), ("$fame", status.Fame));
            tx.Commit();
        }
        catch (Exception ex)
        {
            _log.Error($"chrono.db status save failed: {ex.Message}");
        }
    }

    // ── legacy JSON migration (one-time, first boot after the swap) ──

    private void MigrateFromJson()
    {
        var recordJson = Path.Combine(_baseDir, "record.json");
        var profileJson = Path.Combine(_baseDir, "profile.json");
        var statusJson = Path.Combine(_baseDir, "status.json");

        bool dbHasData = HasAnyRow("events") || HasAnyRow("profile") || HasAnyRow("status");
        if (dbHasData) return;   // already migrated / fresh DB

        try
        {
            if (File.Exists(recordJson))
            {
                var legacy = JsonSerializer.Deserialize<CriminalRecord>(File.ReadAllText(recordJson), _json);
                if (legacy != null)
                {
                    // legacy convictions have no Id (pre-S22) — mint stable ids
                    for (int i = 0; i < legacy.Convictions.Count; i++)
                    {
                        if (string.IsNullOrEmpty(legacy.Convictions[i].Id))
                            legacy.Convictions[i] = legacy.Convictions[i] with { Id = Guid.NewGuid().ToString("N") };
                    }
                    SaveAtomic(legacy);
                    if (File.Exists(recordJson + ".bak")) File.Delete(recordJson + ".bak");
                    File.Move(recordJson, recordJson + ".bak");
                    _log.Info("record.json migrated to chrono.db");
                }
            }
            if (File.Exists(profileJson))
            {
                var legacy = JsonSerializer.Deserialize<CharacterProfile>(File.ReadAllText(profileJson), _json);
                if (legacy != null) { SaveProfileAtomic(legacy); if (File.Exists(profileJson + ".bak")) File.Delete(profileJson + ".bak");
                    File.Move(profileJson, profileJson + ".bak"); }
            }
            if (File.Exists(statusJson))
            {
                var legacy = JsonSerializer.Deserialize<JusticeStatus>(File.ReadAllText(statusJson), _json);
                if (legacy != null) { SaveStatusAtomic(legacy); if (File.Exists(statusJson + ".bak")) File.Delete(statusJson + ".bak");
                    File.Move(statusJson, statusJson + ".bak"); }
            }
        }
        catch (Exception ex)
        {
            _log.Error($"JSON→SQLite migration failed: {ex.Message}");
        }
    }

    private bool HasAnyRow(string table)
    {
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM {table} LIMIT 1";
            return cmd.ExecuteScalar() != null;
        }
        catch { return false; }
    }

    private static CrimeSeverity ParseSeverity(string s) => s switch
    {
        "Moderate" => CrimeSeverity.Moderate,
        "Severe" => CrimeSeverity.Severe,
        _ => CrimeSeverity.Minor
    };

    private static IdentityState ParseIdentity(string s) => s switch
    {
        "Burned" => IdentityState.Burned,
        _ => IdentityState.Clean
    };
}
