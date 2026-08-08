using System;
using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// VFX orchestration — Goku-style Instant Transmission (Animation &amp; VFX doc v2).
/// Cosmetics only: every effect is toggleable via config and NEVER blocks a power.
/// Phase model: Begin (flash-out + vanish) → [teleport] → Complete (bursts + rematerialize
/// + flash-in + shake). Abort restores visibility if the teleport is refused.
/// </summary>
public sealed class VfxService
{
    private readonly IVfxBoundary _vfx;
    private readonly ILogSink _log;
    private readonly VisualConfig _visual;

    // Particle effects verified against DurtyFree gta-v-data-dumps particleEffectsCompact.json
    // (2026-08-08): scr_trevor4_teleport DOES NOT exist on this build — real ones below.
    private const string TeleportAsset = "scr_rcbarry1";
    private const string TeleportEffect = "scr_alien_teleport";     // actual alien teleport burst
    private const string FlashAsset = "core";
    private const string FlashEffect = "exp_arc_grd_flashbang";     // bright flash pop
    private const string TrailAsset = "core";
    private const string TrailEffect = "bullet_tracer";             // speed streak
    private const string DesatModifier = "hud_def_desat";
    private const long WarpWindupMs = 1200;

    // Fourth Hokage (Minato) yellow flash vs Goku white flash (user request v0.3.0)
    private const int MinatoR = 255, MinatoG = 210, MinatoB = 60;
    private const int GokuR = 255, GokuG = 255, GokuB = 255;
    private const int FlashAlpha = 190;

    private Vector3? _warpFrom;
    private Vector3? _warpTo;
    private long _warpStartedMs;
    private bool _hidden;   // player alpha currently 0

    public VfxService(IVfxBoundary vfx, ILogSink log, VisualConfig visual)
    {
        _vfx = vfx;
        _log = log;
        _visual = visual;
    }

    public bool IsWarping => _warpTo.HasValue;

    /// <summary>Per-frame maintenance: color flash overlays, particle retries.</summary>
    public void Tick() => _vfx.Tick();

    // --- screen transitions (justice flow, S3) ---

    public void ScreenFadeOut(int ms) => _vfx.ScreenFadeOut(ms);
    public void ScreenFadeIn(int ms) => _vfx.ScreenFadeIn(ms);
    public void ScreenFlash(int ms) => _vfx.ScreenFlash(ms);

    /// <summary>Time Stop cue: desaturation tint while active.</summary>
    public void SetTimeStopCue(bool active)
    {
        if (active)
        {
            if (_visual.TimeStop.TintStrength > 0f)
                _vfx.SetTimecycleModifier(DesatModifier, _visual.TimeStop.TintStrength);
            // strength 0 → deliberate no-op (user disabled the cue)
        }
        else
        {
            _vfx.ClearTimecycleModifier();
        }
    }

    /// <summary>Phase 1 of Instant Transmission: flash out + vanish (Minato yellow flash).</summary>
    public void BeginInstantTransmission() => BeginTransmission(MinatoR, MinatoG, MinatoB);

    /// <summary>Phase 1, Goku style (map teleport): white flash + vanish.</summary>
    public void BeginGokuTransmission() => BeginTransmission(GokuR, GokuG, GokuB);

    /// <summary>Phase 2: bursts at both ends + afterimage trail + rematerialize + flash in + shake.</summary>
    public void CompleteInstantTransmission(Vector3 from, Vector3 to)
        => CompleteTransmission(MinatoR, MinatoG, MinatoB, from, to);

    /// <summary>Phase 2, Goku style (map teleport): white flash + arrival burst.</summary>
    public void CompleteGokuTransmission(Vector3 from, Vector3 to)
        => CompleteTransmission(GokuR, GokuG, GokuB, from, to);

    /// <summary>Dash aim reticle (v0.8.0): draw a green cone at the blink destination.</summary>
    public void DrawDashTarget(Vector3 pos)
    {
        if (!_visual.Dash.Enabled) return;
        try
        {
            _vfx.DrawMarker(pos, 0.7f, 0, 255, 120, 220);
        }
        catch (Exception ex)
        {
            _log.Error("DrawDashTarget failed: " + ex.Message);
        }
    }

    private void BeginTransmission(int r, int g, int b)
    {
        if (_visual.Dash.Enabled)
        {
            _vfx.ScreenFadeOut(0);
            _vfx.SetPlayerAlpha(0);
            _hidden = true;
            // NOTE: no color flash here — ONE flash on arrival only (user report v0.4.0:
            // "double light animation, expect only show once on character")
        }
    }

    private void CompleteTransmission(int r, int g, int b, Vector3 from, Vector3 to)
    {
        if (_visual.Dash.Enabled)
        {
            try
            {
                // Origin burst + landing burst (real particle effects)
                _vfx.SpawnParticle(TeleportAsset, TeleportEffect, from, 2.0f);
                _vfx.SpawnParticle(TeleportAsset, TeleportEffect, to, 2.4f);
                _vfx.SpawnParticle(FlashAsset, FlashEffect, to, 0.8f);   // arrival flash pop

                if (_visual.Dash.Trail)
                {
                    for (int i = 1; i <= 7; i++)
                    {
                        var point = TeleportMath.Lerp(from, to, i / 8f);
                        _vfx.SpawnParticle(TrailAsset, TrailEffect, point, 0.5f);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Transmission VFX failed (power still works): {ex.Message}");
            }
        }

        if (_hidden)
        {
            _vfx.ResetPlayerAlpha();
            _hidden = false;
        }
        if (_visual.Dash.Enabled)
        {
            _vfx.FlashColor(r, g, b, FlashAlpha, 6);
            _vfx.ScreenFlash(180);
            if (_visual.MapTeleport.Shake) _vfx.ShakeCamera(0.3f);
        }
    }

    /// <summary>Abort: teleport refused — restore visibility + flash in (never leave the player invisible).</summary>
    public void AbortInstantTransmission()
    {
        if (_hidden)
        {
            _vfx.ResetPlayerAlpha();
            _hidden = false;
            if (_visual.Dash.Enabled) _vfx.ScreenFlash(150);
        }
    }

    /// <summary>Begin map-teleport wind-up (cancel window — UIUX §4.3).</summary>
    public void StartWarp(Vector3 from, Vector3 to)
    {
        _warpFrom = from;
        _warpTo = to;
        _warpStartedMs = 0;
        _vfx.SetTimecycleModifier(DesatModifier, 0.2f);
    }

    /// <summary>Advance the wind-up; returns true when the warp completes.</summary>
    public bool TickWarp(long nowMs)
    {
        if (!_warpTo.HasValue) return false;

        if (_warpStartedMs == 0) _warpStartedMs = nowMs;
        if (nowMs - _warpStartedMs < WarpWindupMs) return false;

        _vfx.ClearTimecycleModifier();
        _warpFrom = null;
        _warpTo = null;
        return true;
    }

    /// <summary>Cancel the wind-up (F9 during warp) — restore everything.</summary>
    public void CancelWarp()
    {
        _warpFrom = null;
        _warpTo = null;
        _vfx.ClearTimecycleModifier();
        if (_hidden)
        {
            _vfx.ResetPlayerAlpha();
            _hidden = false;
        }
        _vfx.StopCameraShake();
    }
}
