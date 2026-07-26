using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// Builds this plugin's readouts by cloning one of the game's own HUD labels and
/// parenting the copy into the HUD canvas.
///
/// The plugin used to draw onto a screen space canvas of its own with the Unity
/// built-in font. That was wrong twice over: the canvas did not survive the
/// first scene load, and even when it did the result was bold Arial floating
/// over the screen instead of sitting on the glass with everything else. Cloning
/// a live HUD label inherits the font, the player's HUD colour and text size,
/// the material and the projection for free -- the same trick the fuel time
/// readout has always used, which is why that one looked right from the start.
///
/// Elements are declared once with <see cref="Register"/> and fetched through
/// <see cref="Element"/> every frame rather than held by the caller, so a HUD
/// rebuilt by a scene load or an aircraft change is simply picked up again.
/// </summary>
internal static class Overlay
{
    /// <summary>Everything needed to rebuild one element from nothing.</summary>
    private sealed class Element_
    {
        public Vector2 Anchor;
        public Vector2 Pivot;
        public Vector2 Offset;
        public float FontScale;
        public TextAnchor Alignment;
        public Text Instance;
    }

    private static readonly Dictionary<string, Element_> Elements = new Dictionary<string, Element_>();
    private static readonly List<string> Order = new List<string>();

    // A live HUD label to clone, and the canvas it belongs to.
    private static Text template;
    private static Transform hudRoot;

    // The HUD currently flying the player, captured from the game's own aircraft
    // handoff rather than read off the scene singleton. See ActiveHud.
    private static CombatHUD tracked;

    private static bool loggedDiagnostics;
    private static int builds;

    /// <summary>
    /// Offered every HUD element on every settings refresh; keeps the first
    /// usable label as the clone source.
    ///
    /// Climbrate specifically, rather than whatever arrives first: it is a plain
    /// single line of HUD text present on every airframe, so the clone inherits
    /// ordinary body styling rather than something outsized or coloured for a
    /// warning.
    /// </summary>
    private static readonly System.Reflection.FieldInfo AppTypeField =
        HarmonyLib.AccessTools.Field(typeof(HUDApp), "type");

    public static void OfferTemplate(HUDApp app)
    {
        if (template != null || !(app is Climbrate))
            return;

        // Only the glass-projected flavour will do. The game also builds
        // screen-fixed HMD variants of its readouts; cloning one of those
        // would put every readout on the visor instead of the glass.
        if (AppTypeField.GetValue(app).ToString() != "HUD")
            return;

        Text found = app.GetComponentInChildren<Text>(includeInactive: true);
        if (found == null || found.canvas == null)
            return;

        template = found;
        hudRoot = found.canvas.transform;
    }

    /// <summary>
    /// Declares an element positioned within the HUD canvas, where (0.5, 0.5) is
    /// the centre and (0, 0) the bottom left. <paramref name="pivot"/> is which
    /// corner of the text the offset pins: left-aligned readouts want a left
    /// pivot, or the text runs backwards off the edge of the screen from its
    /// anchor.
    /// </summary>
    public static void Register(
        string name,
        Vector2 anchor,
        Vector2 pivot,
        Vector2 offset,
        float fontScale,
        TextAnchor alignment)
    {
        if (!Elements.ContainsKey(name))
            Order.Add(name);

        Elements[name] = new Element_
        {
            Anchor = anchor,
            Pivot = pivot,
            Offset = offset,
            FontScale = fontScale,
            Alignment = alignment,
            Instance = null,
        };
    }

    /// <summary>
    /// The live element, built or rebuilt as needed. Null until the HUD has
    /// offered a template, which is the same as saying null until there is a HUD
    /// to draw on.
    /// </summary>
    public static Text Element(string name)
    {
        if (!Elements.TryGetValue(name, out Element_ element))
            return null;

        if (element.Instance != null)
            return element.Instance;

        if (template == null || hudRoot == null)
            return null;

        element.Instance = Build(name, element);
        return element.Instance;
    }

    /// <summary>
    /// The element only if it already exists. Used by the clear paths, which must
    /// not build an element purely to blank it.
    /// </summary>
    public static Text Peek(string name) =>
        Elements.TryGetValue(name, out Element_ element) && element.Instance != null
            ? element.Instance
            : null;

