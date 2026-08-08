using Chrono.Application;
using Chrono.Application.Ports;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// Native-style menu rendering via HUD draw natives (UIUX doc §9).
/// Normalized screen coordinates; panel bottom-right quadrant, selection highlighted.
/// </summary>
public sealed class NativeMenuRenderer : IMenuRenderer
{
    private const float PanelX = 0.78f;      // panel center X (normalized)
    private const float PanelWidth = 0.32f;
    private const float StartY = 0.20f;
    private const float RowHeight = 0.038f;
    private const int FontId = 0;            // ChaletLondon

    public void Render(MenuScreen screen)
    {
        float panelHeight = 0.10f + screen.Items.Count * RowHeight;
        DrawPanel(PanelX, StartY + panelHeight / 2f, PanelWidth, panelHeight);

        // Title
        DrawText(screen.Title, PanelX - PanelWidth / 2f + 0.012f, StartY - 0.035f, 0.42f, 220, 220, 220, 255);

        for (int i = 0; i < screen.Items.Count; i++)
        {
            var item = screen.Items[i];
            bool selected = i == screen.SelectedIndex;
            float rowY = StartY + i * RowHeight;

            if (selected) DrawPanel(PanelX, rowY + RowHeight / 2f, PanelWidth, RowHeight - 0.006f, 52, 120, 200, 230);

            float leftX = PanelX - PanelWidth / 2f + 0.012f;
            DrawText((item.Title ?? "") + (item.Submenu != null ? " >" : ""), leftX, rowY, 0.36f,
                selected ? 255 : 210, selected ? 255 : 210, selected ? 255 : 210, 255);

            if (!string.IsNullOrEmpty(item.Value))
            {
                DrawText(item.Value!, PanelX + PanelWidth / 2f - 0.012f - 0.09f, rowY, 0.36f,
                    selected ? 255 : 160, selected ? 255 : 160, selected ? 255 : 160, 255);
            }
        }
    }

    /// <summary>Persistent top-center hint (e.g. fly controls) with drop shadow.</summary>
    public void DrawHint(string text)
    {
        Function.Call(Hash.SET_TEXT_FONT, FontId);
        Function.Call(Hash.SET_TEXT_SCALE, 0.42f, 0.42f);
        Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, 255);
        Function.Call(Hash.SET_TEXT_DROP_SHADOW, 2, 0, 0, 0, 200);
        Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0.5f, 0.06f);
    }

    private static void DrawPanel(float x, float y, float w, float h, int r = 0, int g = 0, int b = 0, int a = 200)
        => Function.Call(Hash.DRAW_RECT, x, y, w, h, r, g, b, a);

    private static void DrawText(string text, float x, float y, float scale, int r, int g, int b, int a)
    {
        Function.Call(Hash.SET_TEXT_FONT, FontId);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, a);
        Function.Call(Hash.SET_TEXT_CENTRE, false);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, x, y);
    }
}
