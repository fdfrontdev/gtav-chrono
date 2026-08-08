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
    private readonly bool _menuKeyShift;
    private readonly Keys? _dashKey;
    private readonly Keys? _timeStopKey;
    private readonly Keys? _invisibleKey;
    private readonly Keys? _interactKey;

    private bool _menuKeyDown, _menuKeyWasDown;
    private bool _timeStopDown, _timeStopWasDown;
    private bool _invisibleDown, _invisibleWasDown;
    private bool _interactDown, _interactWasDown;
    private bool _wDown, _wWasDown, _sDown, _sWasDown, _aDown, _aWasDown, _dDown, _dWasDown;

    public GameInput(string menuKeyName, string dashHotkeyName, string timeStopHotkeyName, string invisibleHotkeyName, string? interactKeyName = null)
    {
        (_menuKey, _menuKeyShift) = ParseComboKey(menuKeyName, Keys.F9);
        _dashKey = ParseOptionalKey(dashHotkeyName);
        _timeStopKey = ParseOptionalKey(timeStopHotkeyName);
        _invisibleKey = ParseOptionalKey(invisibleHotkeyName);
        _interactKey = ParseOptionalKey(interactKeyName ?? "");
    }

    public void Update()
    {
        _menuKeyWasDown = _menuKeyDown;
        _menuKeyDown = Game.IsKeyPressed(_menuKey) && (!_menuKeyShift || Game.IsKeyPressed(Keys.ShiftKey));

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

        if (_interactKey.HasValue)
        {
            _interactWasDown = _interactDown;
            _interactDown = Game.IsKeyPressed(_interactKey.Value);
        }

        // WASD menu navigation edges (S8 — arrows conflict with other bindings)
        _wWasDown = _wDown; _wDown = Game.IsControlPressed(GTA.Control.MoveUpOnly);
        _sWasDown = _sDown; _sDown = Game.IsControlPressed(GTA.Control.MoveDownOnly);
        _aWasDown = _aDown; _aDown = Game.IsControlPressed(GTA.Control.MoveLeftOnly);
        _dWasDown = _dDown; _dDown = Game.IsControlPressed(GTA.Control.MoveRightOnly);
    }

    public bool IsMenuKeyPressed => _menuKeyDown;
    public bool IsMenuKeyJustPressed => _menuKeyDown && !_menuKeyWasDown;
    public bool IsMenuUpJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendUp) || (_wDown && !_wWasDown);
    public bool IsMenuDownJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendDown) || (_sDown && !_sWasDown);
    public bool IsMenuLeftJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendLeft) || (_aDown && !_aWasDown);
    public bool IsMenuRightJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendRight) || (_dDown && !_dWasDown);
    public bool IsMenuAcceptJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendAccept);
    public bool IsMenuCancelJustPressed => Game.IsControlJustPressed(GTA.Control.FrontendCancel);
    public bool IsDashHotkeyPressed => _dashKey.HasValue && Game.IsKeyPressed(_dashKey.Value);

    private bool _dashWasDown;
    public bool IsDashKeyJustPressed
    {
        get
        {
            bool down = _dashKey.HasValue && Game.IsKeyPressed(_dashKey.Value);
            bool edge = down && !_dashWasDown;
            _dashWasDown = down;
            return edge;
        }
    }
    public bool IsTimeStopHotkeyJustPressed => _timeStopKey.HasValue && _timeStopDown && !_timeStopWasDown;
    public bool IsInvisibleHotkeyJustPressed => _invisibleKey.HasValue && _invisibleDown && !_invisibleWasDown;
    public bool IsInteractKeyJustPressed => _interactKey.HasValue && _interactDown && !_interactWasDown;

    public bool IsPhoneKeyJustPressed => Game.IsControlJustPressed(GTA.Control.Phone);

    // --- flight controls (camera-relative movement, controller-friendly) ---
    public bool IsFlyForward => Game.IsControlPressed(GTA.Control.MoveUpOnly);
    public bool IsFlyBack => Game.IsControlPressed(GTA.Control.MoveDownOnly);
    public bool IsFlyLeft => Game.IsControlPressed(GTA.Control.MoveLeftOnly);
    public bool IsFlyRight => Game.IsControlPressed(GTA.Control.MoveRightOnly);
    public bool IsFlyAscend => Game.IsControlPressed(GTA.Control.Jump);
    public bool IsFlyDescend => Game.IsControlPressed(GTA.Control.Duck);

    private static Keys ParseKey(string name, Keys fallback)
        => ParseKeyName(name) ?? fallback;

    /// <summary>Map a key name to Keys, handling digits ("0"–"9" → D0–D9 — plain
    /// digits do NOT exist in the Keys enum, which silently broke 'Shift+0').</summary>
    private static Keys? ParseKeyName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
            return Keys.D0 + (name[0] - '0');
        return Enum.TryParse(name, true, out Keys key) && key != Keys.None ? key : null;
    }

    /// <summary>Parse "Shift+0" style combos — the only supported modifier is Shift.</summary>
    private static (Keys key, bool shift) ParseComboKey(string name, Keys fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return (fallback, false);

        string trimmed = name.Trim();
        int plus = trimmed.IndexOf('+');
        if (plus > 0 && trimmed.Substring(0, plus).Trim().Equals("Shift", StringComparison.OrdinalIgnoreCase))
        {
            var combo = ParseKeyName(trimmed.Substring(plus + 1).Trim());
            return combo.HasValue ? (combo.Value, true) : (fallback, false);
        }

        var plain = ParseKeyName(trimmed);
        return plain.HasValue ? (plain.Value, false) : (fallback, false);
    }

    private static Keys? ParseOptionalKey(string name)
        => ParseKeyName(name);
}
