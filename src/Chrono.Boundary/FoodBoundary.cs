using System;
using System.Numerics;
using Chrono.Application.Ports;
using GTA;
using GTA.Native;

namespace Chrono.Boundary;

/// <summary>
/// v0.10 food/drink world interactions (SRS FR-C8..C11): delivery props, eat
/// anim, vending-machine + eatery detection. Best-effort natives — a missing
/// anim/prop never blocks consumption.
/// </summary>
public sealed class FoodBoundary : IFoodBoundary
{
    // Fixed eatery spots (map landmarks; UAT-tunable). Cluckin' Bell downtown,
    // Burger Shot Vinewood, taco stand Vespucci, noodle bar Little Seoul.
    private static readonly (float X, float Y, float Z)[] Eateries =
    {
        (238.6f, -938.2f, 29.3f),     // downtown fast-food row
        (1199.8f, 2657.3f, 36.0f),    // Paleto strip
        (-1154.0f, -1520.0f, 4.4f),   // Vespucci boardwalk
        (2506.4f, -3898.6f, 40.0f),   // eastern port diner
    };

    public void SpawnFoodProp(Vector3 position, string model)
    {
        try
        {
            var prop = World.CreateProp(new Model(model), EntityFreezer.ToGta(position), false, false);
            prop?.MarkAsNoLongerNeeded();
        }
        catch
        {
            // prop model unknown → fall back to the burger (always exists)
            try
            {
                var prop = World.CreateProp(new Model("prop_food_bs_burger2"), EntityFreezer.ToGta(position), false, false);
                prop?.MarkAsNoLongerNeeded();
            }
            catch { /* never a crash vector */ }
        }
    }

    public void PlayEatAnim()
    {
        try
        {
            var ped = Game.Player.Character;
            if (ped == null || !ped.Exists()) return;
            ped.Task.PlayAnimation("mp_player_inteat@burger", "mp_player_int_eat_burger", 8f, 4000, AnimationFlags.None);
        }
        catch
        {
            // anim dict missing → the food is still consumed (flavor only)
        }
    }

    public void PlayDrinkAnim()
    {
        try
        {
            var ped = Game.Player.Character;
            if (ped == null || !ped.Exists()) return;
            ped.Task.PlayAnimation("mp_player_intdrink", "mp_player_int_drink", 8f, 4000, AnimationFlags.None);
        }
        catch
        {
            // anim dict missing → the drink is still consumed (flavor only)
        }
    }

    public Vector3? FindVendingMachine(Vector3 center, float radiusM)
    {
        try
        {
            var gta = EntityFreezer.ToGta(center);
            foreach (var prop in World.GetNearbyProps(gta, radiusM))
            {
                if (prop == null || !prop.Exists()) continue;
                string model = prop.Model.ToString() ?? "";
                if (model.StartsWith("prop_vend_", StringComparison.OrdinalIgnoreCase))
                    return EntityFreezer.ToNumerics(prop.Position);
            }
        }
        catch { /* scan is flavor — never a crash */ }
        return null;
    }

    public bool TryFindEatery(Vector3 center, float radiusM, out Vector3 spot)
    {
        foreach (var (x, y, z) in Eateries)
        {
            var p = new Vector3(x, y, z);
            if (Vector3.Distance(center, p) <= radiusM)
            {
                spot = p;
                return true;
            }
        }
        spot = default;
        return false;
    }
}
