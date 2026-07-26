using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// Extends the game's own missile threat list entries with the countermeasure
/// that defeats the seeker and the time to impact:
///
///     Missile [IR] 2.1km  FLARE  4.2s
///
/// The game already draws one line per inbound missile, coloured yellow while
/// the seeker searches and flashing red once it locks. Appending to that line
/// inherits the font, the colour and the flash for free -- the same trick the
/// target data block uses on the selected target readout.
/// </summary>
[HarmonyPatch(typeof(ThreatItem), nameof(ThreatItem.AnimateItem))]
internal static class ThreatItemHintPatch
{
    /// <summary>
    /// Seeker ids as reported by <c>Missile.GetSeekerType()</c>, mapped to the
    /// countermeasure that answers them. ARH and SARH are both "radar" to the
    /// pilot: the answer to either is to notch.
    /// </summary>
    private static readonly Dictionary<string, string> Answers = new Dictionary<string, string>
    {
        ["IR"] = "FLARE",
        ["ARH"] = "NOTCH",
        ["SARH"] = "NOTCH",
        ["Optical"] = "HIDE",
        ["ARAD"] = "RADAR OFF",
    };

    /// <summary>
    /// Below this closure rate the missile is not actually gaining on us and a
    /// countdown would be a fiction.
    /// </summary>
    private const float MinClosureForCountdown = 10f;

    private static void Postfix(ThreatItem __instance, Missile ___missile, Text ___text)
    {
        bool wantHint = Plugin.MissileDefeatHint.Value;
        bool wantTime = Plugin.ImpactCountdown.Value;
        if (!wantHint && !wantTime)
            return;
        if (___missile == null || ___text == null)
            return;
        // AnimateItem deactivates itself until the map icons exist; nothing was
        // written for us to extend.
        if (!__instance.gameObject.activeInHierarchy)
            return;

        if (wantHint)
        {
            // An unknown seeker type (a new game version, say) still gets a
            // generic answer rather than nothing.
            string seeker = ___missile.GetSeekerType();
            if (!Answers.TryGetValue(seeker, out string answer))
                answer = "DEFEND";
            ___text.text += "  " + answer;
        }

        if (!wantTime)
            return;

        CombatHUD hud = SceneSingleton<CombatHUD>.i;
        Aircraft aircraft = hud != null ? hud.aircraft : null;
        if (aircraft == null || aircraft.rb == null || ___missile.rb == null)
            return;

        // The same closure maths the game's own AI pilot uses to decide when to
        // break and dispense, so the number agrees with what the AI would do in
        // this seat.
        Vector3 separation = ___missile.transform.position - aircraft.transform.position;
        float distance = separation.magnitude;
        float closure = Vector3.Dot(-separation.normalized, ___missile.rb.velocity - aircraft.rb.velocity);
        if (closure >= MinClosureForCountdown)
            ___text.text += $"  {distance / closure:F1}s";
    }
}
