using System;
using System.Windows.Forms;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>Menu navigation (Frontend controls) + raw key polling (F9 / dash hotkey).</summary>
public sealed class GameInput : IGameInput
{
    private readonly Keys _menuKey;
    private readonly Keys? _dashKey;
    private bool _menuKeyDown;
    private bool _menuKeyWasDown;

    public GameInput(string menuKeyName, string dashHotkeyName)
    {
        _menuKey = ParseKey(menuKeyName, Keys.F9);
        _dashKey = string.IsNullOrWhiteSpace(dashHotkeyName)
            ? null
            : ParseKey(dashHotkeyName, Keys.None) is var k && k != Keys.None ? k : null;
    }

    public void Update()
    {
        _menuKeyWasDown = _menuKeyDown;
        _menuKeyDown = Game.IsKeyPressed(_menuKey);
    }

    public bool IsMenuKeyPressed => _menuKeyDown;
    public bool IsMenuKeyJustPressed => _menuKeyDown && !_menuKeyWasDown;
    public bool IsMenuUpJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendUp);
    public bool IsMenuDownJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendDown);
    public bool IsMenuAcceptJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendAccept);
    public bool IsMenuCancelJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendCancel);
    public bool IsDashHotkeyPressed => _dashKey.HasValue && Game.IsKeyPressed(_dashKey.Value);

    // --- flight controls (camera-relative movement, controller-friendly) ---
    public bool IsFlyForward => Game.IsControlPressed(GTA.Control.MoveUpOnly);
    public bool IsFlyBack => Game.IsControlPressed(GTA.Control.MoveDownOnly);
    public bool IsFlyLeft => Game.IsControlPressed(GTA.Control.MoveLeftOnly);
    public bool IsFlyRight => Game.IsControlPressed(GTA.Control.MoveRightOnly);
    public bool IsFlyAscend => Game.IsControlPressed(GTA.Control.Jump);
    public bool IsFlyDescend => Game.IsControlPressed(GTA.Control.Duck);

    private static Keys ParseKey(string name, Keys fallback)
    {
        return Enum.TryParse(name, true, out Keys key) ? key : fallback;
    }
}
