using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RattenHUD;

/// <summary>
/// A screen space canvas the plugin owns outright. Drawing onto our own canvas
/// rather than into the game's HUD hierarchy keeps the readouts alive across
/// aircraft changes and means a game side layout change cannot silently move
/// our elements somewhere unreadable.
///
/// Elements are declared once with <see cref="Register"/> and fetched through
/// <see cref="Element"/> every frame rather than being held by the caller. The
/// canvas does not survive the first scene load -- BepInEx runs plugin Awake
/// from a static constructor, before the first scene has finished loading, and
/// DontDestroyOnLoad does not stick that early -- so anything holding a Text
/// from load time ends up holding a destroyed object and silently drawing
/// nothing for the rest of the session. Going through Element means a lost
/// canvas is simply rebuilt on the next frame that needs it.
/// </summary>
internal static class Overlay
{
    /// <summary>Design resolution the canvas scaler matches; matches the game's own HUD.</summary>
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    /// <summary>Everything needed to rebuild one element from nothing.</summary>
    private sealed class Element_
    {
        public Vector2 Anchor;
        public Vector2 Offset;
        public int FontSize;
        public TextAnchor Alignment;
        public Text Instance;
    }

    private static Canvas canvas;
    private static Font font;

    private static readonly Dictionary<string, Element_> Elements = new Dictionary<string, Element_>();
    private static readonly List<string> Order = new List<string>();

    // The HUD currently flying the player, captured from the game's own aircraft
    // handoff rather than read off the scene singleton. See ActiveHud.
    private static CombatHUD tracked;

    private static bool loggedDiagnostics;
    private static int rebuilds;

    private static Transform Root
    {
        get
        {
            if (canvas != null)
                return canvas.transform;

            GameObject host = new GameObject("RattenHUD.Overlay");
            Object.DontDestroyOnLoad(host);

            canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the game HUD, below anything modal it might draw later.
            canvas.sortingOrder = 500;

            CanvasScaler scaler = host.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            // Match on height: the HUD is vertically composed and ultrawide
            // displays should not shrink it.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            rebuilds++;
            return canvas.transform;
        }
    }

    /// <summary>
    /// A <see cref="Text"/> with no font draws absolutely nothing, silently, so
    /// resolution is retried until it succeeds rather than cached as a permanent
    /// failure. Elements are declared during plugin load, which is early enough
    /// that the scene fallback below has nothing to find.
    /// </summary>
    private static Font Font
    {
        get
        {
            if (font != null)
                return font;

            // Unity renamed the built-in font in 2022. Checked with Unity's own
            // null semantics rather than ?? , which does not see a destroyed
            // object as null.
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
            {
                Text sample = Object.FindObjectOfType<Text>();
                if (sample != null)
                    font = sample.font;
            }
            return font;
        }
    }

    /// <summary>
    /// Declares an element at a normalised screen position, where (0.5, 0.5) is
    /// the centre of the screen and (0, 0) the bottom left. Nothing is built
    /// until <see cref="Element"/> asks for it.
    /// </summary>
    public static void Register(
        string name, Vector2 anchor, Vector2 offset, int fontSize, TextAnchor alignment)
    {
        if (!Elements.ContainsKey(name))
            Order.Add(name);

        Elements[name] = new Element_
        {
            Anchor = anchor,
            Offset = offset,
            FontSize = fontSize,
            Alignment = alignment,
            Instance = null,
        };
    }

    /// <summary>The live element, built or rebuilt as needed. Null if unregistered.</summary>
    public static Text Element(string name)
    {
        if (!Elements.TryGetValue(name, out Element_ element))
            return null;

        if (element.Instance != null)
        {
            // A font that only became resolvable after the element was built.
            if (element.Instance.font == null)
                element.Instance.font = Font;
            return element.Instance;
        }

        element.Instance = Build(name, element);
        return element.Instance;
    }

    /// <summary>
    /// The element only if it already exists. Used by the clear paths, which
    /// must not resurrect a canvas just to blank it.
    /// </summary>
    public static Text Peek(string name) =>
        Elements.TryGetValue(name, out Element_ element) && element.Instance != null
            ? element.Instance
            : null;

    private static Text Build(string name, Element_ element)
    {
        GameObject host = new GameObject(name);
        host.transform.SetParent(Root, worldPositionStays: false);

        Text text = host.AddComponent<Text>();
        text.font = Font;
        text.fontSize = element.FontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = element.Alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = string.Empty;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = element.Anchor;
        rect.anchorMax = element.Anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(600f, 40f);
        rect.anchoredPosition = element.Offset;

        // Cheap readability win over a bright sky without needing an outline shader.
        Outline outline = host.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        ElementLayout.Register(name, rect);
        return text;
    }

    public static void OnAircraftSet(CombatHUD hud) => tracked = hud;

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
            $"Overlay: canvas={canvas != null}, font={(font != null ? font.name : "<null>")}, "
            + $"registered={Order.Count}, live={live}, canvasBuilds={rebuilds}, "
            + $"tracked={(tracked != null ? "yes" : "no")}");
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
