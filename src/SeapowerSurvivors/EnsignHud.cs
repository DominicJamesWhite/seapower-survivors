using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The ensigns flown, as small flags at the end of the status panel. Hovering
/// one opens a quiet panel under the slot row saying what it does.
/// </summary>
static class EnsignHud
{
    const float FlagWidth = 33f, FlagHeight = 22f, Gap = 6f, PanelWidth = 340f;

    static float styleScale;
    static GUIStyle title, navy, boon, tally, tallyCount;

    static void Styles(float s)
    {
        if (Mathf.Approximately(s, styleScale) && title != null) return;
        styleScale = s;
        int Size(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));
        title = Hud.Text(Size(14), Hud.Value, TextAnchor.UpperLeft, FontStyle.Bold);
        navy = Hud.Text(Size(11), Hud.Label, TextAnchor.UpperLeft);
        boon = Hud.Text(Size(12), Hud.Value, TextAnchor.UpperLeft);
        tally = Hud.Text(Size(12), Hud.Label, TextAnchor.UpperLeft);
        tallyCount = Hud.Text(Size(10), Hud.Value, TextAnchor.MiddleCenter);
        boon.wordWrap = true;
    }

    internal static float Width(Run run, float s) =>
        run.Hoisted.Count == 0 ? 0f : (run.Hoisted.Count * FlagWidth + (run.Hoisted.Count - 1) * Gap) * s;

    /// <param name="area">Where the flags go: they sit in its vertical middle.</param>
    /// <param name="under">The row whose foot the hover panel hangs from.</param>
    internal static void Draw(Rect area, Run run, Rect under, float s)
    {
        Styles(s);
        float w = FlagWidth * s, h = FlagHeight * s, gap = Gap * s;
        Ensign hovered = null;
        Rect hoveredFlag = default;
        for (int i = 0; i < run.Hoisted.Count; i++)
        {
            var flag = new Rect(area.x + i * (w + gap), area.center.y - h / 2f, w, h);
            GUI.DrawTexture(flag, EnsignFlags.Texture(run.Hoisted[i]), ScaleMode.StretchToFill, true);
            if (Ensigns.Tally(run, run.Hoisted[i]) is var counted && counted.HasValue)
            {
                var corner = new Rect(flag.xMax - 13f * s, flag.yMax - 11f * s, 13f * s, 11f * s);
                Hud.Fill(corner, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(corner, counted.Value.count, tallyCount);
            }
            if (!flag.Contains(Event.current.mousePosition)) continue;
            hovered = run.Hoisted[i];
            hoveredFlag = flag;
        }
        if (hovered != null) DrawPanel(run, hovered, hoveredFlag, under, s);
    }

    static void DrawPanel(Run run, Ensign ensign, Rect flag, Rect under, float s)
    {
        float pad = 12f * s, width = PanelWidth * s, inner = width - 2f * pad;
        float titleHeight = title.CalcSize(new GUIContent("A")).y, navyHeight = navy.CalcSize(new GUIContent("A")).y;
        float boonHeight = boon.CalcHeight(new GUIContent(ensign.Boon), inner);
        var counted = Ensigns.Tally(run, ensign);
        float tallyHeight = counted.HasValue ? 4f * s + tally.CalcSize(new GUIContent("A")).y : 0f;
        float height = pad + titleHeight + navyHeight + 8f * s + boonHeight + tallyHeight + pad;
        float x = Mathf.Clamp(flag.center.x - width / 2f, 4f, Screen.width - width - 4f);
        var panel = new Rect(x, under.yMax + 4f * s, width, height);
        Hud.Box(panel, Hud.Dark);
        float y = panel.y + pad;
        GUI.Label(new Rect(panel.x + pad, y, inner, titleHeight), ensign.Name, title);
        y += titleHeight;
        GUI.Label(new Rect(panel.x + pad, y, inner, navyHeight), ensign.Navy, navy);
        y += navyHeight + 8f * s;
        GUI.Label(new Rect(panel.x + pad, y, inner, boonHeight), ensign.Boon, boon);
        if (counted.HasValue) GUI.Label(new Rect(panel.x + pad, y + boonHeight + 4f * s, inner, tallyHeight), counted.Value.line, tally);
    }
}
