namespace Chrono.Application.Ports;

/// <summary>
/// Prison look (S13): swap the player to a prisoner model during confinement
/// (jumpsuit guaranteed by the model) and restore the original model + outfit
/// on release/escape. The boundary owns the natives; failures are logged, never
/// crash vectors.
/// </summary>
public interface IPrisonOutfit
{
    void ApplyPrison();
    void Restore();
}
