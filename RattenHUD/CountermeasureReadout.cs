using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// The stock countermeasure indicator is green until the moment it is empty,
/// then grey. That gives no warning while the load is draining. This repaints
/// it as a state of charge: green, amber, red, then a flashing empty.
/// </summary>
[HarmonyPatch(typeof(CountermeasureIndicator), nameof(CountermeasureIndicator.Refresh))]
internal static class CountermeasureReadoutPatch
{
    private const float AmberFraction = 0.5f;
    private const float RedFraction = 0.2f;
    private const float EmptyFlashHz = 4f;

    private static readonly Color Green = new Color(0.35f, 1f, 0.35f);
    private static readonly Color Amber = new Color(1f, 0.75f, 0.15f);
    private static readonly Color Red = new Color(1f, 0.3f, 0.3f);

    // The game exposes the current ammo but not the full load, and it differs
    // per airframe, so treat the highest count seen on this countermeasure as
    // full. Rearming past the old maximum simply raises the baseline.
    private static readonly Dictionary<int, int> FullLoad = new Dictionary<int, int>();

    private static void Postfix(
        CountermeasureIndicator __instance,
        Aircraft ___aircraft,
        Image ___counterImage,
        Text ___counterName,
        Text ___counterAmmo)
    {
        if (!Plugin.CountermeasureColours.Value || ___aircraft == null || !PlayerSettings.hudWeapons)
            return;

        Countermeasure active = ___aircraft.countermeasureManager.GetActiveCountermeasure();
        if (active == null)
            return;

        float fraction;
        bool empty;

        if (active is FlareEjector flares)
        {
            int ammo = flares.GetAmmo();
            int id = flares.GetInstanceID();
            if (!FullLoad.TryGetValue(id, out int full) || ammo > full)
            {
                full = Mathf.Max(ammo, 1);
                FullLoad[id] = full;
            }
            fraction = (float)ammo / full;
            empty = ammo <= 0;
            ___counterAmmo.text = $"{ammo}";
        }
        else
        {
            float charge = ___aircraft.GetPowerSupply().GetCharge();
            fraction = charge;
            empty = charge < 0.01f;
            ___counterAmmo.text = $"{100f * charge:F0}%";
        }

        Color colour;
        if (empty)
        {
            // Flash rather than grey out: an empty dispenser is the single most
            // important thing on this corner of the HUD.
            bool on = Mathf.Repeat(Time.unscaledTime * EmptyFlashHz, 1f) < 0.5f;
            colour = on ? Red : new Color(Red.r, Red.g, Red.b, 0.2f);
        }
        else if (fraction <= RedFraction)
        {
            colour = Red;
        }
        else if (fraction <= AmberFraction)
        {
            colour = Amber;
        }
        else
        {
            colour = Green;
        }

        if (___counterImage != null)
        {
            // The game disables the image when there is no countermeasure at
            // all; do not fight that, only recolour what is already shown.
            if (___counterImage.enabled)
                ___counterImage.color = colour;
        }
        ___counterName.color = colour;
        ___counterAmmo.color = colour;
    }
}
