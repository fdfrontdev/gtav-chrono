using System.Numerics;
using Chrono.Application.Ports;
using Chrono.Domain;

namespace Chrono.Application;

/// <summary>
/// VFX orchestration (Animation &amp; VFX doc). Cosmetics only — every effect is
/// toggleable via config and NEVER blocks a power (ADR-02 §2.4).
/// </summary>
public sealed class VfxService
{
    private readonly IVfxBoundary _vfx;
    private readonly ILogSink _log;
    private readonly VisualConfig _visual;

    private const string TeleportAsset = "scr_trevor4_teleport";
    private const string TeleportEffect = "scr_trevor4_teleport_blue";
    private const string DesatModifier = "hud_def_desat";

    private Vector3? _warpFrom;
    private Vector3? _warpTo;
    private long _warpStartedMs;
    private const long WarpWindupMs = 1200;

    public VfxService(IVfxBoundary vfx, ILogSink log, VisualConfig visual)
    {
        _vfx = vfx;
        _log = log;
        _visual = visual;
    }

    public bool IsWarping => _warpTo.HasValue;

    /// <summary>Time Stop cue: desaturation tint while active.</summary>
    public void SetTimeStopCue(bool active)
    {
        if (active && _visual.TimeStop.TintStrength > 0f)
            _vfx.SetTimecycleModifier(DesatModifier, _visual.TimeStop.TintStrength);
        else
            _vfx.ClearTimecycleModifier();
    }

    /// <summary>Dash blink: origin burst + landing burst + optional trail (FR-3.5).</summary>
    public void PlayDashBlink(Vector3 from, Vector3 to)
    {
        if (!_visual.Dash.Enabled) return;

        try
        {
            _vfx.SpawnParticle(TeleportAsset, TeleportEffect, from, 1.0f);
            _vfx.SpawnParticle(TeleportAsset, TeleportEffect, to, 1.2f);

            if (_visual.Dash.Trail)
            {
                for (int i = 1; i <= 5; i++)
                {
                    var point = TeleportMath.Lerp(from, to, i / 6f);
                    _vfx.SpawnParticle(TeleportAsset, TeleportEffect, point, 0.5f);
                }
            }
        }
        catch (System.Exception ex)
        {
            _log.Warn($"Dash VFX failed (power still works): {ex.Message}");
        }
    }

    /// <summary>Begin map-teleport wind-up (cancel window — FR via UIUX §4.3).</summary>
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

        // wind-up complete — departure burst + arrival burst
        var from = _warpFrom ?? _warpTo.Value;
        _vfx.ClearTimecycleModifier();
        _vfx.SpawnParticle(TeleportAsset, TeleportEffect, from, 1.5f);
        _vfx.SpawnParticle(TeleportAsset, TeleportEffect, _warpTo.Value, 1.5f);

        if (_visual.MapTeleport.UseScreenFlash) _vfx.ScreenFlash(200);
        if (_visual.MapTeleport.Shake) _vfx.ShakeCamera(0.2f);

        _warpFrom = null;
        _warpTo = null;
        return true;
    }

    /// <summary>Cancel the wind-up (F9 pressed during warp).</summary>
    public void CancelWarp()
    {
        _warpFrom = null;
        _warpTo = null;
        _vfx.ClearTimecycleModifier();
        _vfx.StopCameraShake();
    }
}
