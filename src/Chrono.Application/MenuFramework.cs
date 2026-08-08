using System;
using System.Collections.Generic;
using Chrono.Application.Ports;

namespace Chrono.Application;

/// <summary>
/// Pure menu navigation state machine (DLD §5.1). Rendering is delegated to
/// <see cref="IMenuRenderer"/> (implemented by the boundary). No game types here.
/// </summary>
public sealed class MenuFramework
{
    private readonly Stack<MenuScreen> _stack = new();
    private readonly IMenuRenderer _renderer;

    public MenuFramework(IMenuRenderer renderer)
    {
        _renderer = renderer;
    }

    public bool IsOpen => _stack.Count > 0;
    public MenuScreen? CurrentScreen => _stack.Count > 0 ? _stack.Peek() : null;

    public void Open(MenuScreen root)
    {
        _stack.Clear();
        _stack.Push(root);
    }

    public void Close()
    {
        _stack.Clear();
    }

    public void NavigateUp() => MoveSelection(-1);

    public void NavigateDown() => MoveSelection(+1);

    public void NavigateBack()
    {
        if (_stack.Count > 1) _stack.Pop();   // to parent
        else Close();                          // close at root
    }

    public void Accept()
    {
        var screen = CurrentScreen;
        if (screen == null) return;

        var item = screen.Items[screen.SelectedIndex];
        if (item.Submenu != null)
        {
            _stack.Push(item.Submenu);
            return;
        }
        item.OnActivate?.Invoke();
    }

    public void AdjustValue(int direction)
    {
        var screen = CurrentScreen;
        if (screen == null) return;
        screen.Items[screen.SelectedIndex].OnAdjust?.Invoke(direction);
    }

    /// <summary>Render the current screen via the boundary renderer.</summary>
    public void Render()
    {
        var screen = CurrentScreen;
        if (screen == null) return;
        _renderer.Render(screen);
    }

    private void MoveSelection(int delta)
    {
        var screen = CurrentScreen;
        if (screen == null || screen.Items.Count == 0) return;

        int count = screen.Items.Count;
        screen.SelectedIndex = ((screen.SelectedIndex + delta) % count + count) % count;
    }
}
