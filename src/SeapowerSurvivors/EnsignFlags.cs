using System;
using System.Collections.Generic;
using UnityEngine;

namespace SeapowerSurvivors;

/// <summary>
/// The ensigns as textures, painted in code as the rings' icons are, so the mod
/// ships no image files. Each is a function from a point on the flag (u across,
/// v down, both 0 to 1) to a colour; clear shows through a swallowtail. Crests
/// are simplified to a plain shield: at this size they would be a smudge anyway.
/// One texture serves both the menu and the HUD.
/// </summary>
static class EnsignFlags
{
    const int Width = 120, Height = 80, Samples = 3;
    /// <summary>Flags are 3:2, so one unit across is this much of one unit down.</summary>
    const float Aspect = 1.5f;

    static readonly Dictionary<Ensign, Texture2D> built = new Dictionary<Ensign, Texture2D>();
    static readonly Dictionary<Ensign, Noesis.ImageSource> sources = new Dictionary<Ensign, Noesis.ImageSource>();

    static Color C(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    static readonly Color White = C(0xFFFFFF), Red = C(0xC8102E), Navy = C(0x012169), Gold = C(0xFFCC00), Black = C(0x111111), Clear = new Color(0f, 0f, 0f, 0f);

    static Color Bands(float t, params Color[] colours) => colours[Mathf.Min(colours.Length - 1, (int)(t * colours.Length))];

    /// <summary>A cross, its upright at <paramref name="at"/> across and its arm halfway down.</summary>
    static bool Cross(float u, float v, float at, float half) =>
        Mathf.Abs(v - 0.5f) < half || Mathf.Abs(u - at) * Aspect < half;

    static bool Disc(float u, float v, float cu, float cv, float r)
    {
        float du = (u - cu) * Aspect, dv = v - cv;
        return du * du + dv * dv < r * r;
    }

    static bool Star(float u, float v, float cu, float cv, float r, int points = 5, float inner = 0.382f)
    {
        float du = (u - cu) * Aspect, dv = v - cv, d = Mathf.Sqrt(du * du + dv * dv);
        if (d > r) return false;
        float sector = 2f * Mathf.PI / points;
        float a = Mathf.Abs(Mathf.Repeat(Mathf.Atan2(du, -dv) + sector / 2f, sector) - sector / 2f);
        // Inside if this point is on the centre's side of the edge from the tip to the notch.
        var tip = new Vector2(r, 0f);
        var notch = new Vector2(r * inner * Mathf.Cos(sector / 2f), r * inner * Mathf.Sin(sector / 2f));
        var p = new Vector2(d * Mathf.Cos(a), d * Mathf.Sin(a));
        return (notch.x - tip.x) * (p.y - tip.y) - (notch.y - tip.y) * (p.x - tip.x) >= 0f;
    }

    /// <summary>The small plain shield standing in for a crest.</summary>
    static bool Shield(float u, float v, float cu, float cv, float h)
    {
        float du = Mathf.Abs(u - cu) * Aspect, dv = v - cv;
        if (dv < -h || dv > h) return false;
        return dv < 0.2f * h ? du < 0.75f * h : du < 0.75f * h * (h - dv) / (0.8f * h);
    }

    /// <summary>The Union Flag, in its own 0 to 1 square, for a canton.</summary>
    static Color Union(float u, float v)
    {
        float d1 = Mathf.Abs(u - v), d2 = Mathf.Abs(u + v - 1f);
        if (Mathf.Abs(u - 0.5f) < 0.06f || Mathf.Abs(v - 0.5f) < 0.1f) return Red;
        if (Mathf.Abs(u - 0.5f) < 0.1f || Mathf.Abs(v - 0.5f) < 0.17f) return White;
        if (d1 < 0.035f || d2 < 0.035f) return Red;
        if (d1 < 0.1f || d2 < 0.1f) return White;
        return Navy;
    }

    /// <summary>True in the notch cut from the fly of a swallowtail.</summary>
    static bool Cut(float u, float v, float from) =>
        u > from && Mathf.Abs(v - 0.5f) < 0.5f * (u - from) / (1f - from);

    /// <summary>True in the two notches of a three-pointed Scandinavian fly, either side of a tongue along the middle.</summary>
    static bool CutWithTongue(float u, float v, float from)
    {
        if (u <= from) return false;
        float off = Mathf.Abs(v - 0.5f), depth = (u - from) / (1f - from);
        return off > 0.07f && off < 0.07f + 0.43f * depth;
    }

    static readonly Dictionary<string, Func<float, float, Color>> Painters = new Dictionary<string, Func<float, float, Color>>
    {
        [Ensigns.HeavyGuns.Id] = (u, v) =>
        {
            if (u < 0.4f && v < 7f / 13f)
            {
                float x = u / 0.4f * 6f, y = v / (7f / 13f) * 5f;
                float fx = x - Mathf.Floor(x) - 0.5f, fy = y - Mathf.Floor(y) - 0.5f;
                return fx * fx + fy * fy < 0.07f ? White : C(0x0A3161);
            }
            return (int)(v * 13f) % 2 == 0 ? C(0xB31942) : White;
        },
        [Ensigns.HeavyMissiles.Id] = (u, v) =>
        {
            if (v > 0.86f) return C(0x1C5FB5);
            if (Star(u, v, 0.28f, 0.34f, 0.22f)) return Red;
            // The hammer and sickle, reduced: a crescent crossed by a bar.
            bool ring = Disc(u, v, 0.64f, 0.42f, 0.17f) && !Disc(u, v, 0.68f, 0.38f, 0.14f);
            bool bar = Mathf.Abs((u - 0.64f) * Aspect - (v - 0.44f)) < 0.025f && Disc(u, v, 0.64f, 0.44f, 0.15f);
            return ring || bar ? Red : White;
        },
        [Ensigns.LongCiws.Id] = (u, v) => Bands(v, C(0xAE1C28), White, C(0x21468B)),
        [Ensigns.EmpMortars.Id] = (u, v) =>
            Mathf.Abs(u - v) < 0.1f || Mathf.Abs(u + v - 1f) < 0.1f ? C(0x0B3D91) : White,
        [Ensigns.DoubleAir.Id] = (u, v) =>
        {
            if (u < 0.5f && v < 0.5f) return Union(u * 2f, v * 2f);
            var blue = C(0x00008B);
            if (Star(u, v, 0.25f, 0.76f, 0.13f, 7, 0.45f)) return blue;
            return Star(u, v, 0.75f, 0.18f, 0.07f, 7, 0.45f) || Star(u, v, 0.75f, 0.82f, 0.07f, 7, 0.45f) ||
                   Star(u, v, 0.62f, 0.45f, 0.07f, 7, 0.45f) || Star(u, v, 0.88f, 0.4f, 0.07f, 7, 0.45f) ||
                   Star(u, v, 0.82f, 0.56f, 0.04f) ? blue : White;
        },
        [Ensigns.FastAsms.Id] = (u, v) =>
        {
            if (u < 0.5f && v < 0.5f) return Bands(v * 2f, C(0xFF9933), White, C(0x138808));
            if (Disc(u, v, 0.75f, 0.5f, 0.2f)) return Shield(u, v, 0.75f, 0.5f, 0.11f) ? Gold : C(0x000080);
            return White;
        },
        [Ensigns.FiveOfAKind.Id] = (u, v) =>
        {
            float cantonHeight = 5f / 9f;
            if (u < 0.37f && v < cantonHeight) return Mathf.Abs(v - cantonHeight / 2f) < 0.055f || Mathf.Abs(u - 0.185f) * Aspect < 0.055f ? White : C(0x0D5EAF);
            return (int)(v * 9f) % 2 == 0 ? C(0x0D5EAF) : White;
        },
        [Ensigns.RareCards.Id] = (u, v) =>
        {
            if (CutWithTongue(u, v, 0.8f)) return Clear;
            if (Cross(u, v, 0.33f, 0.06f)) return C(0x00205B);
            return Cross(u, v, 0.33f, 0.12f) ? White : C(0xBA0C2F);
        },
        [Ensigns.LongRange.Id] = (u, v) =>
        {
            if (u < 0.5f && v < 0.5f) return Union(u * 2f, v * 2f);
            return Cross(u, v, 0.5f, 0.07f) ? Red : White;
        },
        [Ensigns.FastTorpedoes.Id] = (u, v) =>
            Shield(u, v, 0.5f, 0.5f, 0.2f) ? (Disc(u, v, 0.5f, 0.46f, 0.08f) ? C(0x1C5FB5) : Gold) : Bands(u, C(0x008C45), White, C(0xCD212A)),
        [Ensigns.PointBlank.Id] = (u, v) =>
        {
            if (Cut(u, v, 0.75f)) return Clear;
            if (Cross(u, v, 0.36f, 0.05f)) return C(0xDC1E35);
            return Cross(u, v, 0.36f, 0.1f) ? White : C(0x02529C);
        },
        [Ensigns.HitProof.Id] = (u, v) =>
        {
            var red = C(0xE30A17);
            if (Star(u, v, 0.59f, 0.5f, 0.12f)) return White;
            return Disc(u, v, 0.37f, 0.5f, 0.25f) && !Disc(u, v, 0.42f, 0.5f, 0.2f) ? White : red;
        },
        [Ensigns.FreeCards.Id] = (u, v) =>
        {
            if (Shield(u, v, 0.3f, 0.5f, 0.15f)) return C(0xAA151B);
            return v < 0.25f || v > 0.75f ? C(0xAA151B) : C(0xF1BF00);
        },
        [Ensigns.LastStand.Id] = (u, v) =>
        {
            if (Cut(u, v, 0.75f)) return Clear;
            return Cross(u, v, 0.33f, 0.07f) ? White : C(0xC8102E);
        },
        [Ensigns.TorpedoEcm.Id] = (u, v) =>
        {
            if (Cut(u, v, 0.82f)) return Clear;
            if (Shield(u, v, 0.4f, 0.5f, 0.15f)) return Disc(u, v, 0.4f, 0.48f, 0.07f) ? Black : Gold;
            return Bands(v, Black, C(0xDD0000), Gold);
        },
        [Ensigns.ChestsTwice.Id] = (u, v) =>
        {
            if (u < 0.5f && v < 0.5f) return Union(u * 2f, v * 2f);
            return Star(u, v, 0.75f, 0.18f, 0.07f) || Star(u, v, 0.75f, 0.8f, 0.07f) ||
                   Star(u, v, 0.63f, 0.46f, 0.07f) || Star(u, v, 0.87f, 0.4f, 0.07f) ? Red : White;
        },
        [Ensigns.MoreChoice.Id] = (u, v) => Bands(u, C(0x169B62), White, C(0xFF883E)),
        [Ensigns.WireGuided.Id] = (u, v) =>
        {
            if (CutWithTongue(u, v, 0.8f)) return Clear;
            return Cross(u, v, 0.33f, 0.09f) ? Gold : C(0x005BAA);
        },
        [Ensigns.StealthAir.Id] = (u, v) =>
        {
            if (Cut(u, v, 0.82f)) return Clear;
            return Bands(v, C(0x0072CE), Black, White);
        },
        [Ensigns.IronBombs.Id] = (u, v) =>
        {
            if (Disc(u, v, 0.5f, 0.5f, 0.1f)) return Gold;
            return Star(u, v, 0.5f, 0.5f, 0.15f, 16, 0.6f) ? Gold : Bands(v, C(0x74ACDF), White, C(0x74ACDF));
        },
    };

    /// <summary>
    /// The flags on the Survivors screen's navy tabs. Four are the ensigns
    /// painted above; China's and Japan's naval ensigns are painted here.
    /// </summary>
    static readonly Dictionary<string, Func<float, float, Color>> NavyPainters = new Dictionary<string, Func<float, float, Color>>
    {
        [Navies.Us.Id] = Painters[Ensigns.HeavyGuns.Id],
        [Navies.Soviet.Id] = Painters[Ensigns.HeavyMissiles.Id],
        [Navies.Britain.Id] = Painters[Ensigns.LongRange.Id],
        [Navies.Argentina.Id] = Painters[Ensigns.IronBombs.Id],
        // Red over three blue and two white stripes, a gold star in the canton.
        [Navies.China.Id] = (u, v) =>
        {
            if (v > 0.6f) return (int)((v - 0.6f) / 0.4f * 5f) % 2 == 0 ? C(0x1C3F94) : White;
            return Star(u, v, 0.22f, 0.3f, 0.2f) ? Gold : C(0xDE2910);
        },
        // The rising sun, set towards the hoist, with sixteen rays.
        [Navies.Japan.Id] = (u, v) =>
        {
            var red = C(0xBC002D);
            if (Disc(u, v, 0.42f, 0.5f, 0.22f)) return red;
            float angle = Mathf.Atan2(v - 0.5f, (u - 0.42f) * Aspect) / (2f * Mathf.PI) * 32f;
            return Mathf.Abs(Mathf.FloorToInt(angle + 0.5f)) % 2 == 0 ? red : White;
        },
    };

    static readonly Dictionary<string, Noesis.ImageSource> navySources = new Dictionary<string, Noesis.ImageSource>();

    /// <summary>A navy's flag for the menu's views.</summary>
    internal static Noesis.ImageSource Source(Navy navy)
    {
        if (navySources.TryGetValue(navy.Id, out var source)) return source;
        return navySources[navy.Id] = new Noesis.TextureSource(Paint(NavyPainters[navy.Id], "Navy " + navy.Id));
    }

    internal static Texture2D Texture(Ensign ensign)
    {
        if (built.TryGetValue(ensign, out var texture) && texture != null) return texture;
        return built[ensign] = Paint(Painters[ensign.Id], "Ensign " + ensign.Id);
    }

    static Texture2D Paint(Func<float, float, Color> paint, string name)
    {
        var pixels = new Color32[Width * Height];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                float r = 0f, g = 0f, b = 0f, alpha = 0f;
                for (int sy = 0; sy < Samples; sy++)
                    for (int sx = 0; sx < Samples; sx++)
                    {
                        // Texture rows run bottom to top; the painters' v runs down.
                        var c = paint((x + (sx + 0.5f) / Samples) / Width, 1f - (y + (sy + 0.5f) / Samples) / Height);
                        r += c.r * c.a;
                        g += c.g * c.a;
                        b += c.b * c.a;
                        alpha += c.a;
                    }
                pixels[y * Width + x] = alpha > 0f
                    ? new Color(r / alpha, g / alpha, b / alpha, alpha / (Samples * Samples))
                    : Clear;
            }
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, true)
        {
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
            name = name,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    /// <summary>The same texture for the menu's views.</summary>
    internal static Noesis.ImageSource Source(Ensign ensign)
    {
        if (sources.TryGetValue(ensign, out var source)) return source;
        return sources[ensign] = new Noesis.TextureSource(Texture(ensign));
    }
}
