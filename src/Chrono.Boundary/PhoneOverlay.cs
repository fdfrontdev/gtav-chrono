using System.Collections.Generic;
using System.Linq;
using Chrono.Application;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>Phone-styled WEBNET panel drawn over the in-game phone screen area (right side).</summary>
public sealed class PhoneOverlay : IPhoneOverlay
{
    private const float PanelX = 0.72f, PanelY = 0.38f, PanelW = 0.26f, PanelH = 0.46f;

    public void ShowFeed(IReadOnlyList<NewsFeedItem> items)
    {
        // Dark phone glass
        Function.Call(Hash.DRAW_RECT, PanelX, PanelY, PanelW, PanelH, 12, 12, 18, 240);
        // Header
        DrawText("WEBNET", 0.735f, 0.285f, 0.42f, 45, 190, 255);
        DrawText("what the street is talking about", 0.735f, 0.315f, 0.24f, 120, 130, 150);

        int i = 0;
        foreach (var item in items.Take(6))   // phone screen fits ~6 posts
        {
            DrawText(item.When, 0.735f, 0.345f + i * 0.062f, 0.26f, 120, 130, 150);
            DrawText(item.Text, 0.735f, 0.375f + i * 0.062f, 0.30f,
                item.Viral ? 45 : 235, item.Viral ? 225 : 235, item.Viral ? 130 : 235);
            i++;
        }

        if (i == 0)
            DrawText("No breaking stories right now", 0.735f, 0.40f, 0.30f, 150, 150, 160);
    }

    public void Hide()
    {
        // nothing to clear — panel is drawn per-frame only while open
    }

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
