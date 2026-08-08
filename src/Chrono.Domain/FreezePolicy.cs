namespace Chrono.Domain;

/// <summary>Pure rules: which entities may be frozen by Time Stop (DLD §2.4).</summary>
public static class FreezePolicy
{
    public static bool CanFreeze(EntityKind kind, ChronoConfig config)
    {
        return kind switch
        {
            EntityKind.Player => false,                    // player is never frozen
            EntityKind.Ped => true,
            EntityKind.Vehicle => true,
            EntityKind.Prop => config.TimeStop.FreezeProps, // props only when enabled
            _ => false
        };
    }
}
