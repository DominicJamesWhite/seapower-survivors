#!/usr/bin/env python3
"""Draw the location cards' charts from the game's own terrain files.

Each view is centred on the area's deployment boxes in
src/SeapowerSurvivors/Areas.cs, which are not drawn. Heights come from the game's terrain chunks: little-endian shorts in metres,
2400 by 2400 at 1/120 of a degree for each 20 degree square, LZ4 compressed.

    python tools/render_charts.py                            every area, into mod/art
    python tools/render_charts.py --out DIR giuk_gap aegean_sea
    python tools/render_charts.py --game "E:/Games/Sea Power"

Needs numpy, scipy, Pillow and lz4.
"""
import argparse
import re
from pathlib import Path

import lz4.block
import numpy as np
from PIL import Image
from scipy import ndimage

REPO = Path(__file__).resolve().parent.parent
DEFAULT_GAME = Path(r"D:\SteamLibrary\steamapps\common\Sea Power")

WIDTH, HEIGHT = 1184, 640
CHUNK = 2400
PER_DEGREE = 120

# How much room to leave round the two boxes, as a share of the span they cover.
MARGIN = 0.3
MIN_MARGIN_DEG = 0.35

# Depth and height, in metres, against colour. The tones sit under the game's dark UI.
SEA = [(0, 0x51707A), (40, 0x466470), (200, 0x35505E), (1000, 0x28404E), (3000, 0x1E303D), (6000, 0x16232D)]
LAND = [(0, 0x57564E), (150, 0x5F5E55), (800, 0x75736A), (2000, 0x8F8D84), (4500, 0xAAA9A2)]
CONTOURS = [50, 200, 500, 1000, 2000, 3000, 4000, 5000]
COAST = 0xB4B8BC
# The drawn slope, after exaggeration, of a map's steepest ground.
RELIEF_SLOPE = 0.45


def rgb(value):
    return (value >> 16) & 255, (value >> 8) & 255, value & 255


def read_areas():
    source = (REPO / "src/SeapowerSurvivors/Areas.cs").read_text(encoding="utf-8")
    areas = {}
    for block in source.split('new Area("')[1:]:
        area_id = block.split('"', 1)[0]
        corners = [(float(a), float(b)) for a, b in re.findall(r"\{\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\}", block)]
        areas[area_id] = corners[:4], corners[4:8]
    return areas


def frame(player, enemy):
    """South, north, west and east of a view holding both boxes, at the card's shape."""
    lats = [c[0] for c in player + enemy]
    lons = [c[1] for c in player + enemy]
    south, north, west, east = min(lats), max(lats), min(lons), max(lons)
    pad_lat = max((north - south) * MARGIN, MIN_MARGIN_DEG)
    pad_lon = max((east - west) * MARGIN, MIN_MARGIN_DEG)
    south, north, west, east = south - pad_lat, north + pad_lat, west - pad_lon, east + pad_lon

    squash = np.cos(np.radians((south + north) / 2))
    tall, wide = north - south, (east - west) * squash
    shape = WIDTH / HEIGHT
    if wide / tall < shape:
        extra = (tall * shape - wide) / squash / 2
        west, east = west - extra, east + extra
    else:
        extra = (wide / shape - tall) / 2
        south, north = south - extra, north + extra
    return south, north, west, east


def load_chunk(folder, cache, y, x):
    name = f"y{y}x{x:02d}"
    if name not in cache:
        data = (folder / f"{name}.lz4").read_bytes()
        # An LZ4 block, then the uncompressed size as a footer of four bytes.
        size = int.from_bytes(data[-4:], "little")
        if size != CHUNK * CHUNK * 2:
            raise ValueError(f"{name}: footer says {size} bytes, expected {CHUNK * CHUNK * 2}")
        cache[name] = np.frombuffer(lz4.block.decompress(data[:-4], uncompressed_size=size), dtype="<i2").reshape(CHUNK, CHUNK)
    return cache[name]


