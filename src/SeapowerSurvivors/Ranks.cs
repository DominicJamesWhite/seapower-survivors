using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SeapowerSurvivors;

/// <summary>What a difficulty is called and shown as under one navy: a rank, and its insignia.</summary>
sealed class Rank
{
    public readonly string Name;
    public readonly Insignia Insignia;

    public Rank(string name, Insignia insignia)
    {
        Name = name;
        Insignia = insignia;
    }
}

/// <summary>
/// The five difficulties as each navy's own ranks, from a lieutenant to its
/// most senior admiral, so the Survivors screen shows the insignia of the navy
/// being sailed for. Drawn from memory of each navy's pattern, not from a
/// reference: the stripes and stars are right in kind, and worth a check.
/// </summary>
/// <remarks>
/// - Soviet: shoulder boards of 1955 to 1991. One gap and two stars, two gaps
///   and one star, two gaps and three; an admiral's woven board with one large
///   star, and an admiral of the fleet's larger marshal's star, drawn with
///   rays to tell it from a rear admiral's.
/// - American, British, Argentine and Japanese: sleeve lace. Two stripes, two
///   and a half, four; then a broad stripe with one above it, and with the
///   most that navy wears. Above the lace an American line officer's star, the
///   British and Argentine curl, or the Japanese cherry blossom.
/// - Chinese: the People's Liberation Army wore no ranks from 1965 to 1988, so
///   these are the boards of 1988, the nearest to the game's years.
/// </remarks>
static class Ranks
{
    static readonly Dictionary<string, Rank[]> byNavy = new Dictionary<string, Rank[]>
    {
        ["ussr"] = new[]
        {
            new Rank("Lieutenant", Insignia.Board(1, 2)),
            new Rank("Captain 3rd Rank", Insignia.Board(2, 1)),
            new Rank("Captain 1st Rank", Insignia.Board(2, 3)),
            new Rank("Rear Admiral", Insignia.FlagBoard(1)),
            new Rank("Admiral of the Fleet", Insignia.FlagBoard(1, marshal: true)),
        },
        ["us"] = Lace(Emblem.Star, 4, "Lieutenant", "Lieutenant Commander", "Captain", "Rear Admiral", "Fleet Admiral"),
        ["uk"] = Lace(Emblem.Curl, 4, "Lieutenant", "Lieutenant Commander", "Captain", "Rear Admiral", "Admiral of the Fleet"),
        ["argentina"] = Lace(Emblem.Curl, 3, "Teniente de Navío", "Capitán de Corbeta", "Capitán de Navío", "Contraalmirante", "Almirante"),
        ["japan"] = Lace(Emblem.Blossom, 3, "Lieutenant", "Lieutenant Commander", "Captain", "Rear Admiral", "Admiral"),
        ["china"] = new[]
        {
            new Rank("Lieutenant", Insignia.Board(1, 2)),
            new Rank("Lieutenant Commander", Insignia.Board(2, 1)),
            new Rank("Captain", Insignia.Board(2, 3)),
            new Rank("Rear Admiral", Insignia.FlagBoard(1)),
            new Rank("Admiral", Insignia.FlagBoard(3)),
        },
    };

    /// <param name="most">Stripes above the broad one for the navy's most senior admiral.</param>
    static Rank[] Lace(Emblem emblem, int most, params string[] names) => new[]
    {
        new Rank(names[0], Insignia.Lace(emblem, false, 2)),
        new Rank(names[1], Insignia.Lace(emblem, false, 2, half: true)),
        new Rank(names[2], Insignia.Lace(emblem, false, 4)),
        new Rank(names[3], Insignia.Lace(emblem, true, 1)),
        new Rank(names[4], Insignia.Lace(emblem, true, most)),
    };

    /// <summary>The rank a difficulty goes by in this navy; a navy with no ranks of its own here borrows the American ones.</summary>
    internal static Rank Of(Difficulty difficulty, Navy navy)
    {
        var ranks = byNavy.TryGetValue(navy?.Id ?? "", out var own) ? own : byNavy["us"];
        return ranks[Math.Min(difficulty.Index, ranks.Length - 1)];
    }

    /// <summary>Every navy's ranks, for checking the drawings outside the game.</summary>
    internal static IEnumerable<(string navy, Rank rank)> All => byNavy.SelectMany(n => n.Value.Select(r => (n.Key, r)));
}

/// <summary>What is worn above a navy's sleeve lace.</summary>
enum Emblem { Star, Curl, Blossom }

/// <summary>
/// One rank's insignia as path data, on a shoulder board lying on its side:
/// 84 wide and 26 high, the outer end to the left and the button to the right.
/// Only lines, so it can be drawn by anything; the Survivors screen's view
/// draws the board itself under these three layers.
/// </summary>
sealed class Insignia
{
    public const float Width = 84f, Height = 26f;
    const float Middle = Height / 2f;

    /// <summary>Gaps or stripes, to fill in gold.</summary>
    public string Gaps = "";
    /// <summary>Stars or the emblem above the lace, to fill in pale gold with a dark edge.</summary>
    public string Stars = "";
    /// <summary>An admiral's weave, and the rays round a marshal's star, to stroke.</summary>
    public string Weave = "";

    static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    static float Cos(float angle) => (float)Math.Cos(angle);
    static float Sin(float angle) => (float)Math.Sin(angle);

