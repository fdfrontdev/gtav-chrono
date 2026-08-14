namespace Chrono.Domain;

/// <summary>
/// Police-DB hack pricing (SRS FR-A1, ADR D1) — the bigger the file, the
/// higher the price: base + per-event/conviction fee. Pure math, tested.
/// </summary>
public static class HackPricingPolicy
{
    public static int Cost(HackConfig c, CriminalRecord record)
        => c.BaseCost + c.PerEventCost * (record.Events.Count + record.ConvictionCount);
}
