using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// One threat block on the glass, drawn like the fuel time and climb rate
/// readouts: a clone of a real HUD label, in the HUD font and the player's HUD
/// colour, sitting inside the projected HUD rather than over the map.
///
///     Missile [IR] 2.0km  FLARE  2.3s
///     Missile [ARH] 5.2km  NOTCH  9.4s
///     Radar [MIG-29] LOCK
///     Radar [RDR] SEARCH
///
/// Missiles come first, soonest impact first, each with the countermeasure
/// that defeats its seeker and a live time to impact. Radar emitters follow,
/// shooters before trackers before sweeps. The game's own threat list over the
/// map is left exactly as it is -- this is the same information said where the
/// pilot is actually looking.
/// </summary>
internal static class ThreatReadout
{
    private const string ElementName = "Threats";

    /// <summary>How long an emitter stays listed after its last sweep. Matches
    /// the lifetime the game gives its own directional warning icons.</summary>
    private const float ContactLifetime = 4f;

    /// <summary>
    /// Below this closure rate the missile is not actually gaining on us and a
    /// countdown would be a fiction.
    /// </summary>
    private const float MinClosureForCountdown = 10f;

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

    private sealed class Contact
    {
        public Unit Emitter;
        public bool IsTarget;
        public float LastSeen;
    }

    private struct Inbound
    {
        public Missile Missile;
        public float Distance;
        public float TimeToImpact;
        public bool Gaining;
    }

    private static readonly Dictionary<Unit, Contact> Contacts = new Dictionary<Unit, Contact>();
    private static readonly List<Unit> Expired = new List<Unit>();
    private static readonly List<Contact> SortedContacts = new List<Contact>();
    private static readonly List<Inbound> Inbounds = new List<Inbound>();

    // Reused every frame so the readout does not allocate a fresh buffer at
    // exactly the moment the player can least afford a hitch.
    private static readonly System.Text.StringBuilder Builder = new System.Text.StringBuilder(256);

    public static void Initialize()
    {
        // On the glass with the flight readouts: the right-hand column, a line
        // of space under the altitude block (which the default layout table
        // pulls up under the climb rate), stacking downwards. Movable via the
        // layout table as "Threats".
        Overlay.Register(
            ElementName,
            anchor: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0f, 1f),
            offset: new Vector2(197f, -22f),
            fontScale: 1f,
            TextAnchor.UpperLeft);
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
        bool wantMissiles = Plugin.MissileDefeatHint.Value || Plugin.ImpactCountdown.Value;
        bool wantRadars = Plugin.RadarTags.Value;

        if ((!wantMissiles && !wantRadars) || !Overlay.InCockpit)
        {
            Contacts.Clear();
            Clear();
            return;
        }

        Aircraft aircraft = Overlay.PlayerAircraft;
        MissileWarning warning = aircraft.GetMissileWarningSystem();

        Builder.Length = 0;

        if (wantMissiles && warning != null)
            AppendMissiles(aircraft, warning);
        if (wantRadars)
            AppendRadars(warning);

        if (Builder.Length == 0)
        {
            Clear();
            return;
        }

        Text readout = Overlay.Element(ElementName);
        if (readout != null)
            readout.text = Builder.ToString();
    }

    private static void AppendMissiles(Aircraft aircraft, MissileWarning warning)
    {
        Inbounds.Clear();
        foreach (Missile missile in warning.knownMissiles)
        {
            if (missile == null || missile.rb == null)
                continue;

            // The same closure maths the game's own AI pilot uses to decide
            // when to break and dispense, so the number agrees with what the AI
            // would do in this seat.
            Vector3 separation = missile.transform.position - aircraft.transform.position;
            float distance = separation.magnitude;
            float closure = Vector3.Dot(-separation.normalized, missile.rb.velocity - aircraft.rb.velocity);

            Inbounds.Add(new Inbound
            {
                Missile = missile,
                Distance = distance,
                TimeToImpact = distance / Mathf.Max(closure, 1f),
                Gaining = closure >= MinClosureForCountdown,
            });
        }

        // Soonest impact first; anything not actually gaining sorts last.
        Inbounds.Sort(CompareInbound);

        foreach (Inbound inbound in Inbounds)
        {
            if (Builder.Length > 0)
                Builder.Append('\n');

            Builder.Append("Missile [")
                   .Append(inbound.Missile.GetSeekerType())
                   .Append("] ")
                   .Append(UnitConverter.DistanceReading(inbound.Distance));

            if (Plugin.MissileDefeatHint.Value)
            {
                // An unknown seeker type (a new game version, say) still gets a
                // generic answer rather than nothing.
                if (!Answers.TryGetValue(inbound.Missile.GetSeekerType(), out string answer))
                    answer = "DEFEND";
                Builder.Append("  ").Append(answer);
            }

            if (Plugin.ImpactCountdown.Value && inbound.Gaining)
                Builder.Append("  ").Append(inbound.TimeToImpact.ToString("F1")).Append('s');
        }
    }

    private static int CompareInbound(Inbound a, Inbound b)
    {
        if (a.Gaining != b.Gaining)
            return a.Gaining ? -1 : 1;
        return a.TimeToImpact.CompareTo(b.TimeToImpact);
    }

    private static void AppendRadars(MissileWarning warning)
    {
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
            return;

        SortedContacts.Clear();
        foreach (Contact contact in Contacts.Values)
            SortedContacts.Add(contact);
        // Shooters first, then trackers, then everything merely sweeping.
        SortedContacts.Sort((a, b) => Rank(b, warning).CompareTo(Rank(a, warning)));

        foreach (Contact contact in SortedContacts)
        {
            if (Builder.Length > 0)
                Builder.Append('\n');

            string tag = HasLaunched(contact.Emitter, warning) ? "LAUNCH"
                : contact.IsTarget ? "LOCK"
                : "SEARCH";
            Builder.Append("Radar [").Append(EmitterName(contact.Emitter)).Append("] ").Append(tag);
        }
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
        // Peek, not Element: blanking an empty readout must not build one.
        Text readout = Overlay.Peek(ElementName);
        if (readout != null && readout.text.Length > 0)
            readout.text = string.Empty;
    }
}

/// <summary>
/// Taps the radar warning receiver's own event handler so the contact table
/// sees exactly the sweeps the game decided were worth warning about.
/// </summary>
[HarmonyPatch(typeof(RadarWarning), "RadarWarning_OnRadarWarning")]
internal static class RadarWarningSweepPatch
{
    private static void Prefix(Aircraft.OnRadarWarning radarSource)
    {
        if (Plugin.RadarTags.Value && radarSource.detected)
            ThreatReadout.OnSweep(radarSource.emitter, radarSource.isTarget);
    }
}

/// <summary>
/// Suppresses the stock directional arrows, for players who would rather read
/// the tagged list than the undifferentiated wedges.
///
/// This gates icon creation only. The sweep itself still runs, so the warning
/// tone and the tags are untouched -- unlike disabling the whole receiver,
/// which would take the audio with it.
/// </summary>
[HarmonyPatch(typeof(RadarWarning), "ShowDirectionalWarning")]
internal static class StockRadarWarningIconPatch
{
    private static bool Prefix() => !Plugin.HideStockRadarWarning.Value;
}
