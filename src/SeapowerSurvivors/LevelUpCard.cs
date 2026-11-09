using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// One card of the level-up screen. Each section answers one question, in the
/// order a player asks them: the head says what it is and how much of it, the
/// body what changes, ability first, and the foot what it leads to. Heights
/// are measured, so nothing is cut off; the screen makes every card as tall as
/// the tallest. Hovered, the head takes the solid red of the game's list rows.
/// </summary>
sealed class LevelUpCard
{
    /// <summary>Rows in a card's table; weapons past this are named instead.</summary>
    const int MaxTableRows = 8;
    /// <summary>A row whose values would take more of the card than this goes on two lines.</summary>
    const float MaxValueShare = 0.62f;
    const float RingSize = 40f, RingInset = 6f, RingGap = 14f;
    const float Pad = 16f;

    enum Part { Rule, Label, Name, Text, Detail, Table }

    readonly struct Block
    {
        public readonly Part Part;
        public readonly string Text;
        /// <summary>Space above, at 1080p.</summary>
        public readonly float Above;

        public Block(Part part, string text, float above)
        {
            Part = part;
            Text = text;
            Above = above;
        }
    }

    string title, status, slot;
    bool lastSlot;
    Rarity? rarity;
    /// <summary>The ring's line, its rank and the ranks the card would add; null for a ship.</summary>
    Upgrade line;
    int held, ranks;
    readonly List<Block> body = new List<Block>(), foot = new List<Block>();
    readonly List<(string group, List<PreviewRow> rows)> table = new List<(string, List<PreviewRow>)>();

    void Add(Part part, string text, float above = 0f) => body.Add(new Block(part, text, body.Count == 0 ? 0f : above));

    internal static LevelUpCard For(Run run, Upgrade upgrade, Rarity rarity, int ranks, CardPreview preview)
    {
        int held = run.RankOf(upgrade);
        var card = new LevelUpCard { title = upgrade.Name, rarity = rarity, line = upgrade, held = held, ranks = ranks };

        if (held > 0) card.status = $"Rank {held} to {held + ranks} of {upgrade.MaxRank}";
        else
        {
            card.status = ranks > 1 ? $"New · {ranks} ranks" : "New";
            if (upgrade.Kind == Kind.Fleet) card.slot = "Fleet upgrade: needs no weapon slot";
            else
            {
                int slot = run.WeaponSlots().Count + 1;
                card.lastSlot = slot >= Upgrades.WeaponCap;
                card.slot = card.lastSlot ? "Takes your last weapon slot" : $"Takes weapon slot {slot} of {Upgrades.WeaponCap}";
            }
        }

        var (stats, abilities) = Upgrades.CardText(run, upgrade, ranks);
        if (abilities.Count > 0) card.Add(Part.Label, abilities.Count > 1 ? "NEW ABILITIES" : "NEW ABILITY");
        foreach (var (name, text) in abilities)
        {
            card.Add(Part.Name, name, 2f);
            card.Add(Part.Text, text, 1f);
        }
        // The plain sentence only where the fleet has no numbers to show instead.
        foreach (var (stat, text) in stats)
            if (!preview.Covered.Contains(stat)) card.Add(Part.Text, text, 8f);

        card.FillTable(preview.Rows.Where(r => !r.IsNote).ToList());
        foreach (var row in preview.Rows.Where(r => r.IsNote)) card.Add(Part.Detail, row.Label, 8f);

        var next = Upgrades.NextAbility(upgrade, held + ranks);
        card.foot.Add(new Block(Part.Rule, null, 0f));
        if (next.HasValue)
        {
            string away = next.Value.ranks == 1 ? "in 1 rank" : $"in {next.Value.ranks} ranks";
            card.foot.Add(new Block(Part.Text, $"Next ability: {next.Value.step.Name}, {away}", 8f));
            card.foot.Add(new Block(Part.Detail, next.Value.step.Text, 1f));
        }
        else card.foot.Add(new Block(Part.Detail, held + ranks >= upgrade.MaxRank ? "This completes the line" : "No more abilities on this line", 8f));
        return card;
    }

    internal static LevelUpCard For(Run run, Escort escort)
    {
        int multiple = Ensigns.ChestMultiple(run, escort), total = escort.Count * multiple;
        string kind = escort.IsAir ? (total > 1 ? $"{total} aircraft" : "Aircraft") : (total > 1 ? $"{total} ships" : "Ship");
        var card = new LevelUpCard { title = escort.Name + Ensigns.Times(multiple), status = $"{kind} · Tier {escort.Tier}" };
        var adds = Escorts.Missing(run, escort);
        var carries = Ensigns.Usable(escort.Roles) & ~adds;
        if (adds != Role.None)
        {
            card.Add(Part.Label, "ADDS TO YOUR FLEET");
            card.Add(Part.Text, Escorts.RoleList(adds), 2f);
        }
        if (carries != Role.None)
        {
            card.Add(Part.Label, adds != Role.None ? "ALSO CARRIES" : "CARRIES", 10f);
            card.Add(Part.Text, Escorts.RoleList(carries), 2f);
        }
        return card;
    }

