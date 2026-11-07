using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The upgrade rings: one ring per line, a notch per rank, clockwise from
/// the top, with a dot outside the ring at every ability. Quiet by design:
/// taken notches are grey, what is to come is faint, and the one colour is
/// the accent on the next notch. On a card, what is taken steps back and
/// the notches the card would fill turn white.
/// </summary>
/// <remarks>
/// IMGUI draws no shapes, so each ring is a small texture made in code,
/// rebuilt only when its line ranks up, its preview changes or the screen
/// is resized. The icons are drawn the same way, from a few strokes each,
/// so the mod ships no image files.
/// </remarks>
static class Rings
{
    static readonly Color Taken = Hud.Label;
    static readonly Color ToCome = new Color(1f, 1f, 1f, 0.12f);
    static readonly Color Earned = Hud.Value;
    static readonly Color Faint = new Color(1f, 1f, 1f, 0.25f);
    /// <summary>Taken notches on a card, behind the ones it would fill.</summary>
    static readonly Color Behind = new Color(0.37f, 0.41f, 0.44f);
    static readonly Color Filling = Hud.Value;
    static readonly Color IconColour = Hud.Label;

    /// <summary>Notches on an empty slot's ring.</summary>
    const int EmptyNotches = 20;

    sealed class Built
    {
        public int Rank, Ghost, Diameter;
        public Texture2D Texture;
    }

    static readonly Dictionary<(Upgrade line, bool card), Built> rings = new Dictionary<(Upgrade, bool), Built>();
    static readonly Dictionary<(string icon, int size), Texture2D> icons = new Dictionary<(string, int), Texture2D>();

    /// <summary>Room around the ring for the ability dots.</summary>
    static float Margin(float diameter) => Mathf.Max(4f, diameter * 0.12f) + 3f;

    /// <summary>
    /// A ring in <paramref name="ring"/> (its square, the dots drawn just
    /// outside it): the HUD's when <paramref name="ghost"/> is zero, a
    /// card's preview of that many ranks otherwise. A null line is an empty slot.
    /// </summary>
    internal static void Draw(Rect ring, Upgrade line, int rank, int ghost = 0, bool icon = true)
    {
        int diameter = Mathf.Max(8, Mathf.RoundToInt(ring.width));
        var key = (line, ghost > 0);
        if (!rings.TryGetValue(key, out var built) || built.Rank != rank || built.Ghost != ghost || built.Diameter != diameter)
        {
            if (built?.Texture != null) Object.Destroy(built.Texture);
            rings[key] = built = new Built { Rank = rank, Ghost = ghost, Diameter = diameter, Texture = Build(line, rank, ghost, diameter) };
        }
        float m = Margin(diameter);
        GUI.DrawTexture(new Rect(ring.x - m, ring.y - m, diameter + 2f * m, diameter + 2f * m), built.Texture);

        if (!icon || line?.Icon == null) return;
        int size = Mathf.Max(8, Mathf.RoundToInt(diameter * 0.42f));
        if (!icons.TryGetValue((line.Icon, size), out var texture))
            icons[(line.Icon, size)] = texture = BuildIcon(line.Icon, size);
        if (texture != null)
            GUI.DrawTexture(new Rect(ring.center.x - size / 2f, ring.center.y - size / 2f, size, size), texture);
    }

    static Texture2D NewTexture(int size) =>
        new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

