using System.Linq;
using TMPro;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// Drawing in the game's own look: Roboto, and the colours of its theme
/// (NoesisTheme.Brushes.DarkRed), by the names it gives them.
/// </summary>
/// <remarks>
/// The game's Noesis UI has its Roboto as font files IMGUI cannot read, but
/// sharedassets2 also holds Roboto Medium and Bold as Unity fonts. Regular is
/// not among them, so the mod ships its own, in mod/fonts. If that will not
/// load, Medium stands in, then a TMP font asset's source font, then a plain
/// Windows UI font.
/// </remarks>
static class Hud
{
    internal static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    /// <summary>Brush.Window.Background: the game's windows and in-mission panels.</summary>
    internal static readonly Color Panel = Hex(0x2B2D31);
    /// <summary>Brush.Window.BackgroundDark: its tooltips.</summary>
    internal static readonly Color Dark = Hex(0x242629);
    /// <summary>The game's modal window: black at 75%.</summary>
    internal static readonly Color Modal = new Color(0f, 0f, 0f, 0.75f);
    /// <summary>The light veil its modal lays over everything behind it.</summary>
    internal static readonly Color Veil = new Color(0.8f, 0.8f, 0.8f, 0.2f);
    /// <summary>Brush.Border.Normal.</summary>
    internal static readonly Color Edge = Hex(0x3F4147);
    /// <summary>Brush.Foreground.Normal.</summary>
    internal static readonly Color Label = Hex(0x949AA1);
    /// <summary>Brush.Foreground.Highlight.</summary>
    internal static readonly Color Value = Hex(0xD7D7D7);
    /// <summary>Brush.Foreground.Emphasis.</summary>
    internal static readonly Color Accent = Hex(0xD5A876);
    /// <summary>Brush.Button.Background and Brush.Item.BackgroundOver.</summary>
    internal static readonly Color Control = Hex(0x393D40);
    /// <summary>Color.SeaPowerRed: a hovered button, a selected item, a progress bar's fill.</summary>
    internal static readonly Color Red = Hex(0xC04A3C);
    /// <summary>Brush.Damage.Heavy: the game's red for text about harm.</summary>
    internal static readonly Color Danger = Hex(0xD35B55);

