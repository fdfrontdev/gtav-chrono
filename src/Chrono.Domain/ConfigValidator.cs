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
        ValidateFly(cfg.Fly, warnings);
        ValidateNpc(cfg.Npc, warnings);
        ValidateJustice(cfg.Justice, warnings);
        ValidateVisual(cfg.Visual, warnings);
        ValidateLogging(cfg.Logging, warnings);

        return new ValidationResult(cfg, warnings);
    }

    private static void ValidateMenuKey(ChronoConfig cfg, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(cfg.MenuKey))
        {
            cfg.MenuKey = "Shift+0";   // S8: F9 collided with other bindings
            warnings.Add("menuKey empty — using F9");
        }
    }

    private static void ValidateDash(DashConfig dash, List<string> warnings)
    {
        if (dash.Range < 5.0f || dash.Range > 30.0f)
        {
            dash.Range = 12.0f;
            warnings.Add($"dash.range {Format(dash.Range)} outside [5,30] — using 12");
        }
        if (dash.MaxRange < dash.Range)
        {
            dash.MaxRange = Math.Max(dash.Range, 30.0f);
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
        if (ts.MaxFrozenEntities < 1 || ts.MaxFrozenEntities > 4096)
        {
            ts.MaxFrozenEntities = 1024;
            warnings.Add("timeStop.maxFrozenEntities outside [1,4096] — using 1024");
        }
        if (ts.FreezeRadius < 0f || ts.FreezeRadius > 400f)
        {
            ts.FreezeRadius = 100f;
            warnings.Add("timeStop.freezeRadius outside [0,400] — using 100");
        }
    }

    private static void ValidateFly(FlyConfig fly, List<string> warnings)
    {
        if (fly.Speed < 5f || fly.Speed > 80f)
        {
            fly.Speed = 25f;
            warnings.Add($"fly.speed {Format(fly.Speed)} outside [5,80] — using 25");
        }
    }

    private static void ValidateNpc(NpcConfig npc, List<string> warnings)
    {
        if (npc.ReactionDelayMs < 0 || npc.ReactionDelayMs > 10000)
        {
            npc.ReactionDelayMs = 3000;
            warnings.Add("npc.reactionDelayMs outside [0,10000] — using 3000");
        }
    }

    private static void ValidateJustice(JusticeConfig justice, List<string> warnings)
    {
        if (justice.ClinicBaseCost < 0) { justice.ClinicBaseCost = 5000; warnings.Add("justice.clinicBaseCost < 0 — using 5000"); }
        if (justice.PerEventCost < 0) { justice.PerEventCost = 1000; warnings.Add("justice.perEventCost < 0 — using 1000"); }
        if (justice.SurgeryCooldownDays < 0 || justice.SurgeryCooldownDays > 30)
        {
            justice.SurgeryCooldownDays = 1;
            warnings.Add("justice.surgeryCooldownDays outside [0,30] — using 1");
        }
        if (justice.HackCooldownDays < 0 || justice.HackCooldownDays > 30)
        {
            justice.HackCooldownDays = 1;
            warnings.Add("justice.hackCooldownDays outside [0,30] — using 1");
        }
        if (justice.PrisonDayRealSeconds < 5 || justice.PrisonDayRealSeconds > 3600)
        {
            justice.PrisonDayRealSeconds = 30;
            warnings.Add("justice.prisonDayRealSeconds outside [5,3600] — using 30");
        }
        if (justice.PrisonYardSeconds < 1 || justice.PrisonYardSeconds >= justice.PrisonDayRealSeconds)
        {
            justice.PrisonYardSeconds = 10;
            warnings.Add("justice.prisonYardSeconds outside [1,dayLen) — using 10");
        }
        if (justice.TrialDelaySeconds < 5 || justice.TrialDelaySeconds > 600)
        {
            justice.TrialDelaySeconds = 45;
            warnings.Add("justice.trialDelaySeconds outside [5,600] — using 45");
        }
        if (justice.WarrantReportSeconds < 1 || justice.WarrantReportSeconds > 120)
        {
            justice.WarrantReportSeconds = 10;
            warnings.Add("justice.warrantReportSeconds outside [1,120] — using 10");
        }
        if (justice.WarrantReportChance < 0.05 || justice.WarrantReportChance > 1.0)
        {
            justice.WarrantReportChance = 0.35;
            warnings.Add("justice.warrantReportChance outside [0.05,1.0] → 0.35");
        }

        if (justice.FineToPrisonRate < 100 || justice.FineToPrisonRate > 100000)
        {
            justice.FineToPrisonRate = 1000;
            warnings.Add("justice.fineToPrisonRate outside [100,100000] → 1000");
        }
        if (justice.EscapeStealthChance < 0.05 || justice.EscapeStealthChance > 1.0)
        {
            justice.EscapeStealthChance = 0.5;
            warnings.Add("justice.escapeStealthChance outside [0.05,1.0] → 0.5");
        }
        if (justice.EscapeFightChance < 0.05 || justice.EscapeFightChance > 1.0)
        {
            justice.EscapeFightChance = 0.7;
            warnings.Add("justice.escapeFightChance outside [0.05,1.0] → 0.7");
        }
        if (justice.EscapeChoiceSeconds < 3 || justice.EscapeChoiceSeconds > 60)
        {
            justice.EscapeChoiceSeconds = 10;
            warnings.Add("justice.escapeChoiceSeconds outside [3,60] → 10");
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
