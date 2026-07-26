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
    public const string Version = "2.1.0";

    internal static new ManualLogSource Logger;

    // Master switch
    internal static ConfigEntry<bool> Enabled;

    // Threat readouts
    internal static ConfigEntry<bool> MissileDefeatHint;
    internal static ConfigEntry<bool> ImpactCountdown;
    internal static ConfigEntry<bool> RadarTags;
    internal static ConfigEntry<bool> HideStockRadarWarning;
    internal static ConfigEntry<bool> HideStockThreatList;
    internal static ConfigEntry<bool> CountermeasureColours;

    // Weapon readouts
    internal static ConfigEntry<bool> ShootCue;
    internal static ConfigEntry<bool> TargetDataBlock;

    // Contact readouts
    internal static ConfigEntry<bool> ContactGlyphs;
    internal static ConfigEntry<bool> ContactGlyphFriendlies;
    internal static ConfigEntry<float> ContactGlyphScale;

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

    /// <summary>A feature is active only if both it and the whole mod are enabled.</summary>
    internal static bool On(ConfigEntry<bool> feature)
    {
        return Enabled.Value && feature.Value;
    }

    private void Awake()
    {
        Logger = base.Logger;

        BindConfig();
        ElementLayout.Initialize();
        Declutter.Initialize();
        FuelTime.Initialize();
        ThreatReadout.Initialize();

        new Harmony(Guid).PatchAll();
        Logger.LogInfo($"{Name} {Version} loaded.");
    }

    private void Update()
    {
        Overlay.LogDiagnosticsOnce();

        ElementLayout.TickSweep();
        ThreatReadout.Tick();
        RattenHUD.ShootCue.Tick();
    }

    private void BindConfig()
    {
        Enabled = Config.Bind(
            "1. General", "Enable mod", true,
            "Master switch for the whole mod. Off: every feature below is disabled "
            + "and the HUD looks exactly like the unmodded game.");

        MissileDefeatHint = Config.Bind(
            "2. Threat warnings", "Missile defeat hint", true,
            "List every missile coming at you in red, centred just above the "
            + "SHOOT / OUT OF RANGE hint, each with the counter that defeats it: "
            + "FLARE (drop flares), NOTCH (fly at a right angle to the radar), "
            + "HIDE (break line of sight) or RADAR OFF (switch your radar off). "
            + "The soonest impact is always the bottom line. The block can be "
            + "moved via '6. HUD layout' under the name 'Missiles'.");
        ImpactCountdown = Config.Bind(
            "2. Threat warnings", "Impact countdown", true,
            "Add the estimated seconds until impact to each missile line (only "
            + "shown while the missile is actually catching up to you).");
        RadarTags = Config.Bind(
            "2. Threat warnings", "Radar warning tags", true,
            "List enemy radars that see you in the right-hand flight column, "
            + "named by unit type. A radar merely scanning shows how often it "
            + "has pinged you (x2, x3, ...) and a single ping is ignored as "
            + "noise; LOCK (locked on) and LAUNCH (missile on the way) show "
            + "immediately. The list can be moved via '6. HUD layout' under "
            + "the name 'Threats'.");
        HideStockRadarWarning = Config.Bind(
            "2. Threat warnings", "Hide stock radar arrows", false,
            "Remove the game's own radar warning wedges around the minimap, so the "
            + "tag list above becomes your only radar warning display. The audio "
            + "warning tone stays.");
        HideStockThreatList = Config.Bind(
            "2. Threat warnings", "Hide stock missile list", true,
            "Remove the game's own missile warning lines over the minimap -- "
            + "the red block on the HUD replaces them. Only the text goes; the "
            + "notch line on the map, the notch indicator and the alarm sound "
            + "all stay.");
        CountermeasureColours = Config.Bind(
            "2. Threat warnings", "Countermeasure colours", true,
            "Colour the flare/chaff counter by how much is left (green, amber, "
            + "red) and flash it when empty, instead of the stock green-then-grey.");

        ShootCue = Config.Bind(
            "3. Weapons", "Extended shoot cue", true,
            "The game only shows SHOOT once the target cannot escape your missile "
            + "any more. This additionally shows IN RANGE as soon as a shot is "
            + "possible at all, so you see the whole firing window.");
        TargetDataBlock = Config.Bind(
            "3. Weapons", "Target data block", true,
            "Add closure speed, aspect and altitude to the readout of the "
            + "currently selected target.");

        ContactGlyphs = Config.Bind(
            "4. Contacts", "Aircraft type symbols", true,
            "Draw each aircraft's type number inside its HUD marker: 12 for an "
            + "FS-12, 46 for an SAH-46. Without this, every aircraft is the same "
            + "anonymous triangle and you have to select one to find out whether "
            + "it is a fighter or a bomber.");
        ContactGlyphFriendlies = Config.Bind(
            "4. Contacts", "Symbols on friendly aircraft", true,
            "Also draw the type number on aircraft of your own faction -- useful "
            + "for spotting which one is the tanker.");
        ContactGlyphScale = Config.Bind(
            "4. Contacts", "Symbol size", 0.2f,
            new ConfigDescription(
                "Size of the type number as a fraction of the HUD marker it sits "
                + "in (0.2 = a fifth, which is already readable). It follows your "
                + "HUD icon size and shrinks with distance; symbols smaller than "
                + "five pixels are not drawn at all. Can be changed while the game "
                + "is running.",
                new AcceptableValueRange<float>(0.05f, 1f)));

        FuelTimeReadout = Config.Bind(
            "5. Flight info", "Fuel time readout", true,
            "Show the estimated remaining flying time next to the fuel gauge. "
            + "Based on your actual fuel burn, so it follows your current "
            + "throttle setting.");
        FuelTimeUpdateRate = Config.Bind(
            "5. Flight info", "Fuel check interval (seconds)", 10f,
            "How often the fuel level is sampled for the estimate. Smaller reacts "
            + "faster to throttle changes but jumps around more.");

        LayoutEnabled = Config.Bind(
            "6. HUD layout", "Enable custom layout", true,
            "Apply the 'Element layout' line below, which moves, scales or hides "
            + "individual HUD elements. Off: everything sits where the game "
            + "puts it.");
        Layout = Config.Bind(
            "6. HUD layout", "Element layout",
            "Climbrate:0,40;Altitude:0,28;Bearing.HMD:0,0,1,false",
            "Advanced setting: where each HUD element sits. One entry per "
            + "element, separated by semicolons.\n"
            + "Format: Name:right,up[,scale][,visible] -- offsets in pixels on a "
            + "1080p reference screen, positive = right/up, and a trailing "
            + "'false' hides the element entirely.\n"
            + "Example: Climbrate:0,40;SpeedGauge:20,0,0.9;CountermeasureIndicator:0,0,1,false\n"
            + "(climb rate 40 up; speed gauge 20 right at 90% size; "
            + "countermeasure counter hidden.)\n"
            + "Element names match the game's HUD components: Climbrate, "
            + "Altitude, SpeedGauge, FuelGauge, CountermeasureIndicator, "
            + "WeaponIndicator, and so on. Screen-fixed variants of a readout "
            + "get a suffix: Bearing.HMD is the boxed heading at the top of "
            + "the screen, Bearing the one on the glass. This mod's radar "
            + "list moves as 'Threats' and its missile warnings as "
            + "'Missiles'. ArtificialHorizon and GearIndicator cannot be "
            + "moved.\n"
            + "The default moves the climb rate up (in the stock layout it "
            + "collides with its neighbours), tucks the altitude block "
            + "underneath it so the threat readout fits below, and hides the "
            + "screen-fixed heading box, which duplicates the heading under "
            + "the compass tape.\n"
            + "If you also run MKMods, set its ClimbRateVerticalOffset to 0 "
            + "first, or the climb rate moves twice.");

        ObjectiveLabel = Config.Bind(
            "7. Declutter", "Objective label", ObjectiveLabelMode.Hidden,
            "How much text to show on the objective marker. Hidden: just the "
            + "circle, dot and off-screen pointer, no text. DistanceOnly: only "
            + "the distance. Full: name and distance, like the unmodded game.");
        HideMarkers = Config.Bind(
            "7. Declutter", "Hide chosen unit markers", true,
            "Remove HUD markers for the unit types listed below. The units stay "
            + "visible on the map; only the marker on the HUD glass goes away.");
        HiddenMarkerUnits = Config.Bind(
            "7. Declutter", "Hidden unit types", "pilot",
            "Which units 'Hide chosen unit markers' removes. Comma separated, "
            + "capitalisation does not matter, and each entry matches any unit "
            + "whose name contains it -- the default 'pilot' hides downed pilot "
            + "markers. Not sure what a unit is called? Check its name on the "
            + "map and use that.");
    }
}
