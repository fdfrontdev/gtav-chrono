using System.Collections.Generic;
using System.Linq;
using Chrono.Application;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// Phone-styled WEBNET social feed (S7, redesigned S10 per SB design guidance:
/// "distill the most important thing at each moment; scroll-based feed with
/// extreme filtering" — Design for How People Think). Cards, live header, viral
/// badges, dim timestamps: hierarchy first, no visual noise.
/// </summary>
public sealed class PhoneOverlay : IPhoneOverlay
{
    // Phone screen area (right side of the screen)
    private const float PanelX = 0.715f, PanelY = 0.175f, PanelW = 0.285f, PanelH = 0.55f;
    private const float CardH = 0.075f;
    private const int MaxPosts = 6;

    public void ShowFeed(IReadOnlyList<NewsFeedItem> items)
    {
        // Phone glass
        Function.Call(Hash.DRAW_RECT, PanelX, PanelY, PanelW, PanelH, 8, 10, 16, 242);
        // Status strip
        Function.Call(Hash.DRAW_RECT, PanelX, PanelY - PanelH / 2 + 0.016f, PanelW, 0.032f, 12, 14, 22, 255);

        // Header
        DrawText("WEBNET", PanelX - PanelW / 2 + 0.014f, PanelY - PanelH / 2 + 0.012f, 0.40f, 45, 190, 255);
        // Live indicator (pulsing dot flavor)
        Function.Call(Hash.DRAW_RECT, PanelX + PanelW / 2 - 0.038f, PanelY - PanelH / 2 + 0.028f, 0.008f, 0.008f, 60, 220, 120, 255);
        DrawText("LIVE", PanelX + PanelW / 2 - 0.032f, PanelY - PanelH / 2 + 0.019f, 0.22f, 60, 220, 120);

        // Section divider
        DrawText("SAN ANDREAS · TRENDING NOW", PanelX - PanelW / 2 + 0.014f, PanelY - PanelH / 2 + 0.062f, 0.20f, 110, 120, 140);

        float top = PanelY - PanelH / 2 + 0.088f;
        int i = 0;
        foreach (var item in items.Take(MaxPosts))
        {
            float cy = top + i * (CardH + 0.008f);
            if (cy + CardH / 2 > PanelY + PanelH / 2 - 0.012f) break;   // no overflow past the glass

            // Post card
            Function.Call(Hash.DRAW_RECT, PanelX, cy, PanelW - 0.024f, CardH, 16, 20, 30, 225);
            // Viral badge
            if (item.Viral)
            {
                Function.Call(Hash.DRAW_RECT, PanelX + PanelW / 2 - 0.055f, cy - CardH / 2 + 0.008f, 0.052f, 0.014f, 40, 160, 90, 255);
                DrawText("VIRAL", PanelX + PanelW / 2 - 0.048f, cy - CardH / 2 + 0.0035f, 0.17f, 210, 255, 230);
            }
            // Timestamp (dim, top-left of card)
            DrawText(item.When, PanelX - PanelW / 2 + 0.026f, cy - CardH / 2 + 0.005f, 0.18f, 120, 130, 150);
            // Post text (clamped to card)
            DrawText(Clamp(item.Text, 44), PanelX - PanelW / 2 + 0.026f, cy - CardH / 2 + 0.026f, 0.27f, 235, 238, 245);
            i++;
        }

        if (i == 0)
            DrawText("No breaking stories right now — go make some", PanelX - PanelW / 2 + 0.014f, top + 0.02f, 0.26f, 150, 155, 165);

        // Footer
        DrawText($"↑ close · {i} posts", PanelX - PanelW / 2 + 0.014f, PanelY + PanelH / 2 - 0.024f, 0.20f, 110, 120, 140);
    }

    public void Hide()
    {
        // nothing to clear — panel is drawn per-frame only while open
    }

    private static string Clamp(string text, int max)
        => text.Length <= max ? text : text.Substring(0, max - 1) + "…";

    private static void DrawText(string text, float x, float y, float scale, int r, int g, int b)
    {
        Function.Call(Hash.SET_TEXT_FONT, 4);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, 255);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }
}
