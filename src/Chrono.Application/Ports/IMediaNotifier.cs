namespace Chrono.Application.Ports;

/// <summary>Media output (FR-4): news headlines + WEBNET viral flavor.</summary>
public interface IMediaNotifier
{
    void News(string headline);
    void Viral(string message);
}