    /// <summary>The table, a weapon at a time, naming any weapons there is no room for.</summary>
    void FillTable(List<PreviewRow> rows)
    {
        int shown = 0;
        var also = new List<string>();
        foreach (var group in rows.GroupBy(r => r.Group))
        {
            var list = group.ToList();
            if (table.Count > 0 && shown + list.Count > MaxTableRows) also.Add(group.Key);
            else
            {
                table.Add((group.Key, list));
                shown += list.Count;
            }
        }
        if (table.Count > 0) Add(Part.Table, null, 10f);
        if (also.Count > 0) Add(Part.Detail, $"Also: {string.Join(", ", also)}", 6f);
    }

    // Measuring and drawing.

    static float styleScale;
    static GUIStyle titleStyle, statusStyle, rarityStyle, label, labelRight, nameStyle, text, detail, group, rowLabel, cell, cellAfter, wide;

    static void Styles(float s)
    {
        if (Mathf.Approximately(s, styleScale) && titleStyle != null) return;
        styleScale = s;
        int Size(float px) => Mathf.Max(8, Mathf.RoundToInt(px * s));
        GUIStyle Make(float px, Color colour, TextAnchor anchor, bool wrap, FontStyle font = FontStyle.Normal)
        {
            var style = Hud.Text(Size(px), colour, anchor, font);
            style.wordWrap = wrap;
            return style;
        }
        titleStyle = Make(16, Hud.Value, TextAnchor.UpperLeft, true, FontStyle.Bold);
        statusStyle = Make(12, Hud.Label, TextAnchor.UpperLeft, true);
        rarityStyle = Make(12, Hud.Label, TextAnchor.UpperRight, false);
        label = Make(11, Hud.Label, TextAnchor.UpperLeft, false);
        labelRight = Make(11, Hud.Label, TextAnchor.UpperRight, false);
        nameStyle = Make(13, Hud.Value, TextAnchor.UpperLeft, true, FontStyle.Bold);
        text = Make(12, Hud.Value, TextAnchor.UpperLeft, true);
        detail = Make(11, Hud.Label, TextAnchor.UpperLeft, true);
        group = Make(12, Hud.Value, TextAnchor.UpperLeft, true, FontStyle.Bold);
        rowLabel = Make(12, Hud.Label, TextAnchor.UpperLeft, true);
        cell = Make(12, Hud.Label, TextAnchor.UpperRight, false);
        cellAfter = Make(12, Hud.Value, TextAnchor.UpperRight, false);
        wide = Make(12, Hud.Value, TextAnchor.UpperRight, true);
    }

    static float Width(GUIStyle style, string content) => style.CalcSize(new GUIContent(content)).x;
    static float Height(GUIStyle style, string content, float width) => style.CalcHeight(new GUIContent(content), width);

    static float Line(GUIStyle style, string content, float x, float y, float width, bool draw)
    {
        float height = Height(style, content, width);
        if (draw) GUI.Label(new Rect(x, y, width, height), content, style);
        return height;
    }

    /// <summary>How tall the head is on a card this wide.</summary>
    internal float HeadHeight(float cardWidth, float s) => Head(new Rect(0f, 0f, cardWidth, 0f), s, false, false);

    /// <summary>How tall the body and foot are on a card this wide.</summary>
    internal float ContentHeight(float cardWidth, float s)
    {
        Styles(s);
        float width = cardWidth - 2f * Pad * s;
        float height = Blocks(body, 0f, 0f, width, s, false) + Blocks(foot, 0f, 0f, width, s, false);
        return 2f * Pad * s + height + (body.Count > 0 && foot.Count > 0 ? 12f * s : 0f);
    }

