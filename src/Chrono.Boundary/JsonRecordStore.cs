using System;
using System.IO;
using System.Text.Json;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Boundary;

/// <summary>
/// JSON persistence for the justice layer (FR-1.5/FR-7.1). Writes are crash-safe:
/// temp file first, then copy-over + delete (net48 has no File.Move overwrite).
/// A corrupt main file is preserved as .bak and a fresh record is returned.
/// </summary>
public sealed class JsonRecordStore : IRecordStore
{
    private readonly string _recordPath;
    private readonly string _profilePath;
    private readonly string _statusPath;
    private readonly ILogSink _log;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public JsonRecordStore(string directory, ILogSink log)
    {
        _log = log;
        _recordPath = Path.Combine(directory, "record.json");
        _profilePath = Path.Combine(directory, "profile.json");
        _statusPath = Path.Combine(directory, "status.json");
    }

    public CriminalRecord Load()
    {
        try
        {
            if (!File.Exists(_recordPath)) return new CriminalRecord();
            var json = File.ReadAllText(_recordPath);
            return JsonSerializer.Deserialize<CriminalRecord>(json, _options) ?? new CriminalRecord();
        }
        catch (Exception ex)
        {
            _log.Error($"record.json load failed: {ex.Message}");
            TryBackup(_recordPath);
            return new CriminalRecord();
        }
    }

    public void SaveAtomic(CriminalRecord record)
    {
        try
        {
            WriteAtomic(_recordPath, JsonSerializer.Serialize(record, _options));
        }
        catch (Exception ex)
        {
            _log.Error($"record.json save failed: {ex.Message}");
        }
    }

    public CharacterProfile LoadProfile()
    {
        try
        {
            if (!File.Exists(_profilePath)) return new CharacterProfile();
            var json = File.ReadAllText(_profilePath);
            return JsonSerializer.Deserialize<CharacterProfile>(json, _options) ?? new CharacterProfile();
        }
        catch (Exception ex)
        {
            _log.Error($"profile.json load failed: {ex.Message}");
            TryBackup(_profilePath);
            return new CharacterProfile();
        }
    }

    public void SaveProfileAtomic(CharacterProfile profile)
    {
        try
        {
            WriteAtomic(_profilePath, JsonSerializer.Serialize(profile, _options));
        }
        catch (Exception ex)
        {
            _log.Error($"profile.json save failed: {ex.Message}");
        }
    }

    public JusticeStatus LoadStatus()
    {
        try
        {
            if (!File.Exists(_statusPath)) return new JusticeStatus();
            var json = File.ReadAllText(_statusPath);
            return JsonSerializer.Deserialize<JusticeStatus>(json, _options) ?? new JusticeStatus();
        }
        catch (Exception ex)
        {
            _log.Error($"status.json load failed: {ex.Message}");
            TryBackup(_statusPath);
            return new JusticeStatus();
        }
    }

    public void SaveStatusAtomic(JusticeStatus status)
    {
        try
        {
            WriteAtomic(_statusPath, JsonSerializer.Serialize(status, _options));
        }
        catch (Exception ex)
        {
            _log.Error($"status.json save failed: {ex.Message}");
        }
    }

    private static void WriteAtomic(string path, string json)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(path)) File.Copy(tmp, path, true);   // never leave the main file missing
        else File.Move(tmp, path);
        try { File.Delete(tmp); } catch { /* best effort */ }
    }

    private void TryBackup(string path)
    {
        try
        {
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        }
        catch
        {
            // best effort — never crash the load path
        }
    }
}
