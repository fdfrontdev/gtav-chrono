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
        ValidatePowers(cfg.Powers, warnings);
        ValidateHack(cfg.Hack, warnings);
        ValidateNeeds(cfg.Needs, warnings);
        ValidateCheat(cfg.Cheat, warnings);

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
        if (justice.BailFraction < 0.1 || justice.BailFraction > 1.0)
        {
            justice.BailFraction = 0.5;
            warnings.Add("justice.bailFraction outside [0.1,1.0] → 0.5");
        }
        if (justice.BailMinCost < 100 || justice.BailMinCost > 100000)
        {
            justice.BailMinCost = 1000;
            warnings.Add("justice.bailMinCost outside [100,100000] → 1000");
        }
        if (justice.ComplianceSeconds < 1 || justice.ComplianceSeconds > 20)
        {
            justice.ComplianceSeconds = 3;
            warnings.Add("justice.complianceSeconds outside [1,20] → 3");
        }
        if (justice.ParoleDays < 0 || justice.ParoleDays > 30)
        {
            justice.ParoleDays = 3;
            warnings.Add("justice.paroleDays outside [0,30] → 3");
        }
        // --- S20 — act-based crime detection (ADR-04 D1) ---
        if (justice.CrimeWitnessRadiusM < 5f || justice.CrimeWitnessRadiusM > 100f)
        {
            justice.CrimeWitnessRadiusM = 30f;
            warnings.Add("justice.crimeWitnessRadiusM outside [5,100] → 30");
        }
        if (justice.CrimePollRadiusM < 10f || justice.CrimePollRadiusM > 100f)
        {
            justice.CrimePollRadiusM = 40f;
            warnings.Add("justice.crimePollRadiusM outside [10,100] → 40");
        }
        if (justice.CrimeKindCooldownSeconds < 2 || justice.CrimeKindCooldownSeconds > 120)
        {
            justice.CrimeKindCooldownSeconds = 20;
            warnings.Add("justice.crimeKindCooldownSeconds outside [2,120] → 20");
        }
        if (justice.RobberyRangeM < 2f || justice.RobberyRangeM > 15f)
        {
            justice.RobberyRangeM = 6f;
            warnings.Add("justice.robberyRangeM outside [2,15] → 6");
        }
        if (justice.VehicularManslaughterSpeedMps < 5f || justice.VehicularManslaughterSpeedMps > 50f)
        {
            justice.VehicularManslaughterSpeedMps = 15f;
            warnings.Add("justice.vehicularManslaughterSpeedMps outside [5,50] → 15");
        }
        // --- S20 — use of force (ADR-04 D2) ---
        if (justice.UseOfForceMinStars < 1 || justice.UseOfForceMinStars > 5)
        {
            justice.UseOfForceMinStars = 2;
            warnings.Add("justice.useOfForceMinStars outside [1,5] → 2");
        }
        if (justice.PoliceHoldRadiusM < 20f || justice.PoliceHoldRadiusM > 200f)
        {
            justice.PoliceHoldRadiusM = 60f;
            warnings.Add("justice.policeHoldRadiusM outside [20,200] → 60");
        }
        // --- S21 — physical capture + HUD widget (user UAT r15) ---
        if (justice.CaptureRangeM < 1f || justice.CaptureRangeM > 10f)
        {
            justice.CaptureRangeM = 3f;
            warnings.Add("justice.captureRangeM outside [1,10] → 3");
        }
        if (justice.SurrenderRangeM < 3f || justice.SurrenderRangeM > 30f)
        {
            justice.SurrenderRangeM = 12f;
            warnings.Add("justice.surrenderRangeM outside [3,30] → 12");
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

    // ── v0.10 (SRS 05) ──

    private static void ValidatePowers(PowersConfig p, List<string> warnings)
    {
        if (p.EnergyMax < 10 || p.EnergyMax > 1000) { p.EnergyMax = 100; warnings.Add("powers.energyMax outside [10,1000] → 100"); }
        if (p.EnergyRegenPerSecond < 0 || p.EnergyRegenPerSecond > 100) { p.EnergyRegenPerSecond = 8; warnings.Add("powers.energyRegenPerSecond outside [0,100] → 8"); }
        if (p.PushCost < 0 || p.PushCost > p.EnergyMax) { p.PushCost = 25; warnings.Add("powers.pushCost outside [0,energyMax] → 25"); }
        if (p.PushRangeM < 2f || p.PushRangeM > 50f) { p.PushRangeM = 12f; warnings.Add("powers.pushRangeM outside [2,50] → 12"); }
        if (p.PushConeDeg < 10f || p.PushConeDeg > 180f) { p.PushConeDeg = 60f; warnings.Add("powers.pushConeDeg outside [10,180] → 60"); }
        if (p.BlastCost < 0 || p.BlastCost > p.EnergyMax) { p.BlastCost = 40; warnings.Add("powers.blastCost outside [0,energyMax] → 40"); }
        if (p.BlastRadiusM < 1f || p.BlastRadiusM > 30f) { p.BlastRadiusM = 6f; warnings.Add("powers.blastRadiusM outside [1,30] → 6"); }
        if (p.BlastRangeM < 5f || p.BlastRangeM > 100f) { p.BlastRangeM = 30f; warnings.Add("powers.blastRangeM outside [5,100] → 30"); }
        if (p.BulletTimeCostPerSecond < 0 || p.BulletTimeCostPerSecond > 100) { p.BulletTimeCostPerSecond = 12; warnings.Add("powers.bulletTimeCostPerSecond outside [0,100] → 12"); }
        if (p.BulletTimeScale < 0.05f || p.BulletTimeScale > 1f) { p.BulletTimeScale = 0.3f; warnings.Add("powers.bulletTimeScale outside [0.05,1] → 0.3"); }
        if (p.RegenCost < 0 || p.RegenCost > p.EnergyMax) { p.RegenCost = 35; warnings.Add("powers.regenCost outside [0,energyMax] → 35"); }
        if (p.RegenSeconds < 1 || p.RegenSeconds > 30) { p.RegenSeconds = 5; warnings.Add("powers.regenSeconds outside [1,30] → 5"); }
    }

    private static void ValidateHack(HackConfig h, List<string> warnings)
    {
        if (h.BaseCost < 0 || h.BaseCost > 1_000_000) { h.BaseCost = 10000; warnings.Add("hack.baseCost outside [0,1e6] → 10000"); }
        if (h.PerEventCost < 0 || h.PerEventCost > 100_000) { h.PerEventCost = 1500; warnings.Add("hack.perEventCost outside [0,1e5] → 1500"); }
    }

    private static void ValidateNeeds(NeedsConfig n, List<string> warnings)
    {
        if (n.GameHourRealSeconds < 10 || n.GameHourRealSeconds > 3600) { n.GameHourRealSeconds = 120; warnings.Add("needs.gameHourRealSeconds outside [10,3600] → 120"); }
        if (n.ThirstPerGameHour < 0.5 || n.ThirstPerGameHour > 20) { n.ThirstPerGameHour = 100.0 / 18.0; warnings.Add("needs.thirstPerGameHour outside [0.5,20] → 100/18"); }
        if (n.HungerPerGameHour < 0.5 || n.HungerPerGameHour > 20) { n.HungerPerGameHour = 100.0 / 24.0; warnings.Add("needs.hungerPerGameHour outside [0.5,20] → 100/24"); }
        if (n.MoodPerGameHour < 0.5 || n.MoodPerGameHour > 20) { n.MoodPerGameHour = 100.0 / 30.0; warnings.Add("needs.moodPerGameHour outside [0.5,20] → 100/30"); }
        if (n.EnergyIdlePerGameHour < 0.5 || n.EnergyIdlePerGameHour > 20) { n.EnergyIdlePerGameHour = 100.0 / 24.0; warnings.Add("needs.energyIdlePerGameHour outside [0.5,20] → 100/24"); }
        if (n.EnergyActiveMultiplier < 1.0 || n.EnergyActiveMultiplier > 5.0) { n.EnergyActiveMultiplier = 1.5; warnings.Add("needs.energyActiveMultiplier outside [1,5] → 1.5"); }
        if (n.CriticalHungerDrainPerSecond < 0 || n.CriticalHungerDrainPerSecond > 10) { n.CriticalHungerDrainPerSecond = 1.2; warnings.Add("needs.criticalHungerDrainPerSecond outside [0,10] → 1.2"); }
        if (n.CriticalThirstDrainPerSecond < 0 || n.CriticalThirstDrainPerSecond > 10) { n.CriticalThirstDrainPerSecond = 2.0; warnings.Add("needs.criticalThirstDrainPerSecond outside [0,10] → 2.0"); }
        if (n.PassOutSkipGameHours < 1 || n.PassOutSkipGameHours > 24) { n.PassOutSkipGameHours = 4; warnings.Add("needs.passOutSkipGameHours outside [1,24] → 4"); }
        if (n.SleepSkipGameHours < 1 || n.SleepSkipGameHours > 24) { n.SleepSkipGameHours = 6; warnings.Add("needs.sleepSkipGameHours outside [1,24] → 6"); }
        if (n.DeliveryFee < 0 || n.DeliveryFee > 1000) { n.DeliveryFee = 25; warnings.Add("needs.deliveryFee outside [0,1000] → 25"); }
        if (n.DeliverySecondsMin < 5 || n.DeliverySecondsMax < n.DeliverySecondsMin) { n.DeliverySecondsMin = 30; n.DeliverySecondsMax = 60; warnings.Add("needs.deliverySeconds invalid → 30-60"); }
        if (n.EateryRadiusM < 2f || n.EateryRadiusM > 30f) { n.EateryRadiusM = 8f; warnings.Add("needs.eateryRadiusM outside [2,30] → 8"); }
        // v0.12 escort (FR-D1..D3)
        if (n.EscortPrice < 1 || n.EscortPrice > 10000) { n.EscortPrice = 100; warnings.Add("needs.escortPrice outside [1,10000] → 100"); }
        if (n.EscortEtaSeconds < 5 || n.EscortEtaSeconds > 120) { n.EscortEtaSeconds = 20; warnings.Add("needs.escortEtaSeconds outside [5,120] → 20"); }
        if (n.EscortMoodGain < 0 || n.EscortMoodGain > 100) { n.EscortMoodGain = 30; warnings.Add("needs.escortMoodGain outside [0,100] → 30"); }
    }

    private static void ValidateCheat(CheatConfig c, List<string> warnings)
    {
        if (c.MoneyAmount < 1 || c.MoneyAmount > 1_000_000) { c.MoneyAmount = 10000; warnings.Add("cheat.moneyAmount outside [1,1e6] → 10000"); }
    }

    private static string Format(float v) => v.ToString(CultureInfo.InvariantCulture);
}