    /// <summary>A closed figure through these points.</summary>
    static string Figure(IEnumerable<(float x, float y)> points) =>
        "M" + string.Join(" L", points.Select(p => $"{N(p.x)},{N(p.y)}")) + " Z ";

    /// <summary>A five-pointed star, a point towards the button.</summary>
    static string Star(float x, float y, float radius) =>
        Figure(Enumerable.Range(0, 10).Select(i =>
        {
            float r = i % 2 == 0 ? radius : radius * 0.4f, angle = i * (float)Math.PI / 5f;
            return (x + r * Cos(angle), y + r * Sin(angle));
        }));

    /// <summary>A disc, as a many-sided figure; drawn the other way round it cuts a hole in one drawn before it.</summary>
    static string Disc(float x, float y, float radius, bool hole = false) =>
        Figure(Enumerable.Range(0, 20).Select(i =>
        {
            float angle = (hole ? -i : i) * (float)Math.PI / 10f;
            return (x + radius * Cos(angle), y + radius * Sin(angle));
        }));

    static string Bar(float x, float y, float width, float height) =>
        Figure(new[] { (x, y), (x + width, y), (x + width, y + height), (x, y + height) });

    /// <summary>A board with lengthwise gaps and small stars: one gap for a junior officer, two for a senior one.</summary>
    public static Insignia Board(int gaps, int stars)
    {
        const float Small = 4f, Upper = 8f, Lower = 18f;
        string Gap(float y) => Bar(0f, y - 1f, Width, 2f);
        return new Insignia
        {
            Gaps = gaps == 1 ? Gap(Middle) : Gap(Upper) + Gap(Lower),
            Stars = (gaps, stars) switch
            {
                // Junior officers: either side of the one gap.
                (1, _) => Star(20f, 6.5f, Small) + Star(20f, 19.5f, Small),
                // Senior officers: one between the gaps, or two on them and a third further in between.
                (_, 1) => Star(22f, Middle, Small),
                _ => Star(18f, Upper, Small) + Star(18f, Lower, Small) + Star(30f, Middle, Small),
            },
        };
    }

    /// <summary>An admiral's board: woven in a zigzag, no gap, large stars in a row; or the one larger star of a marshal's rank, with rays.</summary>
    public static Insignia FlagBoard(int stars, bool marshal = false)
    {
        float radius = marshal ? 8f : stars > 1 ? 5.5f : 6.5f, first = marshal ? 26f : stars > 1 ? 14f : 24f, step = 2f * radius + 2f;
        float last = first + (stars - 1) * step;
        var board = new Insignia { Stars = string.Concat(Enumerable.Range(0, stars).Select(i => Star(first + i * step, Middle, radius))) };

        var weave = new StringBuilder();
        // Two zigzags along the board, clear of the stars and the button.
        float from = last + radius + (marshal ? 8f : 6f);
        foreach (float y in new[] { 5f, 21f })
        {
            int points = 0;
            for (float at = from; at <= 72f; at += 3f, points++)
                weave.Append(points == 0 ? "M" : " L").Append(N(at)).Append(',').Append(N(y + (points % 2 == 0 ? 2.5f : -2.5f)));
            weave.Append(' ');
        }
        if (marshal)
            for (int i = 0; i < 20; i++)
            {
                float angle = (i + 0.5f) * (float)Math.PI / 10f;
                weave.Append($"M{N(first + 9.5f * Cos(angle))},{N(Middle + 9.5f * Sin(angle))} L{N(first + 12f * Cos(angle))},{N(Middle + 12f * Sin(angle))} ");
            }
        board.Weave = weave.ToString();
        return board;
    }

    /// <summary>
    /// Sleeve lace, as it lies across a shoulder board: stripes from the outer
    /// end inwards, the broad one of a flag officer first, then the emblem.
    /// </summary>
    /// <param name="half">A narrow stripe between the first two: a lieutenant commander's two and a half.</param>
    public static Insignia Lace(Emblem emblem, bool broad, int stripes, bool half = false)
    {
        const float Broad = 9f, Medium = 4f, Narrow = 2f, Between = 3f, Edge = 0.75f;
        var widths = new List<float>();
        if (broad) widths.Add(Broad);
        for (int i = 0; i < stripes; i++)
        {
            widths.Add(Medium);
            if (half && i == 0) widths.Add(Narrow);
        }
        var lace = new StringBuilder();
        float x = 6f;
        foreach (float width in widths)
        {
            lace.Append(Bar(x, Edge, width, Height - 2f * Edge));
            x += width + Between;
        }
        // x is now one space past the last stripe.
        var insignia = new Insignia { Gaps = lace.ToString() };
        insignia.Stars = emblem switch
        {
            Emblem.Star => Star(x + 6f, Middle, 5.5f),
            // The curl is a loop standing on the last stripe.
            Emblem.Curl => Disc(x + 1f, Middle, 5f) + Disc(x + 1f, Middle, 2.6f, hole: true),
            _ => Blossom(x + 6f, Middle),
        };
        return insignia;
    }

    /// <summary>A cherry blossom: five petals round a centre.</summary>
    static string Blossom(float x, float y)
    {
        var blossom = new StringBuilder(Disc(x, y, 1f));
        for (int i = 0; i < 5; i++)
        {
            float angle = i * 2f * (float)Math.PI / 5f;
            blossom.Append(Disc(x + 3.3f * Cos(angle), y + 3.3f * Sin(angle), 1.9f));
        }
        return blossom.ToString();
    }
}
