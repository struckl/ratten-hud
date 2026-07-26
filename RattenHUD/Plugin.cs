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
    public const string Version = "2.0.0";

    internal static new ManualLogSource Logger;

    // Threat readouts
    internal static ConfigEntry<bool> MissileDefeatHint;
    internal static ConfigEntry<bool> ImpactCountdown;
    internal static ConfigEntry<bool> RadarTags;
    internal static ConfigEntry<bool> HideStockRadarWarning;
    internal static ConfigEntry<bool> CountermeasureColours;

    // Weapon readouts
    internal static ConfigEntry<bool> ShootCue;
    internal static ConfigEntry<bool> TargetDataBlock;

    // Contact readouts
    internal static ConfigEntry<bool> ContactLabels;
    internal static ConfigEntry<bool> ContactLabelFriendlies;
    internal static ConfigEntry<bool> ContactLabelRange;
    internal static ConfigEntry<float> ContactLabelMaxRange;
    internal static ConfigEntry<float> ContactLabelScale;

    // Flight readouts
    internal static ConfigEntry<bool> FuelTimeReadout;
    internal static ConfigEntry<float> FuelTimeUpdateRate;

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
        FuelTime.Initialize();

        new Harmony(Guid).PatchAll();
        Logger.LogInfo($"{Name} {Version} loaded.");
    }

    private void Update()
    {
        RadarWarningTags.Tick();
        RattenHUD.ShootCue.Tick();
    }

    private void BindConfig()
    {
        MissileDefeatHint = Config.Bind(
            "Threats", "MissileDefeatHint", true,
            "Append the countermeasure that defeats the seeker to each entry in "
            + "the game's missile threat list: FLARE, NOTCH, HIDE or RADAR OFF.");
        ImpactCountdown = Config.Bind(
            "Threats", "ImpactCountdown", true,
            "Append the estimated time to impact to each entry in the game's "
            + "missile threat list, while the missile is actually gaining.");
        RadarTags = Config.Bind(
            "Threats", "RadarWarningTags", true,
            "Add radar emitters painting you to the game's threat list, tagged "
            + "SEARCH / LOCK / LAUNCH and named by unit type.");
        HideStockRadarWarning = Config.Bind(
            "Threats", "HideStockRadarWarning", false,
            "Suppress the game's own directional radar warning arrows, leaving "
            + "the tagged list as the only radar warning display. The warning "
            + "tone still sounds and the tags are unaffected; only the wedges "
            + "the game draws around the map go away.");
        CountermeasureColours = Config.Bind(
            "Threats", "CountermeasureColours", true,
            "Colour the countermeasure indicator by remaining load (green, amber, "
            + "red) and flash it when empty, instead of the stock green-then-grey.");

        ShootCue = Config.Bind(
            "Weapons", "ShootCue", true,
            "Show the firing cue across the whole valid envelope: the game's own "
            + "SHOOT inside the no-escape zone, IN RANGE between it and Rmax. The "
            + "game computes this but only ever displays it inside the no-escape "
            + "zone.");
        TargetDataBlock = Config.Bind(
            "Weapons", "TargetDataBlock", true,
            "Add closure, aspect and altitude to the selected target readout.");

        ContactLabels = Config.Bind(
            "Contacts", "ContactLabels", true,
            "Write each aircraft's type code beside its HUD marker. Ground units "
            + "have icons that differ; every aircraft draws the same triangle, so "
            + "without this the only way to tell a bomber from a fighter is to "
            + "select it and read the target block.");
        ContactLabelFriendlies = Config.Bind(
            "Contacts", "LabelFriendlyAircraft", false,
            "Also label aircraft of your own faction. Off by default: friendlies "
            + "are already a different colour, and in a busy sky they are most of "
            + "the clutter.");
        ContactLabelRange = Config.Bind(
            "Contacts", "LabelRange", true,
            "Include the range to the contact after the type code.");
        ContactLabelMaxRange = Config.Bind(
            "Contacts", "LabelMaxRange", 25000f,
            "Metres. Contacts further out than this keep their marker but lose "
            + "the label, so a busy radar picture does not fill the glass with "
            + "text. 0 removes the limit.");
        ContactLabelScale = Config.Bind(
            "Contacts", "LabelTextScale", 0.6f,
            "Label size relative to your HUD text size. The labels are anchored "
            + "to the markers rather than to a fixed place on the glass, so the "
            + "layout table below does not apply to them; this is the only size "
            + "control they have.");

        FuelTimeReadout = Config.Bind(
            "Flight", "FuelTimeReadout", true,
            "Show the estimated remaining fuel time next to the fuel gauge. The "
            + "estimate comes from fuel burned between samples, so it tracks the "
            + "current throttle setting rather than a fixed rate.");
        FuelTimeUpdateRate = Config.Bind(
            "Flight", "FuelTimeUpdateRate", 10f,
            "Seconds between fuel level samples used to estimate the remaining time.");

        LayoutEnabled = Config.Bind(
            "Layout", "Enabled", true,
            "Apply the element layout table below.");
        Layout = Config.Bind(
            "Layout", "Elements", "Climbrate:0,40",
            "Per-element offset, scale and visibility, as a semicolon separated "
            + "list of Name:xOffset,yOffset[,scale][,visible].\n"
            + "Positive Y is up, offsets are in 1080p reference pixels, and a "
            + "trailing 'false' hides the element entirely (declutter).\n"
            + "Elements are named after the HUD component that drives them: "
            + "Climbrate, CountermeasureIndicator, FuelGauge, SpeedGauge, "
            + "WeaponIndicator, and so on. ArtificialHorizon and GearIndicator "
            + "cannot be moved this way; they are the only two HUD elements that "
            + "do not route through the shared settings refresh.\n"
            + "The default Climbrate:0,40 is the old MKMods "
            + "ClimbRateVerticalOffset, which now lives here: the stock climb "
            + "rate readout sits low enough to collide with its neighbours.\n"
            + "If you still run MKMods alongside this, set its "
            + "ClimbRateVerticalOffset to 0 first, or the readout moves twice.\n"
            + "Example: Climbrate:0,40;SpeedGauge:20,0,0.9;CountermeasureIndicator:0,0,1,false");

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