def mosaic(folder, cache, top, bottom, left, right):
    """Heights for rows top..bottom and columns left..right of the world grid."""
    out = np.zeros((bottom - top, right - left), dtype=np.float32)
    for row in range(top // CHUNK, (bottom - 1) // CHUNK + 1):
        for col in range(left // CHUNK, (right - 1) // CHUNK + 1):
            chunk = load_chunk(folder, cache, row + 1, col + 1)
            r0, r1 = max(top, row * CHUNK), min(bottom, (row + 1) * CHUNK)
            c0, c1 = max(left, col * CHUNK), min(right, (col + 1) * CHUNK)
            out[r0 - top:r1 - top, c0 - left:c1 - left] = chunk[r0 - row * CHUNK:r1 - row * CHUNK, c0 - col * CHUNK:c1 - col * CHUNK]
    return out


def sample(folder, cache, view):
    south, north, west, east = view
    lats = north - (np.arange(HEIGHT) + 0.5) / HEIGHT * (north - south)
    lons = west + (np.arange(WIDTH) + 0.5) / WIDTH * (east - west)
    rows = (90 - lats) * PER_DEGREE - 0.5
    cols = (lons + 180) * PER_DEGREE - 0.5
    top, bottom = int(rows.min()) - 4, int(rows.max()) + 5
    left, right = int(cols.min()) - 4, int(cols.max()) + 5
    grid = mosaic(folder, cache, top, bottom, left, right)
    r, c = np.meshgrid(rows - top, cols - left, indexing="ij")
    return ndimage.map_coordinates(grid, [r, c], order=3, mode="nearest").astype(np.float32)


def ramp(values, stops):
    points = [s[0] for s in stops]
    channels = np.stack([np.interp(values, points, [rgb(s[1])[i] for s in stops]) for i in range(3)], axis=-1)
    return channels.astype(np.float32)


def relief(heights, metres_per_px):
    """How lit each pixel is against flat ground, with the light from the north-west."""
    dy, dx = np.gradient(ndimage.gaussian_filter(heights, 2.5), metres_per_px)
    slope = np.hypot(dx, dy)

    def scale(mask, most):
        # Enough to show this map's slopes, whether it is a flat basin or a steep archipelago.
        steep = np.percentile(slope[mask], 95) if mask.any() else 0.0
        return min(most, RELIEF_SLOPE / steep) if steep > 0 else most

    sea = heights < 0
    exaggerate = np.where(sea, scale(sea, 18.0), scale(~sea, 5.0))
    dx, dy = dx * exaggerate, dy * exaggerate
    light = np.array([-0.5, 0.5, 0.71])  # x east, y south-positive rows, z up
    length = np.sqrt(dx * dx + dy * dy + 1)
    lit = (-dx * light[0] - dy * light[1] + light[2]) / length
    return np.clip(lit / light[2], 0.72, 1.25)


def edges(mask):
    """Soft one-pixel lines where a boolean field changes."""
    line = mask ^ ndimage.binary_erosion(mask, border_value=1)
    return ndimage.gaussian_filter(line.astype(np.float32), 0.6) * 1.6


def blend(image, alpha, colour):
    alpha = np.clip(alpha, 0, 1)[..., None]
    return image * (1 - alpha) + np.array(rgb(colour), dtype=np.float32) * alpha


def render(folder, cache, player, enemy):
    view = frame(player, enemy)
    heights = sample(folder, cache, view)
    south, north = view[0], view[1]
    metres_per_px = (north - south) * 111320 / HEIGHT

    sea = heights < 0
    image = np.where(sea[..., None], ramp(-heights, SEA), ramp(np.maximum(heights, 0), LAND))
    image = image * relief(heights, metres_per_px)[..., None]
    for depth in CONTOURS:
        image = blend(image, edges(heights < -depth) * 0.13, COAST)
    image = blend(image, edges(sea) * 0.75, COAST)
    return Image.fromarray(np.clip(image, 0, 255).astype(np.uint8), "RGB")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("areas", nargs="*", help="area ids; every area if none")
    parser.add_argument("--game", type=Path, default=DEFAULT_GAME)
    parser.add_argument("--out", type=Path, default=REPO / "mod/art")
    args = parser.parse_args()

    folder = args.game / "Sea Power_Data/StreamingAssets/original/terrain/data"
    areas = read_areas()
    unknown = [a for a in args.areas if a not in areas]
    if unknown:
        parser.error(f"unknown area(s): {', '.join(unknown)}; known: {', '.join(areas)}")

    args.out.mkdir(parents=True, exist_ok=True)
    cache = {}
    for area_id in args.areas or areas:
        player, enemy = areas[area_id]
        path = args.out / f"{area_id}.png"
        render(folder, cache, player, enemy).save(path, optimize=True)
        print(f"{path}  {path.stat().st_size // 1024} KB")


if __name__ == "__main__":
    main()
