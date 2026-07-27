using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using TMPro;

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
        TextMeshProUGUI ___targetInfo)
    {
        // The game returns false when there is nothing to show, and in that
        // case has not written a readout for us to extend.
        if (!__result || !Plugin.On(Plugin.TargetDataBlock))
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

        // Unit.rb is null for anything without a rigidbody -- structures, static
        // SAM sites, radars. Those are genuinely stationary, so read them as zero
        // velocity rather than dropping the readout: closure off our own motion
        // is still exactly what the pilot wants against a building.
        Vector3 targetVelocity = target.rb != null ? target.rb.velocity : Vector3.zero;

        // Positive closure means the gap is shrinking.
        float closure = Vector3.Dot(losDirection, ___aircraft.rb.velocity - targetVelocity);

        // Aspect: 0 degrees is the target pointing straight at us (hot), 180 is
        // straight away (cold). Measured from the target's nose to the reciprocal
        // of the line of sight.
        Vector3 targetHeading = targetVelocity.sqrMagnitude > 1f
            ? targetVelocity.normalized
            : target.transform.forward;
        float aspect = Vector3.Angle(targetHeading, -losDirection);

        string aspectTag = aspect < 45f ? "HOT" : aspect > 135f ? "COLD" : "BEAM";
        char trend = closure >= 0f ? '▼' : '▲';

        // One compact line at a reduced size, not a stacked block. This text is
        // anchored to the target marker, so every extra line pushes the readout
        // further over whatever else is on the glass -- and with several targets
        // selected there are several markers competing for the same space.
        int size = Mathf.Max(8, Mathf.RoundToInt(___targetInfo.fontSize * 0.7f));
        ___targetInfo.text +=
            $"\n<size={size}>{trend}{UnitConverter.SpeedReading(Mathf.Abs(closure))}"
            + $"  {aspectTag} {aspect:F0}°"
            + $"  {UnitConverter.AltitudeReading(knownPosition.y)}</size>";
    }
}
