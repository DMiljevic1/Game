# Procedural pixel-art texture atlas for the house.
#
# Lethal Company's look is not high-detail: it is low texel density, hard
# pixels, muted palettes and a bit of grime. So every tile here is generated
# at 128x128 and read back with point filtering — the chunkiness is the point.
#
# Pure stdlib (zlib + struct), no Blender API, so it runs anywhere and writes
# byte-exact sRGB. Going through Blender's image buffer would drag the pixels
# through its linear colour pipeline and shift every value.

import math
import random
import struct
import zlib

TILE = 128          # pixels per tile
GRID = 4            # tiles per row/column
SIZE = TILE * GRID  # 512x512 atlas

# Tile slots. Index = row * 4 + col, row 0 at the top of the image.
FLOOR_WOOD = 0
FLOOR_TILE = 1
WALL_PLASTER = 2
SIDING = 3
SHINGLE = 4
DOOR_WOOD = 5
TRIM = 6
CONCRETE = 7
GLASS = 8
CEILING = 9
METAL = 10
WOOD_DARK = 11
PLASTER_GRIMY = 12
BRICK = 13
FLOOR_WORN = 14
TARP = 15


def _mix(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def _clamp(v):
    return 0 if v < 0 else (255 if v > 255 else int(v))


def _shade(c, amount):
    return tuple(_clamp(v + amount) for v in c)


def _blank(colour):
    return [[colour for _ in range(TILE)] for _ in range(TILE)]


def _grain(px, rng, strength, coverage=1.0):
    """Per-pixel value noise. Wraps trivially because it is per-pixel."""
    for y in range(TILE):
        row = px[y]
        for x in range(TILE):
            if coverage < 1.0 and rng.random() > coverage:
                continue
            row[x] = _shade(row[x], rng.randint(-strength, strength))


def _blotches(px, rng, colour, count, radius, strength):
    """Soft dirt patches. Wraps in both axes so the tile still tiles."""
    for _ in range(count):
        cx = rng.randrange(TILE)
        cy = rng.randrange(TILE)
        r = rng.randint(radius // 2, radius)
        for dy in range(-r, r + 1):
            for dx in range(-r, r + 1):
                d = math.hypot(dx, dy)
                if d > r:
                    continue
                t = (1.0 - d / r) * strength
                x = (cx + dx) % TILE
                y = (cy + dy) % TILE
                px[y][x] = _mix(px[y][x], colour, t)


def _planks(base, dark, light, count, vertical, rng, gap=1):
    """Tileable plank boards running across the tile."""
    px = _blank(base)
    step = TILE // count
    for i in range(count):
        tone = rng.uniform(-0.18, 0.18)
        board = _mix(base, light if tone > 0 else dark, abs(tone))
        start = i * step
        for j in range(step):
            pos = start + j
            for k in range(TILE):
                x, y = (pos, k) if vertical else (k, pos)
                px[y][x] = board
        # Seam between boards, plus a highlight on the far side of it.
        for k in range(TILE):
            for g in range(gap):
                pos = (start + g) % TILE
                x, y = (pos, k) if vertical else (k, pos)
                px[y][x] = dark
            pos = (start + gap) % TILE
            x, y = (pos, k) if vertical else (k, pos)
            px[y][x] = _mix(board, light, 0.35)
        # Nail heads and short end-joints so boards do not read as one slab.
        if rng.random() < 0.6:
            joint = rng.randrange(TILE)
            for j in range(step):
                pos = start + j
                x, y = (pos, joint) if vertical else (joint, pos)
                px[y][x] = _shade(dark, -8)
    _grain(px, rng, 6)
    return px


def tile_floor_wood(rng):
    px = _planks((109, 86, 60), (58, 44, 30), (150, 124, 92), 4, False, rng)
    _blotches(px, rng, (52, 42, 32), 6, 22, 0.35)
    return px


def tile_floor_worn(rng):
    px = _planks((96, 78, 58), (48, 38, 28), (132, 112, 86), 5, False, rng)
    _blotches(px, rng, (40, 34, 28), 10, 26, 0.45)
    _grain(px, rng, 10)
    return px


def tile_floor_tile(rng):
    a, b, grout = (168, 163, 149), (128, 124, 112), (74, 72, 66)
    px = _blank(a)
    cells = 4
    step = TILE // cells
    for cy in range(cells):
        for cx in range(cells):
            colour = a if (cx + cy) % 2 == 0 else b
            colour = _shade(colour, rng.randint(-6, 6))
            for y in range(cy * step, (cy + 1) * step):
                for x in range(cx * step, (cx + 1) * step):
                    px[y][x] = colour
    for i in range(cells):
        for k in range(TILE):
            px[(i * step) % TILE][k] = grout
            px[k][(i * step) % TILE] = grout
    _grain(px, rng, 5)
    _blotches(px, rng, (60, 58, 52), 5, 18, 0.3)
    return px


def tile_plaster(rng, base=(176, 166, 144), dirt=0.25):
    px = _blank(base)
    _blotches(px, rng, (128, 118, 98), 14, 30, dirt)
    _blotches(px, rng, (198, 190, 170), 8, 24, dirt * 0.6)
    _grain(px, rng, 7)
    # Hairline cracks — a couple of dark random walks.
    for _ in range(2):
        x, y = rng.randrange(TILE), rng.randrange(TILE)
        for _ in range(rng.randint(30, 70)):
            px[y % TILE][x % TILE] = _shade(px[y % TILE][x % TILE], -28)
            x += rng.choice((-1, 0, 1))
            y += rng.choice((0, 1, 1))
    return px


def tile_plaster_grimy(rng):
    px = tile_plaster(rng, base=(150, 140, 120), dirt=0.4)
    _blotches(px, rng, (92, 84, 70), 12, 34, 0.4)
    return px


def tile_siding(rng):
    px = _planks((124, 118, 100), (68, 64, 54), (158, 152, 132), 6, True, rng)
    _blotches(px, rng, (78, 76, 66), 8, 26, 0.3)
    return px


def tile_shingle(rng):
    base, dark = (78, 74, 70), (44, 42, 40)
    px = _blank(base)
    rows = 8
    step = TILE // rows
    for r in range(rows):
        offset = (r % 2) * (step // 2)
        tone = _shade(base, rng.randint(-10, 10))
        for y in range(r * step, (r + 1) * step):
            for x in range(TILE):
                px[y][x] = tone
        for x in range(TILE):
            px[(r * step) % TILE][x] = dark            # row shadow
            px[(r * step + 1) % TILE][x] = _shade(tone, 14)
        for t in range(4):                              # tab splits
            x = (offset + t * (TILE // 4)) % TILE
            for y in range(r * step, (r + 1) * step):
                px[y][x] = dark
    _grain(px, rng, 8)
    _blotches(px, rng, (58, 62, 56), 6, 20, 0.3)
    return px


def tile_door_wood(rng):
    px = _planks((122, 92, 58), (62, 46, 28), (158, 126, 86), 3, True, rng)
    _blotches(px, rng, (70, 54, 36), 5, 18, 0.3)
    return px


def tile_trim(rng):
    px = _planks((94, 74, 50), (48, 38, 25), (126, 102, 72), 2, False, rng)
    return px


def tile_wood_dark(rng):
    px = _planks((70, 56, 40), (34, 27, 20), (96, 78, 56), 4, False, rng)
    return px


def tile_concrete(rng):
    px = _blank((118, 114, 106))
    _blotches(px, rng, (92, 90, 84), 16, 30, 0.35)
    _blotches(px, rng, (140, 136, 128), 10, 22, 0.25)
    _grain(px, rng, 9)
    return px


def tile_glass(rng):
    px = _blank((58, 72, 82))
    for y in range(TILE):
        for x in range(TILE):
            # Triangle wave, not a saw: a saw would leave a hard seam at the
            # tile edge where the sheen jumps from bright back to dark.
            t = abs(((x + y) % TILE) / TILE * 2.0 - 1.0)
            px[y][x] = _mix((44, 56, 66), (104, 124, 134), t * 0.7)
    _grain(px, rng, 4)
    _blotches(px, rng, (150, 165, 172), 3, 14, 0.25)
    return px


def tile_ceiling(rng):
    px = _blank((188, 182, 168))
    _blotches(px, rng, (150, 142, 126), 10, 26, 0.3)
    _blotches(px, rng, (128, 116, 96), 3, 16, 0.35)   # water stains
    _grain(px, rng, 5)
    return px


def tile_metal(rng):
    px = _blank((132, 132, 128))
    for y in range(TILE):
        for x in range(TILE):
            px[y][x] = _shade(px[y][x], rng.randint(-14, 14) if x % 3 == 0 else rng.randint(-6, 6))
    _blotches(px, rng, (108, 84, 62), 5, 16, 0.3)      # rust
    return px


def tile_brick(rng):
    mortar = (146, 140, 128)
    px = _blank(mortar)
    rows, cols = 8, 4
    rh, cw = TILE // rows, TILE // cols
    for r in range(rows):
        offset = (r % 2) * (cw // 2)
        for c in range(cols):
            colour = _shade((124, 74, 60), rng.randint(-16, 16))
            for y in range(r * rh + 1, (r + 1) * rh - 1):
                for x in range(c * cw + 1, (c + 1) * cw - 1):
                    px[y % TILE][(x + offset) % TILE] = colour
    _grain(px, rng, 6)
    return px


def tile_tarp(rng):
    px = _blank((72, 84, 74))
    _blotches(px, rng, (52, 62, 54), 12, 28, 0.4)
    _grain(px, rng, 8)
    return px


BUILDERS = {
    FLOOR_WOOD: tile_floor_wood,
    FLOOR_TILE: tile_floor_tile,
    WALL_PLASTER: tile_plaster,
    SIDING: tile_siding,
    SHINGLE: tile_shingle,
    DOOR_WOOD: tile_door_wood,
    TRIM: tile_trim,
    CONCRETE: tile_concrete,
    GLASS: tile_glass,
    CEILING: tile_ceiling,
    METAL: tile_metal,
    WOOD_DARK: tile_wood_dark,
    PLASTER_GRIMY: tile_plaster_grimy,
    BRICK: tile_brick,
    FLOOR_WORN: tile_floor_worn,
    TARP: tile_tarp,
}


def _posterize(px, step=11):
    """Snap to a coarse palette. Smooth gradients read as photographic; the
    banding is what sells the low-fi look once point filtering is on."""
    for y in range(TILE):
        row = px[y]
        for x in range(TILE):
            row[x] = tuple(_clamp(int(v / step + 0.5) * step) for v in row[x])


def build_atlas(seed=7):
    atlas = [[(0, 0, 0)] * SIZE for _ in range(SIZE)]
    for index, fn in BUILDERS.items():
        px = fn(random.Random(seed + index * 101))
        _posterize(px)
        col, row = index % GRID, index // GRID
        ox, oy = col * TILE, row * TILE
        for y in range(TILE):
            dst = atlas[oy + y]
            src = px[y]
            for x in range(TILE):
                dst[ox + x] = src[x]
    return atlas


def write_png(path, rows):
    """Minimal 8-bit RGB PNG writer — avoids any colour management."""
    raw = b"".join(b"\x00" + bytes(v for px in row for v in px) for row in rows)

    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", len(rows[0]), len(rows), 8, 2, 0, 0, 0)
    with open(path, "wb") as fh:
        fh.write(b"\x89PNG\r\n\x1a\n")
        fh.write(chunk(b"IHDR", header))
        fh.write(chunk(b"IDAT", zlib.compress(raw, 9)))
        fh.write(chunk(b"IEND", b""))


if __name__ == "__main__":
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "House_Atlas.png"
    write_png(out, build_atlas())
    print("wrote", out)
