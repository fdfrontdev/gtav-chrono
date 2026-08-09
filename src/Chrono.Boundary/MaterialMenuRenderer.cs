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
/// S21 follow-up: ALL geometry is computed by the pure <see cref="MenuLayoutEngine"/>
/// (Application) — this renderer only maps the layout to DRAW_RECT/DRAW_TEXT.
/// The same engine drives the HTML preview tool (Playwright-style visual testing,
/// user request): one layout, two outputs. Text width measured with the game's
/// own width command; ellipsis happens in the engine.
/// </summary>
public sealed class MaterialMenuRenderer : IMenuRenderer
{
    private string _hint = "";

    private static readonly (int R, int G, int B) Primary = (24, 103, 192);       // Vuetify #1867C0
    private static readonly (int R, int G, int B) Surface = (30, 30, 30);         // #1E1E1E
    private static readonly (int R, int G, int B) SurfaceVariant = (44, 44, 44);  // #2C2C2C
    private static readonly (int R, int G, int B) OnSurface = (235, 235, 235);    // near-white
    private static readonly (int R, int G, int B) OnSurfaceDim = (160, 160, 160); // secondary text
    private static readonly (int R, int G, int B) Success = (76, 175, 80);        // #4CAF50

    public void Render(MenuScreen screen)
    {
        var layout = MenuLayoutEngine.Compute(screen, Measure);

        float left = MenuLayoutEngine.PanelX - MenuLayoutEngine.PanelWidth / 2f;
        float right = MenuLayoutEngine.PanelX + MenuLayoutEngine.PanelWidth / 2f;
        var p = layout.Panel;

        // ── Elevation: shadow layer → surface (Material cards have NO outline —
        // shadows only; the S21 outline border read as a "divider line", user UAT) ──
        // NOTE: DRAW_RECT x/y are CENTER coords; the engine gives left-edge rects,
        // so every rect center = X + W/2 (this was the "text starts in a 2nd
        // column" bug — text was left-edge but rects were centered at the same X).
        Rect(layout.Shadow.X + layout.Shadow.W / 2f, layout.Shadow.Y + layout.Shadow.H / 2f,
            layout.Shadow.W, layout.Shadow.H, 0, 0, 0, 170);
        Rect(p.X + p.W / 2f, p.Y + p.H / 2f, p.W, p.H, Surface.R, Surface.G, Surface.B, 252);

        // ── Header: primary bar + screen title (single text — no brand column) ──
        float barCenter = p.Y + 0.021f;
        float barLeft = left + 0.006f, barW = MenuLayoutEngine.PanelWidth - 0.012f;
        Rect(barLeft + barW / 2f, barCenter, barW, 0.042f, Primary.R, Primary.G, Primary.B, 255);
        Text(layout.HeaderTitle, left + 0.012f, CenterY(barCenter, 0.30f, 4), 0.30f,
            OnSurface.R, OnSurface.G, OnSurface.B, 255, bold: true, font: 4);

        // ── List rows (viewport slice) — every rect centered on its row center ──
        int rowIndex = 0;
        foreach (var row in layout.Rows)
        {
            float rowLeft = left + 0.006f, rowW = MenuLayoutEngine.PanelWidth - 0.012f;
            if (row.Selected)
            {
                Rect(rowLeft + rowW / 2f, row.CenterY, rowW, MenuLayoutEngine.RowHeight - 0.005f,
                    Primary.R, Primary.G, Primary.B, 90);
            }
            else if (rowIndex % 2 == 1)
            {
                Rect(rowLeft + rowW / 2f, row.CenterY, rowW, MenuLayoutEngine.RowHeight - 0.005f,
                    SurfaceVariant.R, SurfaceVariant.G, SurfaceVariant.B, 110);
            }
            rowIndex++;

            var color = row.Selected ? OnSurface : OnSurfaceDim;
            string label = row.Title + (row.HasSubmenu ? "  >" : "");
            Text(label, left + 0.012f, CenterY(row.CenterY, 0.26f, 4), 0.26f,
                color.R, color.G, color.B, 255, font: 4);

            if (!string.IsNullOrEmpty(row.Value))
            {
                // S21 fix: values use the ACCENT green when selected, secondary otherwise —
                // never error-red (that made every row look broken)
                var vc = row.Selected ? Success : OnSurfaceDim;
                Text(row.Value!, right - 0.016f - 0.10f, CenterY(row.CenterY, 0.24f, 4), 0.24f,
                    vc.R, vc.G, vc.B, 255, font: 4);
            }
        }

        // ── Scroll strip ──
        float rowsEnd = layout.RowsEndY;
        if (layout.ScrollUp != null)
            Text(layout.ScrollUp, left + 0.020f, CenterY(rowsEnd + 0.007f, 0.20f, 4), 0.20f, OnSurfaceDim.R, OnSurfaceDim.G, OnSurfaceDim.B, 255, font: 4);
        if (layout.ScrollDown != null)
            Text(layout.ScrollDown, right - 0.016f - 0.085f, CenterY(rowsEnd + 0.007f, 0.20f, 4), 0.20f, OnSurfaceDim.R, OnSurfaceDim.G, OnSurfaceDim.B, 255, font: 4);

        // ── Footer (ASCII only — font 4 has no arrow/enter/back glyphs) ──
        var foot = layout.FooterBar;
        Rect(foot.X + foot.W / 2f, foot.Y, foot.W, foot.H, SurfaceVariant.R, SurfaceVariant.G, SurfaceVariant.B, 235);
        Text(layout.FooterText, left + 0.020f, CenterY(foot.Y, 0.20f, 4), 0.20f,
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

    // ── Text measurement: the game's own width command (injected into the engine) ──
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
