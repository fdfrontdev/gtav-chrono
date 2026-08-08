using System.Collections.Generic;
using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// VFX primitives: timecycle tint, particles (retry-until-loaded), camera shake,
/// screen flash, player alpha (vanish/rematerialize — Goku instant transmission).
/// </summary>
public sealed class VfxBoundary : IVfxBoundary
{
    private readonly Dictionary<string, ParticleEffectAsset> _assets = new();

    public void SetTimecycleModifier(string name, float strength)
        => Function.Call(Hash.SET_TIMECYCLE_MODIFIER, name, strength);

    public void ClearTimecycleModifier()
        => Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);

    public void SpawnParticle(string assetName, string effectName, Vector3 position, float scale)
    {
        var asset = GetOrCreateAsset(assetName);
        // Particle assets load asynchronously — keep requesting until ready (retry model,
        // never blocks a power; the flash+alpha carry the effect meanwhile).
        if (!asset.IsLoaded)
        {
            asset.Request();
            return;
        }

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
        ScreenFadeOut(0);
        GTA.UI.Screen.FadeIn(fadeInMs);
    }

    public void ScreenFadeOut(int fadeOutMs)
        => GTA.UI.Screen.FadeOut(fadeOutMs);

    public void SetPlayerAlpha(int alpha)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        Function.Call(Hash.SET_ENTITY_ALPHA, ped.Handle, alpha, false);
    }

    public void ResetPlayerAlpha()
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        Function.Call(Hash.RESET_ENTITY_ALPHA, ped.Handle);
    }

    private ParticleEffectAsset GetOrCreateAsset(string assetName)
    {
        if (!_assets.TryGetValue(assetName, out var asset))
        {
            asset = new ParticleEffectAsset(assetName);
            _assets[assetName] = asset;
            asset.Request();
        }
        return asset;
    }
}