    static Texture2D Build(Upgrade line, int rank, int ghost, int diameter)
    {
        int n = line?.MaxRank ?? EmptyNotches;
        float m = Margin(diameter);
        int size = Mathf.CeilToInt(diameter + 2f * m);
        float centre = size / 2f, outer = diameter / 2f, thickness = Mathf.Max(3f, diameter * 0.1f), inner = outer - thickness;
        float notch = 360f / n, gap = n > 16 ? 4f : 5f;

        Color Notch(int i)
        {
            if (line == null) return ToCome;
            if (ghost > 0) return i < rank ? Behind : i < rank + ghost ? Filling : ToCome;
            return i < rank ? Taken : i == rank ? Hud.Accent : ToCome;
        }

        // The dots: filled once earned (or filled by the card), outlined while to come.
        var dots = new List<(Vector2 at, Color colour, bool filled)>();
        float dotRadius = Mathf.Max(1.8f, diameter * 0.055f), dotRing = outer + 2f + dotRadius;
        bool nextMarked = false;
        if (line != null)
            for (int i = 0; i < n; i++)
            {
                if (!line.Path[i].IsAbility) continue;
                float a = (i + 0.5f) * notch * Mathf.Deg2Rad;
                var at = new Vector2(centre + Mathf.Sin(a) * dotRing, centre + Mathf.Cos(a) * dotRing);
                if (ghost > 0)
                    dots.Add(i < rank ? (at, Behind, true) : i < rank + ghost ? (at, Filling, true) : (at, Faint, false));
                else if (i < rank) dots.Add((at, Earned, true));
                else
                {
                    dots.Add((at, nextMarked ? Faint : Taken, false));
                    nextMarked = true;
                }
            }

        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Texture rows run bottom to top, so +y is up: 0° at the top, clockwise.
                float dx = x + 0.5f - centre, dy = y + 0.5f - centre;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                var colour = new Color(0f, 0f, 0f, 0f);
                if (r > inner - 1f && r < outer + 1f)
                {
                    float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    if (angle < 0f) angle += 360f;
                    int i = Mathf.Min(n - 1, (int)(angle / notch));
                    float within = angle - i * notch;
                    float edge = (Mathf.Min(within, notch - within) - gap / 2f) * Mathf.Deg2Rad * r;
                    float alpha = Mathf.Clamp01(edge + 0.5f) * Mathf.Clamp01(outer - r + 0.5f) * Mathf.Clamp01(r - inner + 0.5f);
                    if (alpha > 0f)
                    {
                        colour = Notch(i);
                        colour.a *= alpha;
                    }
                }
                foreach (var (at, dotColour, filled) in dots)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), at);
                    float alpha = filled
                        ? Mathf.Clamp01(dotRadius - d + 0.5f)
                        : Mathf.Clamp01(0.6f - Mathf.Abs(d - (dotRadius - 0.6f)) + 0.5f);
                    if (alpha * dotColour.a > colour.a) colour = new Color(dotColour.r, dotColour.g, dotColour.b, dotColour.a * alpha);
                }
                pixels[y * size + x] = colour;
            }

        var texture = NewTexture(size);
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    /// <summary>The icons, as strokes on a 24-unit square, y down as in SVG.</summary>
    static readonly Dictionary<string, Vector2[][]> Shapes = new Dictionary<string, Vector2[][]>
    {
        ["sam"] = new[]
        {
            P(12, 2, 15, 7, 15, 17, 9, 17, 9, 7, 12, 2),
            P(9, 13, 6, 17, 6, 19, 9, 19),
            P(15, 13, 18, 17, 18, 19, 15, 19),
        },
        ["asm"] = new[] { P(3, 9, 15, 9, 20, 11, 15, 13, 3, 13, 3, 9), Wave(2f, 20f, 19f, 1.3f) },
        ["torpedo"] = new[] { P(4, 10, 17, 10, 20, 12, 17, 14, 4, 14, 4, 10), P(4, 10, 2, 7), P(4, 14, 2, 17) },
        ["mortar"] = new[] { P(7, 15, 15, 4, 18, 6, 10, 17, 7, 15), P(4, 20, 20, 20), P(12, 13, 15, 20), P(9, 20, 10, 17) },
        ["pd"] = new[] { P(12, 3, 19, 6, 19, 11, 18, 15, 15.5f, 18.5f, 12, 21, 8.5f, 18.5f, 6, 15, 5, 11, 5, 6, 12, 3) },
        ["chaff"] = Enumerable.Range(0, 8).Select(i =>
        {
            float a = i * Mathf.PI / 4f;
            return P(12 + 3.5f * Mathf.Sin(a), 12 - 3.5f * Mathf.Cos(a), 12 + 8.5f * Mathf.Sin(a), 12 - 8.5f * Mathf.Cos(a));
        }).ToArray(),
        ["gun"] = new[] { P(9, 21, 9, 10, 12, 3, 15, 10, 15, 21, 9, 21) },
        ["air"] = new[] { P(12, 2, 12, 21), P(3, 13, 12, 8, 21, 13), P(8, 21, 12, 18.5f, 16, 21) },
        ["react"] = new[] { P(13, 2, 4, 14, 11, 14, 10, 22, 19, 10, 12, 10, 13, 2) },
        ["acc"] = new[] { Circle(12, 12, 8), Circle(12, 12, 3) },
        // A mast, and waves spreading from its top.
        ["ecm"] = new[] { P(12, 21, 12, 8), Arc(12, 8, 4f), Arc(12, 8, 7.5f), Arc(12, 8, 11f) },
    };

    static Vector2[] P(params float[] xy)
    {
        var points = new Vector2[xy.Length / 2];
        for (int i = 0; i < points.Length; i++) points[i] = new Vector2(xy[2 * i], xy[2 * i + 1]);
        return points;
    }

    static Vector2[] Circle(float cx, float cy, float r) =>
        Enumerable.Range(0, 33).Select(i => new Vector2(cx + r * Mathf.Cos(i * Mathf.PI / 16f), cy + r * Mathf.Sin(i * Mathf.PI / 16f))).ToArray();

    /// <summary>An arc over the top of a point: 60° either side of straight up.</summary>
    static Vector2[] Arc(float cx, float cy, float r) =>
        Enumerable.Range(0, 13).Select(i =>
        {
            float a = (i - 6) * 10f * Mathf.Deg2Rad;
            return new Vector2(cx + r * Mathf.Sin(a), cy - r * Mathf.Cos(a));
        }).ToArray();

    static Vector2[] Wave(float from, float to, float y, float height) =>
        Enumerable.Range(0, 25).Select(i =>
        {
            float x = from + (to - from) * i / 24f;
            return new Vector2(x, y + height * Mathf.Sin((x - from) / 6f * 2f * Mathf.PI));
        }).ToArray();

    static Texture2D BuildIcon(string name, int size)
    {
        if (!Shapes.TryGetValue(name, out var strokes)) return null;
        float scale = size / 24f, half = Mathf.Max(0.6f, 1.8f * scale / 2f);
        var segments = strokes.SelectMany(s => s.Zip(s.Skip(1), (a, b) => (a * scale, b * scale))).ToList();
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Shapes are y down; texture rows run bottom to top.
                var p = new Vector2(x + 0.5f, size - (y + 0.5f));
                float d = segments.Min(s => DistanceToSegment(p, s.Item1, s.Item2));
                float alpha = Mathf.Clamp01(half - d + 0.5f);
                pixels[y * size + x] = new Color(IconColour.r, IconColour.g, IconColour.b, alpha);
            }
        var texture = NewTexture(size);
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector2.Distance(p, a + t * ab);
    }

    // The HUD's slot row and its hover panel.

    static float styleScale;
    static GUIStyle slotName, slotSub, tipTitle, tipRank, tipRight, tipLabel, tipText, tipBold, tipNext, tipNum, tipNumNext;

    static void Styles(float s)
    {
        if (Mathf.Approximately(s, styleScale) && slotName != null) return;
        styleScale = s;
        int Size(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));
        slotName = Hud.Text(Size(12), Hud.Value);
        slotSub = Hud.Text(Size(10), Hud.Label);
        tipTitle = Hud.Text(Size(14), Hud.Value, style: FontStyle.Bold);
        tipRank = Hud.Text(Size(12), Hud.Label);
        tipRight = Hud.Text(Size(12), Hud.Label, TextAnchor.MiddleRight);
        tipLabel = Hud.Text(Size(11), Hud.Label, TextAnchor.UpperLeft);
        tipText = Hud.Text(Size(12), Hud.Label, TextAnchor.UpperLeft);
        tipBold = Hud.Text(Size(12), Hud.Value, TextAnchor.UpperLeft, FontStyle.Bold);
        tipNext = Hud.Text(Size(12), Hud.Value, TextAnchor.UpperLeft);
        tipNum = Hud.Text(Size(12), Hud.Label, TextAnchor.UpperRight);
        tipNumNext = Hud.Text(Size(12), Hud.Accent, TextAnchor.UpperRight);
        foreach (var style in new[] { tipText, tipBold, tipNext, tipLabel }) style.wordWrap = true;
        foreach (var style in new[] { slotName, slotSub }) style.clipping = TextClipping.Clip;
    }

    /// <summary>The lines in the slot row: the weapon slots, empty ones as null, then any fleet lines taken.</summary>
    internal static List<Upgrade> Slots(Run run)
    {
        var slots = new List<Upgrade>(run.WeaponSlots());
        while (slots.Count < Upgrades.WeaponCap) slots.Add(null);
        slots.AddRange(run.Held.Where(u => u.Kind == Kind.Fleet));
        return slots;
    }

    internal static float SlotHeight(float s) => 48f * s;

    /// <summary>
    /// The slot row across <paramref name="row"/>: a ring, the line's name,
    /// and its rank with the ranks to its next ability. Hovering a slot opens
    /// its panel underneath.
    /// </summary>
    internal static void DrawSlots(Rect row, Run run, float s)
    {
        Styles(s);
        var slots = Slots(run);
        float gap = 4f * s, width = (row.width - gap * (slots.Count - 1)) / slots.Count;
        Upgrade hovered = null;
        Rect hoveredBox = default;
        for (int i = 0; i < slots.Count; i++)
        {
            var line = slots[i];
            var box = new Rect(row.x + i * (width + gap), row.y, width, row.height);
            bool hover = line != null && box.Contains(Event.current.mousePosition);
            Hud.Box(box, hover ? Hud.Control : Hud.Panel);
            if (hover)
            {
                hovered = line;
                hoveredBox = box;
            }

            float diameter = (line?.Kind == Kind.Fleet ? 28f : 32f) * s;
            float pad = 10f * s;
            var ring = new Rect(box.x + pad, box.center.y - diameter / 2f, diameter, diameter);
            int rank = line == null ? 0 : run.RankOf(line);
            Draw(ring, line, rank);

            float textX = ring.xMax + 9f * s, textWidth = box.xMax - textX - 4f * s;
            float nameHeight = slotName.CalcSize(new GUIContent("A")).y, subHeight = slotSub.CalcSize(new GUIContent("A")).y;
            float top = box.center.y - (nameHeight + subHeight) / 2f;
            slotName.normal.textColor = line == null ? new Color(Hud.Label.r, Hud.Label.g, Hud.Label.b, 0.6f) : Hud.Value;
            GUI.Label(new Rect(textX, top, textWidth, nameHeight), line == null ? "Empty slot" : line.Short, slotName);
            if (line != null)
            {
                var next = Upgrades.NextAbility(line, rank);
                string sub = $"{rank}/{line.MaxRank}" + (next.HasValue ? $" · ability in {next.Value.ranks}" : "");
                GUI.Label(new Rect(textX, top + nameHeight, textWidth, subHeight), sub, slotSub);
            }
        }
        if (hovered != null) DrawPanel(new Rect(row.x, row.yMax + gap, row.width, 0f), run, hovered, s);
    }

    const int UpcomingRows = 4;

    /// <summary>A line's panel: what is in effect, the abilities earned, and the next few ranks.</summary>
    static void DrawPanel(Rect at, Run run, Upgrade line, float s)
    {
        int rank = run.RankOf(line);
        float pad = 12f * s, columnGap = 20f * s, rowGap = 3f * s;
        float column = (at.width - 2f * pad - columnGap) / 2f;

        var left = new List<(string text, GUIStyle style, string num)>();
        foreach (var text in Upgrades.Summary(run, line)) left.Add((text, tipText, null));
        foreach (var step in Upgrades.Earned(run, line)) left.Add(($"{step.Name} · {step.Text}", tipText, null));
        if (left.Count == 0) left.Add(("Nothing yet", tipText, null));

        var right = new List<(string text, GUIStyle style, string num)>();
        for (int i = rank; i < line.MaxRank && i < rank + UpcomingRows; i++)
        {
            var step = line.Path[i];
            string text = step.IsAbility ? $"{step.Name} · {step.Text}" : Upgrades.Describe(line, i);
            right.Add((text, step.IsAbility ? tipBold : i == rank ? tipNext : tipText, (i + 1).ToString()));
        }
        if (right.Count == 0) right.Add(("Line complete", tipText, null));

        float numWidth = 22f * s, numGap = 8f * s;
        float Height(List<(string text, GUIStyle style, string num)> rows) =>
            rows.Sum(r => r.style.CalcHeight(new GUIContent(r.text), r.num == null ? column : column - numWidth - numGap) + rowGap);
        float headerHeight = tipTitle.CalcSize(new GUIContent("A")).y;
        float labelHeight = tipLabel.CalcSize(new GUIContent("A")).y + 2f * s;
        float height = pad + headerHeight + 8f * s + labelHeight + Mathf.Max(Height(left), Height(right)) + pad;
        var panel = new Rect(at.x, at.y, at.width, height);
        Hud.Box(panel, Hud.Dark);

        float x = panel.x + pad, y = panel.y + pad;
        string title = line.Name;
        float titleWidth = tipTitle.CalcSize(new GUIContent(title)).x;
        GUI.Label(new Rect(x, y, titleWidth, headerHeight), title, tipTitle);
        GUI.Label(new Rect(x + titleWidth, y, column, headerHeight), $"  · rank {rank} of {line.MaxRank}", tipRank);
        var next = Upgrades.NextAbility(line, rank);
        if (next.HasValue) GUI.Label(new Rect(x, y, panel.width - 2f * pad, headerHeight), $"{next.Value.step.Name} in {next.Value.ranks}", tipRight);
        y += headerHeight + 8f * s;

        void Column(float cx, string label, List<(string text, GUIStyle style, string num)> rows)
        {
            GUI.Label(new Rect(cx, y, column, labelHeight), label, tipLabel);
            float cy = y + labelHeight;
            foreach (var (text, style, num) in rows)
            {
                float tx = cx, width = column;
                if (num != null)
                {
                    GUI.Label(new Rect(cx, cy, numWidth, style.CalcHeight(new GUIContent(num), numWidth)), num, num == (rank + 1).ToString() ? tipNumNext : tipNum);
                    tx += numWidth + numGap;
                    width -= numWidth + numGap;
                }
                float h = style.CalcHeight(new GUIContent(text), width);
                GUI.Label(new Rect(tx, cy, width, h), text, style);
                cy += h + rowGap;
            }
        }
        Column(x, "In effect", left);
        Column(x + column + columnGap, "Coming up", right);
    }
}
