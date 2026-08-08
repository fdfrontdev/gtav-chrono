using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>
/// Cinematic cutscene rendering (S11): script camera + player freeze + anims + banners.
/// The cutscene director (Application) drives phases; this boundary owns the natives.
/// </summary>
public interface ICutsceneRenderer
{
    /// <summary>Freeze the player and ready the script camera.</summary>
    void Begin();

    /// <summary>Position the camera looking at a point (fov in degrees).</summary>
    void SetCamera(Vector3 position, Vector3 lookAt, float fov);

    /// <summary>Center-screen cinematic banner (persists until changed).</summary>
    void ShowBanner(string text);

    /// <summary>Play an animation on the player ped (loop for idle poses).</summary>
    void PlayAnim(string dict, string anim, bool loop);

    /// <summary>Restore the game camera + player control.</summary>
    void End();
}
