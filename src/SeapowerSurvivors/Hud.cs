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
