"""Draws an editor skin from its description (Skins/<name>.json) into the editor's data folder: each part a 9-slice
PNG (Data/Skin/<name>/<Part>.png: a vertical gradient, a border, round corners, a lit top line, a shaded bottom line,
an inner shadow, an outer drop shadow) and the skin's style sheet (Data/Skin/<name>.hss): the colors as variables on
the root and a class per part (.hx-<part>) with its image and slices. Another skin is another description.
Python's standard library only:

    python Tools/GenerateSkin.py [Skins/HelixClassic.json]

Then run Tools/GenerateData.py, which lists the files.
"""

import json
import os
import re
import struct
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Data")


def png(width, height, pixels):
    """An RGBA PNG of rows top first, each pixel four bytes."""
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    raw = b"".join(b"\x00" + bytes(pixels[y * width * 4:(y + 1) * width * 4]) for y in range(height))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) +
            chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def color(text):
    """#rrggbb or #rrggbbaa as (r, g, b, a) from 0 to 1; None stays None."""
    if text is None:
        return None
    a = int(text[7:9], 16) / 255 if len(text) == 9 else 1.0
    return (int(text[1:3], 16) / 255, int(text[3:5], 16) / 255, int(text[5:7], 16) / 255, a)


def over(dst, src):
    """src over dst, both straight alpha."""
    sa = src[3]
    da = dst[3] * (1 - sa)
    a = sa + da
    if a <= 0:
        return (0.0, 0.0, 0.0, 0.0)
    return tuple((src[k] * sa + dst[k] * da) / a for k in range(3)) + (a,)


def inside_rounded(x, y, x0, y0, x1, y1, r, corners):
    """Whether a point is inside a box with round corners (all of them, or the top ones only)."""
    if not (x0 <= x <= x1 and y0 <= y <= y1):
        return False
    if r <= 0:
        return True
    top_only = corners == "top"
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    if top_only and y > y0 + r:
        return True
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def distance_outside(x, y, x0, y0, x1, y1, r):
    """Distance from a point to a box with round corners (0 inside)."""
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5 - r
    return max(0.0, d)


def shade_part(spec, x, y, w, h):
    """The color of a point of a part (pixels, y down) before averaging."""
    drop = spec.get("drop", 0)
    r = spec.get("radius", 0)
    corners = spec.get("corners", "all")
    x0, y0, x1, y1 = drop, drop, w - drop, h - drop
    result = (0.0, 0.0, 0.0, 0.0)
    if drop:
        # A soft shadow below and around, a little lower than the box.
        d = distance_outside(x, y - 2, x0, y0, x1, y1, max(r, 2))
        if d < drop:
            t = 1 - d / drop
            result = (0.0, 0.0, 0.0, 0.5 * t * t)
    if not inside_rounded(x, y, x0, y0, x1, y1, r, corners):
        return result
    top, bottom = color(spec["top"]), color(spec["bottom"])
    t = (y - y0) / max(1.0, (y1 - y0))
    c = tuple(top[k] + (bottom[k] - top[k]) * t for k in range(4))
    border = color(spec.get("border"))
    open_bottom = spec.get("open") == "bottom"
    edge = min(x - x0, x1 - x, y - y0, (y1 - y) if not open_bottom else 99)
    # Inner shadow: darkest at the top and left edges.
    inner = color(spec.get("inner"))
    if inner:
        depth = min(x - x0, y - y0)
        if depth < 4:
            c = over(c, inner[:3] + (inner[3] * (1 - depth / 4),))
    highlight = color(spec.get("highlight"))
    if highlight and (y - y0) >= 1 and (y - y0) < 2:
        c = over(c, highlight)
    shade = color(spec.get("shade"))
    if shade and (y1 - y) >= 1 and (y1 - y) < 2 and not open_bottom:
        c = over(c, shade)
    if border and edge < 1:
        c = over(c, border)
    if r > 0 and border:
        # The rounded edge's ring.
        inner_box = inside_rounded(x, y, x0 + 1, y0 + 1, x1 - 1, y1 - (0 if open_bottom else 1), max(0, r - 1), corners)
        if not inner_box:
            c = over(c, border)
    return over(result, c)


def render(spec):
    w, h = spec["size"]
    drop = spec.get("drop", 0)
    w += 2 * drop
    h += 2 * drop
    out = bytearray(w * h * 4)
    n = 4
    for y in range(h):
        for x in range(w):
            acc = [0.0, 0.0, 0.0, 0.0]
            for j in range(n):
                for i in range(n):
                    c = shade_part(spec, x + (i + 0.5) / n, y + (j + 0.5) / n, w, h)
                    for k in range(3):
                        acc[k] += c[k] * c[3]
                    acc[3] += c[3]
            a = acc[3] / (n * n)
            rgb = [acc[k] / acc[3] if acc[3] > 0 else 0.0 for k in range(3)]
            o = (y * w + x) * 4
            out[o:o + 4] = bytes(int(round(max(0.0, min(1.0, v)) * 255)) for v in rgb + [a])
    return w, h, out


def kebab(name):
    return re.sub(r"(?<!^)(?=[A-Z])", "-", name).lower()


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "Skins", "HelixClassic.json")
    with open(path, encoding="utf-8") as f:
        skin = json.load(f)
    name = skin["name"]
    folder = os.path.join(DATA, "Skin", name)
    os.makedirs(folder, exist_ok=True)
    sheet = [f"/* The skin {name}: written by Tools/GenerateSkin.py from Skins/{name}.json. */", ":root {"]
    for key, value in skin["colors"].items():
        sheet.append(f"    --hx-{kebab(key)}: {value};")
    # The sizes as lengths (the editor's C# reads them too, as custom style properties).
    for key, value in skin.get("metrics", {}).items():
        if key != "comment":
            sheet.append(f"    --hx-{kebab(key)}: {value}px;")
    sheet.append("}")
    for part, spec in skin["parts"].items():
        w, h, pixels = render(spec)
        with open(os.path.join(folder, part + ".png"), "wb") as f:
            f.write(png(w, h, pixels))
        s = spec["slice"] + spec.get("drop", 0)
        sheet.append(f".hx-{kebab(part)} {{ background-image: url(\"{name}/{part}.png\"); -helix-slice-left: {s}; "
                     f"-helix-slice-top: {s}; -helix-slice-right: {s}; -helix-slice-bottom: {s}; }}")
    with open(os.path.join(DATA, "Skin", name + ".hss"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(sheet) + "\n")


if __name__ == "__main__":
    main()
