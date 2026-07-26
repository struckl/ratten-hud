using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// The stock radar warning receiver draws an undifferentiated arrow per
/// emitter: you can see that something is painting you but not what, nor
/// whether it has progressed from sweeping to tracking to shooting.
///
/// This keeps a small table of emitters seen recently and renders it as a
/// strobe list, tagged SEARCH / LOCK / LAUNCH and named by unit code.
/// </summary>
internal static class RadarWarningTags
{
    /// <summary>How long an emitter stays listed after its last sweep. Matches
    /// the lifetime the game gives its own directional warning icons.</summary>
    private const float ContactLifetime = 4f;
    private const float LaunchFlashHz = 5f;

    private static readonly Color Search = new Color(0.55f, 0.95f, 0.55f);
    private static readonly Color Lock = new Color(1f, 0.75f, 0.15f);
    private static readonly Color Launch = new Color(1f, 0.25f, 0.25f);

    private sealed class Contact
    {
        public Unit Emitter;
        public bool IsTarget;
        public float LastSeen;
    }

    private static readonly Dictionary<Unit, Contact> Contacts = new Dictionary<Unit, Contact>();
    private static readonly List<Unit> Expired = new List<Unit>();
    private static readonly List<Contact> Sorted = new List<Contact>();
    private static readonly System.Text.StringBuilder Builder = new System.Text.StringBuilder(256);

    private static Text readout;

    public static void Initialize()
    {
        if (!Plugin.RadarTags.Value)
            return;

        readout = Overlay.CreateText(
            ElementLayout.Elements.RadarWarnings,
            anchor: new Vector2(0f, 0.5f),
            offset: new Vector2(230f, 0f),
            fontSize: 20,
            TextAnchor.MiddleLeft);
    }

    /// <summary>Records a sweep. Called from the radar warning patch below.</summary>
    public static void OnSweep(Unit emitter, bool isTarget)
    {
        if (emitter == null)
            return;

        if (!Contacts.TryGetValue(emitter, out Contact contact))
        {
            contact = new Contact { Emitter = emitter };
            Contacts[emitter] = contact;
        }
        contact.IsTarget = isTarget;
        contact.LastSeen = Time.timeSinceLevelLoad;
    }

    public static void Reset() => Contacts.Clear();

    public static void Tick()
    {
        if (readout == null)
            return;

        if (!Plugin.RadarTags.Value || !Overlay.InCockpit)
        {
            Clear();
            return;
        }

        float now = Time.timeSinceLevelLoad;
        Expired.Clear();
        foreach (KeyValuePair<Unit, Contact> pair in Contacts)
        {
            if (pair.Key == null || now - pair.Value.LastSeen > ContactLifetime)
                Expired.Add(pair.Key);
        }
        foreach (Unit stale in Expired)
            Contacts.Remove(stale);

        if (Contacts.Count == 0)
        {
            Clear();
            return;
        }

        Aircraft aircraft = Overlay.PlayerAircraft;
        MissileWarning warning = aircraft.GetMissileWarningSystem();

        Sorted.Clear();
        foreach (Contact contact in Contacts.Values)
            Sorted.Add(contact);
        // Shooters first, then trackers, then everything merely sweeping.
        Sorted.Sort((a, b) => Rank(b, warning).CompareTo(Rank(a, warning)));

        Builder.Length = 0;
        for (int i = 0; i < Sorted.Count; i++)
        {
            Contact contact = Sorted[i];
            if (i > 0)
                Builder.Append('\n');

            bool launched = HasLaunched(contact.Emitter, warning);
            string tag = launched ? "LAUNCH" : contact.IsTarget ? "LOCK" : "SEARCH";
            Color colour = launched ? Launch : contact.IsTarget ? Lock : Search;

            float alpha = 1f;
            if (launched)
                alpha = Mathf.Repeat(Time.unscaledTime * LaunchFlashHz, 1f) < 0.5f ? 1f : 0.25f;

            Builder.Append("<color=#")
                   .Append(ColorUtility.ToHtmlStringRGB(colour))
                   .Append(Mathf.RoundToInt(alpha * 255f).ToString("X2"))
                   .Append('>')
                   .Append(tag.PadRight(7))
                   .Append(EmitterName(contact.Emitter))
                   .Append("</color>");
        }

        readout.text = Builder.ToString();
    }

    private static int Rank(Contact contact, MissileWarning warning)
    {
        if (HasLaunched(contact.Emitter, warning))
            return 2;
        return contact.IsTarget ? 1 : 0;
    }

    /// <summary>True if any missile currently inbound was fired by this emitter.</summary>
    private static bool HasLaunched(Unit emitter, MissileWarning warning)
    {
        if (warning == null || emitter == null)
            return false;

        foreach (Missile missile in warning.knownMissiles)
        {
            if (missile != null && missile.owner == emitter)
                return true;
        }
        return false;
    }

    private static string EmitterName(Unit emitter)
    {
        if (emitter == null)
            return "UNKNOWN";
        // definition.code is the short type code the game already uses for the
        // selected target readout, e.g. the airframe or SAM designation.
        return emitter.definition != null && !string.IsNullOrEmpty(emitter.definition.code)
            ? emitter.definition.code
            : emitter.name;
    }

    private static void Clear()
    {
        if (readout != null && readout.text.Length > 0)
            readout.text = string.Empty;
    }
}

/// <summary>
/// Taps the radar warning receiver's own event handler so the tag table sees
/// exactly the sweeps the game decided were worth warning about.
/// </summary>
[HarmonyPatch(typeof(RadarWarning), "RadarWarning_OnRadarWarning")]
internal static class RadarWarningSweepPatch
{
    private static void Prefix(Aircraft.OnRadarWarning radarSource)
    {
        if (Plugin.RadarTags.Value && radarSource.detected)
            RadarWarningTags.OnSweep(radarSource.emitter, radarSource.isTarget);
    }
}

/// <summary>
/// Suppresses the stock directional arrows, for players who would rather read
/// the tagged list than the undifferentiated wedges.
///
/// This gates icon creation only. The sweep itself still runs, so the warning
/// tone and the tag list above are untouched -- unlike disabling the whole
/// receiver, which would take the audio with it.
/// </summary>
[HarmonyPatch(typeof(RadarWarning), "ShowDirectionalWarning")]
internal static class StockRadarWarningIconPatch
{
    private static bool Prefix() => !Plugin.HideStockRadarWarning.Value;
}
