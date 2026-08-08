using Chrono.Domain;

namespace Chrono.Application.Ports;

/// <summary>Config persistence (implemented by JsonConfigStore).</summary>
public interface IConfigStore
{
    ChronoConfig Load();
    void Save(ChronoConfig config);
}
