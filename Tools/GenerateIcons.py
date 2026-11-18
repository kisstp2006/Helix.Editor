"""Draws the editor's icons (Data/Skin/Icons/<Name>.png, 32 pixels, and their classes in Data/Skin/Icons.hss, white with alpha, tinted by the style sheets) from
shapes: capsules (lines with round ends), rings and arcs, filled polygons and boxes, by distance with a pixel of
antialiasing. Python's standard library only:

    python Tools/GenerateIcons.py
"""

import math
import os
import struct
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Data", "Skin", "Icons")
SIZE = 32


def png(width, height, pixels):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    raw = b"".join(b"\x00" + bytes(pixels[y * width * 4:(y + 1) * width * 4]) for y in range(height))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def seg(px, py, ax, ay, bx, by):
    """Distance from a point to a segment."""
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy or 1)))
    return math.hypot(px - (ax + t * dx), py - (ay + t * dy))


def in_poly(px, py, pts):
    inside = False
    n = len(pts)
    for i in range(n):
        (ax, ay), (bx, by) = pts[i], pts[(i + 1) % n]
        if (ay > py) != (by > py) and px < ax + (py - ay) * (bx - ax) / (by - ay):
            inside = not inside
    return inside


def poly_dist(px, py, pts):
    d = min(seg(px, py, *pts[i], *pts[(i + 1) % len(pts)]) for i in range(len(pts)))
    return -d if in_poly(px, py, pts) else d


# Each shape answers a signed distance (negative inside).
def line(ax, ay, bx, by, w):
    return lambda x, y: seg(x, y, ax, ay, bx, by) - w / 2


def ring(cx, cy, r, w):
    return lambda x, y: abs(math.hypot(x - cx, y - cy) - r) - w / 2


def arc(cx, cy, r, w, a0, a1):
    """An arc from angle a0 to a1 (degrees, clockwise from the right, y down) with round ends."""
    def d(x, y):
        a = math.degrees(math.atan2(y - cy, x - cx)) % 360
        lo, hi = a0 % 360, a1 % 360
        within = lo <= a <= hi if lo <= hi else (a >= lo or a <= hi)
        if within:
            return abs(math.hypot(x - cx, y - cy) - r) - w / 2
        ends = [(cx + r * math.cos(math.radians(t)), cy + r * math.sin(math.radians(t))) for t in (a0, a1)]
        return min(math.hypot(x - ex, y - ey) for ex, ey in ends) - w / 2
    return d


def poly(*pts):
    return lambda x, y: poly_dist(x, y, pts)


def box(x0, y0, x1, y1):
    return poly((x0, y0), (x1, y0), (x1, y1), (x0, y1))


def frame(x0, y0, x1, y1, w):
    edges = [line(x0, y0, x1, y0, w), line(x1, y0, x1, y1, w), line(x1, y1, x0, y1, w), line(x0, y1, x0, y0, w)]
    return lambda x, y: min(e(x, y) for e in edges)


def disc(cx, cy, r):
    return lambda x, y: math.hypot(x - cx, y - cy) - r


def arrow(ax, ay, bx, by, w, head):
    """A line with a filled head at b."""
    ang = math.atan2(by - ay, bx - ax)
    hx, hy = bx - head * math.cos(ang), by - head * math.sin(ang)
    nx, ny = -math.sin(ang) * head * 0.6, math.cos(ang) * head * 0.6
    tip = poly((bx, by), (hx + nx, hy + ny), (hx - nx, hy - ny))
    shaft = line(ax, ay, hx, hy, w)
    return lambda x, y: min(tip(x, y), shaft(x, y))


