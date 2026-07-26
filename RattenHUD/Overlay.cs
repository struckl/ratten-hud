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

    private static Font Font
    {
        get
        {
            if (font != null)
                return font;

            // Unity renamed the built-in font in 2022; try both before falling
            // back to whatever the game already loaded.
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                   ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
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
    /// Creates a text element anchored at a normalised screen position, where
    /// (0.5, 0.5) is the centre of the screen and (0, 0) the bottom left.
    /// </summary>
    public static Text CreateText(
        string name, Vector2 anchor, Vector2 offset, int fontSize, TextAnchor alignment)
    {
        GameObject host = new GameObject(name);
        host.transform.SetParent(Root, worldPositionStays: false);

        Text text = host.AddComponent<Text>();
        text.font = Font;
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

        ElementLayout.Register(name, rect);
        return text;
    }

    /// <summary>True once the player is in a cockpit with a live combat HUD.</summary>
    public static bool InCockpit =>
        SceneSingleton<CombatHUD>.i != null && SceneSingleton<CombatHUD>.i.aircraft != null;

    public static Aircraft PlayerAircraft =>
        SceneSingleton<CombatHUD>.i != null ? SceneSingleton<CombatHUD>.i.aircraft : null;
}
