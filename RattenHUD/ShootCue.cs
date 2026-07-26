using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// The game already solves the launch envelope — it computes Rmin, Rmax and the
/// no-escape range every second and decides whether every firing requirement is
/// met. It then throws most of that away: the "SHOOT" hint is only ever shown
/// inside the no-escape zone (<c>hint.enabled = maxTargetDist &lt; noEscapeRange</c>),
/// so between NEZ and Rmax you get a valid firing solution and no cue at all.
///
/// This surfaces the state the game is already tracking: SHOOT inside the NEZ,
/// IN RANGE between NEZ and Rmax, and the numbers behind it on the overlay.
/// </summary>
internal static class ShootCue
{
    private const float ShootFlashHz = 3f;

    private static readonly Color Shoot = new Color(0.3f, 1f, 0.35f);
    private static readonly Color InRange = new Color(1f, 0.85f, 0.2f);

    /// <summary>
    /// How long the cue may go unrefreshed before it is assumed stale. The game
    /// recomputes the envelope every 0.1s while the missile UI is up, so
    /// anything past a few frames of that means the UI is gone.
    /// </summary>
    private const float StaleAfterSeconds = 0.35f;

    private static bool registered;
    private static float lastRefresh = float.NegativeInfinity;

    // The game's own hint label, kept so a stale cue can hand it back rather
    // than leaving our text forced on.
    private static Text drivenHint;

    public static void Initialize()
    {
        // The cue itself lives in the game's own hint label. The extra overlay
        // line is off by default: the game already draws MAX, MIN and NEZ next
        // to the range ladder, so it mostly duplicates them, and being on the
        // always-on-top canvas it lands over whatever weapon view you switch to.
        if (!Plugin.ShootCue.Value || !Plugin.ShootCueOverlay.Value)
            return;

        Overlay.Register(
            ElementLayout.Elements.ShootCue,
            anchor: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            offset: new Vector2(0f, -210f),
            fontScale: 1f,
            TextAnchor.MiddleCenter);

        registered = true;
    }

    public static void Clear()
    {
        Text readout = Overlay.Peek(ElementLayout.Elements.ShootCue);
        if (readout != null && readout.text.Length > 0)
            readout.text = string.Empty;
    }

    /// <summary>
    /// Watchdog. The cue is written from a patch on the missile UI, and that
    /// patch simply stops running when the player selects a weapon that is not
    /// a radar missile -- guns, bombs, an IR seeker view. Without this the last
    /// string written stays stranded on the overlay canvas, which draws over
    /// everything, for the rest of the sortie.
    /// </summary>
    public static void Tick()
    {
        if (Overlay.InCockpit && Time.unscaledTime - lastRefresh <= StaleAfterSeconds)
            return;

        Clear();

        // Hand the game's label back. Its own DisplayText re-establishes the
        // correct enabled state the moment the missile UI runs again.
        if (drivenHint != null)
        {
            drivenHint.enabled = false;
            drivenHint = null;
        }
    }

    /// <summary>
    /// Called from the <see cref="HUDMissileState"/> postfix with the envelope
    /// the game just finished computing.
    /// </summary>
    public static void Apply(
        Text hint, bool requirementsMet, float targetDist, float noEscapeRange, float maxRange)
    {
        // Liveness, recorded whether or not there is anything to show: it means
        // the missile UI is still driving us.
        lastRefresh = Time.unscaledTime;

        if (!requirementsMet || !PlayerSettings.hudWeapons)
        {
            // The game's own rejection reason (OUT OF RANGE, TOO CLOSE, OUT OF
            // ARC, TOO SLOW) is already correct; leave it alone.
            Clear();
            drivenHint = null;
            return;
        }

        bool inNoEscape = targetDist < noEscapeRange;
        Color colour = inNoEscape ? Shoot : InRange;
        string label = inNoEscape ? "SHOOT" : "IN RANGE";

        if (hint != null)
        {
            hint.text = label;
            hint.color = colour;
            // The one line that matters: show the cue across the whole valid
            // envelope, not just the no-escape slice of it.
            hint.enabled = true;
            drivenHint = hint;
        }

        if (!registered)
            return;

        Text readout = Overlay.Element(ElementLayout.Elements.ShootCue);
        if (readout == null)
            return;

        float alpha = 1f;
        if (inNoEscape)
            alpha = Mathf.Repeat(Time.unscaledTime * ShootFlashHz, 1f) < 0.5f ? 1f : 0.45f;

        Color tinted = colour;
        tinted.a = alpha;
        readout.color = tinted;
        readout.text =
            $"{label}   {UnitConverter.DistanceReading(targetDist)} / "
            + $"RMAX {UnitConverter.DistanceReading(maxRange)}"
            + (noEscapeRange < maxRange * 0.9f
                ? $" / NEZ {UnitConverter.DistanceReading(noEscapeRange)}"
                : string.Empty);
    }
}

[HarmonyPatch(typeof(HUDMissileState), "DisplayText")]
internal static class ShootCuePatch
{
    private static void Postfix(
        bool ___hidden,
        bool ___allRequirementsMet,
        float ___maxTargetDist,
        float ___noEscapeRange,
        float ___maxRange,
        Text ___hint)
    {
        if (!Plugin.ShootCue.Value)
            return;

        // Nothing selected or no ammo: the panel is hidden and must stay that way.
        if (___hidden)
        {
            ShootCue.Clear();
            return;
        }

        ShootCue.Apply(___hint, ___allRequirementsMet, ___maxTargetDist, ___noEscapeRange, ___maxRange);
    }
}
