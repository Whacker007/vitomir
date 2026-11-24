# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
Builds Vitomir's own fonts (ours): the letters of `faces.py` (Vitomir Capitals),
`uncial.py` (Vitomir Uncial) and `book.py` (Vitomir Book), with their figures, marks and accented letters
(`accents.py`), written as TrueType fonts, a specimen of each to look at, and with `--atlas` the
distance-field atlases the engine draws them from.

    python3 tools/ui/font/build.py --out out/fonts                 the fonts and their specimens
    python3 tools/ui/font/build.py --out out/fonts --text "OPTIONS"  a specimen of these words too
    python3 tools/ui/font/build.py --out out/fonts --atlas src/Vitomir.Engine/Fonts
                                                                   and the engine's atlases (capitals, uncial, book)
    python3 tools/ui/font/build.py --delivered assets/fonts --atlas src/Vitomir.Platform.Render/Fonts \
        --measures src/Vitomir.Platform.Interface/Fonts            the atlases of the faces delivered as TrueType
                                                                   files (Vitomir Text, Wedge, Knife: DELIVERED)

The faces drawn here (capitals, uncial, book) are built from their code until a delivered face replaces one; a
delivered face's atlas is built from its file as it came, its pair kerning with it (`atlas.py`).

Needs fontTools, skia-pathops, freetype-py, numpy, SciPy and Pillow (`pip install fonttools skia-pathops freetype-py
numpy scipy pillow`). The engine does not read the TrueType files; it reads the atlases made from them (`atlas.py`).
"""
from __future__ import annotations

import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from fontTools.fontBuilder import FontBuilder                      # noqa: E402
from fontTools.pens.cu2quPen import Cu2QuPen                        # noqa: E402
from fontTools.pens.ttGlyphPen import TTGlyphPen                    # noqa: E402

from faces import Capitals, Glyph, Measures                         # noqa: E402
from uncial import Uncial                                           # noqa: E402

FAMILY = "Vitomir Capitals"

# The engine's atlases (`atlas.py`): an em of 64 atlas pixels, the distance running 16 pixels each side of an edge
# (a quarter of an em), room for the menu words' glow (0.16 em) and an outline beside it.
ATLAS_EM, ATLAS_SPREAD = 64, 16
VERSION = "0.1"

# The faces delivered as TrueType files (assets/fonts/), by the atlas's name: the launcher's running words and values
# (Vitomir Text Regular), its warnings (Vitomir Text SemiBold), its titles, labels, tabs and buttons (Vitomir Wedge
# SemiBold) and its numbers (Vitomir Knife).
DELIVERED = (
    ("text", "VitomirText-Regular.ttf"),
    ("text-semibold", "VitomirText-SemiBold.ttf"),
    ("wedge-semibold", "VitomirWedge-SemiBold.ttf"),
    ("knife", "VitomirKnife.ttf"),
)

# The characters an atlas holds: Latin-1, and the marks a text writes past it: curled quotes, dashes, the ellipsis,
# the euro, oe and OE (a font that lacks one leaves it out; the engine draws a question mark for it).
ATLAS_CHARS = "".join(chr(c) for c in range(32, 256) if c < 127 or c > 159) + "\u2018\u2019\u201c\u201d\u2013\u2014\u2026\u20ac\u0153\u0152"

UNICODE = {"space": 0x20, "ankh": 0x2625, "hyphen": 0x2D}


def capitals_everything():
    """The capitals and their accented letters (the accents the uncial's, a little smaller; ours)."""
    from accents import ACCENTED_CAPITALS, accented
    from geom import union
    m = Measures()
    out = Capitals(m).letters()
    u = Uncial()
    out += accented({g.name: g for g in out}, u.accent, m.cap, 75, ACCENTED_CAPITALS, scale=0.8)
    o = next(g for g in out if g.name == "O")
    x0, _, x1, _ = o.path.bounds
    out.append(Glyph("Oslash", union([o.path, u.line((x0 - 10, -50), (x1 + 10, m.cap + 50), ends=(False, False))]), o.advance))
    return out


def code_of(name: str) -> int | None:
    """A glyph's character: a one-letter name is itself; any other is named as the Adobe glyph list names it."""
    if len(name) == 1:
        return ord(name)
    if name in UNICODE:
        return UNICODE[name]
    from fontTools.agl import toUnicode
    text = toUnicode(name)
    return ord(text) if len(text) == 1 else None


def write_ttf(glyphs, path: str, family: str = FAMILY, style: str = "Regular") -> None:
    order = [".notdef"] + [g.name for g in glyphs]
    fb = FontBuilder(1000, isTTF=True)
    fb.setupGlyphOrder(order)
    fb.setupCharacterMap({code_of(g.name): g.name for g in glyphs if code_of(g.name) is not None})
    ttglyphs = {}
    metrics = {}
    pen = TTGlyphPen(None)
    ttglyphs[".notdef"] = pen.glyph()
    metrics[".notdef"] = (500, 0)
    for g in glyphs:
        pen = TTGlyphPen(None)
        g.path.draw(Cu2QuPen(pen, max_err=0.6, reverse_direction=True))
        ttglyphs[g.name] = pen.glyph()
        x0 = g.path.bounds[0] if g.path.bounds != (0, 0, 0, 0) else 0
        metrics[g.name] = (round(g.advance), round(x0))
    fb.setupGlyf(ttglyphs)
    fb.setupHorizontalMetrics(metrics)
    fb.setupHorizontalHeader(ascent=820, descent=-240)
    fb.setupNameTable({"familyName": family, "styleName": style, "uniqueFontIdentifier": f"{family} {style} {VERSION}",
                       "fullName": f"{family} {style}", "psName": (family + "-" + style).replace(" ", ""),
                       "version": f"Version {VERSION}",
                       "copyright": "Copyright (c) 2026 Whacker007",
                       "licenseDescription": "The Vitomir Licence 1.0, as the rest of Vitomir"})
    fb.setupOS2(sTypoAscender=820, sTypoDescender=-240, usWinAscent=900, usWinDescent=300, sCapHeight=680)
    fb.setupPost()
    fb.save(path)


def specimen(ttf: str, lines: list[tuple[str, int, int]], path: str, width: int = 2400) -> None:
    """Draws lines of (text, pixel size, letter spacing in thousandths of the size) with FreeType."""
    import freetype
    import numpy as np
    from PIL import Image
    face = freetype.Face(ttf)
    rows = []
    for text, size, track in lines:
        face.set_pixel_sizes(0, size)
        h = int(size * 1.5)
        canvas = np.zeros((h, width), dtype=np.uint8)
        x = size * 0.3
        base = int(size * 1.05)
        for ch in text:
            face.load_char(ch, freetype.FT_LOAD_RENDER)
            bm = face.glyph.bitmap
            if bm.width and bm.rows:
                a = np.array(bm.buffer, dtype=np.uint8).reshape(bm.rows, bm.pitch)[:, :bm.width]
                left = int(round(x)) + face.glyph.bitmap_left
                top = base - face.glyph.bitmap_top
                y0, y1 = max(0, top), min(h, top + bm.rows)
                x0, x1 = max(0, left), min(width, left + bm.width)
                if y1 > y0 and x1 > x0:
                    canvas[y0:y1, x0:x1] = np.maximum(canvas[y0:y1, x0:x1], a[y0 - top:y1 - top, x0 - left:x1 - left])
            x += face.glyph.advance.x / 64 + size * track / 1000
        rows.append(canvas)
    img = np.concatenate(rows, axis=0)
    bg = np.array([22, 18, 16], dtype=np.float32)
    fg = np.array([222, 208, 186], dtype=np.float32)
    k = (img.astype(np.float32) / 255)[..., None]
    rgb = (bg * (1 - k) + fg * k).astype(np.uint8)
    Image.fromarray(rgb).save(path)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=None, help="write our own faces (capitals, uncial, book) and their specimens here")
    ap.add_argument("--text", action="append", default=[])
    ap.add_argument("--atlas", default=None, help="also write the atlases (the PNGs, and the measures unless --measures) to this folder")
    ap.add_argument("--delivered", default=None, help="the folder of the delivered faces' TrueType files (DELIVERED): their atlases")
    ap.add_argument("--measures", default=None, help="write the atlases' measures (the JSON files) here instead of beside the PNGs")
    args = ap.parse_args()
    if args.delivered:
        if not args.atlas:
            ap.error("--delivered needs --atlas")
        for name, file in DELIVERED:
            write_atlas(os.path.join(args.delivered, file), name, args.atlas, args.measures or args.atlas)
        if not args.out:
            return 0
    if not args.out:
        ap.error("--out is needed (or --delivered and --atlas)")
    os.makedirs(args.out, exist_ok=True)
    glyphs = capitals_everything()
    ttf = os.path.join(args.out, "VitomirCapitals-Regular.ttf")
    write_ttf(glyphs, ttf)
    lines = [("ABCDEFGHIJKLM", 150, 40), ("NOPQRSTUVWXYZ", 150, 40),
             ("SINGLE PLAYER  MULTIPLAYER", 64, 180), ("OPTIONS  CREDITS  QUIT", 64, 180),
             ("THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG", 40, 60), ("HAMBURGEFONTS  NOHOHOHO", 40, 60)]
    lines += [(t, 64, 150) for t in args.text if t.upper() == t]
    specimen(ttf, lines, os.path.join(args.out, "specimen.png"))
    print(f"wrote {ttf} ({len(glyphs)} glyphs) and specimen.png")
    uncial = Uncial().everything()
    uttf = os.path.join(args.out, "VitomirUncial-Regular.ttf")
    write_ttf(uncial, uttf, family="Vitomir Uncial")
    ulines = [("abcdefghijklm", 130, 30), ("nopqrstuvwxyz", 130, 30),
              ("game controls keybinds display sound fixes done", 64, 40), ("cancel ok defaults key bindings", 64, 40)]
    ulines += [(t, 64, 40) for t in args.text if t.upper() != t]
    specimen(uttf, ulines, os.path.join(args.out, "uncial_specimen.png"))
    print(f"wrote {uttf} ({len(uncial)} glyphs) and uncial_specimen.png")
    from book import Book
    book = Book().everything()
    bttf = os.path.join(args.out, "VitomirBook-Regular.ttf")
    write_ttf(book, bttf, family="Vitomir Book")
    blines = [("abcdefghijklmnopqrstuvwxyz", 90, 0), ("ABCDEFGHIJKLMNOPQRSTUVWXYZ", 90, 0),
              ("0123456789 .,:;'\"!?()&%#", 90, 0),
              ("A game with these switches is faithful to 1.1's rules.", 40, 0),
              ("Press a key to bind it, or Escape to unmap it.", 34, 0)]
    blines += [(t, 40, 0) for t in args.text]
    specimen(bttf, blines, os.path.join(args.out, "book_specimen.png"))
    print(f"wrote {bttf} ({len(book)} glyphs) and book_specimen.png")
    if args.atlas:
        for name, path in (("capitals", ttf), ("uncial", uttf), ("book", bttf)):
            write_atlas(path, name, args.atlas, args.measures or args.atlas)
    return 0


def write_atlas(ttf: str, name: str, folder: str, measures: str) -> None:
    """A face's atlas (`<name>.png` in `folder`) and its measures (`<name>.json` in `measures`): an em of ATLAS_EM pixels."""
    import json
    import atlas as atlas_tool
    from PIL import Image
    os.makedirs(folder, exist_ok=True)
    os.makedirs(measures, exist_ok=True)
    picture, meta = atlas_tool.build(ttf, ATLAS_CHARS, ATLAS_EM, ATLAS_SPREAD, 8)
    Image.fromarray(picture, "L").save(os.path.join(folder, name + ".png"), optimize=True)
    with open(os.path.join(measures, name + ".json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, separators=(",", ":"), sort_keys=True)
        f.write("\n")
    pairs = len(meta.get("kern", []))
    print(f"wrote the atlas {name} ({meta['atlasWidth']}x{meta['atlasHeight']}, {sum(1 for g in meta['glyphs'].values() if 'atlas' in g)} glyphs"
          + (f", {pairs} kerned pairs)" if pairs else ")"))


if __name__ == "__main__":
    sys.exit(main())