    private static Text Build(string name, Element_ element)
    {
        GameObject clone = Object.Instantiate(template.gameObject, hudRoot);
        clone.name = name;
        clone.SetActive(true);

        // Whatever drove the original must not drive the copy.
        foreach (HUDApp driver in clone.GetComponents<HUDApp>())
            Object.Destroy(driver);

        Text text = clone.GetComponent<Text>();
        if (text == null)
        {
            Object.Destroy(clone);
            return null;
        }

        text.text = string.Empty;
        text.alignment = element.Alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = true;
        if (!Mathf.Approximately(element.FontScale, 1f))
            text.fontSize = Mathf.Max(1, Mathf.RoundToInt(text.fontSize * element.FontScale));

        RectTransform rect = text.rectTransform;
        rect.anchorMin = element.Anchor;
        rect.anchorMax = element.Anchor;
        rect.pivot = element.Pivot;
        // The template renders at its font size times every scale above it, and
        // the clone hangs directly under the canvas root, skipping that parent
        // chain. Bake the chain into the clone's own scale, or the copy draws
        // several times larger than the label it was cloned from.
        Vector3 templateScale = template.rectTransform.lossyScale;
        Vector3 rootScale = hudRoot.lossyScale;
        rect.localScale = new Vector3(
            rootScale.x != 0f ? templateScale.x / rootScale.x : 1f,
            rootScale.y != 0f ? templateScale.y / rootScale.y : 1f,
            rootScale.z != 0f ? templateScale.z / rootScale.z : 1f);
        rect.localRotation = Quaternion.identity;
        rect.anchoredPosition = element.Offset;

        builds++;
        // Recorded with the game's own elements, so the layout table can move
        // this readout too and a config reload re-applies immediately.
        ElementLayout.Apply(name, rect);
        return text;
    }

    public static void OnAircraftSet(CombatHUD hud)
    {
        tracked = hud;
        // A new seat means a freshly built HUD; sweep it once it settles.
        ElementLayout.RequestSweep();
    }

    public static void OnAircraftRemoved() => tracked = null;

    /// <summary>
    /// The HUD that is actually flying the player.
    ///
    /// Preferring the instance the game handed an aircraft to, over
    /// <c>SceneSingleton&lt;CombatHUD&gt;.i</c>, matters because the singleton is
    /// whichever CombatHUD awoke last -- which is not necessarily the one in the
    /// seat.
    /// </summary>
    private static CombatHUD ActiveHud
    {
        get
        {
            if (tracked != null && tracked.aircraft != null)
                return tracked;

            CombatHUD singleton = SceneSingleton<CombatHUD>.i;
            return singleton != null && singleton.aircraft != null ? singleton : null;
        }
    }

    /// <summary>True once the player is in a cockpit with a live combat HUD.</summary>
    public static bool InCockpit => ActiveHud != null;

    public static Aircraft PlayerAircraft
    {
        get
        {
            CombatHUD hud = ActiveHud;
            return hud != null ? hud.aircraft : null;
        }
    }

    /// <summary>
    /// One line, the first time the player is actually in a cockpit, naming every
    /// gate a missing readout can be stuck behind.
    /// </summary>
    public static void LogDiagnosticsOnce()
    {
        if (loggedDiagnostics || !InCockpit)
            return;

        loggedDiagnostics = true;

        int live = 0;
        foreach (string name in Order)
        {
            if (Elements[name].Instance != null)
                live++;
        }

        Plugin.Logger.LogInfo(
            $"Overlay: template={(template != null ? template.font != null ? template.font.name : "<no font>" : "<null>")}, "
            + $"hudRoot={hudRoot != null}, registered={Order.Count}, live={live}, builds={builds}");
    }
}

/// <summary>
/// Follows the game's own aircraft handoff so the overlay always knows which
/// CombatHUD is in the seat.
/// </summary>
[HarmonyPatch(typeof(CombatHUD))]
internal static class CombatHudAircraftPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CombatHUD.SetAircraft))]
    private static void SetAircraft(CombatHUD __instance) => Overlay.OnAircraftSet(__instance);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CombatHUD.RemoveAircraft))]
    private static void RemoveAircraft() => Overlay.OnAircraftRemoved();
}
