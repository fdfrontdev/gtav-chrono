using Chrono.Domain;

namespace Chrono.Application.Ports;

/// <summary>Persistent criminal record + profile store (FR-1.5, atomic writes).</summary>
public interface IRecordStore
{
    CriminalRecord Load();
    void SaveAtomic(CriminalRecord record);
    CharacterProfile LoadProfile();
    void SaveProfileAtomic(CharacterProfile profile);
}
