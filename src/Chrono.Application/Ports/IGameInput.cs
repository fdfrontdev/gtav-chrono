namespace Chrono.Application.Ports;

/// <summary>Input abstraction — menu navigation + power hotkeys (implemented by the SHVDN boundary).</summary>
public interface IGameInput
{
    /// <summary>Sample raw state and compute edges. MUST be called once per tick before reading properties.</summary>
    void Update();
    bool IsMenuKeyPressed { get; }       // F9 held (configurable)
    bool IsMenuKeyJustPressed { get; }   // F9 edge — for toggles
    bool IsMenuUpJustPressed { get; }
    bool IsMenuDownJustPressed { get; }
    bool IsMenuAcceptJustPressed { get; }
    bool IsMenuCancelJustPressed { get; }
    bool IsDashHotkeyPressed { get; }    // optional dash hotkey ("" = disabled)
    bool IsTimeStopHotkeyJustPressed { get; }   // Z — edge
    bool IsInvisibleHotkeyJustPressed { get; }  // B — edge
    bool IsInteractKeyJustPressed { get; }      // G — world interaction (clinic, S5) — edge

    /// <summary>Phone key (Up arrow) — WEBNET social feed (S7).</summary>
    bool IsPhoneKeyJustPressed { get; }

    // --- flight controls (held, camera-relative) ---
    bool IsFlyForward { get; }
    bool IsFlyBack { get; }
    bool IsFlyLeft { get; }
    bool IsFlyRight { get; }
    bool IsFlyAscend { get; }
    bool IsFlyDescend { get; }
}
