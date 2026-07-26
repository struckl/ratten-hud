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
/// </summary>
internal static class Overlay
{
    /// <summary>Design resolution the canvas scaler matches; matches the game's own HUD.</summary>
    private const float ReferenceWidth = 1920f;
    private const float ReferenceHeight = 1080f;

    private static Canvas canvas;
    private static Font font;

    // Every element we created, so a font that only became resolvable later can
    // be back-filled onto elements built before it existed.
    private static readonly List<Text> Created = new List<Text>();

    // The HUD currently flying the player, captured from the game's own aircraft
    // handoff rather than read off the scene singleton. See ActiveHud.
    private static CombatHUD tracked;

    private static bool loggedDiagnostics;

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

            return canvas.transform;
        }
    }

    /// <summary>
    /// A <see cref="Text"/> with no font draws absolutely nothing, silently. The
    /// elements are built during plugin load, which is early enough that the
    /// scene fallback below has nothing to find, so resolution is retried every
    /// frame until it succeeds rather than being cached as a permanent failure.
    /// </summary>
    private static Font ResolveFont()
    {
        // Unity renamed the built-in font in 2022. Checked with Unity's own
        // null semantics rather than ?? , which does not see a destroyed object.
        Font resolved = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (resolved == null)
            resolved = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (resolved == null)
        {
            Text sample = Object.FindObjectOfType<Text>();
            if (sample != null)
                resolved = sample.font;
        }
        return resolved;
    }

    /// <summary>Resolves the font if it is still missing and back-fills it.</summary>
    public static void EnsureFonts()
    {
        if (font != null)
            return;

        font = ResolveFont();
        if (font == null)
            return;

        foreach (Text text in Created)
        {
            if (text != null)
                text.font = font;
        }
    }

    /// <summary>
    /// Creates a text element anchored at a normalised screen position, where
    /// (0.5, 0.5) is the centre of the screen and (0, 0) the bottom left.
    /// </summary>
    public static Text CreateText(
        string name, Vector2 anchor, Vector2 offset, int fontSize, TextAnchor alignment)
    {
        GameObject host = new GameObject(name);
        host.transform.SetParent(Root, worldPositionStays: false);

        Text text = host.AddComponent<Text>();
        if (font == null)
            font = ResolveFont();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = string.Empty;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(600f, 40f);
        rect.anchoredPosition = offset;

        // Cheap readability win over a bright sky without needing an outline shader.
        Outline outline = host.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        Created.Add(text);
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
    /// whichever CombatHUD awoke last — which is not necessarily the one in the
    /// seat. Everything on this canvas is gated on this, so when it picks the
    /// wrong instance every readout here clears itself every frame and the
    /// plugin looks dead while its patch-based readouts carry on working.
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
    /// One line, the first time we are in a cockpit, naming every gate a missing
    /// readout can be stuck behind. Cheaper than another round of guessing when
    /// nothing shows up on the glass.
    /// </summary>
    public static void LogDiagnosticsOnce()
    {
        if (loggedDiagnostics)
            return;

        CombatHUD singleton = SceneSingleton<CombatHUD>.i;
        bool inCockpit = InCockpit;
        if (!inCockpit && singleton == null && tracked == null)
            return;

        loggedDiagnostics = true;
        Plugin.Logger.LogInfo(
            $"Overlay: canvas={canvas != null}, font={(font != null ? font.name : "<null>")}, "
            + $"elements={Created.Count}, inCockpit={inCockpit}, "
            + $"tracked={(tracked != null ? (tracked.aircraft != null ? "with aircraft" : "no aircraft") : "<null>")}, "
            + $"singleton={(singleton != null ? (singleton.aircraft != null ? "with aircraft" : "no aircraft") : "<null>")}");
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
