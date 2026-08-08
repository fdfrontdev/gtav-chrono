namespace Chrono.Application.Ports;

/// <summary>Native-style menu rendering (implemented by the SHVDN boundary via HUD natives).</summary>
public interface IMenuRenderer
{
    void Render(MenuScreen screen);
    void DrawHint(string text);   // persistent top-center hint (e.g. fly controls)
}
