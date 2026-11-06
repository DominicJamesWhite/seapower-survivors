using System.Linq;
using TMPro;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Drawing in the game's own look: its UI font, on the dark translucent
/// panels (black at 75%) with a faint edge that its windows use.
/// </summary>
/// <remarks>
/// The game draws with TextMeshPro, which IMGUI cannot use; but a TMP font
/// asset keeps the font it was made from, and that IMGUI can. If no loaded
/// asset has one, a plain Windows UI font stands in.
/// </remarks>
static class Hud
{
    internal static readonly Color Panel = new Color(0f, 0f, 0f, 0.75f);
    internal static readonly Color Edge = new Color(1f, 1f, 1f, 0.18f);
    internal static readonly Color Label = new Color(0.62f, 0.66f, 0.70f);
    internal static readonly Color Value = new Color(0.95f, 0.96f, 0.97f);
    internal static readonly Color Accent = new Color(1f, 0.62f, 0.1f);
    internal static readonly Color Danger = new Color(1f, 0.32f, 0.25f);

    static Font font;
    static Texture2D white;
    static GUISkin skin;
    static float skinScale;

    /// <summary>The game's UI is laid out for 1080p and scaled with the screen; so is ours.</summary>
    internal static float Scale => Screen.height / 1080f;

    internal static Font Font
    {
        get
        {
            if (font != null) return font;
            var asset = TMP_Settings.defaultFontAsset != null && TMP_Settings.defaultFontAsset.sourceFontFile != null
                ? TMP_Settings.defaultFontAsset
                : Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(a => a.sourceFontFile != null);
            font = asset != null ? asset.sourceFontFile : Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI" }, 16);
            Entry.Log.LogInfo($"HUD font: {font.name}" + (asset != null ? $" (from {asset.name})" : " (no game font found)"));
            return font;
        }
    }

    internal static GUIStyle Text(int size, Color colour, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal) =>
        new GUIStyle(GUI.skin.label) { font = Font, fontSize = size, alignment = anchor, fontStyle = style, normal = { textColor = colour }, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };

    internal static void Fill(Rect rect, Color colour)
    {
        if (white == null)
        {
            white = new Texture2D(1, 1);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
        }
        var previous = GUI.color;
        GUI.color = colour;
        GUI.DrawTexture(rect, white);
        GUI.color = previous;
    }

    /// <summary>
    /// IMGUI's default skin redrawn in the HUD's look, for windows laid out
    /// with GUILayout: a dark edged panel, flat edged buttons lit amber on
    /// hover and when toggled on, slim scrollbars. Rebuilt if the screen
    /// size changes. Read it only from OnGUI.
    /// </summary>
    internal static GUISkin Skin
    {
        get
        {
            float scale = Scale;
            if (skin != null && skinScale == scale) return skin;
            if (skin != null) Object.Destroy(skin);
            skinScale = scale;
            int Px(float v) => Mathf.Max(1, Mathf.RoundToInt(v * scale));

            skin = Object.Instantiate(GUI.skin);
            skin.font = Font;

            var window = skin.window;
            SetAll(window, Slice(new Color(0.03f, 0.035f, 0.04f, 0.9f), Edge), Label);
            window.fontSize = Px(13);
            window.alignment = TextAnchor.UpperLeft;
            window.border = new RectOffset(1, 1, 1, 1);
            window.padding = new RectOffset(Px(14), Px(14), Px(34), Px(14));
            // The title is drawn inside the padding; this lifts it into the top strip.
            window.contentOffset = new Vector2(0f, -Px(25));

            var label = skin.label;
            label.fontSize = Px(14);
            label.normal.textColor = Value;
            label.padding = new RectOffset(0, 0, Px(2), Px(2));
            label.margin = new RectOffset(Px(3), Px(3), Px(3), Px(3));

            var button = skin.button;
            button.fontSize = Px(14);
            button.alignment = TextAnchor.MiddleCenter;
            button.wordWrap = false;
            button.border = new RectOffset(1, 1, 1, 1);
            button.padding = new RectOffset(Px(10), Px(10), Px(6), Px(6));
            button.margin = new RectOffset(Px(3), Px(3), Px(3), Px(3));
            Set(button.normal, Slice(new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.16f)), Value);
            Set(button.hover, Slice(new Color(1f, 1f, 1f, 0.12f), new Color(1f, 1f, 1f, 0.3f)), Accent);
            Set(button.active, Slice(new Color(1f, 0.62f, 0.1f, 0.3f), Accent), Accent);
            Set(button.focused, button.normal.background, Value);
            var on = Slice(new Color(1f, 0.62f, 0.1f, 0.18f), Accent);
            Set(button.onNormal, on, Accent);
            Set(button.onHover, on, Accent);
            Set(button.onActive, button.active.background, Accent);
            Set(button.onFocused, on, Accent);

            SetAll(skin.scrollView, null, Value);
            var track = Slice(new Color(1f, 1f, 1f, 0.04f), new Color(1f, 1f, 1f, 0.04f));
            var thumb = Slice(new Color(1f, 1f, 1f, 0.22f), new Color(1f, 1f, 1f, 0.22f));
            foreach (var (bar, barThumb) in new[] { (skin.verticalScrollbar, skin.verticalScrollbarThumb), (skin.horizontalScrollbar, skin.horizontalScrollbarThumb) })
            {
                SetAll(bar, track, Value);
                SetAll(barThumb, thumb, Value);
                bar.border = barThumb.border = new RectOffset(1, 1, 1, 1);
                bar.padding = barThumb.padding = new RectOffset(0, 0, 0, 0);
            }
            skin.verticalScrollbar.fixedWidth = skin.verticalScrollbarThumb.fixedWidth = Px(8);
            skin.horizontalScrollbar.fixedHeight = skin.horizontalScrollbarThumb.fixedHeight = Px(8);
            skin.verticalScrollbar.margin = new RectOffset(Px(4), 0, 0, 0);
            return skin;
        }
    }

    static void Set(GUIStyleState state, Texture2D background, Color text)
    {
        state.background = background;
        state.textColor = text;
    }

    static void SetAll(GUIStyle style, Texture2D background, Color text)
    {
        foreach (var state in new[] { style.normal, style.hover, style.active, style.focused, style.onNormal, style.onHover, style.onActive, style.onFocused })
            Set(state, background, text);
    }

    /// <summary>A 3×3 texture for nine-slicing: a fill with a one-pixel edge.</summary>
    static Texture2D Slice(Color fill, Color edge)
    {
        var texture = new Texture2D(3, 3) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                texture.SetPixel(x, y, x == 1 && y == 1 ? fill : edge);
        texture.Apply();
        return texture;
    }

    /// <summary>A panel: dark fill with a one-pixel edge.</summary>
    internal static void Box(Rect rect)
    {
        Fill(rect, Panel);
        Fill(new Rect(rect.x, rect.y, rect.width, 1f), Edge);
        Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Edge);
        Fill(new Rect(rect.x, rect.y, 1f, rect.height), Edge);
        Fill(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), Edge);
    }
}
