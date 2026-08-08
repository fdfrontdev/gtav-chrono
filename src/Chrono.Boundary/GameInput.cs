using System;
using System.Windows.Forms;
using Chrono.Application.Ports;
using GTA;

namespace Chrono.Boundary;

/// <summary>
/// Menu navigation (Frontend controls) + raw key polling with edge detection.
/// Edge detection is essential for toggles (menu, time stop, invisibility) —
/// level polling would flip them open/closed while the key is held.
/// </summary>
public sealed class GameInput : IGameInput
{
    private readonly Keys _menuKey;
    private readonly Keys? _dashKey;
    private readonly Keys? _timeStopKey;
    private readonly Keys? _invisibleKey;

    private bool _menuKeyDown, _menuKeyWasDown;
    private bool _timeStopDown, _timeStopWasDown;
    private bool _invisibleDown, _invisibleWasDown;

    public GameInput(string menuKeyName, string dashHotkeyName, string timeStopHotkeyName, string invisibleHotkeyName)
    {
        _menuKey = ParseKey(menuKeyName, Keys.F9);
        _dashKey = ParseOptionalKey(dashHotkeyName);
        _timeStopKey = ParseOptionalKey(timeStopHotkeyName);
        _invisibleKey = ParseOptionalKey(invisibleHotkeyName);
    }

    public void Update()
    {
        _menuKeyWasDown = _menuKeyDown;
        _menuKeyDown = Game.IsKeyPressed(_menuKey);

        if (_timeStopKey.HasValue)
        {
            _timeStopWasDown = _timeStopDown;
            _timeStopDown = Game.IsKeyPressed(_timeStopKey.Value);
        }

        if (_invisibleKey.HasValue)
        {
            _invisibleWasDown = _invisibleDown;
            _invisibleDown = Game.IsKeyPressed(_invisibleKey.Value);
        }
    }

    public bool IsMenuKeyPressed => _menuKeyDown;
    public bool IsMenuKeyJustPressed => _menuKeyDown && !_menuKeyWasDown;
    public bool IsMenuUpJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendUp);
    public bool IsMenuDownJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendDown);
    public bool IsMenuAcceptJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendAccept);
    public bool IsMenuCancelJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendCancel);
    public bool IsDashHotkeyPressed => _dashKey.HasValue && Game.IsKeyPressed(_dashKey.Value);
    public bool IsTimeStopHotkeyJustPressed => _timeStopKey.HasValue && _timeStopDown && !_timeStopWasDown;
    public bool IsInvisibleHotkeyJustPressed => _invisibleKey.HasValue && _invisibleDown && !_invisibleWasDown;

    // --- flight controls (camera-relative movement, controller-friendly) ---
    public bool IsFlyForward => Game.IsControlPressed(GTA.Control.MoveUpOnly);
    public bool IsFlyBack => Game.IsControlPressed(GTA.Control.MoveDownOnly);
    public bool IsFlyLeft => Game.IsControlPressed(GTA.Control.MoveLeftOnly);
    public bool IsFlyRight => Game.IsControlPressed(GTA.Control.MoveRightOnly);
    public bool IsFlyAscend => Game.IsControlPressed(GTA.Control.Jump);
    public bool IsFlyDescend => Game.IsControlPressed(GTA.Control.Duck);

    private static Keys ParseKey(string name, Keys fallback)
        => Enum.TryParse(name, true, out Keys key) ? key : fallback;

    private static Keys? ParseOptionalKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return Enum.TryParse(name, true, out Keys key) && key != Keys.None ? key : null;
    }
}
