using System;
using Chrono.Application;
using Chrono.Application.Ports;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// Modern custom-drawn menu — FBI case-file theme (S17 → S18).
/// S18 CRITICAL FIX: GTA's DRAW_RECT x/y = the CENTER of the rect (not the top-left).
/// S17 drew every rect at its top coordinate → rows overlapped, the selection tint
/// rode above its own text, the navy bar was half-clipped and the footer covered the
/// last row — the "text off from container" that survived S14 and S17. Every rect y
/// here is now an explicit center: rectCenterY = top + height/2.
/// Also: footer labels are ASCII (font 4 lacks the arrow/enter/back glyphs — they
/// rendered as tofu boxes), and the selected row is a crisp tint + rail instead of
/// a pink slab.
/// </summary>
public sealed class ModernMenuRenderer : IMenuRenderer
{
    private const float PanelX = 0.78f;         // panel center X (normalized)
    private const float PanelWidth = 0.34f;
    private const float StartY = 0.15f;
    private const float RowHeight = 0.038f;
    private const float HeaderH = 0.072f;       // bureau bar + case title + red rule
    private const int MaxVisibleRows = 12;
    private string _hint = "";

    private static readonly (int R, int G, int B) Paper = (246, 240, 222);
    private static readonly (int R, int G, int B) Ink = (28, 26, 22);
    private static readonly (int R, int G, int B) Navy = (16, 26, 42);
    private static readonly (int R, int G, int B) Red = (176, 24, 24);
    private static readonly (int R, int G, int B) RedTint = (252, 238, 234);      // pale red, not salmon slab
    private static readonly (int R, int G, int B) PaperDim = (96, 88, 74);
    private static readonly (int R, int G, int B) Rule = (120, 110, 90);

    /// <summary>Viewport window for a list (S17): centers the selection, clamps at the
    /// edges. Pure math — unit-tested.</summary>
    public static (int First, int Visible) ViewportWindow(int selected, int count, int maxVisible)
    {
        if (count <= maxVisible) return (0, count);
        int first = Math.Max(0, Math.Min(selected - maxVisible / 2, count - maxVisible));
        return (first, maxVisible);
    }

