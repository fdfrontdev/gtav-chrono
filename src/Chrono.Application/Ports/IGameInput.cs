namespace Chrono.Application.Ports;

/// <summary>Input abstraction — menu navigation + power hotkeys (implemented by the SHVDN boundary).</summary>
public interface IGameInput
{
    bool IsMenuKeyPressed { get; }     // F9 (configurable)
    bool IsMenuUpJustPressed { get; }
    bool IsMenuDownJustPressed { get; }
    bool IsMenuAcceptJustPressed { get; }
    bool IsMenuCancelJustPressed { get; }
    bool IsDashHotkeyPressed { get; }  // optional dash hotkey ("" = disabled)
}
