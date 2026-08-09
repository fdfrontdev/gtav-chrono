using System;
using Chrono.Application;
using Chrono.Application.Ports;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// S21 — Vuetify/Material-style menu renderer (user UAT r15: "use a proper
/// component design like vuetifyjs.com"). Material Design tokens: dark surface
/// with elevation layers, primary accent bar, list rows with measured text.
///
/// THE definitive text fix: every label is MEASURED with
/// BEGIN/END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT and ellipsized to its
/// column width — text physically cannot overflow its container (this kills the
/// S14/S17/S18 "text off from container" bug class for good).
/// </summary>
public sealed class MaterialMenuRenderer : IMenuRenderer
{
    // ── Material 3 tokens (Vuetify palette) ──
    private const float PanelX = 0.78f;         // panel center X (normalized)
    private const float PanelWidth = 0.34f;
    private const float StartY = 0.14f;
    private const float RowHeight = 0.038f;
    private const float HeaderH = 0.058f;       // primary bar + title
    private const int MaxVisibleRows = 12;
    private string _hint = "";

    private static readonly (int R, int G, int B) Primary = (24, 103, 192);       // Vuetify #1867C0
    private static readonly (int R, int G, int B) Surface = (30, 30, 30);         // #1E1E1E
    private static readonly (int R, int G, int B) SurfaceVariant = (44, 44, 44);  // #2C2C2C
    private static readonly (int R, int G, int B) OnSurface = (235, 235, 235);    // near-white
    private static readonly (int R, int G, int B) OnSurfaceDim = (160, 160, 160); // secondary text
    private static readonly (int R, int G, int B) Outline = (122, 122, 122);
    private static readonly (int R, int G, int B) Error = (207, 102, 121);        // #CF6679
    private static readonly (int R, int G, int B) Success = (76, 175, 80);        // #4CAF50

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
        float panelH = HeaderH + rowsH + 0.012f + 0.028f + 0.012f;   // header + rows + gap + footer + pad
        float top = StartY;
        float left = PanelX - PanelWidth / 2f;
        float right = PanelX + PanelWidth / 2f;

        // ── Elevation: shadow layer → surface → 1px outline border (Material) ──
        Rect(left - 0.008f, top - 0.008f, PanelWidth + 0.016f, panelH + 0.016f, 0, 0, 0, 170);
        Rect(left, top, PanelWidth, panelH, Surface.R, Surface.G, Surface.B, 252);
        Rect(left, top, PanelWidth, 0.003f, Outline.R, Outline.G, Outline.B, 200);            // top border
        Rect(left, top + panelH, PanelWidth, 0.003f, Outline.R, Outline.G, Outline.B, 200);   // bottom border
        Rect(left, top, 0.003f, panelH, Outline.R, Outline.G, Outline.B, 200);                // left border
        Rect(left + PanelWidth, top, 0.003f, panelH, Outline.R, Outline.G, Outline.B, 200);   // right border
        Rect(left, top + panelH / 2f, PanelWidth, 0.003f, Primary.R, Primary.G, Primary.B, 255);  // top accent line

        // ── Header: primary bar + screen title + section label ──
        float barCenter = top + 0.021f;
        Rect(left + 0.006f, barCenter, PanelWidth - 0.012f, 0.042f, Primary.R, Primary.G, Primary.B, 255);
        Text(screen.Title, left + 0.016f, CenterY(barCenter, 0.30f, 4), 0.30f,
            OnSurface.R, OnSurface.G, OnSurface.B, 255, bold: true, font: 4);
        Text("CHRONO", right - 0.016f - 0.055f, CenterY(barCenter, 0.22f, 4), 0.22f,
            200, 215, 240, 255, font: 4);

