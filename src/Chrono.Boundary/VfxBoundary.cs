using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>VFX primitives: timecycle tint, particles, camera shake, screen flash.</summary>
public sealed class VfxBoundary : IVfxBoundary
{
    public void SetTimecycleModifier(string name, float strength)
        => Function.Call(Hash.SET_TIMECYCLE_MODIFIER, name, strength);

    public void ClearTimecycleModifier()
        => Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);

    public void SpawnParticle(string assetName, string effectName, Vector3 position, float scale)
    {
        var asset = new ParticleEffectAsset(assetName);
        asset.Request();
        if (!asset.IsLoaded) return;   // VFX never blocks a power

        World.CreateParticleEffectNonLooped(
            asset,
            effectName,
            EntityFreezer.ToGta(position),
            new GTA.Math.Vector3(0f, 0f, 0f),
            scale,
            InvertAxisFlags.None);
    }

    public void ShakeCamera(float amplitude)
        => GameplayCamera.Shake(CameraShake.Hand, amplitude);

    public void StopCameraShake()
        => GameplayCamera.StopShaking(true);

    public void ScreenFlash(int fadeInMs)
    {
        GTA.UI.Screen.FadeOut(0);
        GTA.UI.Screen.FadeIn(fadeInMs);
    }
}
