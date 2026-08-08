using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>Player state, aim direction, waypoint and teleport (DLD §3).</summary>
public sealed class PlayerContext : IPlayerContext
{
    public int PlayerHandle => Game.Player.Character?.Handle ?? 0;
    public int? PlayerVehicleHandle => Game.Player.Character?.CurrentVehicle?.Handle;
    public Vector3 Position => EntityFreezer.ToNumerics(Game.Player.Character.Position);
    public float Heading => Game.Player.Character.Heading;
    public bool IsAiming => Game.Player.IsAiming;

    public Vector3 GetAimDirection()
        => EntityFreezer.ToNumerics(GameplayCamera.Direction);

    public bool IsWaypointActive()
        => Game.IsWaypointActive;

    public Vector3 GetWaypointPosition()
        => EntityFreezer.ToNumerics(World.WaypointPosition);

    public void Teleport(Vector3 position)
    {
        var ped = Game.Player.Character;
        if (ped == null || !ped.Exists()) return;

        var vehicle = ped.CurrentVehicle;
        if (vehicle != null && vehicle.Exists())
            vehicle.Position = EntityFreezer.ToGta(position);   // teleport vehicle with player (FR-3.4)
        else
            ped.Position = EntityFreezer.ToGta(position);
    }
}
