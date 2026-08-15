using System;
using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// v0.10 combat-power effects (SRS FR-B4..B7) — impulse, explosion, world
/// slow-mo, regeneration. All natives try/catch-guarded; estimates count
/// what the effect hit, the justice pipeline classifies.
/// </summary>
public sealed class PowerFxBoundary : IPowerFxBoundary
{
    private readonly ILogSink? _log;

    public PowerFxBoundary(ILogSink? log = null) => _log = log;

    public PowerHitReport Push(Vector3 origin, Vector3 direction, float rangeM, float coneDeg, float vehicleImpulse)
    {
        int injured = 0, vehicles = 0;
        try
        {
            var gtaOrigin = EntityFreezer.ToGta(origin);
            var gtaDir = EntityFreezer.ToGta(direction);
            if (gtaDir.LengthSquared() < 0.001f) gtaDir = GTA.Math.Vector3.RelativeFront;

            // Peds in the forward cone → ragdoll + launch
            foreach (var ped in World.GetNearbyPeds(gtaOrigin, rangeM))
            {
                if (ped == null || !ped.Exists() || ped == Game.Player.Character) continue;
                if (!InCone(ped.Position - gtaOrigin, gtaDir, coneDeg)) continue;
                Function.Call(Hash.SET_PED_TO_RAGDOLL, ped.Handle, 1500, 1500, 0, true, true, false);
                var impulse = gtaDir * 24f;
                impulse.Z += 5f;
                ped.Velocity = impulse;
                injured++;
            }

            // Vehicles in the cone → impulse (light push; the game's mass handles the rest)
            foreach (var veh in World.GetNearbyVehicles(gtaOrigin, rangeM))
            {
                if (veh == null || !veh.Exists()) continue;
                if (Game.Player.Character?.CurrentVehicle != null
                    && veh.Handle == Game.Player.Character.CurrentVehicle.Handle) continue;
                if (!InCone(veh.Position - gtaOrigin, gtaDir, coneDeg)) continue;
                veh.Velocity += gtaDir * (10f * vehicleImpulse);
                vehicles++;
            }
        }
        catch (Exception ex)
        {
            _log?.Error($"Force push failed: {ex.Message}");
        }
        return new PowerHitReport(injured, 0, vehicles, 0);
    }

    public PowerHitReport Blast(Vector3 target, float radiusM, float damageScale)
    {
        int injured = 0, killed = 0, vehicles = 0, props = 0;
        try
        {
            var gtaTarget = EntityFreezer.ToGta(target);
            // Type 4 = rocket-grade explosion — a superpowered energy blast
            Function.Call(Hash.ADD_EXPLOSION, gtaTarget.X, gtaTarget.Y, gtaTarget.Z,
                4, 1.2f * damageScale, true, false, 0.6f);

            float inner = radiusM * 0.4f;   // inner radius = kills, outer = injuries
            foreach (var ped in World.GetNearbyPeds(gtaTarget, radiusM))
            {
                if (ped == null || !ped.Exists() || ped == Game.Player.Character) continue;
                if ((ped.Position - gtaTarget).LengthSquared() <= inner * inner) killed++;
                else injured++;
            }
            vehicles = World.GetNearbyVehicles(gtaTarget, radiusM).Length;
            props = World.GetNearbyProps(gtaTarget, radiusM).Length;
        }
        catch (Exception ex)
        {
            _log?.Error($"Energy blast failed: {ex.Message}");
        }
        return new PowerHitReport(injured, killed, vehicles, props);
    }

    public void SetWorldTimeScale(float scale)
    {
        try { Function.Call(Hash.SET_TIME_SCALE, scale); }
        catch (Exception ex) { _log?.Error($"Time-scale failed: {ex.Message}"); }
    }

    public void HealOverTime(int totalSeconds, float damageResist)
    {
        try
        {
            // The application pulses RefillHealth; here we add damage resistance
            // for the window (anime regen vibe — the invincibility IS the effect).
            var ped = Game.Player.Character;
            if (ped != null && ped.Exists()) ped.IsInvincible = true;
            Game.Player.IsInvincible = true;
        }
        catch (Exception ex) { _log?.Error($"Heal window failed: {ex.Message}"); }
    }

    public void CancelHeal()
    {
        try
        {
            var ped = Game.Player.Character;
            if (ped != null && ped.Exists()) ped.IsInvincible = false;
            Game.Player.IsInvincible = false;
        }
        catch (Exception ex) { _log?.Error($"Heal cancel failed: {ex.Message}"); }
    }

    private static bool InCone(GTA.Math.Vector3 offset, GTA.Math.Vector3 dir, float coneDeg)
    {
        offset.Z = 0;
        if (offset.LengthSquared() < 0.01f) return false;
        float cos = GTA.Math.Vector3.Dot(GTA.Math.Vector3.Normalize(offset), GTA.Math.Vector3.Normalize(dir));
        if (cos < -1f) cos = -1f;
        if (cos > 1f) cos = 1f;
        float angle = (float)Math.Acos(cos) * 180f / (float)Math.PI;
        return angle <= coneDeg / 2f;
    }
}
