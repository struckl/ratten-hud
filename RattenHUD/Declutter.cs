using System;
using HarmonyLib;
using UnityEngine;

namespace RattenHUD;

/// <summary>How much of the objective overlay label to keep.</summary>
public enum ObjectiveLabelMode
{
    /// <summary>Stock: objective name and range.</summary>
    Full,

    /// <summary>Range only, no name.</summary>
    DistanceOnly,

    /// <summary>No text at all: just the circle, dot and pointer.</summary>
    Hidden,
}

/// <summary>
/// Removes HUD furniture that belongs on the map rather than on the glass.
/// </summary>
internal static class Declutter
{
    private static string[] hiddenMarkers = Array.Empty<string>();

    public static void Initialize()
    {
        ParseHiddenMarkers(Plugin.HiddenMarkerUnits.Value);
        Plugin.HiddenMarkerUnits.SettingChanged += (_, _) =>
            ParseHiddenMarkers(Plugin.HiddenMarkerUnits.Value);
    }

    private static void ParseHiddenMarkers(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            hiddenMarkers = Array.Empty<string>();
            return;
        }

        string[] parts = value.Split(',');
        var cleaned = new System.Collections.Generic.List<string>(parts.Length);
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
                cleaned.Add(trimmed.ToLowerInvariant());
        }
        hiddenMarkers = cleaned.ToArray();
    }

    /// <summary>
    /// True if this unit should not get a HUD marker. Matched on the unit's
    /// display name, type code and object name, because the game has no single
    /// class for a downed pilot to key off.
    /// </summary>
    public static bool IsMarkerHidden(Unit unit)
    {
        if (hiddenMarkers.Length == 0 || unit == null)
            return false;

        string unitName = unit.definition != null ? unit.definition.unitName : null;
        string code = unit.definition != null ? unit.definition.code : null;
        string objectName = unit.name;

        foreach (string needle in hiddenMarkers)
        {
            if (Contains(unitName, needle) || Contains(code, needle) || Contains(objectName, needle))
                return true;
        }
        return false;
    }

    private static bool Contains(string haystack, string lowercaseNeedle) =>
        !string.IsNullOrEmpty(haystack)
        && haystack.ToLowerInvariant().Contains(lowercaseNeedle);
}

/// <summary>
/// Drops HUD markers for units the player would rather only see on the map.
/// This is the single point where every marker is created, and the map's icons
/// come from <see cref="DynamicMap"/> instead, so filtering here takes a unit
/// off the glass without taking it off the map.
/// </summary>
[HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.CreateMarker))]
internal static class MarkerFilterPatch
{
    private static bool Prefix(PersistentID id)
    {
        if (!Plugin.HideMarkers.Value)
            return true;

        if (UnitRegistry.TryGetUnit(id, out Unit unit) && Declutter.IsMarkerHidden(unit))
            return false;

        return true;
    }
}

/// <summary>
/// Trims the objective overlay label. The circle, dot and off-screen pointer are
/// left exactly as they are; only the text is touched.
/// </summary>
[HarmonyPatch(typeof(ObjectiveOverlay), nameof(ObjectiveOverlay.UpdateOverlay))]
internal static class ObjectiveLabelPatch
{
    private static void Postfix(
        MissionPosition.PositionResult result,
        UnityEngine.UI.Text ___objectiveInfo)
    {
        if (___objectiveInfo == null)
            return;

        switch (Plugin.ObjectiveLabel.Value)
        {
            case ObjectiveLabelMode.Hidden:
                ___objectiveInfo.enabled = false;
                break;

            case ObjectiveLabelMode.DistanceOnly:
                ___objectiveInfo.enabled = true;
                ___objectiveInfo.text = UnitConverter.DistanceReading(result.Distance);
                break;

            // Full: the game already wrote name and range; leave it alone.
        }
    }
}
