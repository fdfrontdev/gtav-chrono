namespace Chrono.Application;

/// <summary>Meal offered by delivery + eateries (SRS FR-C8/C10).</summary>
public sealed record FoodItem(string Name, int Price, int HungerRestore, int MoodGain, string PropModel);

/// <summary>Drink offered by vending machines (SRS FR-C9).</summary>
public sealed record DrinkItem(string Name, int Price, int ThirstRestore, int EnergyRestore);

/// <summary>
/// Static food catalog (v0.10). Prop models are best-effort — the boundary
/// falls back gracefully if a model fails to spawn.
/// </summary>
public static class FoodCatalog
{
    public static readonly FoodItem[] Meals =
    {
        new("Burger & fries", 18, 45, 6, "prop_food_bs_burger2"),
        new("Taco platter", 15, 40, 5, "prop_food_bs_taco"),
        new("Chips & dip", 12, 30, 4, "prop_food_bs_chips"),
        new("Hot dog", 14, 35, 5, "prop_food_bs_hotdog"),
    };

    public static readonly DrinkItem[] Drinks =
    {
        new("Bottled water", 8, 40, 0),
        new("eCola", 10, 35, 0),
        new("Energy drink", 15, 25, 25),
    };
}
