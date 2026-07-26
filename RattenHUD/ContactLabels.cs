using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// Types the aircraft markers on the glass.
///
/// Ground units are told apart at a glance because their icons differ: a tank
/// does not look like a radar. Every aircraft draws the same triangle, so the
/// only way to find out what is actually out there is to select a contact and
/// read the target block -- one contact at a time, and only for the one target
/// the block belongs to.
///
/// This writes the type code beside each aircraft marker, so a four-ship reads
/// as four airframes without touching the target list. The code is the same
/// <c>definition.code</c> the game's own target block prints, and nothing here
/// reveals a contact the game had already decided to draw: the marker was on
/// the glass either way, this only says what it is.
///
/// Tags are per marker rather than a registered overlay element, so the layout
/// table does not apply to them -- there is no fixed place on the HUD to move.
/// They live in the game's own icon layer, positioned off the marker every
/// frame, and inherit the HUD font and the marker's colour: hostile red,
/// friendly blue, dimmed while the track is stale, flickering while jammed.
/// </summary>
internal static class ContactLabels
{
    /// <summary>How often the text itself is rewritten, in seconds. The tag
    /// follows its marker every frame; only the string is throttled, because a
    /// range that ticks every frame is both unreadable and a text mesh rebuild
    /// per contact per frame.</summary>
    private const float TextInterval = 0.25f;

    /// <summary>Gap between the icon edge and the tag, as a fraction of the tag's
    /// own text size, so it scales with the player's HUD text setting.</summary>
    private const float Gap = 0.35f;

    private sealed class Tag
    {
        public Text Text;
        public string Content;
        public int Frame;
        public float NextText;
    }

    private static readonly Dictionary<HUDUnitMarker, Tag> Tags =
        new Dictionary<HUDUnitMarker, Tag>();
    private static readonly List<HUDUnitMarker> Stale = new List<HUDUnitMarker>();
    private static readonly StringBuilder Builder = new StringBuilder(32);

    /// <summary>
    /// Called after the game has moved every marker for this frame, so the tags
    /// read positions that are already up to date.
    /// </summary>
    public static void Update(CombatHUD hud, List<HUDUnitMarker> markers, Text template)
    {
        if (!Plugin.ContactLabels.Value)
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

        // Whatever the game is already annotating with its own target block; a
        // tag there would print the type code twice, on top of itself.
        List<Unit> targets = hud.GetTargetList();
        Unit primary = targets != null && targets.Count > 0 ? targets[0] : null;

        int fontSize = Mathf.Max(6, Mathf.RoundToInt(template.fontSize * Plugin.ContactLabelScale.Value));
        float maxRange = Plugin.ContactLabelMaxRange.Value;
        GlobalPosition eye = player.GlobalPosition();
        int frame = Time.frameCount;
        float now = Time.unscaledTime;

        for (int i = 0; i < markers.Count; i++)
        {
            HUDUnitMarker marker = markers[i];
            if (marker == null || marker.image == null)
                continue;

            Unit unit = marker.unit;
            if (unit == null || !(unit is Aircraft) || unit == primary)
                continue;
            if (!Plugin.ContactLabelFriendlies.Value && unit.NetworkHQ == player.NetworkHQ)
                continue;

            Tags.TryGetValue(marker, out Tag tag);
            if (tag != null)
                tag.Frame = frame;

            // The marker itself is the authority on whether this contact is
            // being drawn at all: it is disabled behind the camera, with the
            // gear down, and for a selected target pinned to the screen edge.
            GlobalPosition known = default;
            bool show = marker.image.enabled
                && player.NetworkHQ != null
                && player.NetworkHQ.TryGetKnownPosition(unit, out known);

            float distance = show ? FastMath.Distance(eye, known) : 0f;
            if (maxRange > 0f && distance > maxRange)
                show = false;

            if (!show)
            {
                if (tag != null && tag.Text != null && tag.Text.enabled)
                    tag.Text.enabled = false;
                continue;
            }

            if (tag == null || tag.Text == null)
            {
                Text text = Build(template, layer);
                if (text == null)
                    continue;

                tag = new Tag { Text = text, Frame = frame };
                Tags[marker] = tag;
            }

            if (tag.Text.fontSize != fontSize)
                tag.Text.fontSize = fontSize;

            if (now >= tag.NextText)
            {
                tag.NextText = now + TextInterval;
                string content = Compose(unit, distance);
                if (content != tag.Content)
                {
                    tag.Content = content;
                    tag.Text.text = content;
                }
            }

            tag.Text.color = marker.image.color;
            Place(tag.Text, marker.image.rectTransform, fontSize);

            if (!tag.Text.enabled)
                tag.Text.enabled = true;
        }

        Sweep(frame);
    }