ICONS = {
    "Minimize": [line(9, 16, 23, 16, 2)],
    "Maximize": [frame(9, 9, 23, 23, 2)],
    "Restore": [frame(9, 12, 20, 23, 2), line(12, 9, 23, 9, 2), line(23, 9, 23, 20, 2)],
    "Close": [line(10, 10, 22, 22, 2.2), line(22, 10, 10, 22, 2.2)],
    "Play": [poly((11, 8), (24, 16), (11, 24))],
    "Pause": [box(10, 9, 14, 23), box(18, 9, 22, 23)],
    "Step": [poly((9, 8), (20, 16), (9, 24)), box(21, 8, 24, 24)],
    "ToolView": [line(16, 7, 16, 17, 3), line(12, 9, 12, 18, 3), line(20, 9, 20, 18, 3), line(24, 12, 24, 19, 3),
                 line(9, 15, 12, 21, 3), poly((10.5, 17), (25.5, 17), (24, 24), (19, 27), (13, 27), (10.5, 22))],
    "ToolMove": [arrow(16, 16, 16, 4, 2, 6), arrow(16, 16, 16, 28, 2, 6), arrow(16, 16, 4, 16, 2, 6), arrow(16, 16, 28, 16, 2, 6)],
    "ToolRotate": [arc(16, 16, 9, 2.4, 300, 220), poly((5, 13), (11, 13), (8, 20))],
    "ToolScale": [frame(6, 12, 20, 26, 2), arrow(13, 19, 26, 6, 2, 6)],
    "ToolRect": [frame(7, 9, 25, 23, 1.6), disc(7, 9, 2.4), disc(25, 9, 2.4), disc(7, 23, 2.4), disc(25, 23, 2.4)],
    "ArrowDown": [poly((10, 13), (22, 13), (16, 20))],
    "Lock": [box(9, 15, 23, 26), arc(16, 14, 5, 2.4, 180, 360), line(11, 14, 11, 16, 2.4), line(21, 14, 21, 16, 2.4)],
    "Search": [ring(14, 14, 6, 2.2), line(18.5, 18.5, 25, 25, 2.6)],
    "Menu": [disc(16, 9, 2), disc(16, 16, 2), disc(16, 23, 2)],
    "Help": [ring(16, 16, 10, 2), arc(16, 13, 3.5, 2, 180, 90), line(16, 16.5, 16, 18, 2), disc(16, 22, 1.4)],
    "Layers": [poly((16, 6), (27, 12), (16, 18), (5, 12)), line(5, 17, 16, 23, 2), line(16, 23, 27, 17, 2)],
    "Light": [disc(16, 16, 5)] + [line(16 + 8 * math.cos(a), 16 + 8 * math.sin(a), 16 + 12 * math.cos(a), 16 + 12 * math.sin(a), 2)
                                  for a in [k * math.pi / 4 for k in range(8)]],
    "Effects": [poly((16, 5), (18.5, 13.5), (27, 16), (18.5, 18.5), (16, 27), (13.5, 18.5), (5, 16), (13.5, 13.5)), disc(25, 7, 2)],
    "Grid": [frame(6, 6, 26, 26, 1.6), line(12.7, 6, 12.7, 26, 1.4), line(19.3, 6, 19.3, 26, 1.4), line(6, 12.7, 26, 12.7, 1.4),
             line(6, 19.3, 26, 19.3, 1.4)],
    "Info": [ring(16, 16, 10, 2), disc(16, 10.5, 1.6), line(16, 14.5, 16, 22, 2.4)],
    "Warning": [poly((16, 5), (28, 26), (4, 26)), ],
    "Error": [disc(16, 16, 11)],
    "WindowHierarchyWindow": [line(7, 9, 25, 9, 2), line(11, 16, 25, 16, 2), line(11, 23, 25, 23, 2), line(7, 9, 7, 23, 1.6), line(7, 16, 10, 16, 1.6),
                              line(7, 23, 10, 23, 1.6)],
    "WindowSceneView": [poly((16, 5), (26, 10.5), (16, 16), (6, 10.5)), poly((6, 12), (15, 17), (15, 27), (6, 21.5)),
                        poly((26, 12), (17, 17), (17, 27), (26, 21.5))],
    "WindowGameView": [poly((7, 11), (25, 11), (28, 21), (24, 24), (20, 20), (12, 20), (8, 24), (4, 21)), ],
    "WindowInspectorWindow": [ring(16, 16, 10, 2), disc(16, 10.5, 1.6), line(16, 14.5, 16, 22, 2.4)],
    "WindowProjectWindow": [poly((5, 9), (13, 9), (15, 12), (27, 12), (27, 25), (5, 25))],
    "WindowConsoleWindow": [frame(5, 7, 27, 25, 2), line(9, 12, 13, 16, 2), line(13, 16, 9, 20, 2), line(15, 20, 22, 20, 2)],
}


def render(shapes):
    out = bytearray(SIZE * SIZE * 4)
    n = 4
    for y in range(SIZE):
        for x in range(SIZE):
            cover = 0.0
            for j in range(n):
                for i in range(n):
                    px, py = x + (i + 0.5) / n, y + (j + 0.5) / n
                    d = min(s(px, py) for s in shapes)
                    cover += 1.0 if d <= 0 else 0.0
            a = cover / (n * n)
            o = (y * SIZE + x) * 4
            out[o:o + 4] = bytes((255, 255, 255, int(round(a * 255))))
    return out


def kebab(name):
    out = ""
    for i, c in enumerate(name):
        if c.isupper() and i:
            out += "-"
        out += c.lower()
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    sheet = ["/* The editor's icons: written by Tools/GenerateIcons.py. An element of the class icon-<name> shows one. */"]
    for name, shapes in ICONS.items():
        with open(os.path.join(OUT, name + ".png"), "wb") as f:
            f.write(png(SIZE, SIZE, render(shapes)))
        sheet.append(f".icon-{kebab(name)} {{ background-image: url(\"Icons/{name}.png\"); }}")
    with open(os.path.join(os.path.dirname(OUT), "Icons.hss"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(sheet) + "\n")


if __name__ == "__main__":
    main()
