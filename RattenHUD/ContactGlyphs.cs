using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace RattenHUD;

/// <summary>
/// Puts a symbol inside the aircraft marker, so the triangles differ from each
/// other the way the ground icons already do.
///
/// Ground units are told apart at a glance because their icons differ: a tank
/// does not look like a radar. Every aircraft draws the same triangle, so a
/// fighter, a bomber and the AWACS behind them are one shape repeated, and the
/// only way to tell them apart is to select each in turn and read the target
/// block.
///
/// The symbol is the number out of the type code -- 12 for an FS-12, 46 for an
/// SAH-46 -- drawn in the middle of the marker:
///
///     /12\        /81\        /25\
///
/// Numbers rather than letters because the codes collide on their letters and
/// not on their numbers: FS-12 and FS-20 are both F, SAH-46 and SFB-81 are both
/// S. And a symbol rather than a label beside the marker, which is what this
/// started as: a dozen contacts each trailing text turned the glass into a wall
/// of words. Inside the icon it costs no space at all -- the triangle was
/// already there.
///
/// Nothing here reveals a contact the game had decided not to draw. The marker
/// was on the glass either way; the number is the same <c>definition.code</c>
/// the game's own target block prints once you select it.
/// </summary>
internal static class ContactGlyphs
{
    /// <summary>Smallest readable glyph, in HUD text units. A marker shrunk into
    /// the distance is left with no symbol rather than an illegible smudge in
    /// the middle of it.</summary>
    private const int MinimumSize = 5;

    private sealed class Glyph
    {
        public TextMeshProUGUI Text;
        public string Content;
        public int Frame;
    }

    private static readonly Dictionary<HUDUnitMarker, Glyph> Glyphs =
        new Dictionary<HUDUnitMarker, Glyph>();
    private static readonly List<HUDUnitMarker> Stale = new List<HUDUnitMarker>();

    // definition.code is fixed per unit type, so the symbol for one is worked
    // out once rather than per contact per frame.
    private static readonly Dictionary<string, string> Symbols =
        new Dictionary<string, string>();

    private static bool loggedSize;

    /// <summary>
    /// Called after the game has moved every marker for this frame, so the
    /// symbols sit on positions that are already up to date.
    /// </summary>
    public static void Update(CombatHUD hud, List<HUDUnitMarker> markers, TextMeshProUGUI template)
    {
        if (!Plugin.On(Plugin.ContactGlyphs))
        {
            Clear();
            return;
        }

        if (hud == null || markers == null || template == null)
            return;

        Aircraft player = hud.aircraft;
        Transform layer = hud.iconLayer;
        if (player == null || layer == null)
            return;

        // The one contact the game is already annotating. Its target block
        // prints the full code centred on the same marker, so a symbol there
        // would sit underneath the first line of it.
        List<Unit> targets = hud.GetTargetList();
        Unit primary = targets != null && targets.Count > 0 ? targets[0] : null;

        bool friendlies = Plugin.ContactGlyphFriendlies.Value;
        float scale = Plugin.ContactGlyphScale.Value;
        int frame = Time.frameCount;

        for (int i = 0; i < markers.Count; i++)
        {
            HUDUnitMarker marker = markers[i];
            if (marker == null || marker.image == null)
                continue;

            Unit unit = marker.unit;
            if (unit == null || !(unit is Aircraft) || unit == primary)
                continue;
            if (!friendlies && unit.NetworkHQ == player.NetworkHQ)
                continue;

            Glyphs.TryGetValue(marker, out Glyph glyph);
            if (glyph != null)
                glyph.Frame = frame;

            // The marker itself is the authority on whether this contact is
            // drawn at all: it is disabled behind the camera, with the gear
            // down, and while a selected target is pinned to the screen edge.
            int size = marker.image.enabled ? GlyphSize(marker.image.rectTransform, template, scale) : 0;
            if (size < MinimumSize)
            {
                if (glyph != null && glyph.Text != null && glyph.Text.enabled)
                    glyph.Text.enabled = false;
                continue;
            }

            if (glyph == null || glyph.Text == null)
            {
                TextMeshProUGUI text = Build(template, layer);
                if (text == null)
                    continue;

                glyph = new Glyph { Text = text, Frame = frame };
                Glyphs[marker] = glyph;
                LogSizeOnce(marker.image.rectTransform, template, scale, size);
            }

            string symbol = Symbol(unit);
            if (symbol != glyph.Content)
            {
                glyph.Content = symbol;
                glyph.Text.text = symbol;
            }

            if (glyph.Text.fontSize != size)
                glyph.Text.fontSize = size;

            // Same colour as the icon it sits in, so the symbol dims with a
            // stale track and flickers under jamming exactly as the marker
            // does, instead of staying bright over a fading triangle.
            glyph.Text.color = marker.image.color;

            RectTransform rect = glyph.Text.rectTransform;
            rect.position = marker.image.rectTransform.position;
            rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);

            if (!glyph.Text.enabled)
                glyph.Text.enabled = true;
        }

