namespace Chrono.Application.Ports;

/// <summary>Structured logging sink (implemented by ChronoLogger).</summary>
public interface ILogSink
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}
