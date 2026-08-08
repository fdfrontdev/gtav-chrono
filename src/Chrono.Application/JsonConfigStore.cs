using System;
using System.IO;
using System.Text.Json;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>JSON config persistence (FR-5). Fail-soft: missing/corrupt file → defaults.</summary>
public sealed class JsonConfigStore : IConfigStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonConfigStore(string filePath)
    {
        _filePath = filePath;
    }

    public ChronoConfig Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return new ChronoConfig();
            var json = File.ReadAllText(_filePath);
            var parsed = JsonSerializer.Deserialize<ChronoConfig>(json, Options);
            return parsed ?? new ChronoConfig();
        }
        catch (Exception)
        {
            return new ChronoConfig(); // corrupt file → defaults (validator logs)
        }
    }

    public void Save(ChronoConfig config)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(config, Options));
        }
        catch
        {
            // persistence failure is operational — never crash
        }
    }
}
