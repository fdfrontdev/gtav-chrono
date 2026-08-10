namespace Chrono.Application;

/// <summary>Single source of truth for all user-visible strings (UIUX doc §5).
/// Text-only — the GTA font cannot render emoji (UIUX doc rule).</summary>
public static class UiStrings
{
    public const string MenuTitle = "CHRONO";
    public const string ItemTimeStop = "Time Stop";
    public const string ItemDash = "Dash Teleport";
    public const string ItemMapTeleport = "Map Teleport";
    public const string ItemGodMode = "God Mode";
    public const string ItemInvisible = "Invisible";
    public const string ItemFly = "Fly";
    public const string ItemSuperpowers = "Superpowers";   // S21 v3: one category for all powers
    public const string ItemJustice = "Justice";
    public const string ItemHackPoliceDb = "Hack Police DB";
    public const string ItemCriminalRecord = "Criminal Record";
    public const string ItemWebnet = "WEBNET News";   // S14: feed lives in the menu
    public const string ItemSettings = "Settings";
    public const string ItemModToggle = "Mod Enabled";        // S22
    public const string ItemPowersToggle = "Superpowers";     // S22
    public const string ItemJusticeToggle = "Justice System"; // S22
    public const string ItemHotkeys = "Hotkeys";
    public const string ItemShowHud = "Show HUD";
    public const string ItemDashRange = "Dash Range";
    public const string ItemFlySpeed = "Fly Speed";
    public const string ItemFreezeProps = "Freeze Props";
    public const string ItemPauseClock = "Pause Clock";
    public const string ItemBack = "Back";

    public const string TimeStopOn = "Time frozen - move freely";
    public const string TimeStopOff = "Time resumes";
    public const string TimeStopCapped = "Too many entities - freeze capped";
    public const string DashSuccess = "Dash";
    public const string DashBlocked = "No clear path";
    public const string MapEdge = "Map edge - can't go there";
    public const string WarpStart = "Warping...";
    public const string WarpArrived = "Arrived";
    public const string WarpCancelled = "Warp cancelled";
    public const string NoWaypoint = "Set a waypoint on the map first";
    public const string FlyHint = "Fly: WASD move | Space up | Ctrl down | F9 menu";
    public const string ConfigError = "Chrono: config issue - defaults loaded (see log)";
    public const string BugError = "Chrono error - see chrono.log";
    public const string FirstRun = "Chrono ready - press F9";
}
