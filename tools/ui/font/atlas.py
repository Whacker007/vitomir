# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
A face's **distance-field atlas** (ours): what the engine draws our lettering from. Each glyph
is rasterised large through FreeType, its signed distance to the outline worked out (inside positive), and that
distance stored at a smaller size as one grey byte a pixel — 128 on the outline, `spread` atlas pixels each way to 0
and 255. One atlas serves every size: the engine reads the edge at 128 wherever the glyph is drawn, sharp at 4K.

    python3 tools/ui/font/atlas.py --ttf out/fonts/VitomirUncial-Regular.ttf --name uncial --out src/Vitomir.Engine/Fonts

writes `<name>.png` (the atlas) and `<name>.json` (the measures: the em, the spread, the line, and each glyph's
advance, its box in em units and its place in the atlas). A font with pair kerning (an OpenType GPOS `kern` feature,
pair adjustment formats 1 and 2, under the default script and language) gives its pairs too, as `kern`: a list of
[first character, second character, the second glyph's move in ems], only the pairs whose move is not nought; the
engine adds the move to the first glyph's advance when the two stand together. A font without them writes no `kern`.
"""
from __future__ import annotations

import argparse
import json
import os
import sys

import freetype
import numpy as np
from PIL import Image
from scipy import ndimage


def sdf_of(mask: np.ndarray) -> np.ndarray:
    """Signed distance in the mask's pixels: positive inside, negative outside, zero on the edge."""
    inside = mask >= 0.5
    if not inside.any():
        return np.full(mask.shape, -1e3, dtype=np.float32)
    d_in = ndimage.distance_transform_edt(inside)
    d_out = ndimage.distance_transform_edt(~inside)
    return (d_in - d_out).astype(np.float32)


def kerning(ttf: str, chars: str) -> list[list]:
    """
    The font's pair kerning between the characters of `chars`: the GPOS lookups the `kern` feature names under the
    default script (DFLT, else latn) and its default language, each a pair adjustment (type 2, or an extension of one).
    For each pair the first subtable that covers it gives its value (format 1: the pair listed; format 2: the first
    glyph covered, the classes' record), the first value record's x advance; the lookups' values add up. In ems.
    """
    from fontTools.ttLib import TTFont
    font = TTFont(ttf)
    if "GPOS" not in font:
        return []
    gpos = font["GPOS"].table
    upm = font["head"].unitsPerEm
    cmap = font.getBestCmap()
    scripts = {s.ScriptTag: s.Script for s in gpos.ScriptList.ScriptRecord}
    script = scripts.get("DFLT") or scripts.get("latn")
    if script is None or script.DefaultLangSys is None:
        return []
    features = gpos.FeatureList.FeatureRecord
    lookups: list[int] = []
    for i in script.DefaultLangSys.FeatureIndex:
        if features[i].FeatureTag == "kern":
            lookups += [k for k in features[i].Feature.LookupListIndex if k not in lookups]
    tables = []
    for k in sorted(lookups):
        lookup = gpos.LookupList.Lookup[k]
        subs = []
        for st in lookup.SubTable:
            if lookup.LookupType == 9:
                if st.ExtensionLookupType != 2:
                    continue
                st = st.ExtSubTable
            elif lookup.LookupType != 2:
                continue
            subs.append(st)
        if subs:
            tables.append(subs)

    def x_advance(record) -> float:
        return getattr(record, "XAdvance", 0) or 0 if record is not None else 0

    def value(subs, a: str, b: str) -> float:
        for st in subs:
            covered = st.Coverage.glyphs
            if a not in covered:
                continue
            if st.Format == 1:
                for pv in st.PairSet[covered.index(a)].PairValueRecord:
                    if pv.SecondGlyph == b:
                        return x_advance(pv.Value1)
                continue                                    # not listed here: the next subtable may have it
            if st.Format == 2:
                c1 = st.ClassDef1.classDefs.get(a, 0)
                c2 = st.ClassDef2.classDefs.get(b, 0)
                return x_advance(st.Class1Record[c1].Class2Record[c2].Value1)
        return 0

    names = [(ch, cmap[ord(ch)]) for ch in chars if ord(ch) in cmap and ch != " "]
    out = []
    for a, ga in names:
        for b, gb in names:
            v = sum(value(subs, ga, gb) for subs in tables)
            if v:
                out.append([ord(a), ord(b), round(v / upm, 5)])
    return out


def build(ttf: str, chars: str, em_px: int, spread: int, oversample: int) -> tuple[np.ndarray, dict]:
    face = freetype.Face(ttf)
    upm = face.units_per_EM
    big = em_px * oversample
    face.set_pixel_sizes(0, big)
    pad = spread + 1
    cells = []
    for ch in chars:
        idx = face.get_char_index(ord(ch))
        if idx == 0 and ch != " ":
            continue
        face.load_char(ch, freetype.FT_LOAD_RENDER | freetype.FT_LOAD_NO_HINTING)
        g = face.glyph
        advance = g.advance.x / 64 / big                                # in ems
        bm = g.bitmap
        if bm.width == 0 or bm.rows == 0:
            cells.append((ch, advance, None, None))
            continue
        a = np.array(bm.buffer, dtype=np.uint8).reshape(bm.rows, bm.pitch)[:, :bm.width].astype(np.float32) / 255
        # The ink itself, in ems (left, bottom, right, top): where the letter is, as against the box drawn round it.
        ink = (g.bitmap_left / big, (g.bitmap_top - bm.rows) / big, (g.bitmap_left + bm.width) / big, g.bitmap_top / big)
        # Room for the spread round the glyph, at the large size.
        p = pad * oversample
        a = np.pad(a, p)
        d = sdf_of(a) / oversample                                       # in atlas pixels
        # Down to the atlas's size: the mean of each block of `oversample` pixels.
        h, w = d.shape
        h2, w2 = h // oversample, w // oversample
        d = d[:h2 * oversample, :w2 * oversample].reshape(h2, oversample, w2, oversample).mean(axis=(1, 3))
        img = np.clip(128 + d * (127 / spread), 0, 255).astype(np.uint8)
        # The glyph's box in ems, from the bitmap's place (FreeType's left and top, y up).
        x0 = (g.bitmap_left - p) / big
        y1 = (g.bitmap_top + p) / big
        box = (x0, y1 - h2 * oversample / big, x0 + w2 * oversample / big, y1)
        cells.append((ch, advance, img, (box, ink)))
    # A shelf packer: tallest first, rows across a square-ish atlas.
    drawn = [c for c in cells if c[2] is not None]
    area = sum(c[2].shape[0] * c[2].shape[1] for c in drawn)
    width = 256
    while width * width < area * 1.4:
        width *= 2
    order = sorted(drawn, key=lambda c: -c[2].shape[0])
    x = y = row_h = 0
    places = {}
    for ch, _, img, _ in order:
        h, w = img.shape
        if x + w > width:
            x, y, row_h = 0, y + row_h + 1, 0
        places[ch] = (x, y)
        x += w + 1
        row_h = max(row_h, h)
    height = y + row_h
    h_pow = 64
    while h_pow < height:
        h_pow *= 2
    atlas = np.zeros((h_pow, width), dtype=np.uint8)
    glyphs = {}
    for ch, advance, img, boxes in cells:
        entry = {"advance": round(advance, 5)}
        if img is not None:
            box, ink = boxes
            px, py = places[ch]
            h, w = img.shape
            atlas[py:py + h, px:px + w] = img
            entry["box"] = [round(v, 5) for v in box]                   # left, bottom, right, top, in ems
            entry["ink"] = [round(v, 5) for v in ink]                   # the letter itself, in ems
            entry["atlas"] = [px, py, w, h]                             # pixels, from the atlas's top left
        glyphs[str(ord(ch))] = entry
    asc = face.ascender / upm
    desc = face.descender / upm
    meta = {
        "format": 1,
        "family": face.family_name.decode() if isinstance(face.family_name, bytes) else str(face.family_name),
        "em": em_px, "spread": spread, "atlasWidth": width, "atlasHeight": h_pow,
        "ascender": round(asc, 5), "descender": round(desc, 5),
        "glyphs": glyphs,
    }
    pairs = kerning(ttf, "".join(ch for ch, *_ in cells))
    if pairs:
        meta["kern"] = pairs
    return atlas, meta


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ttf", required=True)
    ap.add_argument("--name", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--chars", default=None, help="the characters to include (default: every one the font has, of Latin-1)")
    ap.add_argument("--em", type=int, default=72, help="the em in atlas pixels")
    ap.add_argument("--spread", type=int, default=8, help="how far the distance runs each side of the edge, atlas pixels")
    ap.add_argument("--oversample", type=int, default=8)
    args = ap.parse_args()
    chars = args.chars if args.chars is not None else "".join(chr(c) for c in range(32, 256) if c < 127 or c > 159)
    atlas, meta = build(args.ttf, chars, args.em, args.spread, args.oversample)
    os.makedirs(args.out, exist_ok=True)
    Image.fromarray(atlas, "L").save(os.path.join(args.out, args.name + ".png"), optimize=True)
    with open(os.path.join(args.out, args.name + ".json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, separators=(",", ":"), sort_keys=True)
        f.write("\n")
    n = sum(1 for g in meta["glyphs"].values() if "atlas" in g)
    print(f"wrote {args.name}.png ({meta['atlasWidth']}x{meta['atlasHeight']}, {n} glyphs) and {args.name}.json")
    return 0


if __name__ == "__main__":
    sys.exit(main())