        // ── List rows (viewport slice) — every rect centered on its row center ──
        for (int v = 0; v < visible; v++)
        {
            int i = first + v;
            var item = screen.Items[i];
            bool selected = i == screen.SelectedIndex;
            float rowTop = top + HeaderH + v * RowHeight;
            float rowCenter = rowTop + (RowHeight - 0.005f) / 2f;

            if (selected)
            {
                Rect(left + 0.006f, rowCenter, PanelWidth - 0.012f, RowHeight - 0.005f,
                    Primary.R, Primary.G, Primary.B, 90);
                Rect(left + 0.006f, rowCenter, 0.005f, RowHeight - 0.009f, Primary.R, Primary.G, Primary.B, 255);
            }
            else if (v % 2 == 1)
            {
                Rect(left + 0.006f, rowCenter, PanelWidth - 0.012f, RowHeight - 0.005f,
                    SurfaceVariant.R, SurfaceVariant.G, SurfaceVariant.B, 110);
            }

            string title = item.Title ?? "";
            bool hasSub = item.Submenu != null;
            var color = selected ? OnSurface : OnSurfaceDim;
            string label = title + (hasSub ? "  >" : "");

            // Title column: measured + ellipsized to fit beside the value column
            float titleMaxW = PanelWidth - 0.044f - (string.IsNullOrEmpty(item.Value) ? 0f : 0.115f);
            Text(Truncate(label, titleMaxW, 0.26f, 4), left + 0.020f, CenterY(rowCenter, 0.26f, 4), 0.26f,
                color.R, color.G, color.B, 255, font: 4);

            if (!string.IsNullOrEmpty(item.Value))
            {
                float valW = 0.10f;
                string val = Truncate(item.Value!, 0.10f, 0.24f, 4);
                Text(val, right - 0.016f - valW, CenterY(rowCenter, 0.24f, 4), 0.24f,
                    selected ? Success.R : Error.R,
                    selected ? Success.G : Error.G,
                    selected ? Success.B : Error.B, 255, font: 4);
            }
        }

        // ── Scroll strip ──
        float rowsEnd = top + HeaderH + rowsH;
        if (first > 0)
            Text($"{first} UP", left + 0.020f, CenterY(rowsEnd + 0.007f, 0.20f, 4), 0.20f, OnSurfaceDim.R, OnSurfaceDim.G, OnSurfaceDim.B, 255, font: 4);
        if (first + visible < screen.Items.Count)
        {
            int below = screen.Items.Count - (first + visible);
            Text($"{below} DOWN", right - 0.016f - 0.085f, CenterY(rowsEnd + 0.007f, 0.20f, 4), 0.20f, OnSurfaceDim.R, OnSurfaceDim.G, OnSurfaceDim.B, 255, font: 4);
        }

        // ── Footer (ASCII only — font 4 has no arrow/enter/back glyphs) ──
        float footCenter = rowsEnd + 0.012f + 0.014f;
        Rect(left + 0.006f, footCenter, PanelWidth - 0.012f, 0.028f, SurfaceVariant.R, SurfaceVariant.G, SurfaceVariant.B, 235);
        Text("UP/DOWN MOVE  -  ENTER OPEN  -  BACK", left + 0.020f, CenterY(footCenter, 0.20f, 4), 0.20f,
            OnSurfaceDim.R, OnSurfaceDim.G, OnSurfaceDim.B, 255, font: 4);
    }

    public void DrawHint(string text)
    {
        _hint = text;
        if (string.IsNullOrWhiteSpace(text)) return;
        float w = 0.30f;
        Rect(0.5f, 0.88f, w, 0.035f, Surface.R, Surface.G, Surface.B, 200);
        Text(text, 0.5f - w / 2f + 0.008f, CenterY(0.88f, 0.26f, 0), 0.26f, OnSurface.R, OnSurface.G, OnSurface.B, 255);
    }

    // ── Text measurement + ellipsis: the S21 overflow fix ──
    // Measure with the game's own width command; truncate + "..." until it fits.
    private static string Truncate(string text, float maxW, float scale, int font)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (Measure(text, scale, font) <= maxW) return text;
        string t = text;
        for (int i = t.Length - 1; i > 0; i--)
        {
            t = text.Substring(0, i) + "...";
            if (Measure(t, scale, font) <= maxW) return t;
        }
        return "...";
    }

    private static float Measure(string text, float scale, int font)
    {
        Function.Call(Hash.SET_TEXT_FONT, font);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        return Function.Call<float>(Hash.END_TEXT_COMMAND_GET_SCREEN_WIDTH_OF_DISPLAY_TEXT, 0);
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
