using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// Extends the selected target readout from "who and how far" to the numbers
/// you actually need for an intercept: closure, aspect and altitude.
///
/// Range and identity are left to the game. Closure and aspect are derived from
/// the target's velocity, which is the same data the game's own launch envelope
/// code reads off the tracked unit.
/// </summary>
[HarmonyPatch(typeof(CombatHUD), "ShowTargetInfo")]
internal static class TargetDataBlockPatch
{
    private static void Postfix(
        bool __result,
        List<Unit> ___targetList,
        Aircraft ___aircraft,
        Text ___targetInfo)
    {
        // The game returns false when there is nothing to show, and in that
        // case has not written a readout for us to extend.
        if (!__result || !Plugin.TargetDataBlock.Value)
            return;
        if (___targetInfo == null || ___aircraft == null || ___targetList == null || ___targetList.Count == 0)
            return;

        Unit target = ___targetList[0];
        if (target == null)
            return;
        if (!___aircraft.NetworkHQ.TryGetKnownPosition(target, out GlobalPosition knownPosition))
            return;

        Vector3 lineOfSight = knownPosition - ___aircraft.GlobalPosition();
        if (lineOfSight.sqrMagnitude < 1f)
            return;

        Vector3 losDirection = lineOfSight.normalized;

        // Positive closure means the gap is shrinking.
        float closure = Vector3.Dot(losDirection, ___aircraft.rb.velocity - target.rb.velocity);

        // Aspect: 0 degrees is the target pointing straight at us (hot), 180 is
        // straight away (cold). Measured from the target's nose to the reciprocal
        // of the line of sight.
        Vector3 targetHeading = target.rb.velocity.sqrMagnitude > 1f
            ? target.rb.velocity.normalized
            : target.transform.forward;
        float aspect = Vector3.Angle(targetHeading, -losDirection);

        string aspectTag = aspect < 45f ? "HOT" : aspect > 135f ? "COLD" : "BEAM";

        ___targetInfo.text +=
            $"\n{UnitConverter.SpeedReading(Mathf.Abs(closure))} {(closure >= 0f ? "closing" : "opening")}"
            + $"\n{aspectTag} {aspect:F0}°"
            + $"\nANGELS {UnitConverter.AltitudeReading(knownPosition.y)}";
    }
}
