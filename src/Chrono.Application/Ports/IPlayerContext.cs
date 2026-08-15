using System.Numerics;

namespace Chrono.Application.Ports;

/// <summary>Player state + capability set (implemented by the SHVDN boundary).</summary>
public interface IPlayerContext
{
    int PlayerHandle { get; }
    int? PlayerVehicleHandle { get; }
    Vector3 Position { get; }
    bool HasWeapon { get; }               // S19: compliance checks an unarmed suspect
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

    // --- NPC awareness (v0.6.0 realistic world reactions) ---
    /// <summary>false = NPCs &amp; police cannot perceive/track the player (SET_*_IGNORE_PLAYER).</summary>
    void SetNpcAwareness(bool enabled);

    // --- custody suppression (S23) ---
    /// <summary>
    /// S23 (user UAT: \"wanted level stays in custody/prison; civilians keep
    /// calling police\"): while the player is CAPTURED or in PRISON the street
    /// chase is over — police stop targeting and civilians stop reporting
    /// (SET_POLICE_IGNORE_PLAYER + SET_EVERYONE_IGNORE_PLAYER). Restored on
    /// release/escape so the manhunt can engage normally.
    /// </summary>
    void SetLawEnforcementIgnore(bool enabled);

    // --- justice (v0.9.0, S1) ---
    /// <summary>true when the player is visually present (invisible power → false → no burning).</summary>
    bool IsVisible { get; }

    /// <summary>Current map zone name for crime/media flavor (e.g. "Vinewood").</summary>
    string GetDistrictName();

    /// <summary>
    /// S21 v3 — the character's public name (e.g. "Franklin", "Michael",
    /// "Trevor") for media flavor: "PRISON BREAK: FRANKLIN escapes..." instead
    /// of "super-powered suspect". Returns "Unknown" as a safe fallback.
    /// </summary>
    string GetCharacterName();

    /// <summary>Add money (negative = fine deduction, FR-8.3).</summary>
    void AddMoney(int delta);

    /// <summary>Dead/alive (death while wanted → police custody, S7).</summary>
    bool IsDead { get; }

    /// <summary>Freeze/unfreeze the character while the cheat menu is open (S8).</summary>
    void SetControlEnabled(bool enabled);

    /// <summary>Current cash.</summary>
    int GetMoney();

    // --- v0.10 survivor needs (SRS FR-C4..C7) ---
    /// <summary>Health recharge multiplier (0 = no passive regen when hungry/thirsty).</summary>
    void SetHealthRechargeMultiplier(float multiplier);

    /// <summary>Run-speed multiplier (tired).</summary>
    void SetRunSpeedMultiplier(float multiplier);

    /// <summary>Drunken-blur visual (critical thirst).</summary>
    void SetDrunkVisual(bool enabled);

    /// <summary>Direct health damage (starvation / dehydration drain).</summary>
    void ApplyHealthDamage(float amount);

    /// <summary>v0.13: true when the player is not inside an interior (fresh-air mood, ADR 09).</summary>
    bool IsOutdoors();
}
