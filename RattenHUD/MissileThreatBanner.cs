using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// The silent twin of Bitching Ratte's missile callouts: a flashing banner
/// naming the countermeasure for every inbound seeker type, plus a live
/// time-to-impact countdown for the closest one.
///
/// Both plugins can run together. This one reads the game's own
/// <see cref="MissileWarning.knownMissiles"/> list rather than keeping its own
/// tally, so there is no state to drift out of sync with the game and nothing
/// to reset when the player changes aircraft.
/// </summary>
internal static class MissileThreatBanner
{
    /// <summary>
    /// Seeker ids as reported by <c>MissileSeeker.GetSeekerType()</c>, mapped to
    /// the banner text and colour. ARH and SARH are both "radar" to the pilot:
    /// the answer to either is to notch.
    /// </summary>
    private readonly struct Threat
    {
        public readonly string Label;
        public readonly string Answer;
        public readonly Color Colour;
        public readonly int Priority;

        public Threat(string label, string answer, Color colour, int priority)
        {
            Label = label;
            Answer = answer;
            Colour = colour;
            Priority = priority;
        }
    }

    private static readonly Dictionary<string, Threat> Threats = new Dictionary<string, Threat>
    {
        ["IR"] = new Threat("IR", "FLARE", new Color(1f, 0.55f, 0.1f), 1),
        ["ARH"] = new Threat("RADAR", "NOTCH", new Color(1f, 0.25f, 0.25f), 0),
        ["SARH"] = new Threat("RADAR", "NOTCH", new Color(1f, 0.25f, 0.25f), 0),
        ["Optical"] = new Threat("OPTICAL", "HIDE", new Color(0.85f, 0.92f, 1f), 2),
        ["ARAD"] = new Threat("ARAD", "RADAR OFF", new Color(1f, 0.35f, 0.9f), 3),
    };

    // Flash frequency ramp. Far away it is a slow pulse you can ignore; inside
    // a few seconds it is an urgent strobe.
    private const float SlowFlashHz = 2f;
    private const float FastFlashHz = 10f;
    private const float SlowFlashSeconds = 15f;
    private const float FastFlashSeconds = 2f;

    // Below this closure rate the missile is not actually gaining on us and a
    // countdown would be a fiction.
    private const float MinClosureForCountdown = 10f;

    private static bool registered;

    // Reused every frame so the readout does not allocate a fresh buffer at
    // exactly the moment the player can least afford a hitch.
    private static readonly System.Text.StringBuilder Builder = new System.Text.StringBuilder(256);
    private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
    private static readonly List<string> Ordered = new List<string>();

    public static void Initialize()
    {
        if (!Plugin.MissileBanner.Value)
            return;

        Overlay.Register(
            ElementLayout.Elements.MissileBanner,
            anchor: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            offset: new Vector2(0f, 260f),
            fontScale: 1.2f,
            TextAnchor.MiddleCenter);

        Overlay.Register(
            ElementLayout.Elements.ImpactCountdown,
            anchor: new Vector2(0.5f, 0.5f),
            pivot: new Vector2(0.5f, 0.5f),
            offset: new Vector2(0f, 220f),
            fontScale: 1f,
            TextAnchor.MiddleCenter);

        registered = true;
    }

