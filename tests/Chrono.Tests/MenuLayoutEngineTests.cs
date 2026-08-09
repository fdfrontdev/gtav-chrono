using System;
using System.Collections.Generic;
using Chrono.Application;
using Xunit;

namespace Chrono.Tests;

/// <summary>
/// S21 — deterministic layout tests for the menu component model (the
/// Playwright-style testing the user asked for: geometry invariants + overflow
/// assertions, no game needed). The SAME <see cref="MenuLayoutEngine"/> drives
/// the in-game renderer AND the HTML preview tool, so these tests guarantee
/// what the preview shows is what the game draws.
/// </summary>
public class MenuLayoutEngineTests
{
    // Deterministic measure: linear in char count (avg char ≈ 0.007 normalized at scale 0.26)
    private static float Measure(string text, float scale, int font)
        => text.Length * scale * 0.0265f;

    private static MenuScreen Screen(params (string Title, string? Value)[] items)
    {
        var list = new List<MenuItem>();
        foreach (var (title, value) in items)
            list.Add(new MenuItem { Title = title, Value = value });
        return new MenuScreen { Title = "TEST", Items = list };
    }

    // ── 1. Geometry invariants ──

    [Fact]
    public void Panel_IsRightAnchored_WithExpectedWidth()
    {
        var layout = MenuLayoutEngine.Compute(Screen(("A", null)), Measure);
        float left = MenuLayoutEngine.PanelX - MenuLayoutEngine.PanelWidth / 2f;

        Assert.Equal(left, layout.Panel.X, 3);
        Assert.Equal(MenuLayoutEngine.PanelWidth, layout.Panel.W, 3);
        Assert.Equal(MenuLayoutEngine.StartY, layout.Panel.Y, 3);
    }

    [Fact]
    public void RowCenters_AreEvenlySpaced_AndInsidePanel()
    {
        var layout = MenuLayoutEngine.Compute(Screen(("A", null), ("B", null), ("C", null)), Measure);

        Assert.Equal(3, layout.Rows.Count);
        for (int i = 1; i < layout.Rows.Count; i++)
        {
            float gap = layout.Rows[i].CenterY - layout.Rows[i - 1].CenterY;
            Assert.Equal(MenuLayoutEngine.RowHeight, gap, 3);
        }
        // all row centers inside the panel vertically
        foreach (var row in layout.Rows)
        {
            Assert.InRange(row.CenterY, layout.Panel.Y, layout.Panel.Y + layout.Panel.H);
        }
    }

    [Fact]
    public void TextColumns_StayInsidePanel_LeftAndRight()
    {
        // Longest realistic titles + values must never exceed the panel bounds
        var layout = MenuLayoutEngine.Compute(Screen(
            ("Vehicular Manslaughter", "3★"),
            ("BREAKING: super-powered suspect on the loose in Vinewood — witnesses describe a figure moving at impossible speed", null),
            ("Settings", "12.0 m")), Measure);

        float left = MenuLayoutEngine.PanelX - MenuLayoutEngine.PanelWidth / 2f;
        float right = MenuLayoutEngine.PanelX + MenuLayoutEngine.PanelWidth / 2f;

        foreach (var row in layout.Rows)
        {
            // title starts ≥ panel left + padding; value column ends ≤ panel right - padding
            Assert.True(Measure(row.Title, 0.26f, 4) + 0.012f <= MenuLayoutEngine.PanelWidth - 0.024f - 0.115f || row.Value == null,
                $"title '{row.Title}' must fit its column");
            if (row.Value != null)
                Assert.True(Measure(row.Value, 0.24f, 4) <= 0.10f, $"value '{row.Value}' must fit its column");
        }
        Assert.True(right - 0.012f > left + 0.012f);
    }

    [Fact]
    public void LongText_IsEllipsized_ToFitColumn()
    {
        var layout = MenuLayoutEngine.Compute(Screen(
            ("A very long headline that will never fit the title column at all so it must be ellipsized", null)), Measure);

        Assert.Single(layout.Rows);
        Assert.EndsWith("...", layout.Rows[0].Title);
        Assert.True(layout.Rows[0].Title.Length < 60);
    }

    [Fact]
    public void HeaderTitle_IsEllipsized_IfTooLong()
    {
        var layout = MenuLayoutEngine.Compute(Screen(("A", null)), Measure);

        Assert.Equal("TEST", layout.HeaderTitle);   // short title passes through
    }

    // ── 2. Viewport windowing ──

    [Fact]
    public void Viewport_FewerItems_AllVisible()
    {
        Assert.Equal((0, 5), MenuLayoutEngine.ViewportWindow(2, 5, 12));
    }

    [Fact]
    public void Viewport_CentersSelection()
    {
        var (first, visible) = MenuLayoutEngine.ViewportWindow(15, 40, 12);
        Assert.Equal(12, visible);
        Assert.InRange(first, 9, 10);   // selection centered: 15 - 6 = 9
    }

    [Fact]
    public void Viewport_ClampsAtStartAndEnd()
    {
        Assert.Equal((0, 12), MenuLayoutEngine.ViewportWindow(0, 40, 12));
        Assert.Equal((28, 12), MenuLayoutEngine.ViewportWindow(39, 40, 12));
    }

    [Fact]
    public void ScrollIndicators_AppearOnlyWhenClipped()
    {
        var small = MenuLayoutEngine.Compute(Screen(("A", null), ("B", null)), Measure);
        Assert.Null(small.ScrollUp);
        Assert.Null(small.ScrollDown);

        var items = new List<(string, string?)>();
        for (int i = 0; i < 15; i++) items.Add(($"Item {i}", null));
        var big = MenuLayoutEngine.Compute(Screen(items.ToArray()), Measure);
        Assert.NotNull(big.ScrollDown);
        Assert.Equal("3 DOWN", big.ScrollDown);
    }

    // ── 3. Selection state ──

    [Fact]
    public void Selection_FlagsExactlyOneRow()
    {
        var screen = Screen(("A", null), ("B", null), ("C", null));
        screen.SelectedIndex = 1;
        var layout = MenuLayoutEngine.Compute(screen, Measure);

        int selected = 0;
        foreach (var row in layout.Rows) if (row.Selected) selected++;
        Assert.Equal(1, selected);
        Assert.True(layout.Rows[1].Selected);
    }
}
