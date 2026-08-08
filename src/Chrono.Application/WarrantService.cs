using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Standing warrant (FR-3): escaping a chase never clears it. Cleared by surgery
/// (new face), police-DB hack (record erased), or serving a sentence (justice served).
/// </summary>
public sealed class WarrantService
{
    private readonly IRecordStore _store;
    private readonly ILogSink _log;

    public WarrantService(IRecordStore store, ILogSink log)
    {
        _store = store;
        _log = log;
        var status = store.LoadStatus();
        IsActive = status.WarrantActive;
        SinceGameTime = status.WarrantSinceGameTime;
    }

    public bool IsActive { get; private set; }
    public string? SinceGameTime { get; private set; }

    public void Activate(string gameTime)
    {
        if (IsActive) return;
        IsActive = true;
        SinceGameTime = gameTime;
        Persist();
        _log.Info($"WARRANT issued ({gameTime}) — police will arrest on sight");
    }

    public void Clear()
    {
        if (!IsActive) return;
        IsActive = false;
        SinceGameTime = null;
        Persist();
        _log.Info("Warrant cleared");
    }

    private void Persist()
    {
        var status = _store.LoadStatus();
        status.WarrantActive = IsActive;
        status.WarrantSinceGameTime = SinceGameTime;
        _store.SaveStatusAtomic(status);
    }
}
