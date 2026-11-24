# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
The accented letters of Vitomir's faces (ours): each made of a face's own letter and an accent drawn by its pen,
the accent centred over the letter's ink (under it, a cedilla), so a save's name or a player's in French, German,
Spanish or Italian is written in the same hand as the rest. The Latin-1 letters and a few more (œ, Œ).

A letter this cannot make (Æ in the capitals, say) is left out of the face: the engine writes it as its plain letters
(`Lettering`'s fallback: Æ as AE, an accented letter as its base).
"""
from __future__ import annotations

from typing import Callable

from fontTools.pens.transformPen import TransformPen
from pathops import Path

from faces import Glyph
from geom import union

# name: (base, accent)
ACCENTED = {
    "agrave": ("a", "grave"), "aacute": ("a", "acute"), "acircumflex": ("a", "circumflex"), "atilde": ("a", "tilde"),
    "adieresis": ("a", "dieresis"), "aring": ("a", "ring"), "ccedilla": ("c", "cedilla"),
    "egrave": ("e", "grave"), "eacute": ("e", "acute"), "ecircumflex": ("e", "circumflex"), "edieresis": ("e", "dieresis"),
    "igrave": ("i", "grave"), "iacute": ("i", "acute"), "icircumflex": ("i", "circumflex"), "idieresis": ("i", "dieresis"),
    "ntilde": ("n", "tilde"), "ograve": ("o", "grave"), "oacute": ("o", "acute"), "ocircumflex": ("o", "circumflex"),
    "otilde": ("o", "tilde"), "odieresis": ("o", "dieresis"), "ugrave": ("u", "grave"), "uacute": ("u", "acute"),
    "ucircumflex": ("u", "circumflex"), "udieresis": ("u", "dieresis"), "yacute": ("y", "acute"),
    "ydieresis": ("y", "dieresis"),
}
ACCENTED_CAPITALS = {k[0].upper() + k[1:]: (v[0].upper(), v[1]) for k, v in ACCENTED.items() if k != "ydieresis"}


def place(path: Path, dx: float, dy: float, k: float = 1.0) -> Path:
    out = Path()
    path.draw(TransformPen(out.getPen(), (k, 0, 0, k, dx, dy)))
    return out


def accented(glyphs: dict[str, Glyph], accent: Callable[[str], Path], top: float, gap: float,
             table: dict[str, tuple[str, str]], scale: float = 1.0) -> list[Glyph]:
    """
    The accented letters of `table` that `glyphs` has the base of: the accent (from `accent(kind)`, drawn from its
    own origin up) set `gap` above `top` (the letters' height) and centred on the base's ink; a cedilla hung from
    the baseline under the ink's middle.
    """
    out = []
    for name, (base, kind) in table.items():
        g = glyphs.get(base)
        if g is None:
            continue
        x0, _, x1, _ = g.path.bounds
        mark = accent(kind)
        a0, a1_y, a1, _ = mark.bounds
        k = scale
        cx = (x0 + x1) / 2
        if kind == "cedilla":
            # under the middle of the bowl, its top tucked into the letter's foot
            moved = place(mark, cx - (a0 + a1) / 2 * k, 8, k)
        else:
            moved = place(mark, cx - (a0 + a1) / 2 * k, top + gap - a1_y * k, k)
        out.append(Glyph(name, union([g.path, moved]), g.advance))
    return out
