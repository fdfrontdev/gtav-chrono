using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Chrono.Domain;

/// <summary>Result of config validation — warnings collected, invalid values replaced by defaults.</summary>
public sealed record ValidationResult(ChronoConfig Config, IReadOnlyList<string> Warnings);

/// <summary>
/// Pure validation of <see cref="ChronoConfig"/> (SRS §7). Fail-soft: every invalid value is
/// replaced with its default and reported as a warning — the mod always starts.
/// </summary>
public static class ConfigValidator
{
    public static ValidationResult Validate(ChronoConfig raw)
    {
        if (raw == null)
        {
            var empty = new ChronoConfig();
            return new ValidationResult(empty, new[] { "config was null — using defaults" });
        }

        var warnings = new List<string>();
        var cfg = raw;

        ValidateMenuKey(cfg, warnings);
        ValidateDash(cfg.Dash, warnings);
        ValidateTimeStop(cfg.TimeStop, warnings);
        ValidateTeleport(cfg.Teleport, warnings);
        ValidateVisual(cfg.Visual, warnings);
        ValidateLogging(cfg.Logging, warnings);

        return new ValidationResult(cfg, warnings);
    }

    private static void ValidateMenuKey(ChronoConfig cfg, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(cfg.MenuKey))
        {
            cfg.MenuKey = "F9";
            warnings.Add("menuKey empty — using F9");
        }
    }

    private static void ValidateDash(DashConfig dash, List<string> warnings)
    {
        if (dash.Range < 3.0f || dash.Range > 15.0f)
        {
            dash.Range = 7.0f;
            warnings.Add($"dash.range {Format(dash.Range)} outside [3,15] — using 7");
        }
        if (dash.MaxRange < dash.Range)
        {
            dash.MaxRange = Math.Max(dash.Range, 15.0f);
            warnings.Add("dash.maxRange < dash.range — clamped");
        }
    }

    private static void ValidateTimeStop(TimeStopConfig ts, List<string> warnings)
    {
        if (ts.MaintenanceIntervalMs < 250 || ts.MaintenanceIntervalMs > 10000)
        {
            ts.MaintenanceIntervalMs = 2000;
            warnings.Add("timeStop.maintenanceIntervalMs outside [250,10000] — using 2000");
        }
        if (ts.MaxFrozenEntities < 1 || ts.MaxFrozenEntities > 2048)
        {
            ts.MaxFrozenEntities = 512;
            warnings.Add("timeStop.maxFrozenEntities outside [1,2048] — using 512");
        }
    }

    private static void ValidateTeleport(TeleportConfig tp, List<string> warnings)
    {
        if (tp.GroundProbeDistance < 10.0f || tp.GroundProbeDistance > 500.0f)
        {
            tp.GroundProbeDistance = 100.0f;
            warnings.Add("teleport.groundProbeDistance outside [10,500] — using 100");
        }
    }

    private static void ValidateVisual(VisualConfig v, List<string> warnings)
    {
        if (v.TimeStop.TintStrength < 0.0f || v.TimeStop.TintStrength > 1.0f)
        {
            v.TimeStop.TintStrength = 0.4f;
            warnings.Add("visual.timeStop.tintStrength outside [0,1] — using 0.4");
        }
    }

    private static void ValidateLogging(LoggingConfig log, List<string> warnings)
    {
        var valid = new[] { "debug", "info", "warn", "error" };
        if (!valid.Contains(log.Level, StringComparer.OrdinalIgnoreCase))
        {
            log.Level = "info";
            warnings.Add($"logging.level '{log.Level}' invalid — using info");
        }
    }

    private static string Format(float v) => v.ToString(CultureInfo.InvariantCulture);
}
