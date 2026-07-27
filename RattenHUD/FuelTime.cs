using HarmonyLib;
using UnityEngine;
using TMPro;

namespace RattenHUD;

/// <summary>
/// Shows the estimated remaining fuel time next to the fuel gauge. The estimate
/// comes from the fuel burned between samples, so it tracks the current throttle
/// setting rather than a fixed rate. The matching voice callouts ("fuel low",
/// "bingo fuel") live in the separate Bitching Ratte plugin.
///
/// Ported from MKMods, which has been retired in favour of the standalone
/// plugins; the readout is a HUD element, so it lives here now.
/// </summary>
internal static class FuelTime
{
    private const string LabelName = "RattenHUDFuelTime";
    private const float LabelCreationDelaySeconds = 3f;
    private const float LabelVerticalOffset = -20f;

    private static float sampleIntervalSeconds;

    private static TextMeshProUGUI fuelTimeLabel;
    private static float labelReadyTime = float.PositiveInfinity;
    private static float lastFuelLevel;
    private static float lastSampleTime;

    public static void Initialize()
    {
        if (!Plugin.On(Plugin.FuelTimeReadout))
            return;
        sampleIntervalSeconds = Plugin.FuelTimeUpdateRate.Value;
    }

    public static void OnGaugeInitialized(Aircraft aircraft)
    {
        fuelTimeLabel = null;
        lastFuelLevel = aircraft.GetFuelLevel();
        lastSampleTime = Time.timeSinceLevelLoad;
        // Give the HUD a moment to finish its own layout before cloning a label.
        labelReadyTime = Time.timeSinceLevelLoad + LabelCreationDelaySeconds;
    }

    public static void OnGaugeRefreshed(FuelGauge gauge, Aircraft aircraft, TextMeshProUGUI fuelLabel)
    {
        if (aircraft == null)
            return;
        if (fuelTimeLabel == null && !TryCreateLabel(gauge, fuelLabel))
            return;

        float elapsed = Time.timeSinceLevelLoad - lastSampleTime;
        if (elapsed < sampleIntervalSeconds)
            return;

        float fuelLevel = aircraft.GetFuelLevel();
        float burnPerSecond = (lastFuelLevel - fuelLevel) / elapsed;
        lastSampleTime = Time.timeSinceLevelLoad;
        lastFuelLevel = fuelLevel;

        float secondsRemaining = burnPerSecond > 0f ? fuelLevel / burnPerSecond : float.PositiveInfinity;
        fuelTimeLabel.text = float.IsInfinity(secondsRemaining)
            ? "(...)"
            : $"({Mathf.FloorToInt(secondsRemaining / 60f)}m)";
    }

    private static bool TryCreateLabel(FuelGauge gauge, TextMeshProUGUI fuelLabel)
    {
        if (Time.timeSinceLevelLoad < labelReadyTime || fuelLabel == null)
            return false;

        Transform existing = gauge.transform.Find(LabelName);
        GameObject labelObject;
        if (existing != null)
        {
            labelObject = existing.gameObject;
        }
        else
        {
            labelObject = Object.Instantiate(fuelLabel.gameObject, fuelLabel.transform.parent);
            labelObject.name = LabelName;
            var rectTransform = labelObject.GetComponent<RectTransform>();
            rectTransform.anchoredPosition += new Vector2(0f, LabelVerticalOffset);
        }

        fuelTimeLabel = labelObject.GetComponent<TextMeshProUGUI>();
        if (fuelTimeLabel == null)
        {
            Plugin.Logger.LogError("Fuel time label has no Text component; fuel readout disabled.");
            labelReadyTime = float.PositiveInfinity;
            return false;
        }

        fuelTimeLabel.text = "(...)";
        return true;
    }
}

[HarmonyPatch(typeof(FuelGauge))]
internal static class FuelGaugePatches
{
    [HarmonyPostfix]
    [HarmonyPatch("Initialize")]
    private static void Initialize(Aircraft aircraft)
    {
        if (Plugin.On(Plugin.FuelTimeReadout) && aircraft != null)
            FuelTime.OnGaugeInitialized(aircraft);
    }

    [HarmonyPostfix]
    [HarmonyPatch("Refresh")]
    private static void Refresh(FuelGauge __instance, Aircraft ___aircraft, TextMeshProUGUI ___fuelLabel)
    {
        if (Plugin.On(Plugin.FuelTimeReadout))
            FuelTime.OnGaugeRefreshed(__instance, ___aircraft, ___fuelLabel);
    }
}
