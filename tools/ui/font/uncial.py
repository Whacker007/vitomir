# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
**Vitomir Uncial** (ours): a lower case for Vitomir's words on 1.1's own screens
(*"keep the modifications to the original v1.1 menus thematically consistent"*). 1.1 draws the words of those screens
as pictures — round, open letters, thick upright and thin across, with flared ends in the uncial manner — and ships no font for them,
so Vitomir's own words beside them (fixes, always run, confirm quick load, key bindings, defaults) need a lettering
of the same kind. This is drawn from the uncial hand (the round book hand of the 4th to 8th centuries: nobody's),
with its own proportions, not traced from 1.1's pictures: round bowls, short ascenders and descenders, an n whose
right leg drops below the line, a d whose stem leans back over its bowl, strokes thick upright and thin across, widening at their ends.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from fontTools.pens.transformPen import TransformPen
from pathops import Path

from faces import Glyph
from geom import Pen, arc_segments, ellipse_segments, minus, pen_stroke, polygon, rect, through, union


@dataclass(frozen=True)
class UncialMeasures:
    x: float = 500            # the x-height
    asc: float = 720          # an ascender
    desc: float = -190        # a descender
    over: float = 14
    weight: float = 98        # the pen's width
    least: float = 0.36       # its narrowest stroke, as a share of the width: thick upright, thin across
    angle: float = 12         # the pen's angle, degrees
    flare: float = 0.55       # how much wider a stroke grows at its ends
    side: float = 56          # side bearings: 1.1's words are loosely spaced


class Uncial:
    def __init__(self, m: UncialMeasures = UncialMeasures()):
        self.m = m
        self.pen = Pen(m.weight, m.angle, m.weight * m.least)

    # ---- strokes ------------------------------------------------------------------------------------------------

    def flared(self, segs, ends: tuple[bool, bool] = (True, True), steps: int = 36) -> Path:
        """A stroke along cubics, a little wider at its free ends (the flare of a brush lifted)."""
        m = self.m

        def w(i, n, base):
            k = i / max(1, n - 1)
            f = 0.0
            if ends[0] and k < 0.18:
                f = (0.18 - k) / 0.18
            if ends[1] and k > 0.82:
                f = (k - 0.82) / 0.18
            return base * (1 + m.flare * f * f)
        return pen_stroke(segs, self.pen, steps=steps, width_at=w)

    def line(self, a, b, ends=(True, True)) -> Path:
        seg = [(a, (a[0] + (b[0] - a[0]) / 3, a[1] + (b[1] - a[1]) / 3),
                (a[0] + 2 * (b[0] - a[0]) / 3, a[1] + 2 * (b[1] - a[1]) / 3), b)]
        return self.flared(seg, ends, steps=12)

    def curve(self, pts, start=None, end=None, ends=(True, True)) -> Path:
        return self.flared(through(pts, start_dir=start, end_dir=end), ends)

    def ring(self, cx, cy, rx, ry) -> Path:
        return pen_stroke(ellipse_segments(cx, cy, rx, ry), self.pen, closed=True, steps=64)

    def arc(self, cx, cy, rx, ry, a0, a1, ends=(True, True)) -> Path:
        return self.flared(arc_segments(cx, cy, rx, ry, a0, a1), ends, steps=48)

    def build(self, name: str, parts: list[Path], lsb: float | None = None, rsb: float | None = None) -> Glyph:
        from fontTools.pens.transformPen import TransformPen
        m = self.m
        lsb = m.side if lsb is None else lsb
        rsb = m.side if rsb is None else rsb
        path = union(parts)
        x0, _, x1, _ = path.bounds
        moved = Path()
        path.draw(TransformPen(moved.getPen(), (1, 0, 0, 1, lsb - x0, 0)))
        return Glyph(name, moved, lsb + (x1 - x0) + rsb)

    # ---- the letters --------------------------------------------------------------------------------------------
    # Spines run through the middle of the stroke: a stem's spine is half the pen's width in from its edge.

    @property
    def h(self) -> float:            # half the pen, the inset of a spine from the letter's edge
        return self.m.weight / 2

    @property
    def thin(self) -> float:         # half the thin stroke: the inset of a spine from a round letter's top and bottom
        return self.m.weight * self.m.least / 2 * 1.15

    def o(self, name="o"):
        m, h, t = self.m, self.h, self.thin
        w = 560
        return self.build(name, [self.ring(w / 2, m.x / 2, w / 2 - h, m.x / 2 + m.over - t)])

    def c(self, name="c"):
        m, h, t = self.m, self.h, self.thin
        w = 500
        return self.build(name, [self.arc(w / 2 + 10, m.x / 2, w / 2 - h, m.x / 2 + m.over - t, 48, 312)], rsb=m.side * 0.6)

    def e(self):
        m, h, t = self.m, self.h, self.thin
        w = 510
        cx, cy, rx, ry = w / 2 + 10, m.x / 2, w / 2 - h, m.x / 2 + m.over - h
        bowl = self.arc(cx, cy, rx, ry, 30, 318, ends=(False, True))
        bar = self.line((cx - rx + 8, cy + 20), (cx + rx * 0.95, cy + 20), ends=(False, False))
        return self.build("e", [bowl, bar], rsb=m.side * 0.6)

    def a(self):
        # The uncial a: a round bowl and a stem down its right side, its foot turned out.
        m, h, t = self.m, self.h, self.thin
        w = 520
        bowl = self.ring(w / 2 - 20, m.x / 2, w / 2 - h - 20, m.x / 2 + m.over - t)
        stem = self.curve([(w - h, m.x + m.over - t), (w - h, m.x * 0.35), (w - h + 10, m.x * 0.08), (w + 40, h * 0.6)],
                          start=(0, -1), end=(1, -0.2), ends=(False, True))
        return self.build("a", [bowl, stem], rsb=m.side * 0.5)

    def d(self):
        # The uncial d: a round bowl, its stem leaning back over it and curling to the left at the top.
        m, h, t = self.m, self.h, self.thin
        w = 540
        bowl = self.ring(w / 2 - 20, m.x / 2, w / 2 - h - 20, m.x / 2 + m.over - t)
        stem = self.curve([(w - h - 10, m.x * 0.55), (w - h - 24, m.x * 1.08), (w * 0.70, m.asc - h * 0.35), (w * 0.38, m.asc - h * 0.75)],
                          start=(0, 1), end=(-1, -0.25), ends=(False, True))
        return self.build("d", [bowl, stem])

    def b(self):
        m, h, t = self.m, self.h, self.thin
        w = 540
        stem = self.line((h, m.asc - h * 0.3), (h, h * 0.2), ends=(True, False))
        bowl = self.ring(w / 2 + 20, m.x / 2, w / 2 - h - 20, m.x / 2 + m.over - t)
        return self.build("b", [stem, bowl])

    def p(self):
        m, h, t = self.m, self.h, self.thin
        w = 540
        stem = self.line((h, m.x + m.over * 0.5 - h * 0.4), (h, m.desc + h * 0.3))
        bowl = self.ring(w / 2 + 20, m.x / 2, w / 2 - h - 20, m.x / 2 + m.over - t)
        return self.build("p", [stem, bowl])

    def q(self):
        m, h, t = self.m, self.h, self.thin
        w = 540
        bowl = self.ring(w / 2 - 20, m.x / 2, w / 2 - h - 20, m.x / 2 + m.over - t)
        stem = self.line((w - h, m.x + m.over * 0.5 - h * 0.4), (w - h, m.desc + h * 0.3))
        return self.build("q", [bowl, stem])

    def g(self):
        # One storey: the bowl, and a stem down its right side into a hook under the line.
        m, h, t = self.m, self.h, self.thin
        w = 520
        bowl = self.ring(w / 2 - 20, m.x / 2, w / 2 - h - 20, m.x / 2 + m.over - t)
        stem = self.curve([(w - h, m.x + m.over - t), (w - h, 0), (w * 0.62, m.desc + h * 0.7), (w * 0.12, m.desc + h * 1.2)],
                          start=(0, -1), end=(-1, 0.35), ends=(False, True))
        return self.build("g", [bowl, stem])

    def n(self, name="n", ascender=False, hook=True):
        # The left stem, and an arch from it whose right leg drops below the line and turns back under it.
        m, h, t = self.m, self.h, self.thin
        w = 520
        top = m.asc - h * 0.3 if ascender else m.x - h * 0.2
        stem = self.line((h, top), (h, h * 0.2))
        if hook:
            leg = self.curve([(h + 6, m.x * 0.62), (w * 0.45, m.x + m.over - t), (w - h, m.x * 0.66), (w - h, 0),
                              (w - h * 1.6, m.desc + h * 0.8)], start=(0.35, 1), end=(-0.8, -0.6), ends=(False, True))
        else:
            leg = self.curve([(h + 6, m.x * 0.62), (w * 0.45, m.x + m.over - t), (w - h, m.x * 0.66), (w - h, h * 0.2)],
                             start=(0.35, 1), end=(0, -1), ends=(False, True))
        return self.build(name, [stem, leg])

    def h_(self):
        return self.n("h", ascender=True, hook=False)

    def m_(self):
        m, h, t = self.m, self.h, self.thin
        w = 780
        stem = self.line((h, m.x - h * 0.2), (h, h * 0.2))
        mid = w * 0.5
        first = self.curve([(h + 6, m.x * 0.62), (mid * 0.5, m.x + m.over - t), (mid, m.x * 0.66), (mid, h * 0.2)],
                           start=(0.35, 1), end=(0, -1), ends=(False, True))
        second = self.curve([(mid + 4, m.x * 0.62), (mid + (w - mid) * 0.45, m.x + m.over - t), (w - h, m.x * 0.66), (w - h, 0),
                             (w - h * 1.6, m.desc + h * 0.8)], start=(0.35, 1), end=(-0.8, -0.6), ends=(False, True))
        return self.build("m", [stem, first, second])

    def u(self):
        m, h, t = self.m, self.h, self.thin
        w = 520
        bowl = self.curve([(h, m.x - h * 0.2), (h, m.x * 0.35), (w * 0.45, -m.over + t), (w - h - 4, m.x * 0.38)],
                          start=(0, -1), end=(0.3, 1), ends=(True, False))
        stem = self.line((w - h, m.x - h * 0.2), (w - h, h * 0.2))
        return self.build("u", [bowl, stem])

    def i(self, name="i", descender=False):
        # No dot: a short stem with a flared head, as the round hand has it.
        m, h, t = self.m, self.h, self.thin
        if descender:
            stem = self.curve([(h + 60, m.x - h * 0.2), (h + 60, 0), (h, m.desc + h * 0.8), (-h * 1.2, m.desc + h * 1.0)],
                              start=(0, -1), end=(-1, 0.3))
        else:
            stem = self.line((h, m.x - h * 0.2), (h, h * 0.2))
        return self.build(name, [stem])

    def j(self):
        return self.i("j", descender=True)

    def l(self):
        # A tall stem whose foot turns out to the right.
        m, h, t = self.m, self.h, self.thin
        stem = self.curve([(h, m.asc - h * 0.3), (h, m.x * 0.3), (h + 40, h * 0.5), (h + 150, h * 0.7)],
                          start=(0, -1), end=(1, 0.25))
        return self.build("l", [stem], rsb=m.side * 0.5)

    def t(self):
        m, h, t = self.m, self.h, self.thin
        stem = self.curve([(h + 50, m.x + 150), (h + 50, m.x * 0.3), (h + 90, h * 0.5), (h + 230, h * 0.8)],
                          start=(0, -1), end=(1, 0.3), ends=(True, True))
        bar = self.line((0, m.x - h * 0.5), (h + 200, m.x - h * 0.5))
        return self.build("t", [stem, bar], rsb=m.side * 0.5)

    def f(self):
        m, h, t = self.m, self.h, self.thin
        stem = self.curve([(h + 260, m.asc - h * 1.2), (h + 120, m.asc - h * 0.5), (h + 30, m.asc - h * 1.4), (h + 30, m.x),
                           (h + 30, h * 0.2)], start=(-1, 0.5), end=(0, -1), ends=(True, True))
        bar = self.line((0, m.x - h * 0.5), (h + 220, m.x - h * 0.5))
        return self.build("f", [stem, bar], rsb=m.side * 0.3)

    def r(self):
        m, h, t = self.m, self.h, self.thin
        stem = self.line((h, m.x - h * 0.2), (h, h * 0.2))
        arm = self.curve([(h + 6, m.x * 0.6), (h + 110, m.x + m.over - t), (h + 290, m.x - h * 0.5)],
                         start=(0.4, 1), end=(1, -0.3), ends=(False, True))
        return self.build("r", [stem, arm], rsb=m.side * 0.4)

    def s(self):
        m, h, t = self.m, self.h, self.thin
        w = 400
        pts = [(w - h * 0.9, m.x * 0.82), (w * 0.52, m.x + m.over - t), (h + 6, m.x * 0.72), (w * 0.5, m.x * 0.5),
               (w - h - 6, m.x * 0.27), (w * 0.47, -m.over + t), (h * 0.9, m.x * 0.17)]
        return self.build("s", [self.curve(pts, start=(-1, 0.6), end=(-1, 0.6))])

    def v(self, name="v"):
        m, h, t = self.m, self.h, self.thin
        w = 500
        return self.build(name, [self.line((h, m.x - h * 0.2), (w / 2, h * 0.3), ends=(True, False)),
                                 self.line((w / 2, h * 0.3), (w - h, m.x - h * 0.2), ends=(False, True))])

    def w_(self):
        m, h, t = self.m, self.h, self.thin
        w = 760
        q = (w - 2 * h) / 4
        pts = [(h, m.x - h * 0.2), (h + q, h * 0.3), (h + 2 * q, m.x * 0.7), (h + 3 * q, h * 0.3), (w - h, m.x - h * 0.2)]
        parts = [self.line(pts[i], pts[i + 1], ends=(i == 0, i == 3)) for i in range(4)]
        return self.build("w", parts)

    def x_(self):
        m, h, t = self.m, self.h, self.thin
        w = 480
        return self.build("x", [self.line((h, m.x - h * 0.2), (w - h, h * 0.2)), self.line((w - h, m.x - h * 0.2), (h, h * 0.2))])

    def y(self):
        m, h, t = self.m, self.h, self.thin
        w = 500
        left = self.line((h, m.x - h * 0.2), (w * 0.5, h * 0.6), ends=(True, False))
        right = self.curve([(w - h, m.x - h * 0.2), (w * 0.45, 0), (w * 0.22, m.desc + h * 0.9), (-h * 0.4, m.desc + h * 1.1)],
                           start=(-0.45, -1), end=(-1, 0.1))
        return self.build("y", [left, right])

    def k(self):
        m, h, t = self.m, self.h, self.thin
        w = 480
        stem = self.line((h, m.asc - h * 0.3), (h, h * 0.2))
        arm = self.line((h + 10, m.x * 0.42), (w - h, m.x - h * 0.2), ends=(False, True))
        leg = self.line((h + 90, m.x * 0.52), (w - h, h * 0.2), ends=(False, True))
        return self.build("k", [stem, arm, leg])

    def z(self):
        m, h, t = self.m, self.h, self.thin
        w = 460
        top = self.line((h, m.x - h * 0.5), (w - h, m.x - h * 0.5))
        diag = self.line((w - h * 1.2, m.x - h * 0.6), (h * 1.2, h * 0.6), ends=(False, False))
        foot = self.line((h, h * 0.5), (w - h, h * 0.5))
        return self.build("z", [top, diag, foot])

    def space(self):
        return Glyph("space", Path(), 260)

    def hyphen(self):
        m, h, t = self.m, self.h, self.thin
        return self.build("hyphen", [self.line((h, m.x * 0.48), (220, m.x * 0.48))])

    # ---- the figures (ours) -------------------------------------------------------------------------------
    # Lining, as 1.1's own menus write theirs ("32 . 128 . 512", "0 . . . 5"): every figure the height of the line's
    # tall letters' bodies, on the baseline, so a number on a scale or in a list reads level. Drawn by the same pen.
    # Named as the Adobe glyph list names them.

    def fig(self, name: str, parts: list[Path], width: float) -> Glyph:
        """A figure: every figure the same width (they stand in columns: a key, a resolution, a volume)."""
        path = union(parts)
        x0, _, x1, _ = path.bounds
        moved = Path()
        path.draw(TransformPen(moved.getPen(), (1, 0, 0, 1, (width - (x1 - x0)) / 2 - x0, 0)))
        return Glyph(name, moved, width)

    FIGURE = 500
    FIG_H = 650               # a figure's height (the x-height is 500, an ascender 720)

    def zero(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 400
        return self.fig("zero", [self.ring(w / 2, H / 2, w / 2 - h, H / 2 + m.over - t)], self.FIGURE)

    def one(self):
        m, h, H = self.m, self.h, self.FIG_H
        flag = self.line((-30, H - 190), (h + 60, H - h * 0.2), ends=(True, False))
        stem = self.line((h + 60, H - h * 0.2), (h + 60, h * 0.2), ends=(False, True))
        return self.fig("one", [flag, stem], self.FIGURE)

    def two(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 400
        neck = self.curve([(h * 0.9, H * 0.74), (w * 0.48, H + m.over - t), (w - h, H * 0.70), (w * 0.45, H * 0.30),
                           (h * 0.7, h * 0.55)], start=(0.25, 1), end=(-1, -0.9), ends=(True, False))
        foot = self.line((h * 0.7, h * 0.5), (w - h * 0.4, h * 0.5), ends=(False, True))
        return self.fig("two", [neck, foot], self.FIGURE)

    def three(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 400
        top = self.curve([(h * 1.0, H * 0.80), (w * 0.50, H + m.over - t), (w - h, H * 0.76), (w * 0.38, H * 0.53)],
                         start=(0.3, 1), end=(-1, -0.35), ends=(True, False))
        low = self.curve([(w * 0.38, H * 0.53), (w - h + 6, H * 0.26), (w * 0.46, -m.over + t), (h * 0.7, H * 0.16)],
                         start=(1, -0.25), end=(-1, 0.5), ends=(False, True))
        return self.fig("three", [top, low], self.FIGURE)

    def four(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 440
        sx = w * 0.64
        diag = self.line((sx, H - h * 0.1), (h * 0.6, H * 0.30), ends=(False, False))
        bar = self.line((h * 0.6, H * 0.29), (w - h * 0.3, H * 0.29), ends=(False, True))
        stem = self.line((sx, H + m.over * 0.5 - h * 0.3), (sx, h * 0.2), ends=(True, True))
        return self.fig("four", [diag, bar, stem], self.FIGURE)

    def five(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 400
        x0 = h * 1.1
        bar = self.line((x0, H - h * 0.4), (w - h * 0.5, H - h * 0.4), ends=(False, True))
        neck = self.line((x0, H - h * 0.4), (x0 - 6, H * 0.56), ends=(False, False))
        bowl = self.curve([(x0 - 6, H * 0.56), (w * 0.52, H * 0.64), (w - h, H * 0.32), (w * 0.48, -m.over + t),
                           (h * 0.6, H * 0.14)], start=(0.6, 0.6), end=(-1, 0.5), ends=(False, True))
        return self.fig("five", [bar, neck, bowl], self.FIGURE)

    def six(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 410
        bh = H * 0.62
        bowl = self.ring(w / 2, bh / 2, w / 2 - h, bh / 2 + m.over - t)
        back = self.curve([(w - h * 0.9, H - h * 0.55), (w * 0.42, H + m.over - t - 6), (h + 2, H * 0.62), (h, bh * 0.5)],
                          start=(-1, 0.25), end=(0, -1), ends=(True, False))
        return self.fig("six", [bowl, back], self.FIGURE)

    def seven(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 410
        bar = self.line((h * 0.5, H - h * 0.4), (w - h, H - h * 0.4), ends=(True, False))
        leg = self.curve([(w - h, H - h * 0.4), (w * 0.62, H * 0.45), (w * 0.42, h * 0.3)],
                         start=(-0.55, -1), end=(-0.15, -1), ends=(False, True))
        return self.fig("seven", [bar, leg], self.FIGURE)

    def eight(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 410
        low_h = H * 0.56
        low = self.ring(w / 2, low_h / 2, w / 2 - h, low_h / 2 + m.over - t)
        up_y0 = low_h - t * 0.8
        up_h = H - up_y0
        up = self.ring(w / 2, up_y0 + up_h / 2, w / 2 - h - 24, up_h / 2 + m.over - t)
        return self.fig("eight", [low, up], self.FIGURE)

    def nine(self):
        m, h, t, H = self.m, self.h, self.thin, self.FIG_H
        w = 410
        bh = H * 0.62
        bowl = self.ring(w / 2, H - bh / 2, w / 2 - h, bh / 2 + m.over - t)
        tail = self.curve([(w - h, H - bh * 0.5), (w - h + 2, H * 0.30), (w * 0.58, -m.over + t + 6), (h * 0.9, h * 0.6)],
                          start=(0, -1), end=(-1, 0.25), ends=(False, True))
        return self.fig("nine", [bowl, tail], self.FIGURE)

    def figures(self) -> list[Glyph]:
        return [self.zero(), self.one(), self.two(), self.three(), self.four(), self.five(), self.six(), self.seven(),
                self.eight(), self.nine()]

    # ---- punctuation (ours) -------------------------------------------------------------------------------

    def blob(self, cx: float, cy: float, r: float, lean: float = 0.0) -> Path:
        """A dot: a pen's touch, a little taller than wide and leaning with the pen."""
        from fontTools.pens.transformPen import TransformPen
        segs = ellipse_segments(0, 0, r * 0.92, r * 1.08)
        p = Path()
        pen = TransformPen(p.getPen(), (1, 0, lean, 1, cx, cy))
        pen.moveTo(segs[0][0])
        for s in segs:
            pen.curveTo(s[1], s[2], s[3])
        pen.closePath()
        return p

    @property
    def dot_r(self) -> float:
        return self.m.weight * 0.56

    def tail(self, cx: float, cy: float) -> Path:
        """A comma's or an apostrophe's body: a dot with a stroke out of its right side, curling down and left."""
        r = self.dot_r
        return union([self.blob(cx, cy, r),
                      self.curve([(cx + r * 0.6, cy + r * 0.3), (cx + r * 0.75, cy - r * 1.3), (cx - r * 0.9, cy - r * 2.6)],
                                 start=(0.1, -1), end=(-1, -0.6), ends=(False, False))])

    def period(self):
        r = self.dot_r
        return self.build("period", [self.blob(r, r, r)], lsb=self.m.side * 0.8, rsb=self.m.side * 0.8)

    def comma(self):
        r = self.dot_r
        return self.build("comma", [self.tail(r, r)], lsb=self.m.side * 0.8, rsb=self.m.side * 0.8)

    def colon(self):
        m, r = self.m, self.dot_r
        return self.build("colon", [self.blob(r, r, r), self.blob(r, m.x - r, r)], lsb=m.side * 0.9, rsb=m.side * 0.9)

    def semicolon(self):
        m, r = self.m, self.dot_r
        return self.build("semicolon", [self.tail(r, r), self.blob(r, m.x - r, r)], lsb=m.side * 0.9, rsb=m.side * 0.9)

    def quotesingle(self, name="quoteright"):
        m, r = self.m, self.dot_r
        return self.build(name, [self.tail(r, m.asc - r)], lsb=m.side * 0.6, rsb=m.side * 0.6)

    def quoteleft(self):
        from fontTools.pens.transformPen import TransformPen
        m, r = self.m, self.dot_r
        body = self.tail(0, 0)
        turned = Path()
        body.draw(TransformPen(turned.getPen(), (-1, 0, 0, -1, r, m.asc - r * 2.6)))
        return self.build("quoteleft", [turned], lsb=m.side * 0.6, rsb=m.side * 0.6)

    def quotedbl(self, name="quotedblright"):
        m, r = self.m, self.dot_r
        gap = r * 2.6
        return self.build(name, [self.tail(r, m.asc - r), self.tail(r + gap, m.asc - r)], lsb=m.side * 0.6, rsb=m.side * 0.6)

    def quotedblleft(self):
        from fontTools.pens.transformPen import TransformPen
        m, r = self.m, self.dot_r
        gap = r * 2.6
        body = union([self.tail(0, 0), self.tail(gap, 0)])
        turned = Path()
        body.draw(TransformPen(turned.getPen(), (-1, 0, 0, -1, r + gap, m.asc - r * 2.6)))
        return self.build("quotedblleft", [turned], lsb=m.side * 0.6, rsb=m.side * 0.6)

    def exclam(self):
        m, h, r = self.m, self.h, self.dot_r
        stem = self.curve([(r, m.asc - h * 0.3), (r, m.x * 0.6), (r, r * 2.6 + 30)], start=(0, -1), end=(0, -1), ends=(True, False))
        return self.build("exclam", [stem, self.blob(r, r, r)], lsb=m.side * 0.9, rsb=m.side * 0.9)

    def question(self):
        m, h, t, r = self.m, self.h, self.thin, self.dot_r
        w = 380
        hook = self.curve([(h * 0.9, m.asc * 0.80), (w * 0.48, m.asc + m.over - t), (w - h, m.asc * 0.74),
                           (w * 0.48, m.asc * 0.46), (w * 0.44, r * 2.6 + 40)], start=(0.3, 1), end=(0, -1), ends=(True, False))
        return self.build("question", [hook, self.blob(w * 0.44, r, r)])

    def parens(self):
        from fontTools.pens.transformPen import TransformPen
        m, h = self.m, self.h
        w = 230
        left = self.curve([(w - h * 0.4, m.asc + 20), (h, (m.asc + m.desc) / 2), (w - h * 0.4, m.desc - 10)],
                          start=(-0.7, -1), end=(0.7, -1))
        right = Path()
        left.draw(TransformPen(right.getPen(), (-1, 0, 0, 1, w, 0)))
        return [self.build("parenleft", [left], lsb=m.side * 0.9, rsb=m.side * 0.5),
                self.build("parenright", [right], lsb=m.side * 0.5, rsb=m.side * 0.9)]

    def brackets(self):
        m, h = self.m, self.h
        top, bot = m.asc + 20, m.desc - 10
        x = h
        left = [self.line((x, top), (x, bot), ends=(False, False)), self.line((x, top - h * 0.3), (x + 150, top - h * 0.3)),
                self.line((x, bot + h * 0.3), (x + 150, bot + h * 0.3))]
        right = [self.line((x + 150, top), (x + 150, bot), ends=(False, False)), self.line((x + 150, top - h * 0.3), (x, top - h * 0.3)),
                 self.line((x + 150, bot + h * 0.3), (x, bot + h * 0.3))]
        return [self.build("bracketleft", left, lsb=m.side * 0.9, rsb=m.side * 0.4),
                self.build("bracketright", right, lsb=m.side * 0.4, rsb=m.side * 0.9)]

    def slash(self, name="slash", flip=False):
        m, h = self.m, self.h
        a, b = (h, m.desc + 10), (330, m.asc + 10)
        if flip:
            a, b = (330, m.desc + 10), (h, m.asc + 10)
        return self.build(name, [self.line(a, b)], lsb=m.side * 0.4, rsb=m.side * 0.4)

    def plus(self):
        m, h = self.m, self.h
        c, r = m.x * 0.48, 190
        return self.build("plus", [self.line((r, c - r), (r, c + r)), self.line((0, c), (2 * r, c))])

    def equal(self):
        m, h = self.m, self.h
        c = m.x * 0.48
        return self.build("equal", [self.line((0, c + 85), (380, c + 85)), self.line((0, c - 85), (380, c - 85))])

    def underscore(self):
        m = self.m
        return self.build("underscore", [self.line((0, m.desc + 40), (480, m.desc + 40), ends=(False, False))], lsb=0, rsb=0)

    def endash(self):
        m = self.m
        return self.build("endash", [self.line((0, m.x * 0.48), (440, m.x * 0.48))])

    def asterisk(self):
        m, h = self.m, self.h
        cx, cy, r = 0.0, m.asc - 170, 150
        arms = [self.line((cx + r * math.sin(math.radians(a)) * 0.15, cy + r * math.cos(math.radians(a)) * 0.15),
                          (cx + r * math.sin(math.radians(a)), cy + r * math.cos(math.radians(a))), ends=(False, True))
                for a in (0, 72, 144, 216, 288)]
        return self.build("asterisk", arms)

    def numbersign(self):
        m, h = self.m, self.h
        lo, hi = m.desc * 0.35, m.x + 90
        return self.build("numbersign", [self.line((130, lo), (190, hi)), self.line((310, lo), (370, hi)),
                                         self.line((20, m.x * 0.66), (480, m.x * 0.66)), self.line((0, m.x * 0.22), (460, m.x * 0.22))])

    def percent(self):
        m, h, t = self.m, self.h, self.thin
        w, rx, ry = 600, 92, 118
        up = self.ring(rx + 6, m.asc - ry - 10, rx - h * 0.42, ry - t)
        low = self.ring(w - rx - 6, ry + 4, rx - h * 0.42, ry - t)
        bar = self.line((110, m.desc * 0.25), (w - 110, m.asc - 10))
        return self.build("percent", [up, low, bar])

    def ampersand(self):
        # A plain ampersand (the uncial hand's et reads as a 7 beside the figures): a small loop above, a round bowl
        # below, and the stroke through them running out to a foot on the right.
        m, h, t = self.m, self.h, self.thin
        w = 560
        loop = self.curve([(w * 0.70, h * 0.3), (w * 0.38, m.x * 0.70), (w * 0.26, m.asc * 0.78), (w * 0.44, m.asc + m.over - t),
                           (w * 0.62, m.asc * 0.80), (w * 0.47, m.asc * 0.60), (h, m.x * 0.30), (w * 0.36, -m.over + t),
                           (w * 0.70, m.x * 0.30), (w - h * 0.3, m.x * 0.55)],
                          start=(-0.6, 1), end=(1, 0.5), ends=(True, True))
        return self.build("ampersand", [loop])

    def at(self):
        m, h, t = self.m, self.h, self.thin
        w = 760
        a = self.a()
        from fontTools.pens.transformPen import TransformPen
        small = Path()
        a.path.draw(TransformPen(small.getPen(), (0.62, 0, 0, 0.62, w * 0.27, m.x * 0.15)))
        ring = self.arc(w / 2, m.x * 0.42, w / 2 - h, m.x * 0.42 + 190, -40, 300)
        return self.build("at", [small, ring])

    def less_greater(self):
        m = self.m
        c = m.x * 0.48
        return [self.build("less", [self.line((380, c + 190), (0, c), ends=(True, False)), self.line((0, c), (380, c - 190), ends=(False, True))]),
                self.build("greater", [self.line((0, c + 190), (380, c), ends=(True, False)), self.line((380, c), (0, c - 190), ends=(False, True))])]

    def dollar(self):
        m, h = self.m, self.h
        s_ = self.s()
        from fontTools.pens.transformPen import TransformPen
        body = Path()
        s_.path.draw(TransformPen(body.getPen(), (1, 0, 0, 1.12, 0, 20)))
        x0, _, x1, _ = body.bounds
        cx = (x0 + x1) / 2
        bar = self.line((cx, m.x + 130), (cx, -120), ends=(False, False))
        return self.build("dollar", [body, bar])

    def sterling(self):
        m, h = self.m, self.h
        stem = self.curve([(420, m.asc - h * 0.9), (300, m.asc - h * 0.2), (170, m.asc - h * 0.9), (160, m.x * 0.6), (120, h * 0.6)],
                          start=(-1, 0.6), end=(-0.5, -1), ends=(True, False))
        bar = self.line((40, m.x * 0.55), (330, m.x * 0.55))
        foot = self.line((40, h * 0.5), (460, h * 0.5), ends=(False, True))
        return self.build("sterling", [stem, bar, foot])

    def euro(self):
        m, h, t = self.m, self.h, self.thin
        w = 540
        c = self.arc(w / 2 + 40, m.asc * 0.47, w / 2 - h, m.asc * 0.47 + m.over - t, 45, 315)
        bars = [self.line((0, m.asc * 0.56), (w * 0.62, m.asc * 0.56)), self.line((0, m.asc * 0.38), (w * 0.58, m.asc * 0.38))]
        return self.build("Euro", [c] + bars)

    def braces(self):
        from fontTools.pens.transformPen import TransformPen
        m, h = self.m, self.h
        top, bot, mid = m.asc + 20, m.desc - 10, (m.asc + m.desc) / 2
        left = self.curve([(260, top), (140, top - 60), (150, mid + 120), (40, mid), (150, mid - 120), (140, bot + 60), (260, bot)],
                          start=(-1, -0.2), end=(1, 0.2), ends=(True, True))
        right = Path()
        left.draw(TransformPen(right.getPen(), (-1, 0, 0, 1, 300, 0)))
        return [self.build("braceleft", [left], lsb=m.side * 0.9, rsb=m.side * 0.4),
                self.build("braceright", [right], lsb=m.side * 0.4, rsb=m.side * 0.9)]

    def bar(self):
        m = self.m
        return self.build("bar", [self.line((0, m.asc + 20), (0, m.desc - 10), ends=(False, False))], lsb=m.side * 1.2, rsb=m.side * 1.2)

    def guillemets(self):
        m = self.m
        c, r, d = m.x * 0.48, 120, 150

        def chevron(x, left):
            if left:
                return [self.line((x + d, c + r), (x, c), ends=(True, False)), self.line((x, c), (x + d, c - r), ends=(False, True))]
            return [self.line((x, c + r), (x + d, c), ends=(True, False)), self.line((x + d, c), (x, c - r), ends=(False, True))]
        return [self.build("guillemotleft", chevron(0, True) + chevron(170, True)),
                self.build("guillemotright", chevron(0, False) + chevron(170, False))]

    def turned(self, g: Glyph, name: str) -> Glyph:
        """A glyph turned upside down about the x-height's middle (the Spanish ¡ and ¿)."""
        from fontTools.pens.transformPen import TransformPen
        p = Path()
        g.path.draw(TransformPen(p.getPen(), (-1, 0, 0, -1, 0, self.m.x - 40)))
        return self.build(name, [p])

    def degree(self):
        m, h, t = self.m, self.h, self.thin
        return self.build("degree", [self.ring(110, m.asc - 110, 110 - h * 0.42, 110 - t)])

    def middot(self):
        r = self.dot_r
        return self.build("periodcentered", [self.blob(r, self.m.x * 0.48, r)], lsb=self.m.side * 0.8, rsb=self.m.side * 0.8)

    def ellipsis(self):
        r, m = self.dot_r, self.m
        return self.build("ellipsis", [self.blob(r, r, r), self.blob(r + 230, r, r), self.blob(r + 460, r, r)])

    def emdash(self):
        m = self.m
        return self.build("emdash", [self.line((0, m.x * 0.48), (860, m.x * 0.48))])

    def multiply(self):
        m = self.m
        c, r = m.x * 0.48, 150
        return self.build("multiply", [self.line((0, c - r), (2 * r, c + r)), self.line((0, c + r), (2 * r, c - r))])

    def copyright(self):
        m, h, t = self.m, self.h, self.thin
        w = 760
        from fontTools.pens.transformPen import TransformPen
        small = Path()
        self.c().path.draw(TransformPen(small.getPen(), (0.62, 0, 0, 0.62, w * 0.27, m.asc * 0.5 - m.x * 0.31)))
        return self.build("copyright", [small, self.ring(w / 2, m.asc * 0.5, w / 2 - h * 0.6, m.asc * 0.5 + 40)])

    # The accents, drawn by the same pen; the accented letters are made of them by `accents.py`.

    def accent(self, kind: str) -> Path:
        m, h, r = self.m, self.h, self.dot_r * 0.82
        if kind == "acute":
            return self.line((0, 0), (110, 150), ends=(False, True))
        if kind == "grave":
            return self.line((110, 0), (0, 150), ends=(False, True))
        if kind == "circumflex":
            return union([self.line((0, 0), (110, 140), ends=(True, False)), self.line((110, 140), (220, 0), ends=(False, True))])
        if kind == "caron":
            return union([self.line((0, 140), (110, 0), ends=(True, False)), self.line((110, 0), (220, 140), ends=(False, True))])
        if kind == "dieresis":
            return union([self.blob(r, r, r), self.blob(r + 190, r, r)])
        if kind == "dotaccent":
            return self.blob(r, r, r)
        if kind == "tilde":
            return self.curve([(0, 20), (70, 110), (160, 40), (240, 120)], start=(0.5, 1), end=(0.5, 1))
        if kind == "ring":
            return self.ring(85, 85, 85 - h * 0.4, 85 - self.thin * 0.8)
        if kind == "cedilla":
            return self.curve([(40, 0), (60, -70), (120, -110), (40, -190), (-40, -170)], start=(0.2, -1), end=(-1, 0.3),
                              ends=(False, True))
        raise ValueError(kind)

    def spacing_accents(self) -> list[Glyph]:
        out = []
        for name, kind, y in (("grave", "grave", self.m.x + 120), ("asciicircum", "circumflex", self.m.asc - 150),
                              ("asciitilde", "tilde", self.m.x * 0.40), ("acute", "acute", self.m.x + 120),
                              ("dieresis", "dieresis", self.m.x + 120), ("cedilla", "cedilla", 0)):
            from fontTools.pens.transformPen import TransformPen
            p = Path()
            self.accent(kind).draw(TransformPen(p.getPen(), (1, 0, 0, 1, 0, y)))
            out.append(self.build(name, [p]))
        return out

    # The letters of Latin-1 that are not a letter and an accent.

    def joined(self, name: str, first: Glyph, second: Glyph, overlap: float) -> Glyph:
        """Two letters run together (æ, œ): the second set `overlap` into the first's right side."""
        x0, _, x1, _ = first.path.bounds
        sx0, _, _, _ = second.path.bounds
        moved = Path()
        second.path.draw(TransformPen(moved.getPen(), (1, 0, 0, 1, x1 - overlap - sx0, 0)))
        return self.build(name, [first.path, moved])

    def specials(self) -> list[Glyph]:
        m, h = self.m, self.h
        o = self.o()
        ox0, _, ox1, _ = o.path.bounds
        oslash = self.build("oslash", [o.path, self.line((ox0 - 10, -60), (ox1 + 10, m.x + 60), ends=(False, False))])
        d = self.d()
        dx0, _, dx1, _ = d.path.bounds
        eth = self.build("eth", [d.path, self.line((dx0 + (dx1 - dx0) * 0.35, m.asc - 150), (dx1 + 10, m.asc - 70))])
        stem = self.line((h, m.asc - h * 0.3), (h, m.desc + h * 0.3))
        bowl = self.ring(540 / 2 + 20, m.x / 2, 540 / 2 - h - 20, m.x / 2 + m.over - self.thin)
        thorn = self.build("thorn", [stem, bowl])
        s_ = self.s()
        ss = self.joined("germandbls", s_, s_, -self.m.side * 0.4)
        return [self.joined("ae", self.a(), self.e(), self.h * 1.2), self.joined("oe", o, self.e(), self.h * 1.6),
                oslash, eth, thorn, ss]

    def marks(self) -> list[Glyph]:
        out = [self.period(), self.comma(), self.colon(), self.semicolon(), self.quotesingle("quoteright"),
               self.quotesingle("quotesingle"), self.quoteleft(), self.quotedbl("quotedblright"), self.quotedbl("quotedbl"),
               self.quotedblleft(), self.exclam(), self.question(), *self.parens(), *self.brackets(), self.slash(),
               self.slash("backslash", flip=True), self.plus(), self.equal(), self.underscore(), self.endash(),
               self.asterisk(), self.numbersign(), self.percent(), self.ampersand(), self.at(), *self.less_greater(),
               self.dollar(), self.sterling(), self.euro(), *self.braces(), self.bar(), *self.guillemets(),
               self.turned(self.exclam(), "exclamdown"), self.turned(self.question(), "questiondown"), self.degree(),
               self.middot(), self.ellipsis(), self.emdash(), self.multiply(), self.copyright(), *self.spacing_accents()]
        return out

    def everything(self) -> list[Glyph]:
        """Every glyph of the face: the letters, the figures, the marks, and the accented and joined letters."""
        from accents import ACCENTED, accented
        out = self.letters()
        out += accented({g.name: g for g in out}, self.accent, self.m.x, 115, ACCENTED)
        out += self.specials()
        return out

    def letters(self) -> list[Glyph]:
        out = []
        for ch in "abcdefghijklmnopqrstuvwxyz":
            fn = getattr(self, ch + "_", None) or getattr(self, ch)
            out.append(fn() if fn.__name__ != ch or ch not in "hmwx" else fn())
        out.append(self.space())
        out.append(self.hyphen())
        out += self.figures()
        out += self.marks()
        return out
