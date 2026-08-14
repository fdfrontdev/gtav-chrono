using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Session energy pool service (SRS FR-B1/B2) — regen per tick, cost gating.
/// Session-only by design (ADR D2): no persistence.
/// </summary>
public sealed class PowerEnergyService
{
    private readonly PowersConfig _config;
    private PowerEnergy _energy;

    public PowerEnergyService(PowersConfig config)
    {
        _config = config;
        _energy = PowerEnergy.Create(config.EnergyMax, config.EnergyRegenPerSecond);
    }

    public int Current => _energy.Current;
    public int Max => _energy.Max;

    /// <summary>Regen (frozen while the caller gates it — missions freeze via the caller).</summary>
    public void Tick(double deltaSeconds) => _energy = _energy.Tick(deltaSeconds);

    /// <summary>Spend energy for a power; false when unaffordable (FR-B2).</summary>
    public bool TrySpend(int cost)
    {
        if (!_energy.CanAfford(cost)) return false;
        _energy = _energy.Spend(cost);
        return true;
    }

    public void Restore() => _energy = PowerEnergy.Create(_config.EnergyMax, _config.EnergyRegenPerSecond);
}
