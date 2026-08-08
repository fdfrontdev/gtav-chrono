using System;
using System.Collections.Generic;

namespace Chrono.Application;

/// <summary>A selectable row in a menu screen.</summary>
public sealed class MenuItem
{
    public required string Title { get; init; }
    public string? Value { get; set; }          // e.g. "ON" / "OFF" / "7.0 m"
    public Action? OnActivate { get; set; }     // select → execute (toggles, actions)
    public Action<int>? OnAdjust { get; set; }  // left/right adjust (settings rows; direction -1/+1)
    public MenuScreen? Submenu { get; set; }    // navigate into
}

/// <summary>A named screen with items and wrap-around selection.</summary>
public sealed class MenuScreen
{
    public required string Title { get; init; }
    public required IReadOnlyList<MenuItem> Items { get; init; }
    public int SelectedIndex { get; set; }
}
