using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace RattenHUD;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BaseUnityPlugin
{
    public const string Guid = "dev.sewerlabs.rattenhud";
    public const string Name = "Ratten HUD";
    public const string Version = "1.0.0";

    internal static new ManualLogSource Logger;

    // Threat readouts
    internal static ConfigEntry<bool> MissileBanner;
    internal static ConfigEntry<bool> ImpactCountdown;
    internal static ConfigEntry<bool> RadarTags;
    internal static ConfigEntry<bool> CountermeasureColours;

    // Weapon readouts
    internal static ConfigEntry<bool> ShootCue;
    internal static ConfigEntry<bool> TargetDataBlock;

    // Layout
    internal static ConfigEntry<bool> LayoutEnabled;
    internal static ConfigEntry<string> Layout;

    // Declutter
    internal static ConfigEntry<ObjectiveLabelMode> ObjectiveLabel;
    internal static ConfigEntry<bool> HideMarkers;
    internal static ConfigEntry<string> HiddenMarkerUnits;

    private void Awake()
    {
        Logger = base.Logger;

        BindConfig();
        ElementLayout.Initialize();
        Declutter.Initialize();
        MissileThreatBanner.Initialize();
        RadarWarningTags.Initialize();
        RattenHUD.ShootCue.Initialize();

        new Harmony(Guid).PatchAll();
        Logger.LogInfo($"{Name} {Version} loaded.");
    }

    private void Update()
    {
        MissileThreatBanner.Tick();
        RadarWarningTags.Tick();
    }

    private void BindConfig()
    {
        MissileBanner = Config.Bind(
            "Threats", "MissileBanner", true,
            "Flashing banner naming the countermeasure for every inbound missile "
            + "seeker type, with a count per type. The flash rate ramps up as the "
            + "closest missile closes.");
        ImpactCountdown = Config.Bind(
            "Threats", "ImpactCountdown", true,
            "Live time-to-impact for the closest inbound missile, under the banner.");
        RadarTags = Config.Bind(
            "Threats", "RadarWarningTags", true,
            "List radar emitters painting you, tagged SEARCH / LOCK / LAUNCH and "
            + "named by unit type.");
        CountermeasureColours = Config.Bind(
            "Threats", "CountermeasureColours", true,
            "Colour the countermeasure indicator by remaining load (green, amber, "
            + "red) and flash it when empty, instead of the stock green-then-grey.");

        ShootCue = Config.Bind(
            "Weapons", "ShootCue", true,
            "Show the firing cue across the whole valid envelope: SHOOT inside the "
            + "no-escape zone, IN RANGE between it and Rmax. The game computes this "
            + "but only ever displays it inside the no-escape zone.");
        TargetDataBlock = Config.Bind(
            "Weapons", "TargetDataBlock", true,
            "Add closure, aspect and altitude to the selected target readout.");

        LayoutEnabled = Config.Bind(
            "Layout", "Enabled", true,
            "Apply the element layout table below.");
        Layout = Config.Bind(
            "Layout", "Elements", "",
            "Per-element offset, scale and visibility, as a semicolon separated "
            + "list of Name:xOffset,yOffset[,scale][,visible].\n"
            + "Positive Y is up, offsets are in 1080p reference pixels, and a "
            + "trailing 'false' hides the element entirely (declutter).\n"
            + "Game elements are named after the HUD component that drives them: "
            + "Climbrate, CountermeasureIndicator, FuelGauge, SpeedGauge, "
            + "WeaponIndicator, and so on. ArtificialHorizon and GearIndicator "
            + "cannot be moved this way; they are the only two HUD elements that "
            + "do not route through the shared settings refresh.\n"
            + "This plugin's own readouts are named MissileBanner, "
            + "ImpactCountdown, RadarWarnings, ShootCue, TargetData.\n"
            + "Empty by default so that it cannot fight MKMods: if MKMods is "
            + "installed, its ClimbRateVerticalOffset already moves the climb "
            + "rate readout, and adding Climbrate here would move it twice. To "
            + "migrate that tweak, set MKMods' ClimbRateVerticalOffset to 0 and "
            + "add Climbrate:0,40 below.\n"
            + "Example: Climbrate:0,40;RadarWarnings:20,0,0.9;CountermeasureIndicator:0,0,1,false");

        ObjectiveLabel = Config.Bind(
            "Declutter", "ObjectiveLabel", ObjectiveLabelMode.Hidden,
            "How much of the objective overlay text to keep. Hidden leaves just "
            + "the circle, dot and off-screen pointer. DistanceOnly drops the "
            + "objective name but keeps the range. Full is the stock label.");
        HideMarkers = Config.Bind(
            "Declutter", "HideMarkers", true,
            "Drop HUD markers for the unit types listed below. They stay on the "
            + "map; only the marker on the glass goes away.");
        HiddenMarkerUnits = Config.Bind(
            "Declutter", "HiddenMarkerUnits", "pilot",
            "Comma separated, case insensitive. Each entry is matched as a "
            + "substring against the unit's display name, type code and object "
            + "name, because the game has no single class for a downed pilot to "
            + "key off. Widen or narrow this if it catches too much or too "
            + "little: check the unit's name on the map and use that.");
    }
}
