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
/// This adds one line per emitter to the game's own threat list, cloned from
/// the same prefab the game uses for its missile entries, so the tags sit in
/// the list the pilot already watches and inherit its font and layout:
///
///     Radar [MIG-29] LOCK
///
/// The colours are the game's own threat vocabulary: yellow for a search
/// sweep, the red/green lock flash the missile entries use for a track, and a
/// red blink -- the missile warning light's cadence -- for an emitter with a
/// missile currently in the air.
/// </summary>
internal static class RadarWarningTags
{
    /// <summary>How long an emitter stays listed after its last sweep. Matches
    /// the lifetime the game gives its own directional warning icons.</summary>
    private const float ContactLifetime = 4f;

    private sealed class Contact
    {
        public Unit Emitter;
        public bool IsTarget;
        public float LastSeen;
    }

    private sealed class Row
    {
        public GameObject Root;
        public Text Text;
    }

    private static readonly Dictionary<Unit, Contact> Contacts = new Dictionary<Unit, Contact>();
    private static readonly List<Unit> Expired = new List<Unit>();
    private static readonly List<Contact> Sorted = new List<Contact>();
    private static readonly List<Row> Rows = new List<Row>();

    // The game's threat list, its per-threat prefab and the aircraft it serves,
    // captured from ThreatList.SetAircraft below.
    private static ThreatList threatList;
    private static GameObject rowPrefab;
    private static Aircraft aircraft;

    public static void OnThreatList(ThreatList list, Aircraft aircraftIn, GameObject prefab)
    {
        // A new seat: rows parented to the old list die with it, and contacts
        // swept against the old aircraft are meaningless for the new one.
        DestroyRows();
        Contacts.Clear();

        threatList = list;
        rowPrefab = prefab;
        aircraft = aircraftIn;
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

    public static void Tick()
    {
        if (!Plugin.RadarTags.Value)
        {
            HideRows();
            return;
        }

        if (threatList == null || aircraft == null || rowPrefab == null)
        {
            Contacts.Clear();
            HideRows();
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
            HideRows();
            return;
        }

        MissileWarning warning = aircraft.GetMissileWarningSystem();

        Sorted.Clear();
        foreach (Contact contact in Contacts.Values)
            Sorted.Add(contact);
        // Shooters first, then trackers, then everything merely sweeping.
        Sorted.Sort((a, b) => Rank(b, warning).CompareTo(Rank(a, warning)));

        for (int i = 0; i < Sorted.Count; i++)
        {
            Row row = RowAt(i);
            if (row == null)
                return;

            Contact contact = Sorted[i];
            bool launched = HasLaunched(contact.Emitter, warning);
            string tag = launched ? "LAUNCH" : contact.IsTarget ? "LOCK" : "SEARCH";

            row.Root.SetActive(true);
            row.Text.text = $"Radar [{EmitterName(contact.Emitter)}] {tag}";

            // The game's own threat colours: yellow while searched, the missile
            // entries' red/green flash for a track, and the missile warning
            // light's red blink for an emitter that has actually fired.
            if (launched)
            {
                row.Text.color = Color.red;
                row.Text.enabled = Mathf.Sin(Time.timeSinceLevelLoad * 20f) > 0f;
            }
            else
            {
                row.Text.enabled = true;
                row.Text.color = contact.IsTarget
                    ? Color.red + Color.green * Mathf.Sin(Time.realtimeSinceStartup * 20f)
                    : Color.yellow;
            }

            // Missiles are more urgent than the radars that fired them; keep
            // the tags below the game's own entries, in threat order.
            row.Root.transform.SetAsLastSibling();
        }

        for (int i = Sorted.Count; i < Rows.Count; i++)
        {
            if (Rows[i].Root != null)
                Rows[i].Root.SetActive(false);
        }
    }

    private static Row RowAt(int index)
    {
        // Drop rows destroyed behind our back (a scene change we did not see)
        // before indexing, or the pool hands out dead entries.
        for (int i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].Root == null)
                Rows.RemoveAt(i);
        }

        while (Rows.Count <= index)
        {
            GameObject clone = Object.Instantiate(rowPrefab, threatList.transform);
            clone.name = "RattenHUDRadarTag";

            // Whatever drove the original must not drive the copy: ThreatItem
            // would try to resolve a missile this row does not have.
            foreach (ThreatItem driver in clone.GetComponents<ThreatItem>())
                Object.Destroy(driver);

            Text text = clone.GetComponentInChildren<Text>(includeInactive: true);
            if (text == null)
            {
                Object.Destroy(clone);
                Plugin.Logger.LogError("Threat item prefab has no Text; radar warning tags disabled.");
                rowPrefab = null;
                return null;
            }

            Rows.Add(new Row { Root = clone, Text = text });
        }

        return Rows[index];
    }

    private static void HideRows()
    {
        foreach (Row row in Rows)
        {
            if (row.Root != null && row.Root.activeSelf)
                row.Root.SetActive(false);
        }
    }

    private static void DestroyRows()
    {
        foreach (Row row in Rows)
        {
            if (row.Root != null)
                Object.Destroy(row.Root);
        }
        Rows.Clear();
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
}

/// <summary>
/// Captures the game's threat list, its per-threat prefab and the aircraft it
/// serves, at the one moment the game wires all three together.
/// </summary>
[HarmonyPatch(typeof(ThreatList), nameof(ThreatList.SetAircraft))]
internal static class ThreatListCapturePatch
{
    private static void Postfix(ThreatList __instance, Aircraft aircraft, GameObject ___threatItemPrefab) =>
        RadarWarningTags.OnThreatList(__instance, aircraft, ___threatItemPrefab);
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
