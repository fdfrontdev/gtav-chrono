using System;
using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// Script-camera cutscene rendering (S11). Uses World.CreateCamera + RenderingCamera
/// (the SHVDN 3.9 documented path) + player control freeze; banners via HUD text.
/// </summary>
public sealed class CutsceneRenderer : ICutsceneRenderer
{
    private Camera? _camera;

    public void Begin()
    {
        Game.Player.SetControlState(false, SetPlayerControlFlags.None);
        _camera = Camera.Create("chrono_cutscene", GTA.Math.Vector3.Zero, GTA.Math.Vector3.Zero, 60f, false, GTA.EulerRotationOrder.XYZ);
    }

    public void SetCamera(Vector3 position, Vector3 lookAt, float fov)
    {
        if (_camera == null || !_camera.Exists()) return;
        _camera.Position = EntityFreezer.ToGta(position);
        _camera.Rotation = LookRotation(position, lookAt);
        _camera.FieldOfView = fov;
        _camera.IsActive = true;
        GTA.ScriptCameraDirector.StartRendering();
    }

    public void ShowBanner(string text)
    {
        // S21 v3 (user UAT: "mid-screen white text on black — we already have
        // the widget"): the cinematic band is REMOVED. Banner text routes into
        // the widget feed via JusticeCutsceneService → INotifier instead.
    }

    public void PlayAnim(string dict, string anim, bool loop)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;
        try
        {
            ped.Task.PlayAnimation(dict, anim, 4f, loop ? -1 : 0,
                loop ? AnimationFlags.Loop : AnimationFlags.None);
        }
        catch
        {
            // animation is flavor — never a crash vector
        }
    }

    public void End()
    {
        if (_camera != null)
        {
            try { _camera.Delete(); } catch { /* already gone */ }
            _camera = null;
        }
        GTA.ScriptCameraDirector.StopRendering(false);
        Game.Player.SetControlState(true, SetPlayerControlFlags.None);
    }

    /// <summary>Camera rotation (pitch/roll/yaw, degrees) to look from pos toward lookAt —
    /// matches the GTA heading convention (0 = north, 90 = east).</summary>
    private static GTA.Math.Vector3 LookRotation(Vector3 pos, Vector3 lookAt)
    {
        Vector3 dir = lookAt - pos;
        if (dir.LengthSquared() < 0.0001f) return GTA.Math.Vector3.Zero;

        float yaw = (float)(Math.Atan2(dir.X, dir.Y) * 180.0 / Math.PI);
        float pitch = (float)(-Math.Atan2(dir.Z, Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y)) * 180.0 / Math.PI);
        return new GTA.Math.Vector3(pitch, 0f, yaw);
    }
}
