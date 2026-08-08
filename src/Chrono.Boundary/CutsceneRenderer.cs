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
    private string _banner = "";

    public void Begin()
    {
        Game.Player.SetControlState(false, SetPlayerControlFlags.None);
        _camera = Camera.Create("chrono_cutscene", GTA.Math.Vector3.Zero, GTA.Math.Vector3.Zero, 60f, false, GTA.EulerRotationOrder.XYZ);
        _banner = "";
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
        _banner = text;
        if (string.IsNullOrWhiteSpace(text)) return;

        // Cinematic band + centered text
        Function.Call(Hash.DRAW_RECT, 0.5f, 0.62f, 1.0f, 0.09f, 0, 0, 0, 170);
        Function.Call(Hash.SET_TEXT_FONT, 1);
        Function.Call(Hash.SET_TEXT_SCALE, 0.85f, 0.85f);
        Function.Call(Hash.SET_TEXT_COLOUR, 255, 255, 255, 255);
        Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0.5f, 0.605f);
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
        _banner = "";
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
