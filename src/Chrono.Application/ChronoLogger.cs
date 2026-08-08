using System;
using System.IO;
using System.Text;
using Chrono.Application.Ports;

namespace Chrono.Application;

/// <summary>File logger with levels and WARN throttling (FR-6).</summary>
public sealed class ChronoLogger : ILogSink
{
    private readonly string _filePath;
    private readonly string _level;
    private readonly object _lock = new();
    private DateTime _lastWarn;
    private int _warnCount;

    public ChronoLogger(string filePath, string level)
    {
        _filePath = filePath;
        _level = (level ?? "info").ToLowerInvariant();
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            WriteLine("INFO", "ChronoLogger initialized");
        }
        catch
        {
            // logging must never crash the mod
        }
    }

    public void Debug(string message) => WriteIfEnabled("debug", "DEBUG", message);
    public void Info(string message) => WriteIfEnabled("info", "INFO", message);

    public void Warn(string message)
    {
        // throttle: max 5 WARN per second
        var now = DateTime.UtcNow;
        if (now - _lastWarn > TimeSpan.FromSeconds(1)) { _warnCount = 0; _lastWarn = now; }
        _warnCount++;
        if (_warnCount > 5) return;
        WriteIfEnabled("warn", "WARN", message);
    }

    public void Error(string message) => WriteIfEnabled("error", "ERROR", message);

    private void WriteIfEnabled(string level, string tag, string message)
    {
        if (!IsEnabled(level)) return;
        WriteLine(tag, message);
    }

    private bool IsEnabled(string level)
    {
        var order = new[] { "debug", "info", "warn", "error" };
        int cfgIdx = Array.IndexOf(order, _level);
        int msgIdx = Array.IndexOf(order, level);
        if (cfgIdx < 0) cfgIdx = 1;
        return msgIdx >= cfgIdx;
    }

    private void WriteLine(string tag, string message)
    {
        try
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {tag}: {message}{Environment.NewLine}";
            lock (_lock) { File.AppendAllText(_filePath, line, Encoding.UTF8); }
        }
        catch
        {
            // never throw from logging
        }
    }
}