    public static void Tick()
    {
        if (!registered)
            return;

        if (!Plugin.MissileBanner.Value || !Overlay.InCockpit)
        {
            Clear();
            return;
        }

        Aircraft aircraft = Overlay.PlayerAircraft;
        MissileWarning warning = aircraft.GetMissileWarningSystem();
        if (warning == null || warning.knownMissiles.Count == 0)
        {
            Clear();
            return;
        }

        Counts.Clear();
        Ordered.Clear();

        Missile closest = null;
        float closestTime = float.MaxValue;
        float closestClosure = 0f;

        foreach (Missile missile in warning.knownMissiles)
        {
            if (missile == null)
                continue;

            string seeker = missile.GetSeekerType();
            // Unknown seeker types (a new game version, say) still count as a
            // threat; they fall through to a generic line rather than vanishing.
            if (!Counts.ContainsKey(seeker))
            {
                Counts[seeker] = 0;
                Ordered.Add(seeker);
            }
            Counts[seeker]++;

            float time = TimeToImpact(aircraft, missile, out float closure);
            if (time < closestTime)
            {
                closestTime = time;
                closest = missile;
                closestClosure = closure;
            }
        }

        if (closest == null)
        {
            Clear();
            return;
        }

        // Most urgent first: soonest impact drives the flash, and the line
        // ordering should agree with it.
        Ordered.Sort(ComparePriority);

        float alpha = FlashAlpha(closestTime);
        Builder.Length = 0;
        for (int i = 0; i < Ordered.Count; i++)
        {
            string seeker = Ordered[i];
            if (i > 0)
                Builder.Append('\n');
            AppendLine(seeker, Counts[seeker], alpha);
        }

        Text banner = Overlay.Element(ElementLayout.Elements.MissileBanner);
        Text countdown = Overlay.Element(ElementLayout.Elements.ImpactCountdown);
        if (banner == null || countdown == null)
            return;

        banner.text = Builder.ToString();

        if (Plugin.ImpactCountdown.Value && closestClosure >= MinClosureForCountdown)
        {
            Color urgency = Color.Lerp(
                new Color(1f, 0.25f, 0.25f),
                new Color(1f, 0.85f, 0.2f),
                Mathf.InverseLerp(FastFlashSeconds, SlowFlashSeconds, closestTime));
            urgency.a = alpha;
            countdown.color = urgency;
            countdown.text = $"IMPACT {closestTime:F1}s";
        }
        else
        {
            countdown.text = string.Empty;
        }
    }

    private static void AppendLine(string seeker, int count, float alpha)
    {
        Color colour;
        string label;
        string answer;

        if (Threats.TryGetValue(seeker, out Threat threat))
        {
            colour = threat.Colour;
            label = threat.Label;
            answer = threat.Answer;
        }
        else
        {
            colour = new Color(1f, 0.85f, 0.2f);
            label = string.IsNullOrEmpty(seeker) ? "UNKNOWN" : seeker.ToUpperInvariant();
            answer = "DEFEND";
        }

        // Per-line colour needs rich text; the flash rides in the alpha channel
        // so that every line pulses together off the closest missile's clock.
        //
        // ASCII only. The HUD font is the game's own and has no reason to carry
        // an interpunct, an arrow or a multiplication sign; a missing glyph on a
        // missile warning is the worst possible place to find out.
        Builder.Append("<color=#").Append(ToHex(colour, alpha)).Append('>');
        Builder.Append("MISSILE ").Append(label).Append("  ").Append(answer);
        if (count > 1)
            Builder.Append(" X").Append(count);
        Builder.Append("</color>");
    }

    private static int ComparePriority(string a, string b)
    {
        int pa = Threats.TryGetValue(a, out Threat ta) ? ta.Priority : int.MaxValue;
        int pb = Threats.TryGetValue(b, out Threat tb) ? tb.Priority : int.MaxValue;
        return pa.CompareTo(pb);
    }

    /// <summary>
    /// Mirrors the closure maths the game's own AI pilot uses to decide when to
    /// break and pop countermeasures, so the number on the HUD agrees with what
    /// the AI would do in the same seat.
    /// </summary>
    private static float TimeToImpact(Aircraft aircraft, Missile missile, out float closure)
    {
        Vector3 separation = missile.transform.position - aircraft.transform.position;
        float distance = separation.magnitude;
        closure = Vector3.Dot(-separation.normalized, missile.rb.velocity - aircraft.rb.velocity);
        return distance / Mathf.Max(closure, 1f);
    }

    private static float FlashAlpha(float secondsToImpact)
    {
        float t = Mathf.InverseLerp(SlowFlashSeconds, FastFlashSeconds, secondsToImpact);
        float hz = Mathf.Lerp(SlowFlashHz, FastFlashHz, t);
        // Square wave: a crisp blink reads faster in peripheral vision than a
        // sine fade does.
        bool on = Mathf.Repeat(Time.unscaledTime * hz, 1f) < 0.5f;
        return on ? 1f : 0.15f;
    }

    private static string ToHex(Color colour, float alpha) =>
        ColorUtility.ToHtmlStringRGB(colour) + Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f).ToString("X2");

    private static void Clear()
    {
        // Peek, not Element: blanking an empty readout must not resurrect the
        // canvas on every frame the player spends outside a cockpit.
        Text banner = Overlay.Peek(ElementLayout.Elements.MissileBanner);
        Text countdown = Overlay.Peek(ElementLayout.Elements.ImpactCountdown);

        if (banner != null && banner.text.Length > 0)
            banner.text = string.Empty;
        if (countdown != null && countdown.text.Length > 0)
            countdown.text = string.Empty;
    }
}
