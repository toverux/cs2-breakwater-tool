"""Draws breakwater-tool-landscaping.svg, the tool's item icon in the Landscaping menu.

A template more than a tool: the SVG's shapes are derived from the few numbers below (a sunk rock
is cut at the water, its lower part redrawn in blue, the ground on the wall run under it), so a
change to the icon, or a new icon in this style, starts here and not in the SVG.
Run it with `python3` to write the SVG beside it.
"""
from pathlib import Path

# Palette, lifted from the game's own icons.
# PropsStone.svg's three greys, spread apart: the game draws small shapes soft, and the vanilla
# tones, close to one another, merged the rocks into one mass in the menu.
ROCK_TOP, ROCK_MID, ROCK_DARK = "#bdb9b9", "#808080", "#555454"
UNDER_TOP, UNDER_MID, UNDER_DARK = "#5AAED4", "#4EA3C9", "#4195BB"  # the same, 65% water
WATER, WATER_LIGHT = "#2CAFE9", "#50D5F3"  # Water.svg
WALL_SEA_DARK, WALL_SEA = "#155F92", "#1E86C4"
WALL_EARTH = "#583716"  # Texture*.svg, dark wall
SAND = "#FFD478"  # TextureSand.svg, its lit patch

# The vanilla tile: its top face (left, back, right, front corners) and the feet of its walls.
L, T, R, B = (9, 24.5), (29.3, 17), (54.8, 27.6), (34.6, 38.3)
FOOT_L, FOOT_B, FOOT_R = (9.1, 32.1), (34.4, 46.4), (54.5, 36)

SHORE = 0.42  # where the land ends, along the left-to-front edge
SLOPE_END = 0.88  # where the slanted ground reaches the wall's foot
DEPTH = 8.0
PUSH = 0.12  # the rocks stand this far behind where the slope sets their depth
LOWER_DROP = 1.5

# Three rocks: PropsStone.svg's polygons (front, side, top), then two reworked from it so that the
# six rocks do not read as copies. Coordinates are in a 64 box, the base centre on (30, 50).
ROCKS = [
    (
        [
            (8.4, 36.5), (12.3, 47.2), (29.9, 53.1), (40.8, 50.1), (44.9, 43.1), (44.9, 33.6),
            (31.7, 28.3), (23.5, 28.3), (11.4, 23.6),
        ],
        [(40.6, 50.1), (44.9, 42.7), (44.9, 33.5), (52.3, 26), (52.3, 36.5), (44.9, 47)],
        [
            (11.4, 23.5), (21.2, 12.1), (33.3, 10.2), (44.9, 12.1), (49.7, 17.8), (52.3, 26),
            (44.9, 34), (30.1, 30.5), (17.6, 34),
        ],
    ),
    (
        [
            (9.5, 26), (8, 38), (13.5, 48), (28, 53), (40, 50.5), (43.5, 44), (43.5, 34.5),
            (29, 31.5), (18, 34.5),
        ],
        [(40, 50.5), (43.5, 44), (43.5, 34.5), (52.5, 27.5), (52.5, 37.5), (46, 47)],
        [
            (9.5, 26), (17, 15.5), (29, 11), (42, 12.5), (50.5, 19), (52.5, 27.5), (43.5, 35),
            (29, 32.5), (18, 35.5),
        ],
    ),
    (
        [
            (12.5, 22), (9.5, 35), (12.5, 46.5), (31, 53), (41.5, 49.5), (45.5, 42.5), (45.5, 32.5),
            (32, 28.5), (20, 32),
        ],
        [(41.5, 49.5), (45.5, 42.5), (45.5, 32.5), (51.5, 27), (52, 37), (45.5, 46.5)],
        [
            (12.5, 22), (23, 11.5), (35.5, 10), (46, 13.5), (51, 20.5), (51.5, 27), (45.5, 33),
            (32, 29.5), (20, 33),
        ],
    ),
]

# (row, position along the shore, width, squash, lean, shape)
LAYOUT = [
    ("upper", 0.84, 11.5, 0.85, 0.04, 0),
    ("upper", 0.46, 12.5, 0.90, -0.04, 1),
    ("upper", 0.11, 11.5, 0.85, 0.0, 2),
    ("lower", 0.92, 10.5, 0.85, -0.04, 2),
    ("lower", 0.60, 11.5, 0.85, 0.0, 0),
    ("lower", 0.27, 11.5, 0.85, 0.03, 1),
]
ROW_U = {"upper": SHORE + 0.07, "lower": SHORE + 0.22}


