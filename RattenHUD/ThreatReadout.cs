using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// Two threat readouts on the glass, drawn like the fuel time and climb rate
/// readouts: clones of a real HUD label, in the HUD font, sitting inside the
/// projected HUD rather than over the map.
///
/// Missiles are a red block centred over the weapon hint -- where SHOOT and
/// OUT OF RANGE appear, which is where the pilot is looking when the answer
/// matters -- soonest impact nearest the hint, each line with the
/// countermeasure that defeats its seeker and a live time to impact:
///
///     [ARH] 5.2km  NOTCH  9.4s
///     [IR] 2.0km  FLARE  2.3s
///
/// Radar emitters list in the right-hand flight column under the altitude
/// block, shooters before trackers before sweeps. A sweep line carries the
/// number of times that emitter has painted us; a single ping is noise and is
/// not shown at all, a lock or a launch shows immediately:
///
///     [MIG-29] LOCK
///     [RDR] x3
///
/// No "Missile"/"Radar" prefixes: the bracketed code and the tag already say
/// it, and short lines read faster in combat. The game's own threat list over
/// the map is left exactly as it is -- this is the same information said where
/// the pilot is actually looking.
/// </summary>
internal static class ThreatReadout
{
    private const string MissilesElement = "Missiles";
    private const string RadarsElement = "Threats";

    /// <summary>
    /// How long a ping counts towards an emitter's tally. Search radars sweep
    /// slowly -- a scanner that finds you every eight seconds is still hunting
    /// you -- so this window must comfortably hold two of those sweeps, or the
    /// two-ping rule below filters slow scanners out entirely.
    /// </summary>
    private const float PingMemorySeconds = 20f;

    /// <summary>How long a search line stays up after its last ping.</summary>
    private const float SearchShowSeconds = 10f;

    /// <summary>
    /// How long a LOCK stays trusted after its last ping. A tracking radar
    /// paints its target continuously, so a lock that has not pinged for this
    /// long has in truth lost us; the line falls back to a search tally rather
    /// than claiming a track that is gone.
    /// </summary>
    private const float LockShowSeconds = 4f;

    /// <summary>A radar that paints us once and never again is noise; the list
    /// starts caring at the second ping inside <see cref="PingMemorySeconds"/>.</summary>
    private const int MinPingsToShow = 2;

    /// <summary>
    /// Below this closure rate the missile is not actually gaining on us and a
    /// countdown would be a fiction.
    /// </summary>
    private const float MinClosureForCountdown = 10f;

    private static readonly Color WarningRed = new Color(1f, 0.25f, 0.25f);

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
        public readonly List<float> PingTimes = new List<float>();

