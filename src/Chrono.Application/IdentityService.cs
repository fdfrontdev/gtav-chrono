using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// Identity states (FR-2): Burned = face seen during an offense → NPC/police recognition.
/// Clean = unknown face. Persisted via the record store (status.json).
/// </summary>
public sealed class IdentityService
{
    private readonly IRecordStore _store;
    private readonly ILogSink _log;

    public IdentityService(IRecordStore store, ILogSink log)
    {
        _store = store;
        _log = log;
        State = store.LoadStatus().Identity;
    }

    public IdentityState State { get; private set; }
    public bool IsBurned => State == IdentityState.Burned;

    public void SetBurned()
    {
        if (State == IdentityState.Burned) return;
        State = IdentityState.Burned;
        Persist();
        _log.Info("Identity BURNED — face known to the city");
    }

    /// <summary>New face (plastic surgery FR-5.2, police-DB hack FR-6.2, or conviction FR-8.4).</summary>
    public void SetClean()
    {
        if (State == IdentityState.Clean) return;
        State = IdentityState.Clean;
        Persist();
        _log.Info("Identity CLEAN — new face");
    }

    private void Persist()
    {
        var status = _store.LoadStatus();
        status.Identity = State;
        _store.SaveStatusAtomic(status);
    }
}