def lerp(p, q, k):
    return (p[0] + (q[0] - p[0]) * k, p[1] + (q[1] - p[1]) * k)


def top(u, v):
    """A point of the top face, which is no parallelogram: u runs left to front, v left to back."""
    return lerp(lerp(L, B, u), lerp(T, R, u), v)


def fmt(n):
    return f"{n:.2f}".rstrip("0").rstrip(".")


def poly(points, fill):
    d = "M" + "L".join(f"{fmt(x)} {fmt(y)}" for x, y in points) + "Z"
    return f'<path d="{d}" fill="{fill}"/>'


def curved(points, ctrl, fill):
    """A polygon whose last edge, back to its first point, is a curve."""
    d = "M" + "L".join(f"{fmt(x)} {fmt(y)}" for x, y in points)
    d += f"Q{fmt(ctrl[0])} {fmt(ctrl[1])} {fmt(points[0][0])} {fmt(points[0][1])}Z"
    return f'<path d="{d}" fill="{fill}"/>'


def above(points, level):
    """The part of a polygon above a level line of the screen."""
    out = []
    for i, cur in enumerate(points):
        prev = points[i - 1]
        a, b = level - prev[1], level - cur[1]
        if (a >= 0) != (b >= 0):
            k = a / (a - b)
            out.append(lerp(prev, cur, k))
        if b >= 0:
            out.append(cur)
    return out


def rock_point(cx, cy, w, squash, lean, point):
    s = w / 44.0
    dx, dy = (point[0] - 30) * s, (point[1] - 50) * s * squash
    return (cx + dx + lean * dy, cy + dy)


def rock(cx, cy, w, squash, lean, shape, tones, waterline=None):
    out = []
    for points, tone in zip(ROCKS[shape], tones):
        placed = [rock_point(cx, cy, w, squash, lean, point) for point in points]
        if waterline is not None:
            placed = above(placed, waterline)
        if len(placed) > 2:
            out.append(poly(placed, tone))
    return out


def build():
    placed = []
    for row, v, w, squash, lean, shape in LAYOUT:
        u = ROW_U[row]
        under = DEPTH * (u - SHORE) / (SLOPE_END - SHORE) + 0.5
        x, y = top(u - PUSH, v)
        y += 2.5 + under + (LOWER_DROP if row == "lower" else 0.0)
        placed.append((y, x, w, squash, lean, shape, under if row == "lower" else None))

    # On the cut wall the ground runs under the nearest sunk rock, to the low right of its base, and
    # falls to the tile's front corner from there: the rock rests on it, no water under it.
    y, x, w, squash, lean, shape, _ = max(placed)
    front = ROCKS[shape][0]
    low_right = max(range(len(front)), key=lambda i: front[i][0] + front[i][1])
    a = rock_point(x, y, w, squash, lean, front[low_right])
    b = rock_point(x, y, w, squash, lean, front[low_right + 1])
    seat = lerp(lerp(a, b, 0.5), (x, y - 2), 0.12)

    shore_top, slope_foot = top(SHORE, 0), FOOT_B
    body = [
        # Walls: earth all along, then the sea over it from the slanted ground on.
        poly([L, B, FOOT_B, FOOT_L], WALL_EARTH),
        poly([shore_top, B, FOOT_B, slope_foot, seat], WALL_SEA_DARK),
        poly([FOOT_B, B, R, FOOT_R], WALL_SEA),
        # Top: the sea over the whole face, the land over it, so that no seam can show between.
        poly([L, T, R, B], WATER),
        curved([top(0.86, 0), B, R, top(0.9, 1)], top(0.74, 0.5), WATER_LIGHT),
        poly([L, T, top(SHORE, 1), shore_top], SAND),
    ]
    placed.sort()
    # What stands under the water first, seen through it, then what stands out of it.
    for y, x, w, squash, lean, shape, under in placed:
        if under is not None:
            body += rock(x, y, w, squash, lean, shape, (UNDER_MID, UNDER_DARK, UNDER_TOP))
    for y, x, w, squash, lean, shape, under in placed:
        waterline = None if under is None else y + 0.6 - under
        body += rock(x, y, w, squash, lean, shape, (ROCK_MID, ROCK_DARK, ROCK_TOP), waterline)
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64" viewBox="0 0 64 64" '
        'fill="none">\n' + "\n".join(body) + "\n</svg>\n"
    )


Path(__file__).with_suffix(".svg").write_text(build())