    /// <summary>Drops the tag for a marker the game is removing.</summary>
    public static void Forget(HUDUnitMarker marker)
    {
        if (marker == null || !Tags.TryGetValue(marker, out Tag tag))
            return;

        Destroy(tag);
        Tags.Remove(marker);
    }

    /// <summary>Drops every tag: seat change, scene change, or feature switched off.</summary>
    public static void Clear()
    {
        if (Tags.Count == 0)
            return;

        foreach (Tag tag in Tags.Values)
            Destroy(tag);
        Tags.Clear();
    }

    /// <summary>
    /// Removes tags whose marker was not seen this frame. <see cref="Forget"/>
    /// catches the ordinary removal path; this catches the rest, so a marker
    /// list emptied behind our back cannot leave labels hanging on the glass.
    /// </summary>
    private static void Sweep(int frame)
    {
        if (Tags.Count == 0)
            return;

        Stale.Clear();
        foreach (KeyValuePair<HUDUnitMarker, Tag> pair in Tags)
        {
            if (pair.Value.Frame != frame)
                Stale.Add(pair.Key);
        }

        for (int i = 0; i < Stale.Count; i++)
        {
            Destroy(Tags[Stale[i]]);
            Tags.Remove(Stale[i]);
        }
    }

    private static string Compose(Unit unit, float distance)
    {
        if (!Plugin.ContactLabelRange.Value)
            return UnitNaming.TypeCode(unit);

        Builder.Length = 0;
        Builder.Append(UnitNaming.TypeCode(unit))
               .Append("  ")
               .Append(UnitConverter.DistanceReading(distance));
        return Builder.ToString();
    }

    /// <summary>
    /// Puts the tag beside its icon: snapped onto the marker in world space,
    /// then stepped clear of it in the icon layer's own units.
    ///
    /// Going through the marker's position rather than computing a screen
    /// position ourselves means this holds whatever the HUD canvas is scaled
    /// to, and the second step stays in the units the icon's size is expressed
    /// in, which is the only way the two agree.
    /// </summary>
    private static void Place(Text tag, RectTransform icon, int fontSize)
    {
        RectTransform rect = tag.rectTransform;
        rect.position = icon.position;

        float halfIcon = 0.5f * icon.sizeDelta.x * icon.localScale.x;
        // A marker that reports no size still needs the tag off the icon.
        if (halfIcon < 1f)
            halfIcon = fontSize * 0.5f;

        Vector3 local = rect.localPosition;
        local.x += halfIcon + fontSize * Gap;
        local.z = 0f;
        rect.localPosition = local;
    }

    /// <summary>
    /// Clones the game's own target readout. Cloning a live HUD label rather
    /// than building a Text from nothing inherits the HUD font, material and
    /// projection, and puts the tag in the same layer the markers are drawn in.
    /// The target block is the right template because it is the one label the
    /// game already anchors to a marker.
    /// </summary>
    private static Text Build(Text template, Transform layer)
    {
        GameObject clone = Object.Instantiate(template.gameObject, layer);
        clone.name = "RattenHUD_ContactLabel";
        clone.SetActive(true);

        foreach (HUDApp driver in clone.GetComponents<HUDApp>())
            Object.Destroy(driver);

        Text text = clone.GetComponent<Text>();
        if (text == null)
        {
            Object.Destroy(clone);
            return null;
        }

        text.text = string.Empty;
        // Left pivot with left alignment: the tag then starts exactly at the
        // point we place it, instead of straddling it and running back over the
        // icon as the text gets longer.
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = true;

        RectTransform rect = text.rectTransform;
        rect.pivot = new Vector2(0f, 0.5f);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        return text;
    }

    private static void Destroy(Tag tag)
    {
        if (tag.Text != null)
            Object.Destroy(tag.Text.gameObject);
        tag.Text = null;
    }
}

/// <summary>
/// Hooks the marker loop and the two paths that take markers away.
///
/// The tags are updated from <c>UpdateMarkers</c> rather than from the plugin's
/// own Update so they are written after the game has moved the markers for this
/// frame; running first would leave every tag one frame behind its icon, which
/// shows up as lag on a crossing target.
/// </summary>
[HarmonyPatch(typeof(CombatHUD))]
internal static class ContactLabelPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("UpdateMarkers")]
    private static void UpdateMarkers(
        CombatHUD __instance,
        List<HUDUnitMarker> ___markers,
        Text ___targetInfo) =>
        ContactLabels.Update(__instance, ___markers, ___targetInfo);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CombatHUD.RemoveMarker))]
    private static void RemoveMarker(HUDUnitMarker marker) => ContactLabels.Forget(marker);

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CombatHUD.ClearIcons))]
    private static void ClearIcons() => ContactLabels.Clear();
}
