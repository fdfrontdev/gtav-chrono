using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Chrono.Application.Ports;
using Chrono.Domain;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// S20 — polling act sampler (ADR-04 D1). SHVDN 3.9 exposes no C# events, so this
/// Boundary diffs snapshots every ~200 ms: ped deaths (with source-of-death
/// attribution), non-lethal ped damage, vehicle damage, weapon/aim/drive context
/// and crosshair hits. Also implements the use-of-force hold-fire (ADR-04 D2).
/// All "since last poll" facts are CONSUMING (each reports once).
/// </summary>
public sealed class CrimeProbe : ICrimeProbe
{
    private const int MaxBatch = 100;                 // batched entity work (crash-safe rule)
    private const float PedDamageThreshold = 8f;      // health drop that reads as a hit
    private const float VehicleDamageThreshold = 8f;

    private readonly Stopwatch _pollClock = Stopwatch.StartNew();
    private readonly Dictionary<int, bool> _pedWasAlive = new();   // handle → alive last scan
    private readonly Dictionary<int, float> _pedHealth = new();    // handle → health last scan
    private readonly Dictionary<int, float> _vehHealth = new();    // handle → health last scan
    private readonly HashSet<int> _heldCops = new();               // peds currently held
    private readonly Queue<DeathCauseKind> _pendingKills = new();
    private bool _pendingPedDamage;
    private bool _pendingVehDamage;
    private bool _holdActive;
    private readonly RelationshipGroup _copGroup;

    public CrimeProbe()
    {
        // Cop relationship group (verified: RelationshipGroupHash.Cop exists)
        _copGroup = new RelationshipGroup(RelationshipGroupHash.Cop);
    }

    public PlayerActContext SampleContext()
    {
        var ped = Game.Player.Character;
        bool inVehicle = ped.IsInVehicle();
        float speed = 0f;
        if (inVehicle && ped.CurrentVehicle != null)
            speed = Function.Call<float>(Hash.GET_ENTITY_SPEED, ped.CurrentVehicle.Handle);
        return new PlayerActContext(
            WeaponOutClass(ped),
            Game.Player.IsAiming,
            inVehicle,
            speed);
    }

    public DeathCauseKind PollKillSinceLastPoll()
        => _pendingKills.Count > 0 ? _pendingKills.Dequeue() : DeathCauseKind.None;

    public bool PollPedDamageSinceLastPoll()
    {
        bool v = _pendingPedDamage;
        _pendingPedDamage = false;
        return v;
    }

    public bool PollVehicleDamageSinceLastPoll()
    {
        bool v = _pendingVehDamage;
        _pendingVehDamage = false;
        return v;
    }

    public float CrosshairPedDistanceM
    {
        get
        {
            try
            {
                var hit = World.GetCrosshairCoordinates();
                if (!hit.DidHit || hit.HitEntity == null || !(hit.HitEntity is Ped))
                    return float.MaxValue;
                return GTA.Math.Vector3.Distance(Game.Player.Character.Position, hit.HitEntity.Position);
            }
            catch
            {
                return float.MaxValue;   // detection is flavor-driven — never a crash vector
            }
        }
    }

