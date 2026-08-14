using System;
using System.Linq;
using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// v0.12 phone escort companion (FR-D2): spawns near the player, walks to
/// them, gets dismissed after the fade. Model fallback chain — a bad model
/// string must never crash the mod (try/catch each step).
/// </summary>
public sealed class CompanionBoundary : ICompanionBoundary
{
    private static readonly string[] FallbackModels = { "a_f_m_skid_01", "a_f_y_beach_01", "a_f_y_hippie_01" };

    private Ped? _companion;
    private readonly ILogSink? _log;

    public CompanionBoundary(ILogSink? log = null) => _log = log;

    public void SendCompanion(Vector3 playerPosition, string model)
    {
        DismissCompanion();
        try
        {
            var gta = EntityFreezer.ToGta(playerPosition);
            // spawn just outside the player, then walk in
            var spawn = gta + new GTA.Math.Vector3(6f, 0f, 0f);
            Ped? ped = null;
            foreach (var m in new[] { model }.Concat(FallbackModels))
            {
                try
                {
                    ped = World.CreatePed(new Model(m), spawn, gta.Z);
                    if (ped != null && ped.Exists()) break;
                }
                catch { /* try next model */ }
            }
            if (ped == null || !ped.Exists())
            {
                _log?.Error("Companion: no pedestrian model could be created");
                return;
            }
            ped.BlockPermanentEvents = true;   // she has ONE job: walk to you
            ped.RelationshipGroup = Game.Player.Character?.RelationshipGroup ?? 0;
            // TASK_GO_TO_COORD_ANY_MEANS — walk to the player, style 262144 = calm walk
            Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS, ped.Handle,
                gta.X, gta.Y, gta.Z, 1.0f, 0, false, 262144, -1f);
            _companion = ped;
        }
        catch (Exception ex)
        {
            _log?.Error($"Companion spawn failed: {ex.Message}");
        }
    }

    public bool IsCompanionNear(Vector3 playerPosition)
    {
        try
        {
            if (_companion == null || !_companion.Exists()) return false;
            var diff = EntityFreezer.ToGta(playerPosition) - _companion.Position;
            diff.Z = 0;
            return diff.LengthSquared() <= 2.5f * 2.5f;
        }
        catch { return false; }
    }

    public void DismissCompanion()
    {
        try
        {
            if (_companion != null && _companion.Exists())
            {
                _companion.Delete();
                _companion = null;
            }
        }
        catch (Exception ex)
        {
            _log?.Error($"Companion dismiss failed: {ex.Message}");
            _companion = null;
        }
    }
}
