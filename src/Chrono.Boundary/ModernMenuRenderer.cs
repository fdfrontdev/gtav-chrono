using System;
using Chrono.Application;
using Chrono.Application.Ports;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// Modern custom-drawn menu (S13 → S17) — now an FBI case-file theme: cream paper
/// panel, navy header bar with a red CONFIDENTIAL rule, monospace file rows, red
/// selection rail, viewport scrolling (the selection window follows the cursor —
/// long lists like the criminal record scroll naturally), and exact text centering
/// via the game's own GET_TEXT_SCALE_HEIGHT measurement.
/// </summary>
public sealed class ModernMenuRenderer : IMenuRenderer
{
    private const float PanelX = 0.78f;         // panel center X (normalized)
    private const float PanelWidth = 0.34f;
    private const float StartY = 0.15f;
    private const float RowHeight = 0.038f;
    private const float HeaderH = 0.066f;       // navy bar + case title + red rule
    private const int MaxVisibleRows = 12;      // viewport height (S17: scrolls)
    private string _hint = "";

    private static readonly (int R, int G, int B) Paper = (246, 240, 222);       // cream file paper
    private static readonly (int R, int G, int B) Ink = (28, 26, 22);            // near-black text
    private static readonly (int R, int G, int B) Navy = (18, 28, 44);           // header bar
    private static readonly (int R, int G, int B) Red = (176, 24, 24);           // FBI red
    private static readonly (int R, int G, int B) RedTint = (247, 228, 224);     // selected row tint
    private static readonly (int R, int G, int B) PaperDim = (96, 88, 74);       // muted ink
    private static readonly (int R, int G, int B) Rule = (120, 110, 90);         // hairline rules

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
        float panelH = HeaderH + visible * RowHeight + 0.046f;   // + scroll strip + footer
        float top = StartY;
        float left = PanelX - PanelWidth / 2f;

        // ── Panel: shadow → paper border → cream body ──
        DrawRect(left - 0.007f, top - 0.007f, PanelWidth + 0.014f, panelH + 0.014f, 0, 0, 0, 130);            // soft shadow
        DrawRect(left, top, PanelWidth, panelH, Ink.R, Ink.G, Ink.B, 255);                                     // file cover edge
        DrawRect(left + 0.004f, top + 0.004f, PanelWidth - 0.008f, panelH - 0.008f, Paper.R, Paper.G, Paper.B, 245);

        // ── Header: navy bureau bar ──
        DrawRect(left + 0.004f, top + 0.004f, PanelWidth - 0.008f, 0.028f, Navy.R, Navy.G, Navy.B, 255);
        DrawText("FEDERAL BUREAU OF INVESTIGATION · CHRONO", left + 0.018f,
            CenterTextY(top + 0.004f + 0.014f, 0.028f, 0.24f, 4), 0.24f, 235, 232, 220, 255, font: 4);

        // ── Case title row + CONFIDENTIAL stamp + red rule ──
        float titleY = top + 0.034f;
        DrawText(screen.Title.ToUpperInvariant(), left + 0.018f,
            CenterTextY(titleY + 0.016f, 0.032f, 0.34f, 4), 0.34f, Ink.R, Ink.G, Ink.B, 255, bold: true, font: 4);
        DrawText("CONFIDENTIAL", PanelX + PanelWidth / 2f - 0.012f - 0.09f,
            CenterTextY(titleY + 0.016f, 0.032f, 0.24f, 4), 0.24f, Red.R, Red.G, Red.B, 255, font: 4);
        DrawRect(left + 0.008f, titleY + 0.034f, PanelWidth - 0.016f, 0.0035f, Red.R, Red.G, Red.B, 255);      // red rule