        public void Prune(float now)
        {
            while (PingTimes.Count > 0 && now - PingTimes[0] > PingMemorySeconds)
                PingTimes.RemoveAt(0);
        }
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
        // Centred over the weapon hint, growing upwards: the bottom line is
        // the fixed one, so the soonest impact always sits in the same spot
        // just above SHOOT / OUT OF RANGE. Movable as "Missiles".
        Overlay.Register(
            MissilesElement,
            anchor: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0f),
            offset: new Vector2(0f, 155f),
            fontScale: 1f,
            TextAnchor.LowerCenter);

        // The right-hand flight column, a line of space under the altitude
        // block (which the default layout table pulls up under the climb
        // rate), stacking downwards. Pinned by its right edge, just inside the
        // throttle column: emitter codes vary in length, and a left-pinned
        // line long enough ran off the edge of the glass. Growing left instead
        // costs nothing -- the space towards the centre is empty sky.
        // Movable as "Threats".
        Overlay.Register(
            RadarsElement,
            anchor: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(1f, 1f),
            offset: new Vector2(300f, -22f),
            fontScale: 1f,
            TextAnchor.UpperRight);
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
        contact.PingTimes.Add(contact.LastSeen);
    }

    public static void Tick()
    {
        bool wantMissiles = Plugin.On(Plugin.MissileDefeatHint) || Plugin.On(Plugin.ImpactCountdown);
        bool wantRadars = Plugin.On(Plugin.RadarTags);

        if ((!wantMissiles && !wantRadars) || !Overlay.InCockpit)
        {
            Contacts.Clear();
            Clear(MissilesElement);
            Clear(RadarsElement);
            return;
        }

        Aircraft aircraft = Overlay.PlayerAircraft;
        MissileWarning warning = aircraft.GetMissileWarningSystem();

        Builder.Length = 0;
        if (wantMissiles && warning != null)
            AppendMissiles(aircraft, warning);
        Write(MissilesElement, WarningRed);

        Builder.Length = 0;
        if (wantRadars)
            AppendRadars(warning);
        Write(RadarsElement, null);
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

        // Appended in reverse: the block is bottom-anchored and grows upwards,
        // so the last line written is the fixed one nearest the weapon hint --
        // and that line should be the soonest impact.
        for (int i = Inbounds.Count - 1; i >= 0; i--)
        {
            Inbound inbound = Inbounds[i];
            if (Builder.Length > 0)
                Builder.Append('\n');

            Builder.Append('[')
                   .Append(inbound.Missile.GetSeekerType())
                   .Append("] ")
                   .Append(UnitConverter.DistanceReading(inbound.Distance));

            if (Plugin.On(Plugin.MissileDefeatHint))
            {
                // An unknown seeker type (a new game version, say) still gets a
                // generic answer rather than nothing.
                if (!Answers.TryGetValue(inbound.Missile.GetSeekerType(), out string answer))
                    answer = "DEFEND";
                Builder.Append("  ").Append(answer);
            }

            if (Plugin.On(Plugin.ImpactCountdown) && inbound.Gaining)
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
            pair.Value.Prune(now);
            if (pair.Key == null || pair.Value.PingTimes.Count == 0)
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
            bool launched = HasLaunched(contact.Emitter, warning);
            float sinceLastPing = now - contact.LastSeen;
            bool locked = contact.IsTarget && sinceLastPing <= LockShowSeconds;

            if (Builder.Length > 0)
                Builder.Append('\n');

            if (launched)
            {
                Builder.Append('[').Append(EmitterName(contact.Emitter)).Append("] LAUNCH");
            }
            else if (locked)
            {
                Builder.Append('[').Append(EmitterName(contact.Emitter)).Append("] LOCK");
            }
            else if (contact.PingTimes.Count >= MinPingsToShow && sinceLastPing <= SearchShowSeconds)
            {
                // A sweep that happened once is noise; the tally starts at two
                // pings inside the memory window. A lock or a launch is never
                // noise, however fresh the contact.
                Builder.Append('[').Append(EmitterName(contact.Emitter)).Append("] x")
                       .Append(contact.PingTimes.Count);
            }
            else
            {
                // Nothing written for this contact after all.
                if (Builder.Length > 0)
                    Builder.Length -= 1;
            }
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

    private static void Write(string element, Color? colour)
    {
        if (Builder.Length == 0)
        {
            Clear(element);
            return;
        }

        Text readout = Overlay.Element(element);
        if (readout == null)
            return;

        readout.text = Builder.ToString();
        if (colour.HasValue)
            readout.color = colour.Value;
    }

    private static void Clear(string element)
    {
        // Peek, not Element: blanking an empty readout must not build one.
        Text readout = Overlay.Peek(element);
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
        if (Plugin.On(Plugin.RadarTags) && radarSource.detected)
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
    private static bool Prefix() => !Plugin.On(Plugin.HideStockRadarWarning);
}

/// <summary>
/// Blanks the game's own missile warning lines over the map, which the red
/// block on the HUD replaces.
///
/// Only the text component is switched off, and only after AnimateItem has run:
/// the same object drives the notch line on the map, the notch indicator on the
/// glass and the flash of the missile's own marker, and all of those stay. The
/// toggle works both ways mid-flight because the game rewrites the text every
/// frame anyway.
/// </summary>
[HarmonyPatch(typeof(ThreatItem), nameof(ThreatItem.AnimateItem))]
internal static class StockThreatListTextPatch
{
    private static void Postfix(Text ___text)
    {
        if (___text != null)
            ___text.enabled = !Plugin.On(Plugin.HideStockThreatList);
    }
}
