using System;
using Chrono.Application.Ports;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// S21 — Material-style persistent justice widget (bottom-left card): wanted
/// stars, court countdown, prison day counter, bail/parole, warrant status.
/// Same measured-text discipline as MaterialMenuRenderer — no overflow.
/// </summary>
public sealed class MaterialHudRenderer : IHudRenderer
{
    private const float CardX = 0.022f + 0.105f;   // card center X (bottom-left)
    private const float CardW = 0.21f;
    private const float CardY = 0.945f;            // card center Y (bottom)
    private const float RowH = 0.024f;

    private static readonly (int R, int G, int B) Primary = (24, 103, 192);
    private static readonly (int R, int G, int B) Surface = (30, 30, 30);
    private static readonly (int R, int G, int B) OnSurface = (235, 235, 235);
    private static readonly (int R, int G, int B) Dim = (160, 160, 160);
    private static readonly (int R, int G, int B) Amber = (255, 179, 64);    // court urgent
    private static readonly (int R, int G, int B) Blue = (100, 181, 246);    // prison
    private static readonly (int R, int G, int B) Red = (239, 83, 80);       // warrant / wanted

    public void DrawJusticeHud(JusticeHudState state)
    {
        if (!state.Visible) return;

        // 3 rows max: status / countdown / second — measure height from lines
        float h = 0.010f + 3 * RowH + 0.008f;
        float y = CardY - h / 2f;

        // Card: shadow → surface → primary left rail
        Rect(CardX - 0.005f, y - 0.005f, CardW + 0.010f, h + 0.010f, 0, 0, 0, 160);
        Rect(CardX, y + h / 2f, CardW, h, Surface.R, Surface.G, Surface.B, 235);
        Rect(CardX - CardW / 2f + 0.002f, y + h / 2f, 0.006f, h - 0.004f, Primary.R, Primary.G, Primary.B, 255);

        float left = CardX - CardW / 2f + 0.014f;
        float maxW = CardW - 0.028f;

        // Row 1 — status + stars (red when wanted)
        string status = state.StatusLine;
        var sc = state.Stars > 0 ? Red : OnSurface;
        Text(Truncate(status, maxW, 0.24f, 4), left, CenterY(y + RowH / 2f + 0.005f, 0.24f, 4), 0.24f, sc.R, sc.G, sc.B, 255, bold: true, font: 4);

        // Row 2 — countdown (amber when court, blue when prison, dim otherwise)
        if (!string.IsNullOrEmpty(state.CountdownLine))
        {
            var cc = state.CourtCountdown ? Amber : (state.PrisonCountdown ? Blue : Dim);
            Text(Truncate(state.CountdownLine, maxW, 0.26f, 4), left, CenterY(y + RowH * 1.5f + 0.005f, 0.26f, 4), 0.26f, cc.R, cc.G, cc.B, 255, font: 4);
        }

        // Row 3 — second line (warrant / identity)
        if (!string.IsNullOrEmpty(state.SecondLine))
        {
            Text(Truncate(state.SecondLine, maxW, 0.22f, 4), left, CenterY(y + RowH * 2.5f + 0.005f, 0.22f, 4), 0.22f, Dim.R, Dim.G, Dim.B, 255, font: 4);
        }
    }

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

    private static void Rect(float x, float y, float w, float h, int r, int g, int b, int a)
        => Function.Call(Hash.DRAW_RECT, x, y, w, h, r, g, b, a);

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