    public void Render(MenuScreen screen)
    {
        var (first, visible) = ViewportWindow(screen.SelectedIndex, screen.Items.Count, MaxVisibleRows);

        float rowsH = visible * RowHeight;
        float panelH = HeaderH + rowsH + 0.010f + 0.026f + 0.012f;   // header + rows + gap + footer + pad
        float top = StartY;
        float left = PanelX - PanelWidth / 2f;
        float right = PanelX + PanelWidth / 2f;

        // ── Panel: shadow → file edge → cream paper ──
        Rect(left - 0.007f, top - 0.007f, PanelWidth + 0.014f, panelH + 0.014f, 0, 0, 0, 130);
        Rect(left, top, PanelWidth, panelH, Ink.R, Ink.G, Ink.B, 255);
        Rect(left + 0.004f, top + 0.004f, PanelWidth - 0.008f, panelH - 0.008f, Paper.R, Paper.G, Paper.B, 245);

        // ── Header ──
        // navy bureau bar: top 0.004, height 0.026 → center top+0.017
        Rect(left + 0.004f, top + 0.017f, PanelWidth - 0.008f, 0.026f, Navy.R, Navy.G, Navy.B, 255);
        Text("FBI - CHRONO - CLASSIFIED FILE", left + 0.018f, CenterY(top + 0.017f, 0.26f, 4), 0.26f, 236, 232, 220, 255, font: 4);

        // case title row: top 0.034, height 0.030 → center top+0.049; red rule below at top+0.066
        Text(screen.Title.ToUpperInvariant(), left + 0.018f, CenterY(top + 0.049f, 0.30f, 4), 0.30f,
            Ink.R, Ink.G, Ink.B, 255, bold: true, font: 4);
        Text("CONFIDENTIAL", right - 0.012f - 0.105f, CenterY(top + 0.049f, 0.24f, 4), 0.24f,
            Red.R, Red.G, Red.B, 255, font: 4);
        Rect(left + 0.008f, top + 0.066f, PanelWidth - 0.016f, 0.003f, Red.R, Red.G, Red.B, 255);

        // ── Items (viewport slice) — every rect centered on its own row center ──
        for (int v = 0; v < visible; v++)
        {
            int i = first + v;
            var item = screen.Items[i];
            bool selected = i == screen.SelectedIndex;
            float rowTop = top + HeaderH + v * RowHeight;
            float rowCenter = rowTop + (RowHeight - 0.005f) / 2f;

            if (selected)
            {
                Rect(left + 0.004f, rowCenter, PanelWidth - 0.008f, RowHeight - 0.005f,
                    RedTint.R, RedTint.G, RedTint.B, 210);
                Rect(left + 0.004f, rowCenter, 0.006f, RowHeight - 0.009f, Red.R, Red.G, Red.B, 255);
            }
            else if (v % 2 == 1)
            {
                Rect(left + 0.004f, rowCenter, PanelWidth - 0.008f, RowHeight - 0.005f,
                    Rule.R, Rule.G, Rule.B, 40);
            }

            string title = item.Title ?? "";
            bool hasSub = item.Submenu != null;
            var color = selected ? Ink : PaperDim;
            Text(title + (hasSub ? "  >" : ""), left + 0.020f, CenterY(rowCenter, 0.28f, 4), 0.28f,
                color.R, color.G, color.B, 255, font: 4);

            if (!string.IsNullOrEmpty(item.Value))
            {
                float valW = 0.10f;
                Text(item.Value!, right - 0.012f - valW, CenterY(rowCenter, 0.26f, 4), 0.26f,
                    selected ? Red.R : color.R, selected ? Red.G : color.G, selected ? Red.B : color.B, 255, font: 4);
            }
        }

        // ── Scroll strip ──
        float rowsEnd = top + HeaderH + rowsH;
        if (first > 0)
            Text($"{first} UP", left + 0.020f, CenterY(rowsEnd + 0.006f, 0.22f, 4), 0.22f, Red.R, Red.G, Red.B, 255, font: 4);
        if (first + visible < screen.Items.Count)
        {
            int below = screen.Items.Count - (first + visible);
            Text($"{below} DOWN", right - 0.012f - 0.10f, CenterY(rowsEnd + 0.006f, 0.22f, 4), 0.22f, Red.R, Red.G, Red.B, 255, font: 4);
        }

        // ── Footer (ASCII — font 4 has no ▲▼↵⎋ glyphs) ──
        float footCenter = rowsEnd + 0.010f + 0.013f;
        Rect(left + 0.004f, footCenter, PanelWidth - 0.008f, 0.026f, Navy.R, Navy.G, Navy.B, 235);
        Text("UP/DOWN MOVE  -  ENTER OPEN  -  BACK", left + 0.020f, CenterY(footCenter, 0.21f, 4), 0.21f,
            220, 216, 200, 255, font: 4);
    }

    public void DrawHint(string text)
    {
        _hint = text;
        if (string.IsNullOrWhiteSpace(text)) return;
        float w = 0.30f;
        Rect(0.5f, 0.88f, w, 0.035f, 0, 0, 0, 160);
        Text(text, 0.5f - w / 2f + 0.008f, CenterY(0.88f, 0.28f, 0), 0.28f, 220, 220, 220, 255);
    }

    // ── DRAW_RECT x/y = CENTER: caller passes the true center ──
    private static void Rect(float x, float y, float w, float h, int r, int g, int b, int a)
        => Function.Call(Hash.DRAW_RECT, x, y, w, h, r, g, b, a);

    // ── Text y = TOP of the text box; centered with the game's own line-height ──
    private static float CenterY(float rectCenterY, float scale, int font)
    {
        float h = Function.Call<float>((Hash)0xDB88A37483346780, scale, font);
        return rectCenterY - h / 2f;
    }

    private static void Text(string text, float x, float y, float scale, int r, int g, int b, int a, bool bold = false, int font = 0)
    {
        Function.Call(Hash.SET_TEXT_FONT, font);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_EDGE, 1, 0, 0, 0, 120);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }
}
