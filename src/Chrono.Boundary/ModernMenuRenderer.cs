using System;
using Chrono.Application;
using Chrono.Application.Ports;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// Modern custom-drawn menu (S13) — replaces the plain native-style panel with a
/// layered, translucent card UI: accent header bar, shadow border, selection
/// pill, submenu chevrons, value column, scroll indicator and a key-hint footer.
/// Full control via DRAW_RECT/DRAW_TEXT (the approach LemonUI and other modern
/// frameworks use under the hood — researched per user request).
/// </summary>
public sealed class ModernMenuRenderer : IMenuRenderer
{
    private const float PanelX = 0.78f;         // panel center X (normalized)
    private const float PanelWidth = 0.34f;
    private const float StartY = 0.17f;
    private const float RowHeight = 0.036f;
    private const float HeaderH = 0.052f;
    private const int MaxVisibleRows = 14;
    private string _hint = "";

    private static readonly (int R, int G, int B) Accent = (255, 179, 0);       // amber
    private static readonly (int R, int G, int B) PanelBg = (16, 18, 26);
    private static readonly (int R, int G, int B) Border = (64, 70, 92);
    private static readonly (int R, int G, int B) RowBg = (28, 32, 44);
    private static readonly (int R, int G, int B) TextDim = (150, 158, 176);
    private static readonly (int R, int G, int B) TextBright = (240, 242, 247);

    public void Render(MenuScreen screen)
    {
        int rows = Math.Min(screen.Items.Count, MaxVisibleRows);
        float panelH = HeaderH + rows * RowHeight + 0.05f;   // + footer hint space
        float top = StartY;
        float left = PanelX - PanelWidth / 2f;

        // ── Panel: layered card (shadow → border → body) ──
        DrawRect(left - 0.006f, top - 0.006f, PanelWidth + 0.012f, panelH + 0.012f, 0, 0, 0, 130);            // soft shadow
        DrawRect(left, top, PanelWidth, panelH, Border.R, Border.G, Border.B, 255);                            // border
        DrawRect(left + 0.004f, top + 0.004f, PanelWidth - 0.008f, panelH - 0.008f, PanelBg.R, PanelBg.G, PanelBg.B, 236);

        // ── Header: accent bar + title ──
        DrawRect(left + 0.004f, top + 0.004f, PanelWidth - 0.008f, HeaderH, Accent.R, Accent.G, Accent.B, 255);  // accent strip
        DrawText(screen.Title.ToUpperInvariant(), left + 0.02f,
            CenterTextY(top + 0.004f + HeaderH / 2f, HeaderH, 0.40f), 0.40f, 14, 16, 24, 255, bold: true);

        // ── Items ──
        for (int i = 0; i < rows; i++)
        {
            var item = screen.Items[i];
            bool selected = i == screen.SelectedIndex;
            float rowY = top + HeaderH + i * RowHeight;
            float rowX = left + 0.004f;

            if (selected)
            {
                // selection pill: accent-tinted, full width
                DrawRect(rowX, rowY, PanelWidth - 0.008f, RowHeight - 0.005f, Accent.R, Accent.G, Accent.B, 92);
                DrawRect(rowX, rowY, 0.005f, RowHeight - 0.005f, Accent.R, Accent.G, Accent.B, 255);  // left rail
            }
            else if (i % 2 == 1)
            {
                DrawRect(rowX, rowY, PanelWidth - 0.008f, RowHeight - 0.005f, RowBg.R, RowBg.G, RowBg.B, 90);  // zebra
            }

            string title = item.Title ?? "";
            bool hasSub = item.Submenu != null;
            var color = selected ? TextBright : TextDim;
            float rowCenterY = rowY + (RowHeight - 0.005f) / 2f;
            DrawText(title + (hasSub ? "  ▸" : ""), left + 0.022f,
                CenterTextY(rowCenterY, RowHeight - 0.005f, 0.30f), 0.30f, color.R, color.G, color.B, 255);

            if (!string.IsNullOrEmpty(item.Value))
            {
                float valW = 0.085f;
                DrawText(item.Value!, PanelX + PanelWidth / 2f - 0.010f - valW,
                    CenterTextY(rowCenterY, RowHeight - 0.005f, 0.28f), 0.28f,
                    selected ? Accent.R : TextDim.R, selected ? Accent.G : TextDim.G, selected ? Accent.B : TextDim.B, 255);
            }
        }

        // ── Scroll indicator ──
        if (screen.Items.Count > MaxVisibleRows)
            DrawText($"▾ {screen.Items.Count - MaxVisibleRows} more", left + 0.022f,
                top + HeaderH + rows * RowHeight + 0.001f, 0.26f, TextDim.R, TextDim.G, TextDim.B, 255);

        // ── Key-hint footer ──
        float footY = top + panelH - 0.030f;
        DrawRect(left + 0.004f, footY, PanelWidth - 0.008f, 0.026f, 0, 0, 0, 120);
        DrawText("▲▼ move  ·  ↵ select  ·  ⎋ back", left + 0.022f,
            CenterTextY(footY, 0.026f, 0.24f), 0.24f, TextDim.R, TextDim.G, TextDim.B, 255);
    }

    public void DrawHint(string text)
    {
        _hint = text;
        if (string.IsNullOrWhiteSpace(text)) return;
        float w = 0.30f;
        DrawRect(0.5f, 0.88f, w, 0.035f, 0, 0, 0, 160);
        DrawText(text, 0.5f - w / 2f + 0.008f, CenterTextY(0.88f, 0.035f, 0.28f), 0.28f, 220, 220, 220, 255);
    }

    /// S14 fix: GTA text y = TOP of the text box; a rect y = its CENTER. To center
    /// text inside its container: textTop = rectCenterY - textHeight/2, with
    /// textHeight ≈ scale * 0.08 (the rect height cancels out).
    private static float CenterTextY(float rectCenterY, float rectH, float scale)
        => rectCenterY - (scale * 0.08f) / 2f;

    private static void DrawRect(float x, float y, float w, float h, int r, int g, int b, int a)
        => Function.Call(Hash.DRAW_RECT, x, y, w, h, r, g, b, a);

    private static void DrawText(string text, float x, float y, float scale, int r, int g, int b, int a, bool bold = false)
    {
        Function.Call(Hash.SET_TEXT_FONT, bold ? 1 : 0);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_EDGE, 1, 0, 0, 0, 140);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }
}
