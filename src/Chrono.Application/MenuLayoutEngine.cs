using System;
using System.Collections.Generic;

namespace Chrono.Application;

/// <summary>
/// S21 — pure menu layout engine (the "component model"). Computes ALL geometry
/// for a screen: panel, header, rows (title/value columns), scroll strip, footer.
/// Game-free: text width comes from an injected measurer so the SAME engine
/// drives (a) the in-game renderer with the native width command and (b) an HTML
/// preview tool with a font-approximation measurer — one layout, two outputs.
/// This is the Playwright-style testing loop the user asked for (UAT r15 follow-up):
/// preview HTML = screenshot artifact; unit tests = layout assertions.
/// </summary>
public sealed class MenuLayoutEngine
{
    // ── Material tokens (Vuetify palette, normalized 0..1 coords) ──
    public const float PanelX = 0.78f;         // panel center X
    public const float PanelWidth = 0.34f;
    public const float StartY = 0.14f;
    public const float RowHeight = 0.038f;
    public const float HeaderH = 0.058f;
    public const int MaxVisibleRows = 12;

    public readonly record struct Rect(float X, float Y, float W, float H);
    public readonly record struct TextSpan(string Text, float X, float Y, float Scale, int Font, bool Bold,
        int R, int G, int B, int A, bool RightAligned = false);

    public sealed record Row(
        string Title,          // ellipsized to the title column
        string? Value,         // ellipsized to the value column
        bool Selected,
        bool HasSubmenu,
        float CenterY);

    public sealed record Layout(
        Rect Panel,            // outer panel (surface)
        Rect Shadow,           // elevation shadow layer
        string HeaderTitle,
        string HeaderBrand,
        IReadOnlyList<Row> Rows,
        string? ScrollUp,      // "N UP"
        string? ScrollDown,    // "M DOWN"
        Rect FooterBar,
        string FooterText,
        float RowsEndY);

    /// <summary>Viewport window (S17): centers the selection, clamps at the edges.</summary>
    public static (int First, int Visible) ViewportWindow(int selected, int count, int maxVisible)
    {
        if (count <= maxVisible) return (0, count);
        int first = Math.Max(0, Math.Min(selected - maxVisible / 2, count - maxVisible));
        return (first, maxVisible);
    }

    /// <summary>Ellipsize text to fit a column width using the injected measurer.</summary>
    public static string Truncate(string text, float maxW, Func<string, float, int, float> measure,
        float scale, int font)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (measure(text, scale, font) <= maxW) return text;
        for (int i = text.Length - 1; i > 0; i--)
        {
            string t = text.Substring(0, i) + "...";
            if (measure(t, scale, font) <= maxW) return t;
        }
        return "...";
    }

    /// <summary>
    /// Compute the full layout for a screen. measure(text, scale, font) → width
    /// in normalized units (injected: native in-game, font table in the preview).
    /// </summary>
    public static Layout Compute(MenuScreen screen, Func<string, float, int, float> measure,
        string headerBrand = "CHRONO", string footerText = "UP/DOWN MOVE  -  ENTER OPEN  -  BACK")
    {
        var (first, visible) = ViewportWindow(screen.SelectedIndex, screen.Items.Count, MaxVisibleRows);

        float rowsH = visible * RowHeight;
        float panelH = HeaderH + rowsH + 0.012f + 0.028f + 0.012f;   // header + rows + gap + footer + pad
        float top = StartY;
        float left = PanelX - PanelWidth / 2f;

        var panel = new Rect(left, top, PanelWidth, panelH);
        var shadow = new Rect(left - 0.008f, top - 0.008f, PanelWidth + 0.016f, panelH + 0.016f);

        // Header — full-width title (no brand column since S21 follow-up)
        float titleMax = PanelWidth - 0.024f;
        string headerTitle = Truncate(screen.Title, titleMax, measure, 0.30f, 4);

        // Rows
        var rows = new List<Row>(visible);
        float titleColMax = PanelWidth - 0.024f - 0.115f;   // title column (value column reserved)
        float valColMax = 0.10f;
        for (int v = 0; v < visible; v++)
        {
            int i = first + v;
            var item = screen.Items[i];
            float rowCenter = top + HeaderH + v * RowHeight + (RowHeight - 0.005f) / 2f;
            string title = Truncate(item.Title ?? "", titleColMax, measure, 0.26f, 4);
            string? value = string.IsNullOrEmpty(item.Value) ? null
                : Truncate(item.Value!, valColMax, measure, 0.24f, 4);
            rows.Add(new Row(title, value, i == screen.SelectedIndex, item.Submenu != null, rowCenter));
        }

        // Scroll strip
        float rowsEnd = top + HeaderH + rowsH;
        string? scrollUp = first > 0 ? $"{first} UP" : null;
        string? scrollDown = first + visible < screen.Items.Count
            ? $"{screen.Items.Count - (first + visible)} DOWN" : null;

        // Footer
        float footCenter = rowsEnd + 0.012f + 0.014f;
        var footer = new Rect(left + 0.006f, footCenter, PanelWidth - 0.012f, 0.028f);

        return new Layout(panel, shadow, headerTitle, headerBrand, rows,
            scrollUp, scrollDown, footer, footerText, rowsEnd);
    }
}
