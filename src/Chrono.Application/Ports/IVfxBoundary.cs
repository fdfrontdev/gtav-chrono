using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>Visual effects primitives (implemented by the SHVDN boundary).</summary>
public interface IVfxBoundary
{
    void SetTimecycleModifier(string name, float strength);
    void ClearTimecycleModifier();
    void SpawnParticle(string assetName, string effectName, Vector3 position, float scale);
    void ShakeCamera(float amplitude);
    void StopCameraShake();
    void ScreenFlash(int fadeInMs);
}