        Sweep(frame);
    }

    /// <summary>
    /// The symbol for a unit type: the number out of its code, so FS-12 reads
    /// as 12 and T/A-30 as 30.
    ///
    /// Falls back to the leading letters for a code with no number in it --
    /// nothing in the stock roster, but a modded airframe is free to be called
    /// anything, and a blank triangle would look like this feature had broken
    /// rather than like a unit it did not know.
    /// </summary>
    private static string Symbol(Unit unit)
    {
        string code = UnitNaming.TypeCode(unit);
        if (Symbols.TryGetValue(code, out string symbol))
            return symbol;

        int end = -1;
        for (int i = code.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(code[i]))
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            symbol = code.Length <= 2 ? code : code.Substring(0, 2);
        }
        else
        {
            int start = end;
            while (start > 0 && char.IsDigit(code[start - 1]))
                start--;

            // Two digits is what the roster uses and what fits inside the
            // marker; a longer number keeps its last two.
            if (end - start > 1)
                start = end - 1;
            symbol = code.Substring(start, end - start + 1);
        }

        Symbols[code] = symbol;
        return symbol;
    }

    /// <summary>
    /// One line, for the first symbol of a session, with the numbers the size
    /// is worked out from.
    ///
    /// The marker's rect is a good deal larger than the triangle drawn inside
    /// it, and how much larger is a property of a sprite this plugin cannot
    /// see, so a scale that looks right is found by eye. This prints what the
    /// eye is actually looking at, which beats another round of guessing.
    /// </summary>
    private static void LogSizeOnce(RectTransform icon, TextMeshProUGUI template, float scale, int size)
    {
        if (loggedSize)
            return;

        loggedSize = true;
        Plugin.Logger.LogInfo(
            $"ContactGlyphs: marker rect={icon.sizeDelta.x:F1} x scale={icon.localScale.x:F1}"
            + $" = {icon.sizeDelta.x * icon.localScale.x:F1}, glyph={size} at GlyphScale={scale:F2}"
            + $", HUD text={template.fontSize}");
    }

    /// <summary>
    /// Text size for a symbol that has to sit inside this marker, in the units
    /// the icon's own size is measured in.
    /// </summary>
    private static int GlyphSize(RectTransform icon, TextMeshProUGUI template, float scale)
    {
        float span = icon.sizeDelta.x * icon.localScale.x;
        // A marker that reports no size of its own still has to get a symbol
        // from somewhere; the HUD text size is the same fallback the rest of
        // the plugin uses.
        if (span < 1f)
            span = template.fontSize;

        return Mathf.RoundToInt(span * scale);
    }

    /// <summary>Drops the symbol for a marker the game is removing.</summary>
    public static void Forget(HUDUnitMarker marker)
    {
        if (marker == null || !Glyphs.TryGetValue(marker, out Glyph glyph))
            return;

        Destroy(glyph);
        Glyphs.Remove(marker);
    }

    /// <summary>Drops every symbol: seat change, scene change, or switched off.</summary>
    public static void Clear()
    {
        if (Glyphs.Count == 0)
            return;

        foreach (Glyph glyph in Glyphs.Values)
            Destroy(glyph);
        Glyphs.Clear();
    }

    /// <summary>
    /// Removes symbols whose marker was not seen this frame. <see cref="Forget"/>
    /// catches the ordinary removal path; this catches the rest, so a marker
    /// list emptied behind our back cannot leave symbols hanging on the glass.
    /// </summary>
    private static void Sweep(int frame)
    {
        if (Glyphs.Count == 0)
            return;

        Stale.Clear();
        foreach (KeyValuePair<HUDUnitMarker, Glyph> pair in Glyphs)
        {
            if (pair.Value.Frame != frame)
                Stale.Add(pair.Key);
        }

        for (int i = 0; i < Stale.Count; i++)
        {
            Destroy(Glyphs[Stale[i]]);
            Glyphs.Remove(Stale[i]);
        }
    }

    /// <summary>
    /// Clones the game's own target readout. Cloning a live HUD label rather
    /// than building a label from nothing inherits the HUD font, material and
    /// projection, and the target block is the nearest template there is: it is
    /// the one label the game itself anchors to a marker, so the copy lands in
    /// the coordinate space the markers are positioned in.
    /// </summary>
    private static TextMeshProUGUI Build(TextMeshProUGUI template, Transform layer)
    {
        GameObject clone = Object.Instantiate(template.gameObject, layer);
        clone.name = "RattenHUDContactGlyph";
        clone.SetActive(true);

        foreach (HUDApp driver in clone.GetComponents<HUDApp>())
            Object.Destroy(driver);

        TextMeshProUGUI text = clone.GetComponent<TextMeshProUGUI>();
        if (text == null)
        {
            Object.Destroy(clone);
            return null;
        }

        text.text = string.Empty;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.richText = false;

        RectTransform rect = text.rectTransform;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        // Created after every marker that exists so far, so the symbol draws
        // over its own triangle rather than under it.
        rect.SetAsLastSibling();
        return text;
    }

    private static void Destroy(Glyph glyph)
    {
        if (glyph.Text != null)
            Object.Destroy(glyph.Text.gameObject);
        glyph.Text = null;
    }
}

/// <summary>
/// Hooks the marker loop and the two paths that take markers away.
///
/// The symbols are placed from <c>UpdateMarkers</c> rather than from the
/// plugin's own Update so they are written after the game has moved the markers
/// for this frame; running first would leave every symbol one frame behind its
/// triangle, which shows up as a wobble on a crossing target.
/// </summary>
[HarmonyPatch(typeof(CombatHUD))]
internal static class ContactGlyphPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("UpdateMarkers")]
    private static void UpdateMarkers(
        CombatHUD __instance,
        List<HUDUnitMarker> ___markers,
        TextMeshProUGUI ___targetInfo) =>
        ContactGlyphs.Update(__instance, ___markers, ___targetInfo);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CombatHUD.RemoveMarker))]
    private static void RemoveMarker(HUDUnitMarker marker) => ContactGlyphs.Forget(marker);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CombatHUD.ClearIcons))]
    private static void ClearIcons() => ContactGlyphs.Clear();
}