        // ── Items (viewport slice) ──
        for (int v = 0; v < visible; v++)
        {
            int i = first + v;
            var item = screen.Items[i];
            bool selected = i == screen.SelectedIndex;
            float rowY = top + HeaderH + v * RowHeight;
            float rowX = left + 0.004f;

            if (selected)
            {
                DrawRect(rowX, rowY, PanelWidth - 0.008f, RowHeight - 0.005f, RedTint.R, RedTint.G, RedTint.B, 255);
                DrawRect(rowX, rowY, 0.006f, RowHeight - 0.005f, Red.R, Red.G, Red.B, 255);   // red left rail
            }
            else if (v % 2 == 1)
            {
                DrawRect(rowX, rowY, PanelWidth - 0.008f, RowHeight - 0.005f, Rule.R, Rule.G, Rule.B, 42);   // zebra hairline
            }

            string title = item.Title ?? "";
            bool hasSub = item.Submenu != null;
            var color = selected ? Ink : PaperDim;
            float rowCenterY = rowY + (RowHeight - 0.005f) / 2f;
            DrawText(title + (hasSub ? "  ▸" : ""), left + 0.020f,
                CenterTextY(rowCenterY, RowHeight - 0.005f, 0.26f, 4), 0.26f, color.R, color.G, color.B, 255, font: 4);

            if (!string.IsNullOrEmpty(item.Value))
            {
                float valW = 0.10f;
                DrawText(item.Value!, PanelX + PanelWidth / 2f - 0.012f - valW,
                    CenterTextY(rowCenterY, RowHeight - 0.005f, 0.24f, 4), 0.24f,
                    selected ? Red.R : color.R, selected ? Red.G : color.G, selected ? Red.B : color.B, 255, font: 4);
            }
        }

        // ── Scroll strip (above/below indicators) ──
        float stripY = top + HeaderH + visible * RowHeight;
        if (first > 0)
            DrawText($"▴ {first} above", left + 0.020f, CenterTextY(stripY + 0.011f, 0.022f, 0.22f, 4), 0.22f,
                Red.R, Red.G, Red.B, 255, font: 4);
        if (first + visible < screen.Items.Count)
        {
            int below = screen.Items.Count - (first + visible);
            DrawText($"▾ {below} below", PanelX + PanelWidth / 2f - 0.012f - 0.10f,
                CenterTextY(stripY + 0.011f, 0.022f, 0.22f, 4), 0.22f, Red.R, Red.G, Red.B, 255, font: 4);
        }

        // ── Key-hint footer ──
        float footY = top + panelH - 0.030f;
        DrawRect(left + 0.004f, footY, PanelWidth - 0.008f, 0.026f, Navy.R, Navy.G, Navy.B, 235);
        DrawText("▲▼ NAV · ↵ OPEN · ⎋ BACK", left + 0.020f,
            CenterTextY(footY, 0.026f, 0.21f, 4), 0.21f, 220, 216, 200, 255, font: 4);
    }

    public void DrawHint(string text)
    {
        _hint = text;
        if (string.IsNullOrWhiteSpace(text)) return;
        float w = 0.30f;
        DrawRect(0.5f, 0.88f, w, 0.035f, 0, 0, 0, 160);
        DrawText(text, 0.5f - w / 2f + 0.008f, CenterTextY(0.88f, 0.035f, 0.28f, 0), 0.28f, 220, 220, 220, 255);
    }

    /// <summary>S17: text centered with the game's OWN measurement —
    /// GET_TEXT_SCALE_HEIGHT(scale, font) returns the exact line height, so the text
    /// always sits inside its container regardless of font/ascent.</summary>
    private static float CenterTextY(float rectCenterY, float rectH, float scale, int font)
    {
        float h = Function.Call<float>((Hash)0xDB88A37483346780, scale, font);
        return rectCenterY - h / 2f;
    }

    private static void DrawRect(float x, float y, float w, float h, int r, int g, int b, int a)
        => Function.Call(Hash.DRAW_RECT, x, y, w, h, r, g, b, a);

    private static void DrawText(string text, float x, float y, float scale, int r, int g, int b, int a, bool bold = false, int font = 0)
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