    public int CountNearbyPolice(float radius)
    {
        try
        {
            return World.GetNearbyPeds(Game.Player.Character.Position, radius, Array.Empty<Model>())
                .Take(MaxBatch)
                .Count(IsCop);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Poll the world at most every 200 ms and diff snapshots (consuming).</summary>
    public void Poll()
    {
        if (_pollClock.ElapsedMilliseconds < 200) return;
        _pollClock.Restart();
        try
        {
            var pos = Game.Player.Character.Position;
            var playerPed = Game.Player.Character;
            int playerHandle = playerPed.Handle;
            int? playerVehicle = playerPed.CurrentVehicle?.Handle;

            // --- peds: death attribution + non-lethal damage ---
            var peds = World.GetNearbyPeds(pos, 60f, Array.Empty<Model>()).Take(MaxBatch).ToList();
            foreach (var ped in peds)
            {
                int h = ped.Handle;
                bool alive = ped.IsAlive;
                if (_pedWasAlive.TryGetValue(h, out bool wasAlive) && wasAlive && !alive)
                {
                    // KILL — attribute to the player (or the player's vehicle)
                    int source = Function.Call<int>(Hash.GET_PED_SOURCE_OF_DEATH, h);
                    if (source == playerHandle || (playerVehicle.HasValue && source == playerVehicle.Value))
                    {
                        int cause = Function.Call<int>(Hash.GET_PED_CAUSE_OF_DEATH, h);
                        _pendingKills.Enqueue(ClassifyDeathCause(cause, source == playerVehicle));
                    }
                }
                _pedWasAlive[h] = alive;

                // non-lethal damage: health delta while still alive
                float health = Function.Call<float>(Hash.GET_ENTITY_HEALTH, h);
                if (alive && _pedHealth.TryGetValue(h, out float prev) && prev - health >= PedDamageThreshold
                    && (WeaponOutClass(playerPed) != DeathCauseKind.None || Game.Player.IsAiming))
                {
                    _pendingPedDamage = true;
                }
                _pedHealth[h] = health;
            }

            // --- vehicles: property damage (health delta near the player) ---
            var vehicles = World.GetNearbyVehicles(pos, 60f, Array.Empty<Model>()).Take(MaxBatch).ToList();
            bool armedOrDriving = WeaponOutClass(playerPed) != DeathCauseKind.None || playerPed.IsInVehicle();
            foreach (var veh in vehicles)
            {
                int h = veh.Handle;
                float health = veh.EngineHealth + veh.BodyHealth;
                if (_vehHealth.TryGetValue(h, out float prev) && prev - health >= VehicleDamageThreshold
                    && armedOrDriving)
                {
                    _pendingVehDamage = true;
                }
                _vehHealth[h] = health;
            }

            Prune(pos);
        }
        catch
        {
            // polling is flavor — never a crash vector
        }
    }

    /// <summary>Use-of-force hold (ADR-04 D2): nearby cops aim but don't shoot.
    /// Idempotent — natives only on state change; new cops arriving mid-hold join.</summary>
    public void SetPoliceHoldFire(bool hold)
    {
        if (hold == _holdActive) return;
        _holdActive = hold;
        try
        {
            var pos = Game.Player.Character.Position;
            var cops = World.GetNearbyPeds(pos, 80f, Array.Empty<Model>())
                .Take(MaxBatch)
                .Where(IsCop)
                .ToList();
            if (hold)
            {
                foreach (var cop in cops)
                {
                    int h = cop.Handle;
                    if (_heldCops.Add(h))
                    {
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, cop, 5, false);   // CA_ALWAYS_FIGHT off
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, cop, 30, false);  // CA_CAN_SHOOT_WITHOUT_LOS off
                        Function.Call(Hash.SET_PED_SHOOT_RATE, cop, 0);                 // never shoot
                        Function.Call(Hash.TASK_AIM_GUN_AT_ENTITY, cop, Game.Player.Character, -1, true);
                    }
                }
            }
            else
            {
                foreach (int h in _heldCops)
                {
                    var cop = Entity.FromHandle(h);
                    if (cop != null && cop.Exists())
                    {
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, cop, 5, true);
                        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, cop, 30, true);
                        Function.Call(Hash.SET_PED_SHOOT_RATE, cop, 100);
                        Function.Call(Hash.CLEAR_PED_TASKS, cop);   // drop the aim task → vanilla AI resumes
                    }
                }
                _heldCops.Clear();
            }
        }
        catch
        {
            _heldCops.Clear();   // hold-fire is gameplay flavor — never a crash vector
        }
    }

    private bool IsCop(Ped ped)
    {
        try
        {
            return ped.RelationshipGroup != null && ped.RelationshipGroup.Hash == _copGroup.Hash;
        }
        catch
        {
            return false;
        }
    }

    private static DeathCauseKind WeaponOutClass(Ped ped)
    {
        try
        {
            var weapon = ped.Weapons?.Current;
            if (weapon == null) return DeathCauseKind.None;
            return ClassifyWeapon((int)weapon.Hash);
        }
        catch
        {
            return DeathCauseKind.None;
        }
    }

    private static DeathCauseKind ClassifyDeathCause(int weaponHash, bool fromVehicle)
    {
        if (fromVehicle) return DeathCauseKind.Vehicle;
        return ClassifyWeapon(weaponHash);
    }

    private static DeathCauseKind ClassifyWeapon(int hash)
    {
        uint h = unchecked((uint)hash);
        if (MeleeHashes.Contains(h)) return DeathCauseKind.Melee;
        if (ExplosiveHashes.Contains(h)) return DeathCauseKind.Explosive;
        if (h == 0 || h == (uint)WeaponHash.Unarmed) return DeathCauseKind.None;
        return DeathCauseKind.Gun;   // firearms default
    }

    // Verified weapon-hash groups (WeaponHash dump, 2026-08-09). WeaponHash enum
    // values exceed int range — compare as uint.
    private static readonly HashSet<uint> MeleeHashes = new()
    {
        (uint)WeaponHash.Unarmed, (uint)WeaponHash.KnuckleDuster, (uint)WeaponHash.Nightstick,
        (uint)WeaponHash.Bat, (uint)WeaponHash.Bottle, (uint)WeaponHash.Crowbar,
        (uint)WeaponHash.GolfClub, (uint)WeaponHash.Hammer, (uint)WeaponHash.Hatchet,
        (uint)WeaponHash.Dagger, (uint)WeaponHash.PoolCue, (uint)WeaponHash.Wrench,
        (uint)WeaponHash.SwitchBlade, (uint)WeaponHash.StoneHatchet, (uint)WeaponHash.BattleAxe,
        (uint)WeaponHash.CandyCane, (uint)WeaponHash.Flashlight, (uint)WeaponHash.Ball,
        (uint)WeaponHash.Snowball
    };

    private static readonly HashSet<uint> ExplosiveHashes = new()
    {
        (uint)WeaponHash.Grenade, (uint)WeaponHash.StickyBomb, (uint)WeaponHash.PipeBomb,
        (uint)WeaponHash.RPG, (uint)WeaponHash.Molotov, (uint)WeaponHash.Firework,
        (uint)WeaponHash.GrenadeLauncher, (uint)WeaponHash.CompactGrenadeLauncher,
        (uint)WeaponHash.Railgun, (uint)WeaponHash.UpNAtomizer, (uint)WeaponHash.ProximityMine,
        (uint)WeaponHash.BZGas, (uint)WeaponHash.SmokeGrenade, (uint)WeaponHash.AcidPackage
    };

    private void Prune(GTA.Math.Vector3 pos)
    {
        // Drop snapshots of entities far away or long gone (bounded memory)
        if (_pedHealth.Count <= 600 && _vehHealth.Count <= 300) return;
        _pedWasAlive.Clear();
        _pedHealth.Clear();
        _vehHealth.Clear();
    }
}
