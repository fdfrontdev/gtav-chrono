using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>Visual effects primitives (implemented by the SHVDN boundary).</summary>
public interface IVfxBoundary
{
    void Tick();                              // per-frame overlay maintenance (color flashes)
    void SetTimecycleModifier(string name, float strength);
    void ClearTimecycleModifier();
    void SpawnParticle(string assetName, string effectName, Vector3 position, float scale);
    void ShakeCamera(float amplitude);
    void StopCameraShake();
    void ScreenFlash(int fadeInMs);
    void ScreenFadeOut(int fadeOutMs);
    void ScreenFadeIn(int fadeInMs);
    void FlashColor(int r, int g, int b, int alpha, int frames);   // anime flash overlay (Minato/Goku)
    void SetPlayerAlpha(int alpha);      // 0 = invisible (vanish), 255 = visible
    void ResetPlayerAlpha();

    /// <summary>Draw a world-space targeting marker (dash aim reticle, v0.8.0).</summary>
    void DrawMarker(System.Numerics.Vector3 pos, float scale, int r, int g, int b, int a);
}
