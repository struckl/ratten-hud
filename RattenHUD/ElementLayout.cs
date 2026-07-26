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
/// Declutter and reposition table for the game's HUD elements.
///
/// Elements are keyed by the name of the <see cref="HUDApp"/> component that
/// drives them (Climbrate, CountermeasureIndicator, ...) rather than by
/// GameObject name, because the component name is what the game's own code
/// commits to and so survives cosmetic prefab renames.
/// </summary>
internal static class ElementLayout
{
    private static readonly Dictionary<string, LayoutRule> Rules =
        new Dictionary<string, LayoutRule>();

    /// <summary>Everything we overwrote on one element, so it can be put back exactly.</summary>
    private readonly struct Baseline
    {
        public readonly Vector2 Position;
        public readonly Vector3 Scale;
        public readonly bool Active;

        public Baseline(Vector2 position, Vector3 scale, bool active)
        {
            Position = position;
            Scale = scale;
            Active = active;
        }
    }

    // Captured the first time we act on an element, so reapplying the table is
    // idempotent rather than cumulative, and so a removed rule can be undone.
    private static readonly Dictionary<int, Baseline> Baselines = new Dictionary<int, Baseline>();

    // Elements we have actually modified. Anything not in here is untouched and
    // must stay that way.
    private static readonly HashSet<int> Modified = new HashSet<int>();

    // Every element that has come through the settings refresh, so a config
    // reload can re-apply to all of them immediately instead of waiting for
    // the player to open the settings menu.
    private static readonly Dictionary<string, RectTransform> Seen =
        new Dictionary<string, RectTransform>();
    private static readonly List<string> SeenNames = new List<string>();

    public static void Initialize()
    {
        Parse(Plugin.Layout.Value);
        Plugin.Layout.SettingChanged += (_, _) =>
        {
            Parse(Plugin.Layout.Value);
            ReapplySeen();
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

    private static void ReapplySeen()
    {
        // Over a copy of the names: Apply writes back into Seen.
        SeenNames.Clear();
        SeenNames.AddRange(Seen.Keys);
        foreach (string name in SeenNames)
        {
            if (Seen[name] != null)
                Apply(name, Seen[name]);
        }
    }

    /// <summary>
    /// Applies the rule for <paramref name="name"/> to <paramref name="rect"/>.
    ///
    /// An element with no rule is left strictly alone. That matters more than it
    /// sounds: this runs against every HUD element on every settings refresh, so
    /// "helpfully" normalising unruled elements would flatten prefab scaling on
    /// gauges that ship scaled, re-show elements the game deliberately hid, and
    /// silently undo other plugins' positioning every time the player opened the
    /// settings menu. Only elements we were actually asked to move are touched,
    /// and only they are restored when their rule goes away.
    /// </summary>
    public static void Apply(string name, RectTransform rect)
    {
        if (rect == null)
            return;

        Seen[name] = rect;
        int id = rect.GetInstanceID();
        bool hasRule = Rules.TryGetValue(name, out LayoutRule rule);

        if (!hasRule)
        {
            // Undo ourselves only if this element is one we previously moved.
            if (Modified.Remove(id) && Baselines.TryGetValue(id, out Baseline undo))
            {
                rect.anchoredPosition = undo.Position;
                rect.localScale = undo.Scale;
                if (rect.gameObject.activeSelf != undo.Active)
                    rect.gameObject.SetActive(undo.Active);
            }
            return;
        }

        if (!Baselines.TryGetValue(id, out Baseline baseline))
        {
            baseline = new Baseline(rect.anchoredPosition, rect.localScale, rect.gameObject.activeSelf);
            Baselines[id] = baseline;
        }
        Modified.Add(id);

        rect.anchoredPosition = baseline.Position + rule.Offset;
        // Scale multiplies whatever the prefab already had rather than replacing
        // it, so a gauge that ships at 0.8 stays proportioned.
        rect.localScale = baseline.Scale * rule.Scale;
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
        // Also the one place that reliably hands us a live HUD label to clone.
        Overlay.OfferTemplate(__instance);

        if (!Plugin.LayoutEnabled.Value)
            return;
        if (__instance.transform is RectTransform rect)
            ElementLayout.Apply(__instance.GetType().Name, rect);
    }
}
