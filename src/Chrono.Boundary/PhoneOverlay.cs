using System.Collections.Generic;
using System.Linq;
using Chrono.Application;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// WEBNET phone feed v2 (S13) — a modern social-app look: bordered glass,
/// LIVE pill, layered post cards with avatar dot + agency meta line + body +
/// viral badge, clean hierarchy (SB: "distill the most important thing at
/// each moment"). All custom-drawn — full control, no native UI.
/// </summary>
public sealed class PhoneOverlay : IPhoneOverlay
{
    private const float PanelX = 0.715f, PanelY = 0.175f, PanelW = 0.285f, PanelH = 0.56f;
    private const float CardH = 0.082f;
    private const int MaxPosts = 5;

    public void ShowFeed(IReadOnlyList<NewsFeedItem> items)
    {
        float left = PanelX - PanelW / 2f;

        // ── Phone glass: border layer + body ──
        DrawRect(PanelX, PanelY, PanelW + 0.006f, PanelH + 0.006f, 46, 54, 74, 255);
        DrawRect(PanelX, PanelY, PanelW, PanelH, 9, 11, 18, 246);

        // ── Header: WEBNET + LIVE pill ──
        DrawText("WEBNET", left + 0.016f, PanelY - PanelH / 2f + 0.010f, 0.46f, 255, 255, 255, true);
        DrawRect(PanelX + PanelW / 2f - 0.062f, PanelY - PanelH / 2f + 0.024f, 0.050f, 0.016f, 26, 120, 62, 255);
        DrawText("LIVE", PanelX + PanelW / 2f - 0.055f, PanelY - PanelH / 2f + 0.0175f, 0.20f, 120, 255, 160);

        // ── Section line ──
        DrawText("SAN ANDREAS · TRENDING NOW", left + 0.016f, PanelY - PanelH / 2f + 0.058f, 0.20f, 110, 120, 140);

        float top = PanelY - PanelH / 2f + 0.086f;
        int i = 0;
        foreach (var item in items.Take(MaxPosts))
        {
            float cy = top + i * (CardH + 0.008f);
            if (cy + CardH / 2f > PanelY + PanelH / 2f - 0.014f) break;

            // ── Card: border + body layers ──
            DrawRect(PanelX, cy, PanelW - 0.02f, CardH, 30, 36, 52, 255);
            DrawRect(PanelX, cy, PanelW - 0.028f, CardH - 0.005f, 17, 21, 32, 245);

            // Avatar dot (agency color by viralness)
            int avR = item.Viral ? 60 : 90, avG = item.Viral ? 200 : 150, avB = item.Viral ? 110 : 220;
            DrawRect(left + 0.022f, cy - CardH / 2f + 0.024f, 0.014f, 0.014f, avR, avG, avB, 255);

            // Meta line: agency + timestamp (dim)
            DrawText($"WEBNET NEWS · {item.When}", left + 0.042f, cy - CardH / 2f + 0.013f, 0.17f, 120, 130, 150);

            // Viral badge (right pill)
            if (item.Viral)
            {
                DrawRect(PanelX + PanelW / 2f - 0.058f, cy - CardH / 2f + 0.010f, 0.046f, 0.014f, 40, 160, 90, 255);
                DrawText("VIRAL", PanelX + PanelW / 2f - 0.052f, cy - CardH / 2f + 0.006f, 0.16f, 210, 255, 230);
            }

            // Body text (2 lines max, clamped)
            string body = Clamp(item.Text, 60);
            DrawText(body, left + 0.022f, cy - CardH / 2f + 0.038f, 0.25f, 235, 238, 245);
            i++;
        }

        if (i == 0)
            DrawText("No breaking stories right now — go make some", left + 0.016f, top + 0.02f, 0.26f, 150, 155, 165);

        // ── Footer ──
        DrawText($"↑ close · {i} posts", left + 0.016f, PanelY + PanelH / 2f - 0.026f, 0.19f, 110, 120, 140);
    }

    public void Hide()
    {
        // panel is drawn per-frame only while open
    }

    private static string Clamp(string text, int max)
        => text.Length <= max ? text : text.Substring(0, max - 1) + "…";

    private static void DrawRect(float x, float y, float w, float h, int r, int g, int b, int a)
        => Function.Call(Hash.DRAW_RECT, x, y, w, h, r, g, b, a);

    private static void DrawText(string text, float x, float y, float scale, int r, int g, int b, bool bold = false)
    {
        Function.Call(Hash.SET_TEXT_FONT, bold ? 1 : 4);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }
}
