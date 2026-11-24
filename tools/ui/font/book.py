# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
**Vitomir Book** (ours): the text face of Vitomir's own interface (*"Yes build it now"*), for the journal,
dialogue, descriptions and a save's name. A book letter made to sit with Vitomir Capitals: its capitals are the
display face's drawn a little smaller and lighter, its lower case an old-style roman's — the same pen (thick down,
thin across, the round letters' stress tilted to the left), sloped head serifs on the stems, bracketed feet, a
generous x-height so it reads at small sizes. Drawn from these numbers, not traced from any font.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, replace

from fontTools.pens.transformPen import TransformPen
from pathops import Path

from faces import Capitals, Glyph, Measures
from geom import (Pen, SerifStyle, Stroke, arc_segments, ellipse_segments, lerp, meet, minus, pen_stroke, polygon, rect,
                  serif, stroke, stroke_edges, through, union)


@dataclass(frozen=True)
class BookMeasures:
    x: float = 460            # the x-height
    asc: float = 700          # an ascender's top
    desc: float = -220        # a descender's foot
    cap: float = 640          # the capitals' height
    stem: float = 84          # a lower-case stem (heavy enough to read at a hint's size)
    hair: float = 38          # a hairline
    over: float = 11          # how far a round letter passes its lines
    reach: float = 26         # a foot serif's reach
    side: float = 30          # the side bearing beside a stem
    round_side: float = 22
    open_side: float = 10
    stress: float = 22        # the pen's angle, degrees


class Book:
    def __init__(self, b: BookMeasures = BookMeasures()):
        self.b = b
        # The capitals: the display face at this face's cap height, at the lower case's weight.
        k = b.cap / Measures().cap
        self.caps = Capitals(Measures().scaled(k, weight=1.12))   # to the lower case's weight
        # Small helpers on a Capitals drawn at the x-height, for the letters that are the capitals' shapes made small.
        kx = (b.x) / Measures().cap
        self.small = Capitals(replace(Measures().scaled(kx, weight=1.0), thick=b.stem * 1.02, thin=b.hair * 1.05,
                                      reach=b.reach, side=b.side, round_side=b.round_side, open_side=b.open_side))
        self.pen = Pen(math.hypot(b.stem, b.hair) * 1.03, b.stress, b.hair * 0.95)
        self.foot = SerifStyle(b.reach, b.hair * 0.32, b.stem * 0.55, 0.8, 1.5)

    # ---- pieces -------------------------------------------------------------------------------------------------

    def stem(self, x: float, y0: float, y1: float, w: float | None = None) -> Stroke:
        w = self.b.stem if w is None else w
        return stroke_edges(x, x, x + w, x + w, y0, y1)

    def head(self, s: Stroke) -> Path:
        """
        The head serif of a lower-case stem: its top cut on a slope rising to the left, and a flag out to the left,
        hollow underneath.
        """
        b = self.b
        top = s.y1
        xl, xr = s.left.x_at(top), s.right.x_at(top)
        rise = b.stem * 0.32
        reach = b.stem * 0.62
        drop = b.stem * 0.55
        p = Path()
        pen = p.getPen()
        pen.moveTo((xr, top - rise * 0.15))
        pen.lineTo((xl - reach, top + rise * 0.15))
        pen.lineTo((xl - reach * 0.92, top - drop * 0.25))
        pen.curveTo((xl - reach * 0.45, top - drop * 0.35), (xl - 2, top - drop * 0.55), (xl, top - drop * 1.2))
        pen.lineTo((xr, top - drop * 1.2))
        pen.closePath()
        return p

    def cut_top(self, s: Stroke) -> Stroke:
        """The stem taken to just under its head (the head serif makes its top)."""
        return Stroke(s.left, s.right, s.y0, s.y1 - self.b.stem * 0.2)

    def feet(self, s: Stroke, left: float = 1.0, right: float = 1.0) -> Path:
        return serif(s, True, self.foot, left, right)

    def arch(self, x_from: float, x_to: float, y_join: float, top: float | None = None) -> Path:
        """
        A shoulder: out of a stem's right side at `y_join`, up and over to the top, and down into the next stem
        (whose centre is `x_to`), thin where it leaves the stem and thick on its way down.
        """
        b = self.b
        top = (b.x + b.over * 0.3) if top is None else top
        span = x_to - x_from
        pts = [(x_from, y_join), (x_from + span * 0.42, top - b.hair * 0.55), (x_to - span * 0.02, top - b.x * 0.22),
               (x_to, top - b.x * 0.45)]
        segs = through(pts, start_dir=(0.55, 1), end_dir=(0, -1), tension=0.5)
        pen = Pen(math.hypot(b.stem, b.hair) * 1.0, b.stress + 8, b.hair * 0.9)

        def thin_start(i, n, w):
            k = i / n
            return w * (0.55 + 0.45 * min(1.0, k / 0.25))
        return pen_stroke(segs, pen, steps=36, width_at=thin_start)

    def bowl(self, cx: float, width: float, y0: float, y1: float) -> Path:
        """A round lower-case bowl (o, and the round sides of b, d, p, q), drawn by the pen."""
        b = self.b
        rx = (width - self.pen.width * math.cos(math.radians(b.stress))) / 2
        ry = (y1 - y0 - b.hair) / 2
        return pen_stroke(ellipse_segments(cx, (y0 + y1) / 2, rx, ry), self.pen, closed=True, steps=64)

    def build(self, name: str, parts: list[Path], lsb: float, rsb: float) -> Glyph:
        return self.caps.build(name, parts, lsb, rsb)

    def dot(self, cx: float, cy: float, r: float | None = None) -> Path:
        """The dot of an i or a j: round, a little wider than the stem."""
        r = self.b.stem * 0.62 if r is None else r
        return polygon([(cx + r * math.cos(a / 32 * 2 * math.pi), cy + r * math.sin(a / 32 * 2 * math.pi)) for a in range(32)])

    # ---- the lower case -----------------------------------------------------------------------------------------

    def n(self, name="n", ascender=False):
        b = self.b
        top = b.asc if ascender else b.x
        a = self.stem(0, 0, top)
        w = b.x * 1.02
        right = self.stem(w - b.stem, 0, b.x * 0.58)
        parts = [self.cut_top(a).path(), self.head(a), self.feet(a), right.path(), self.feet(right),
                 self.arch(b.stem * 0.92, w - b.stem / 2, b.x * 0.60)]
        return self.build(name, parts, b.side - b.reach, b.side - b.reach)

    def h(self):
        return self.n("h", ascender=True)

    def m(self):
        b = self.b
        a = self.stem(0, 0, b.x)
        w = b.x * 0.92
        mid = self.stem(w - b.stem, 0, b.x * 0.58)
        right = self.stem(2 * w - 1.5 * b.stem, 0, b.x * 0.58)
        parts = [self.cut_top(a).path(), self.head(a), self.feet(a), mid.path(), self.feet(mid), right.path(), self.feet(right),
                 self.arch(b.stem * 0.92, w - b.stem / 2, b.x * 0.60),
                 self.arch(w - b.stem * 0.08, 2 * w - b.stem, b.x * 0.60)]
        return self.build("m", parts, b.side - b.reach, b.side - b.reach)

    def i(self, name="i", dotted=True):
        b = self.b
        a = self.stem(0, 0, b.x)
        parts = [self.cut_top(a).path(), self.head(a), self.feet(a)]
        if dotted:
            parts.append(self.dot(b.stem * 0.5 - b.stem * 0.1, b.x + b.stem * 1.45))
        return self.build(name, parts, b.side - b.reach, b.side - b.reach)

    def l(self):
        b = self.b
        a = self.stem(0, 0, b.asc)
        return self.build("l", [self.cut_top(a).path(), self.head(a), self.feet(a)], b.side - b.reach, b.side - b.reach)

    def o(self, name="o"):
        b = self.b
        w = b.x * 1.04
        return self.build(name, [self.bowl(w / 2, w, -b.over, b.x + b.over)], b.round_side, b.round_side)

    def bowl_on_stem(self, right_side: bool, y0: float, y1: float, top: float, bottom: float, name: str,
                     head_at_top: bool, foot: bool):
        """b, d, p, q: a stem and a bowl; the bowl's side on the stem cut away into it."""
        b = self.b
        w = b.x * 1.05
        if right_side:                                   # b, p: the stem on the left, the bowl out to the right
            st = self.stem(0, bottom, top)
            bowl = self.bowl(b.stem * 0.55 + (w - b.stem * 0.55) / 2, w - b.stem * 0.55, -b.over, b.x + b.over)
        else:                                            # d, q: the bowl on the left, the stem on the right
            st = self.stem(w - b.stem, bottom, top)
            bowl = self.bowl((w - b.stem * 0.55) / 2, w - b.stem * 0.55, -b.over, b.x + b.over)
        parts = [bowl, (self.cut_top(st) if head_at_top else st).path()]
        if head_at_top:
            parts.append(self.head(st))
        if foot:
            parts.append(self.feet(st))
        return self.build(name, parts, b.side - b.reach if right_side else b.round_side,
                          b.round_side if right_side else b.side - b.reach)

    def b_(self):
        return self.bowl_on_stem(True, 0, self.b.x, self.b.asc, 0, "b", True, False)

    def d(self):
        return self.bowl_on_stem(False, 0, self.b.x, self.b.asc, 0, "d", True, True)

    def p(self):
        return self.bowl_on_stem(True, 0, self.b.x, self.b.x, self.b.desc, "p", True, True)

    def q(self):
        return self.bowl_on_stem(False, 0, self.b.x, self.b.x, self.b.desc, "q", False, True)

    # ---- the rest of the lower case (ours) -------------------------------------------------------------------

    def renamed(self, g: Glyph, name: str) -> Glyph:
        return Glyph(name, g.path, g.advance)

    def ball(self, cx: float, cy: float, r: float | None = None) -> Path:
        """A terminal's ball (the ends of a, c, f, r, j): a pen's round end, a little larger than a hairline's."""
        r = self.b.stem * 0.50 if r is None else r
        return self.dot(cx, cy, r)

    def thin_pen(self) -> Pen:
        b = self.b
        return Pen(math.hypot(b.stem, b.hair) * 1.0, b.stress + 8, b.hair * 0.9)

    def spine_stroke(self, pts, start=None, end=None, pen: Pen | None = None, steps: int = 40) -> Path:
        return pen_stroke(through(pts, start_dir=start, end_dir=end), pen or self.thin_pen(), steps=steps)

    def head_on(self, s: Stroke) -> list[Path]:
        return [self.cut_top(s).path(), self.head(s)]

    def u(self):
        b = self.b
        w = b.x * 1.02
        right = self.stem(w - b.stem, 0, b.x)
        left = self.stem(0, b.x * 0.42, b.x)
        # the bowl at the foot: the n's shoulder turned over
        arch = self.arch(b.stem * 0.92, w - b.stem / 2, b.x * 0.60)
        turned = Path()
        arch.draw(TransformPen(turned.getPen(), (-1, 0, 0, -1, w, b.x)))
        parts = [*self.head_on(left), *self.head_on(right), turned, serif(right, True, self.foot, 0.0, 1.0)]
        return self.build("u", parts, b.side - b.reach, b.side - b.reach * 0.3)

    def r(self):
        b = self.b
        a = self.stem(0, 0, b.x)
        arm = self.spine_stroke([(b.stem * 0.92, b.x * 0.62), (b.stem + b.x * 0.18, b.x + b.over * 0.2 - b.hair * 0.5),
                                 (b.stem + b.x * 0.42, b.x - b.hair * 0.6)], start=(0.6, 1), end=(1, -0.2))
        parts = [*self.head_on(a), self.feet(a, 1.0, 1.25), arm, self.ball(b.stem + b.x * 0.40, b.x - b.stem * 0.42)]
        return self.build("r", parts, b.side - b.reach, b.open_side)

    def t(self):
        b = self.b
        top = b.x + b.x * 0.26
        st = stroke_edges(0, 0, b.stem, b.stem, b.x * 0.22, top)
        tip = polygon([(0, top), (b.stem, top), (b.stem, top - 4), (0, top - b.stem * 0.55)])
        foot = self.spine_stroke([(b.stem * 0.5, b.x * 0.26), (b.stem * 0.62, b.x * 0.02), (b.stem + b.x * 0.18, -b.over),
                                  (b.stem + b.x * 0.36, b.x * 0.10)], start=(0, -1), end=(0.7, 0.8))
        bar = rect(-b.stem * 0.55, b.x - b.hair * 1.1, b.stem + b.x * 0.30, b.x)
        return self.build("t", [st.path(), tip, foot, bar], b.open_side, b.open_side)

    def f(self):
        b = self.b
        st = self.stem(0, 0, b.asc - b.x * 0.30)
        hook = self.spine_stroke([(b.stem * 0.5, b.asc - b.x * 0.34), (b.stem * 0.62, b.asc - b.x * 0.06),
                                  (b.stem + b.x * 0.22, b.asc + b.over * 0.3 - b.hair * 0.5), (b.stem + b.x * 0.44, b.asc - b.x * 0.09)],
                                 start=(0, 1), end=(1, -0.5))
        bar = rect(-b.stem * 0.55, b.x - b.hair * 1.1, b.stem + b.x * 0.36, b.x)
        parts = [st.path(), hook, self.ball(b.stem + b.x * 0.40, b.asc - b.x * 0.12), bar, self.feet(st, 1.0, 1.4)]
        return self.build("f", parts, b.open_side, b.open_side - 20)

    def j(self):
        b = self.b
        st = self.stem(0, b.desc * 0.35, b.x)
        hook = self.spine_stroke([(b.stem * 0.5, b.desc * 0.30), (b.stem * 0.35, b.desc * 0.85), (-b.x * 0.22, b.desc + b.over),
                                  (-b.x * 0.36, b.desc * 0.80)], start=(0, -1), end=(-0.6, 1))
        parts = [*self.head_on(st), hook, self.ball(-b.x * 0.34, b.desc * 0.80), self.dot(b.stem * 0.4, b.x + b.stem * 1.45)]
        return self.build("j", parts, b.side - b.x * 0.30, b.side)

    def k(self):
        b = self.b
        st = self.stem(0, 0, b.asc)
        arm = stroke((b.stem * 0.9, b.x * 0.30), (b.stem + b.x * 0.48, b.x), b.hair * 1.25)
        leg = stroke((b.stem + b.x * 0.52, 0), (b.stem + b.x * 0.08, b.x * 0.46), b.stem * 0.92)
        arm_p = minus(arm.path(), rect(-500, -500, b.stem * 0.6, 2000))
        parts = [*self.head_on(st), self.feet(st), arm_p, serif(arm, False, self.foot, 0.8, 0.9), leg.path(),
                 serif(leg, True, self.foot, 0.6, 1.0)]
        return self.build("k", parts, b.side - b.reach, b.open_side)

    def a(self):
        b = self.b
        w = b.x * 0.92
        st = self.stem(w - b.stem, b.x * 0.08, b.x * 0.70)
        hook = self.spine_stroke([(b.x * 0.10, b.x * 0.80), (b.x * 0.42, b.x + b.over - b.hair * 0.5), (w - b.stem * 0.5, b.x * 0.62)],
                                 start=(0.3, 1), end=(0, -1))
        bowl = self.spine_stroke([(w - b.stem * 0.8, b.x * 0.50), (b.x * 0.30, b.x * 0.44), (b.stem * 0.55, b.x * 0.20),
                                  (b.x * 0.32, -b.over + b.hair * 0.5), (w - b.stem * 0.9, b.x * 0.16)],
                                 start=(-1, -0.15), end=(0.8, 0.6), pen=self.pen)
        tail = self.spine_stroke([(w - b.stem * 0.5, b.x * 0.12), (w - b.stem * 0.2, -b.over * 0.5 + b.hair * 0.5),
                                  (w + b.x * 0.10, b.x * 0.06)], start=(0.2, -1), end=(1, 0.6))
        parts = [st.path(), hook, self.ball(b.x * 0.15, b.x * 0.76), bowl, tail]
        return self.build("a", parts, b.round_side, b.open_side)

    def c(self):
        b = self.b
        w = b.x * 0.92
        body = pen_stroke(arc_segments(w / 2, b.x / 2, (w - self.pen.width * 0.9) / 2, (b.x - b.hair) / 2 + b.over, 42, 320),
                          self.pen, steps=48)
        return self.build("c", [body, self.ball(w * 0.84, b.x * 0.76)], b.round_side, b.open_side)

    def e(self):
        b = self.b
        w = b.x * 0.96
        rx, ry = (w - self.pen.width * 0.9) / 2, (b.x - b.hair) / 2 + b.over
        body = pen_stroke(arc_segments(w / 2, b.x / 2, rx, ry, 4, 328), self.pen, steps=48)
        bar = rect(w / 2 - rx, b.x * 0.50, w / 2 + rx + self.pen.width * 0.25, b.x * 0.50 + b.hair * 1.1)
        return self.build("e", [body, bar], b.round_side, b.open_side)

    def g(self):
        b = self.b
        w = b.x * 1.0
        bowl = self.bowl((w - b.stem * 0.55) / 2, w - b.stem * 0.55, -b.over, b.x + b.over)
        st = self.stem(w - b.stem, b.desc * 0.30, b.x)
        hook = self.spine_stroke([(w - b.stem * 0.5, b.desc * 0.25), (w - b.stem * 0.7, b.desc * 0.85), (w * 0.45, b.desc + b.over),
                                  (w * 0.12, b.desc * 0.72)], start=(0, -1), end=(-0.7, 1))
        top_spur = rect(w - b.stem, b.x - b.hair, w + b.reach * 0.6, b.x + b.over * 0.3)
        return self.build("g", [bowl, st.path(), hook, self.ball(w * 0.13, b.desc * 0.70), top_spur], b.round_side, b.side - b.reach)

    def y(self):
        b = self.b
        g = self.small.V()
        w = b.x * 0.84
        tail = self.spine_stroke([(w * 0.50, b.hair), (w * 0.38, b.desc * 0.55), (w * 0.15, b.desc + b.over), (-w * 0.02, b.desc * 0.80)],
                                 start=(-0.35, -1), end=(-0.8, 0.8))
        return self.build("y", [g.path, tail, self.ball(0, b.desc * 0.80)], b.open_side, b.open_side)

    def more_letters(self) -> list[Glyph]:
        sm = self.small
        return [self.a(), self.c(), self.e(), self.f(), self.g(), self.j(), self.k(), self.r(), self.renamed(sm.S(), "s"),
                self.t(), self.u(), self.renamed(sm.V(), "v"), self.renamed(sm.W(), "w"), self.renamed(sm.X(), "x"),
                self.y(), self.renamed(sm.Z(), "z")]

    # ---- everything else (ours) ---------------------------------------------------------------------------
    # The capitals are the display face's (`faces.py`) at this face's cap height, a little lighter. The figures and
    # the marks are the uncial's (`uncial.py`), drawn by a pen of nearly this weight, set to this face's x-height:
    # old-style figures and round dots sit with an old-style roman. The accented letters as `accents.py` makes them.

    def from_uncial(self, glyphs: list[Glyph]) -> list[Glyph]:
        from uncial import UncialMeasures
        k = self.b.x / UncialMeasures().x
        out = []
        for g in glyphs:
            p = Path()
            g.path.draw(TransformPen(p.getPen(), (k, 0, 0, k, 0, 0)))
            out.append(Glyph(g.name, p, g.advance * k))
        return out

    def joined(self, name: str, first: Glyph, second: Glyph, overlap: float) -> Glyph:
        x0, _, x1, _ = first.path.bounds
        sx0, _, _, _ = second.path.bounds
        moved = Path()
        second.path.draw(TransformPen(moved.getPen(), (1, 0, 0, 1, x1 - overlap - sx0, 0)))
        return self.build(name, [first.path, moved], self.b.round_side, self.b.open_side)

    def specials(self, lower: dict[str, Glyph]) -> list[Glyph]:
        b = self.b
        o = lower["o"]
        ox0, _, ox1, _ = o.path.bounds
        slash = stroke((ox0 + b.hair, -b.x * 0.10), (ox1 - b.hair, b.x * 1.10), b.hair * 1.2).path()
        d = lower["d"]
        dx0, _, dx1, _ = d.path.bounds
        eth_bar = rect(dx1 - b.stem * 1.9, b.asc - b.x * 0.30, dx1 + b.reach * 0.6, b.asc - b.x * 0.30 + b.hair * 1.1)
        st = self.stem(0, b.desc, b.asc)
        bowl = self.bowl(b.stem * 0.55 + (b.x * 1.05 - b.stem * 0.55) / 2, b.x * 1.05 - b.stem * 0.55, -b.over, b.x + b.over)
        thorn = self.build("thorn", [*self.head_on(st), bowl, self.feet(st)], b.side - b.reach, b.round_side)
        return [self.joined("ae", lower["a"], lower["e"], b.stem * 0.9), self.joined("oe", o, lower["e"], b.stem * 0.9),
                self.build("oslash", [o.path, slash], b.round_side, b.round_side),
                self.build("eth", [d.path, eth_bar], b.round_side, b.side - b.reach),
                thorn, self.joined("germandbls", lower["s"], lower["s"], -b.side * 0.6)]

    def everything(self) -> list[Glyph]:
        from accents import ACCENTED, ACCENTED_CAPITALS, accented
        from uncial import Uncial
        u = Uncial()
        lower = {g.name: g for g in self.letters()}
        out = list(lower.values())
        caps = self.caps.letters()
        caps = [g for g in caps if g.name not in ("space", "ankh")]
        out += caps
        out += self.from_uncial(u.figures() + u.marks() + [u.hyphen()])
        k = self.b.x / u.m.x
        bases = dict(lower)
        bases["i"] = self.i("dotlessi", dotted=False)                 # an accent replaces the i's dot
        out.append(bases["i"])

        def accent(kind: str) -> Path:
            p = Path()
            u.accent(kind).draw(TransformPen(p.getPen(), (k, 0, 0, k, 0, 0)))
            return p
        out += accented(bases, accent, self.b.x, 90, ACCENTED)
        out += accented({g.name: g for g in caps}, accent, self.b.cap, 70, ACCENTED_CAPITALS, scale=0.85)
        out += self.specials(lower)
        big_o = next(g for g in caps if g.name == "O")
        x0, _, x1, _ = big_o.path.bounds
        cap_slash = stroke((x0 + self.b.hair, -self.b.cap * 0.07), (x1 - self.b.hair, self.b.cap * 1.07), self.b.hair * 1.2).path()
        out.append(Glyph("Oslash", union([big_o.path, cap_slash]), big_o.advance))
        return out

    def letters(self) -> list[Glyph]:
        out = [self.n(), self.h(), self.m(), self.i(), self.l(), self.o(), self.b_(), self.d(), self.p(), self.q()]
        out += self.more_letters()
        out.append(Glyph("space", Path(), self.b.x * 0.50))
        return out
