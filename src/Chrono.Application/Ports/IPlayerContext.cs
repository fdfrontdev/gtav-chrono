using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>Player state + capability set (implemented by the SHVDN boundary).</summary>
public interface IPlayerContext
{
    int PlayerHandle { get; }
    int? PlayerVehicleHandle { get; }
    Vector3 Position { get; }
    float Heading { get; }
    bool IsAiming { get; }
    bool IsInVehicle { get; }
    Vector3 GetAimDirection();
    bool IsWaypointActive();
    Vector3 GetWaypointPosition();
    void Teleport(Vector3 position);

    // --- v0.3.0 powers ---
    void SetVelocity(Vector3 velocity);          // flight control
    void SetGravityEnabled(bool enabled);        // flight (false = hover)
    void SetRagdollEnabled(bool enabled);        // flight (false = stable pose)
    void SetInvincible(bool enabled);            // god mode
    void RefillHealth();                         // god mode
    void SetVisible(bool visible);               // invisibility

    // --- animation (v0.4.0, verified via DurtyFree anim dict dump) ---
    void SetHeading(float headingDegrees);       // face movement direction
    void PlayLoopedAnimation(string dict, string anim);   // e.g. skydive@freefall/free_forward
    void PlayAnimationOnce(string dict, string anim, int durationMs); // e.g. action_chest landing
    void ClearCurrentAnimation();
    void PlaceOnGround();                        // settle ped on terrain after teleport (no falling pose)
}
