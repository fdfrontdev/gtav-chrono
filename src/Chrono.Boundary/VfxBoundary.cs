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
    private int _flashR, _flashG, _flashB, _flashA, _flashFrames;

    /// <summary>Per-frame overlay maintenance: color flash countdown + draw.</summary>
    public void Tick()
    {
        if (_flashFrames > 0)
        {
            // Full-screen flash overlay (normalized coords 0.5/0.5, size 2.0 covers screen)
            Function.Call(Hash.DRAW_RECT, 0.5f, 0.5f, 2.0f, 2.0f, _flashR, _flashG, _flashB, _flashA);
            _flashFrames--;
        }
    }

    /// <summary>Queue a full-screen color flash for N frames (anime flash effect).</summary>
    public void FlashColor(int r, int g, int b, int alpha, int frames)
    {
        _flashR = r; _flashG = g; _flashB = b; _flashA = alpha;
        _flashFrames = frames;
    }

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

    public void ScreenFadeIn(int fadeInMs)
        => GTA.UI.Screen.FadeIn(fadeInMs);

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

    public void DrawMarker(System.Numerics.Vector3 pos, float scale, int r, int g, int b, int a)
    {
        // UpsideDownCone (type 1) — reads as a "land here" reticle at the blink point
        Function.Call(Hash.DRAW_MARKER, 1,
            pos.X, pos.Y, pos.Z,
            0f, 0f, 0f,
            0f, 180f, 0f,
            scale, scale, scale,
            r, g, b, a,
            false, false, 2, false, null, null, false);
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