    static Font font, boldFont, fallback;
    static bool triedBundled;
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
            if (!triedBundled)
            {
                triedBundled = true;
                font = Bundled();
                if (font != null) return font;
            }
            font = Loaded("Roboto-Medium");
            if (font != null)
            {
                Entry.Log.LogInfo("HUD font: Roboto-Medium");
                return font;
            }
            if (fallback != null) return fallback;
            var asset = TMP_Settings.defaultFontAsset != null && TMP_Settings.defaultFontAsset.sourceFontFile != null
                ? TMP_Settings.defaultFontAsset
                : Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault(a => a.sourceFontFile != null);
            fallback = asset != null ? asset.sourceFontFile : Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "Segoe UI" }, 16);
            Entry.Log.LogInfo($"HUD font until Roboto loads: {fallback.name}" + (asset != null ? $" (from {asset.name})" : " (no game font found)"));
            return fallback;
        }
    }

    /// <summary>The Roboto Regular in the mod's folder, or null if it is missing or Unity cannot draw with it.</summary>
    static Font Bundled()
    {
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "SeapowerSurvivors", "fonts", "Roboto-Regular.ttf");
        try
        {
            if (System.IO.File.Exists(path))
            {
                // A name with a directory in it makes Unity load the file rather than look for an OS font.
                var loaded = new Font(path) { hideFlags = HideFlags.DontUnloadUnusedAsset };
                loaded.RequestCharactersInTexture("A", 16);
                if (loaded.GetCharacterInfo('A', out _, 16))
                {
                    Entry.Log.LogInfo("HUD font: Roboto-Regular (bundled)");
                    return loaded;
                }
            }
            Entry.Log.LogWarning($"Could not use {path}");
        }
        catch (System.Exception e) { Entry.Log.LogWarning($"Could not load {path}: {e.Message}"); }
        return null;
    }

    /// <summary>Roboto Bold, or null until it loads.</summary>
    static Font BoldFont => boldFont != null ? boldFont : boldFont = Loaded("Roboto-Bold");

    static Font Loaded(string name) => Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f.name == name);

    internal static GUIStyle Text(int size, Color colour, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
    {
        // Real Roboto Bold where it can be had, rather than Unity's emboldening of Regular.
        var bold = style == FontStyle.Bold ? BoldFont : null;
        return new GUIStyle(GUI.skin.label) { font = bold != null ? bold : Font, fontSize = size, alignment = anchor, fontStyle = bold != null ? FontStyle.Normal : style, normal = { textColor = colour }, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
    }

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

    /// <summary>A panel: the window fill with a one-pixel edge.</summary>
    internal static void Box(Rect rect) => Box(rect, Panel);

    internal static void Box(Rect rect, Color fill)
    {
        Fill(rect, fill);
        Frame(rect, Edge);
    }

    internal static void Frame(Rect rect, Color colour)
    {
        Fill(new Rect(rect.x, rect.y, rect.width, 1f), colour);
        Fill(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), colour);
        Fill(new Rect(rect.x, rect.y, 1f, rect.height), colour);
        Fill(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), colour);
    }

    // A rarity's badge: the game's crew skill marks (CrewSkillGlyphTemplate, in
    // SeaPower.Resources), in its coordinates. One chevron for Common, then its
    // Seasoned, Veterans and Ultra: three chevrons, a star and two stars. IMGUI
    // draws no shapes, so each is a small texture made in code.

    const float BadgeWidth = 26f, BadgeHeight = 20f, BadgeLeft = -3f, BadgeTop = -1f;
    const float StrokeHalfWidth = 1.2f;

    static readonly Vector2[] Star =
    {
        new Vector2(10f, 2.6f), new Vector2(12f, 7.1f), new Vector2(16.8f, 7.5f), new Vector2(13.1f, 10.5f), new Vector2(14.2f, 15.3f),
        new Vector2(10f, 12.8f), new Vector2(5.8f, 15.3f), new Vector2(6.9f, 10.5f), new Vector2(3.2f, 7.5f), new Vector2(8f, 7.1f),
    };

    /// <summary>Each chevron: how far down its arms end, and how far above that they meet.</summary>
    static readonly (float foot, float rise)[] OneChevron = { (11.4f, 5.6f) };
    static readonly (float foot, float rise)[] ThreeChevrons = { (5.8f, 4f), (10.6f, 4f), (15.4f, 4f) };

    static readonly Vector2[] OneStar = { Vector2.zero };
    static readonly Vector2[] TwoStars = { new Vector2(-5.5f, 2f), new Vector2(5.5f, -2f) };

    static readonly System.Collections.Generic.Dictionary<(Rarity, int), Texture2D> badges = new System.Collections.Generic.Dictionary<(Rarity, int), Texture2D>();

    /// <summary>The width of a badge of this height.</summary>
    internal static float BadgeWidthFor(float height) => height * BadgeWidth / BadgeHeight;

    internal static void RarityBadge(Rect rect, Rarity rarity, Color colour)
    {
        int height = Mathf.Max(8, Mathf.RoundToInt(rect.height));
        if (!badges.TryGetValue((rarity, height), out var texture))
            badges[(rarity, height)] = texture = BuildBadge(rarity, height);
        var previous = GUI.color;
        GUI.color = colour;
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        GUI.color = previous;
    }

    static Texture2D BuildBadge(Rarity rarity, int height)
    {
        const int Samples = 3;
        var stars = rarity == Rarity.Legendary ? TwoStars : rarity == Rarity.Epic ? OneStar : null;
        var chevrons = rarity == Rarity.Rare ? ThreeChevrons : OneChevron;
        int width = Mathf.RoundToInt(BadgeWidthFor(height));
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < Samples; sy++)
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        // Textures run from the bottom up; the game's coordinates from the top down.
                        var at = new Vector2(
                            BadgeLeft + (x + (sx + 0.5f) / Samples) / width * BadgeWidth,
                            BadgeTop + (height - y - (sy + 0.5f) / Samples) / height * BadgeHeight);
                        if (stars != null ? stars.Any(offset => InStar(at - offset)) : chevrons.Any(c => OnChevron(at, c.foot, c.rise))) hits++;
                    }
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)(255 * hits / (Samples * Samples)));
            }
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    static bool OnChevron(Vector2 at, float foot, float rise)
    {
        var top = new Vector2(10f, foot - rise);
        return Mathf.Min(Distance(at, new Vector2(3.6f, foot), top), Distance(at, new Vector2(16.4f, foot), top)) <= StrokeHalfWidth;
    }

    static float Distance(Vector2 at, Vector2 a, Vector2 b)
    {
        float along = Mathf.Clamp01(Vector2.Dot(at - a, b - a) / (b - a).sqrMagnitude);
        return (at - (a + along * (b - a))).magnitude;
    }

    static bool InStar(Vector2 at)
    {
        bool inside = false;
        for (int i = 0, j = Star.Length - 1; i < Star.Length; j = i++)
            if ((Star[i].y > at.y) != (Star[j].y > at.y) &&
                at.x < (Star[j].x - Star[i].x) * (at.y - Star[i].y) / (Star[j].y - Star[i].y) + Star[i].x)
                inside = !inside;
        return inside;
    }
}
