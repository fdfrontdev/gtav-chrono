using System;
using Chrono.Application;
using Chrono.Application.Ports;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// S21 — Material-style persistent justice widget (bottom-right card, user
/// ruling): wanted stars, court countdown, prison day counter, bail/parole,
/// warrant status + LIVE message feed (notifier + WEBNET). Geometry comes from
/// the pure <see cref="HudLayoutEngine"/> (Application) — same engine drives the
/// HTML preview tool. v2 (screenshot UAT): balanced rows, wider accent rail.
/// </summary>
public sealed class MaterialHudRenderer : IHudRenderer
{
    private static readonly (int R, int G, int B) Primary = (24, 103, 192);
    private static readonly (int R, int G, int B) Surface = (30, 30, 30);

    public void DrawJusticeHud(JusticeHudState state)
    {
        if (!state.Visible) return;

        var layout = HudLayoutEngine.Compute(
            state.StatusLine, state.CountdownLine, state.SecondLine,
            state.Feed ?? Array.Empty<HudFeedItem>(), Measure,
            hasCountdown: !string.IsNullOrEmpty(state.CountdownLine),
            hasIdentity: !string.IsNullOrEmpty(state.SecondLine));

        // Card: shadow → surface → accent rail
        var s = layout.Shadow;
        Rect(s.X + s.W / 2f, s.Y + s.H / 2f, s.W, s.H, 0, 0, 0, 160);
        var c = layout.Card;
        Rect(c.X + c.W / 2f, c.Y + c.H / 2f, c.W, c.H, Surface.R, Surface.G, Surface.B, 240);
        var a = layout.Accent;
        Rect(a.X + a.W / 2f, a.Y + a.H / 2f, a.W, a.H, Primary.R, Primary.G, Primary.B, 255);

        // Rows
        foreach (var row in layout.Rows)
        {
            var t = row.Text;
            Text(t.Text, t.X, CenterY(t.Y, t.Scale, t.Font), t.Scale,
                row.Color.R, row.Color.G, row.Color.B, 255, bold: t.Bold, font: t.Font);
        }
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
