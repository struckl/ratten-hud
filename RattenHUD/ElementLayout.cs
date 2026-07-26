using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace RattenHUD;

/// <summary>
/// A single offset/scale/visibility override for one named HUD element.
/// </summary>
internal readonly struct LayoutRule
{
    public readonly Vector2 Offset;
    public readonly float Scale;
    public readonly bool Visible;

    public LayoutRule(Vector2 offset, float scale, bool visible)
    {
        Offset = offset;
        Scale = scale;
        Visible = visible;
    }
}

/// <summary>
/// Declutter and reposition table for the whole HUD, the game's elements and
/// this plugin's alike.
///
/// Game elements are keyed by the name of the <see cref="HUDApp"/> component
/// that drives them (Climbrate, CountermeasureIndicator, ...) rather than by
/// GameObject name, because the component name is what the game's own code
/// commits to and so survives cosmetic prefab renames. This plugin's own
/// readouts register under the names in <see cref="Elements"/>.
/// </summary>
internal static class ElementLayout
{
    /// <summary>Names this plugin registers, so the config comment can list them.</summary>
    internal static class Elements
    {
        public const string MissileBanner = "MissileBanner";
        public const string ImpactCountdown = "ImpactCountdown";
        public const string RadarWarnings = "RadarWarnings";
        public const string ShootCue = "ShootCue";
        public const string TargetData = "TargetData";
    }

    private static readonly Dictionary<string, LayoutRule> Rules =
        new Dictionary<string, LayoutRule>();

    // Baseline anchored positions, captured the first time an element is seen,
    // so that reapplying the table is idempotent rather than cumulative.
    private static readonly Dictionary<int, Vector2> Baselines = new Dictionary<int, Vector2>();

    // Our own elements, so a config reload can re-apply to them immediately.
    private static readonly Dictionary<string, RectTransform> Owned =
        new Dictionary<string, RectTransform>();

    public static void Initialize()
    {
        Parse(Plugin.Layout.Value);
        Plugin.Layout.SettingChanged += (_, _) =>
        {
            Parse(Plugin.Layout.Value);
            ReapplyOwned();
        };
    }

    private static void Parse(string table)
    {
        Rules.Clear();
        if (string.IsNullOrWhiteSpace(table))
            return;

        foreach (string raw in table.Split(';'))
        {
            string entry = raw.Trim();
            if (entry.Length == 0)
                continue;

            int colon = entry.IndexOf(':');
            if (colon <= 0)
            {
                Plugin.Logger.LogWarning($"Layout entry '{entry}' has no ':' separator; ignored.");
                continue;
            }

            string name = entry.Substring(0, colon).Trim();
            string[] parts = entry.Substring(colon + 1).Split(',');
            if (parts.Length < 2)
            {
                Plugin.Logger.LogWarning($"Layout entry '{entry}' needs at least an x and y offset; ignored.");
                continue;
            }

            if (!TryParseFloat(parts[0], out float dx) || !TryParseFloat(parts[1], out float dy))
            {
                Plugin.Logger.LogWarning($"Layout entry '{entry}' has a non-numeric offset; ignored.");
                continue;
            }

            float scale = 1f;
            if (parts.Length >= 3 && !TryParseFloat(parts[2], out scale))
                scale = 1f;
            if (scale <= 0f)
                scale = 1f;

            bool visible = true;
            if (parts.Length >= 4 && !bool.TryParse(parts[3].Trim(), out visible))
                visible = true;

            Rules[name] = new LayoutRule(new Vector2(dx, dy), scale, visible);
        }
    }

    // The config file is culture invariant; parsing with the current culture
    // would break "1.5" on a German locale.
    private static bool TryParseFloat(string value, out float result) =>
        float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    /// <summary>Registers one of this plugin's own elements and applies its rule.</summary>
    public static void Register(string name, RectTransform rect)
    {
        Owned[name] = rect;
        Apply(name, rect);
    }

    private static void ReapplyOwned()
    {
        foreach (KeyValuePair<string, RectTransform> owned in Owned)
        {
            if (owned.Value != null)
                Apply(owned.Key, owned.Value);
        }
    }

    /// <summary>
    /// Applies the rule for <paramref name="name"/> to <paramref name="rect"/>.
    /// Elements with no rule are restored to their baseline, so deleting an
    /// entry from the config undoes it rather than freezing the last value.
    /// </summary>
    public static void Apply(string name, RectTransform rect)
    {
        if (rect == null)
            return;

        int id = rect.GetInstanceID();
        if (!Baselines.TryGetValue(id, out Vector2 baseline))
        {
            baseline = rect.anchoredPosition;
            Baselines[id] = baseline;
        }

        if (!Rules.TryGetValue(name, out LayoutRule rule))
        {
            rect.anchoredPosition = baseline;
            rect.localScale = Vector3.one;
            if (!rect.gameObject.activeSelf)
                rect.gameObject.SetActive(value: true);
            return;
        }

        rect.anchoredPosition = baseline + rule.Offset;
        rect.localScale = new Vector3(rule.Scale, rule.Scale, 1f);
        if (rect.gameObject.activeSelf != rule.Visible)
            rect.gameObject.SetActive(rule.Visible);
    }
}

/// <summary>
/// Applies the layout table to the game's own HUD elements. Every HUD element
/// routes through <see cref="HUDApp.RefreshSettings"/> when the HUD is built and
/// whenever the player changes HUD settings, which makes it the one place that
/// sees them all without hunting the hierarchy.
/// </summary>
[HarmonyPatch(typeof(HUDApp), nameof(HUDApp.RefreshSettings))]
internal static class HUDAppLayoutPatch
{
    private static void Postfix(HUDApp __instance)
    {
        if (!Plugin.LayoutEnabled.Value)
            return;
        if (__instance.transform is RectTransform rect)
            ElementLayout.Apply(__instance.GetType().Name, rect);
    }
}
