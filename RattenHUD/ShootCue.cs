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
/// This fills that gap with an "IN RANGE" line in the game's own hint label,
/// exactly as the game styles it. Nothing else is touched: SHOOT inside the NEZ
/// is already the game's, the rejection reasons (OUT OF RANGE, TOO CLOSE, OUT
/// OF ARC, TOO SLOW) stay the game's, and the label's colour is never written,
/// so every cue renders in the stock HUD style.
/// </summary>
internal static class ShootCue
{
    /// <summary>
    /// How long the cue may go unrefreshed before it is assumed stale. The game
    /// recomputes the envelope every 0.1s while the missile UI is up, so
    /// anything past a few frames of that means the UI is gone.
    /// </summary>
    private const float StaleAfterSeconds = 0.35f;

    private static float lastRefresh = float.NegativeInfinity;

    // The game's own hint label while we are forcing it on, kept so a stale cue
    // can hand it back rather than leaving our text stranded on the HUD.
    private static Text drivenHint;

    /// <summary>
    /// Watchdog. The cue is written from a patch on the missile UI, and that
    /// patch simply stops running when the player selects a weapon that is not
    /// a missile -- guns, bombs. Without this a forced-on IN RANGE could stay
    /// lit for the rest of the sortie.
    /// </summary>
    public static void Tick()
    {
        if (Time.unscaledTime - lastRefresh <= StaleAfterSeconds)
            return;
        Release();
    }

    /// <summary>Hands the game's label back to the game.</summary>
    public static void Release()
    {
        if (drivenHint != null)
        {
            // The game's own DisplayText re-establishes the correct text and
            // enabled state the moment the missile UI runs again.
            drivenHint.enabled = false;
            drivenHint = null;
        }
    }

    /// <summary>
    /// Called from the <see cref="HUDMissileState"/> postfix with the envelope
    /// the game just finished computing.
    /// </summary>
    public static void Apply(Text hint, bool requirementsMet, float targetDist, float noEscapeRange)
    {
        // Liveness, recorded whether or not there is anything to show: it means
        // the missile UI is still driving us.
        lastRefresh = Time.unscaledTime;

        if (hint == null)
            return;

        if (!requirementsMet || !PlayerSettings.hudWeapons)
        {
            // The game's own rejection reason is already correct; leave it alone.
            drivenHint = null;
            return;
        }

        if (targetDist < noEscapeRange)
        {
            // Inside the NEZ the game already shows its own SHOOT.
            drivenHint = null;
            return;
        }

        // Between NEZ and Rmax: the game wrote "SHOOT" and then hid it. Show the
        // honest version of the same state instead.
        hint.text = "IN RANGE";
        hint.enabled = true;
        drivenHint = hint;
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
        Text ___hint)
    {
        if (!Plugin.ShootCue.Value)
            return;

        // Nothing selected or no ammo: the panel is hidden and must stay that way.
        if (___hidden)
        {
            ShootCue.Release();
            return;
        }

        ShootCue.Apply(___hint, ___allRequirementsMet, ___maxTargetDist, ___noEscapeRange);
    }
}
