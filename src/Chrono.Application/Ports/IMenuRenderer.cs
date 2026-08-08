namespace Chrono.Application.Ports;

/// <summary>Native-style menu rendering (implemented by the SHVDN boundary via HUD natives).</summary>
public interface IMenuRenderer
{
    void Render(MenuScreen screen);
}