    /// <summary>Draws the card with the given head height, so heads line up. True when clicked.</summary>
    internal bool Draw(Rect rect, float headHeight, float s)
    {
        Styles(s);
        bool hover = rect.Contains(Event.current.mousePosition);
        Hud.Box(rect, Hud.Panel);
        var head = new Rect(rect.x, rect.y, rect.width, headHeight);
        Hud.Fill(head, hover ? Hud.Red : Hud.Dark);
        Head(head, s, true, hover);

        float pad = Pad * s, width = rect.width - 2f * pad;
        Blocks(body, rect.x + pad, head.yMax + pad, width, s, true);
        float footHeight = Blocks(foot, 0f, 0f, width, s, false);
        Blocks(foot, rect.x + pad, rect.yMax - pad - footHeight, width, s, true);
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    /// <summary>The ring, the name, the rank and rarity, and what the card does to the weapon slots. Returns its height.</summary>
    float Head(Rect head, float s, bool draw, bool hover)
    {
        Styles(s);
        float pad = Pad * s, x = head.x + pad, width = head.width - 2f * pad;
        float ring = RingSize * s;
        if (line != null)
        {
            if (draw) Rings.Draw(new Rect(x + RingInset * s, head.y + 12f * s, ring, ring), line, held, ranks);
            float inset = (RingInset + RingGap) * s + ring;
            x += inset;
            width -= inset;
        }

        float y = head.y + 10f * s;
        y += Line(titleStyle, title, x, y, width, draw) + 2f * s;

        statusStyle.normal.textColor = hover ? Hud.Value : Hud.Label;
        float rarityWidth = 0f;
        if (rarity.HasValue)
        {
            string name = rarity.Value.ToString();
            float rowHeight = Height(statusStyle, "A", width), badgeHeight = 12f * s, badgeWidth = Hud.BadgeWidthFor(badgeHeight);
            rarityWidth = Width(rarityStyle, name) + badgeWidth + 4f * s;
            if (draw)
            {
                var colour = hover ? Hud.Value : Rarities.Colour(rarity.Value);
                rarityStyle.normal.textColor = colour;
                GUI.Label(new Rect(x, y, width, rowHeight), name, rarityStyle);
                Hud.RarityBadge(new Rect(x + width - rarityWidth, y + (rowHeight - badgeHeight) / 2f, badgeWidth, badgeHeight), rarity.Value, colour);
            }
        }
        y += Line(statusStyle, status, x, y, width - rarityWidth - 6f * s, draw);

        if (slot != null)
        {
            statusStyle.normal.textColor = hover ? Hud.Value : lastSlot ? Hud.Accent : Hud.Label;
            y += 1f * s + Line(statusStyle, slot, x, y + 1f * s, width, draw);
        }
        y += 10f * s;
        float height = y - head.y;
        return line != null ? Mathf.Max(height, ring + 24f * s) : height;
    }

    /// <summary>Lays blocks out from the top. Returns the height they take.</summary>
    float Blocks(List<Block> blocks, float x, float y, float width, float s, bool draw)
    {
        float start = y;
        foreach (var block in blocks)
        {
            y += block.Above * s;
            switch (block.Part)
            {
                case Part.Rule:
                    float thickness = Mathf.Max(1f, Mathf.Round(s));
                    if (draw) Hud.Fill(new Rect(x, Mathf.Round(y), width, thickness), Hud.Edge);
                    y += thickness;
                    break;
                case Part.Label: y += Line(label, block.Text, x, y, width, draw); break;
                case Part.Name: y += Line(nameStyle, block.Text, x, y, width, draw); break;
                case Part.Text: y += Line(text, block.Text, x, y, width, draw); break;
                case Part.Detail: y += Line(detail, block.Text, x, y, width, draw); break;
                case Part.Table: y += Table(x, y, width, s, draw); break;
            }
        }
        return y - start;
    }

    /// <summary>
    /// Each weapon, then a row per stat: its label, and now and after in
    /// columns. A row too wide for the columns, such as a refit's aircraft,
    /// puts its values on a second line.
    /// </summary>
    float Table(float x, float y, float width, float s, bool draw)
    {
        const string Now = "NOW", After = "AFTER";
        float arrowSize = 11f * s, arrow = arrowSize + 12f * s;
        bool Stacked(PreviewRow r) => Width(cell, r.Before) + arrow + Width(cellAfter, r.After) > width * MaxValueShare;
        var fitting = table.SelectMany(t => t.rows).Where(r => !Stacked(r)).ToList();
        float before = fitting.Select(r => Width(cell, r.Before)).Append(Width(labelRight, Now)).Max();
        float after = fitting.Select(r => Width(cellAfter, r.After)).Append(Width(labelRight, After)).Max();
        float labelWidth = width - before - arrow - after - 8f * s;
        float afterX = x + width - after, beforeX = afterX - arrow - before;

        float start = y;
        bool first = true;
        foreach (var (name, rows) in table)
        {
            if (!first) y += 8f * s;
            float groupWidth = first ? labelWidth : width;
            float groupHeight = Height(group, name, groupWidth);
            if (draw)
            {
                GUI.Label(new Rect(x, y, groupWidth, groupHeight), name, group);
                if (first)
                {
                    GUI.Label(new Rect(beforeX, y, before, groupHeight), Now, labelRight);
                    GUI.Label(new Rect(afterX, y, after, groupHeight), After, labelRight);
                }
            }
            y += groupHeight + 2f * s;
            first = false;

            foreach (var row in rows)
            {
                if (Stacked(row))
                {
                    y += Line(rowLabel, row.Label, x, y, width, draw);
                    y += Line(wide, $"{row.Before} to {row.After}", x, y, width, draw) + 2f * s;
                    continue;
                }
                float height = Mathf.Max(Height(rowLabel, row.Label, labelWidth), Height(cell, "A", width));
                if (draw)
                {
                    GUI.Label(new Rect(x, y, labelWidth, height), row.Label, rowLabel);
                    GUI.Label(new Rect(beforeX, y, before, height), row.Before, cell);
                    Rings.DrawIcon(new Rect(beforeX + before + (arrow - arrowSize) / 2f, y + (Height(cell, "A", width) - arrowSize) / 2f, arrowSize, arrowSize), "arrow");
                    GUI.Label(new Rect(afterX, y, after, height), row.After, cellAfter);
                }
                y += height + 2f * s;
            }
        }
        return y - start;
    }
}
