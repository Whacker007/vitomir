# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
**Vitomir Capitals** (ours): the display face of Vitomir's own interface (the
design). Roman inscriptional capitals — classical proportions (the round
letters near a circle, the half-width letters near half of one), thick verticals, thin horizontals, diagonals thick
going down to the right, bracketed serifs — drawn from these numbers, not traced from any font or picture.

Each letter is a function of the face's measures (`Measures`), so small capitals and other weights are the same
letters drawn with other numbers. Units: 1000 to the em, y up, the baseline at 0.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, replace

from pathops import Path

from geom import (Pen, SerifStyle, Stroke, arc_segments, ellipse_segments, lerp, line_meet, meet, minus, pen_stroke,
                  polygon, rect, serif, stroke, stroke_edges, through, union)


@dataclass(frozen=True)
class Measures:
    cap: float = 680          # the capitals' height
    thick: float = 78         # a vertical stem
    thin: float = 30          # a horizontal, and a thin diagonal
    over: float = 12          # how far a round letter passes the baseline and the cap height
    point: float = 18         # how far a pointed apex or vertex passes them
    reach: float = 40         # a serif's reach beyond a thick stem
    slab: float = 10
    bracket: float = 66
    pull: float = 0.80
    cup: float = 3.0
    side: float = 46          # the side bearing beside a straight stem
    round_side: float = 30    # beside a round side
    open_side: float = 14     # beside a diagonal or an open side
    stress: float = 20        # the pen's angle in the round letters, degrees: thin at upper left, lower right

    def scaled(self, k: float, weight: float = 1.0) -> "Measures":
        """The same face at another size (small capitals): the strokes keep a little more than the scale."""
        return replace(self, cap=self.cap * k, thick=self.thick * k * weight, thin=self.thin * k * weight,
                       over=self.over * k, point=self.point * k, reach=self.reach * k, slab=self.slab * k,
                       bracket=self.bracket * k, side=self.side * k, round_side=self.round_side * k,
                       open_side=self.open_side * k)


@dataclass
class Glyph:
    name: str
    path: Path
    advance: float


class Capitals:
    def __init__(self, m: Measures = Measures()):
        self.m = m
        self.thick_serif = SerifStyle(m.reach, m.slab, m.bracket, m.pull, m.cup)
        self.thin_serif = SerifStyle(m.reach * 0.8, m.slab * 0.9, m.bracket * 0.75, m.pull, m.cup * 0.6)
        self.pen = Pen(math.hypot(m.thick, m.thin) * 1.02, m.stress, m.thin * 0.95)

    # ---- pieces -------------------------------------------------------------------------------------------------

    def stem(self, x: float, w: float | None = None, y0: float = 0.0, y1: float | None = None) -> Stroke:
        w = self.m.thick if w is None else w
        y1 = self.m.cap if y1 is None else y1
        return stroke_edges(x, x, x + w, x + w, y0, y1)

    def serifs(self, s: Stroke, bottom=(1.0, 1.0), top=(1.0, 1.0), thin: bool = False) -> list[Path]:
        style = self.thin_serif if thin else self.thick_serif
        out = []
        if bottom is not None and (bottom[0] or bottom[1]):
            out.append(serif(s, True, style, *bottom))
        if top is not None and (top[0] or top[1]):
            out.append(serif(s, False, style, *top))
        return out

    def arm(self, x0: float, x1: float, y: float, t: float | None = None) -> Path:
        """A horizontal arm whose centre is at y."""
        t = self.m.thin * 1.1 if t is None else t
        return rect(x0, y - t / 2, x1, y + t / 2)

    def beak(self, x: float, y_edge: float, down: bool, length: float, base: float, flare: float = 4.0) -> Path:
        """The serif at an arm's end: a wedge along the end, its inner side hollow, running `length` from the arm."""
        d = -1.0 if down else 1.0
        tip = (x, y_edge + d * length)
        foot = (x - base, y_edge)
        corner = (x - base * 0.08, y_edge + d * length * 0.15)
        p = Path()
        pen = p.getPen()
        pen.moveTo((x - base * 1.2, y_edge - d * 2))
        pen.lineTo((x + flare * 0.3, y_edge - d * (self.m.thin * 0.5)))
        pen.lineTo((x + flare, tip[1]))
        pen.lineTo((x - 6, tip[1] - d * 1.5))
        pen.curveTo(lerp(tip, corner, 0.9), lerp(foot, corner, 0.75), foot)
        pen.closePath()
        return p

    def inside(self, a: tuple, apex: tuple, b: tuple) -> Path:
        """The region between two lines through an apex, far enough to hold a letter: clips a point."""
        def far(p, q, k=4.0):
            return (q[0] + (p[0] - q[0]) * k, q[1] + (p[1] - q[1]) * k)
        return polygon([far(a, apex), apex, far(b, apex)])

    def glyph(self, name: str, parts: list[Path], ink_left: float, ink_right: float, lsb: float, rsb: float) -> Glyph:
        """A letter from its parts, moved so its left side bearing is `lsb` and given its advance."""
        path = union(parts)
        dx = lsb - ink_left
        moved = Path()
        pen = moved.getPen()
        from fontTools.pens.transformPen import TransformPen
        path.draw(TransformPen(pen, (1, 0, 0, 1, dx, 0)))
        return Glyph(name, moved, lsb + (ink_right - ink_left) + rsb)

    def ink(self, parts: list[Path]) -> tuple[float, float]:
        u = union(parts)
        x0, _, x1, _ = u.bounds
        return x0, x1

    def build(self, name: str, parts: list[Path], lsb: float, rsb: float) -> Glyph:
        x0, x1 = self.ink(parts)
        return self.glyph(name, parts, x0, x1, lsb, rsb)

    # ---- the round letters --------------------------------------------------------------------------------------

    def ring(self, cx: float, width: float, height: float | None = None, cy: float | None = None) -> Path:
        """An O's ring: outer `width` wide, the cap height high with its overshoot, drawn by the pen."""
        m = self.m
        height = m.cap + 2 * m.over if height is None else height
        cy = m.cap / 2 if cy is None else cy
        rx = (width - self.pen.width * math.cos(math.radians(m.stress))) / 2
        ry = (height - m.thin) / 2
        return pen_stroke(ellipse_segments(cx, cy, rx, ry), self.pen, closed=True, steps=64)

    def bowl(self, x_stem: float, y0: float, y1: float, reach: float, thin_join: float | None = None) -> Path:
        """
        A bowl on a stem's right (B, D, P, R): from the stem at y1 out to `reach` and back to the stem at y0, its
        horizontals thin and its outer side thick; drawn as half an ellipse with straight runs to the stem.
        """
        m = self.m
        t = m.thin if thin_join is None else thin_join
        cy = (y0 + y1) / 2
        ry = (y1 - y0) / 2 - t / 2
        rx_outer = reach - x_stem
        r = min(rx_outer * 0.6, ry * 1.05)              # how far the round part runs back from the outer side
        cx = reach - r
        segs = arc_segments(cx, cy, r - m.thick * 0.5, ry, 90, -90)
        start = (x_stem + m.thick * 0.5, y1 - t / 2)
        end = (x_stem + m.thick * 0.5, y0 + t / 2)
        run_top = (start, lerp(start, segs[0][0], 1 / 3), lerp(start, segs[0][0], 2 / 3), segs[0][0])
        run_bottom = (segs[-1][3], lerp(segs[-1][3], end, 1 / 3), lerp(segs[-1][3], end, 2 / 3), end)
        pen = Pen(m.thick * 1.04, 0.0, t)
        return pen_stroke([run_top, *segs, run_bottom], pen, steps=40)

    # ---- the letters --------------------------------------------------------------------------------------------

    def H(self):
        m = self.m
        w = m.cap * 0.80
        a, b = self.stem(0), self.stem(w - m.thick)
        parts = [a.path(), b.path(), *self.serifs(a), *self.serifs(b), self.arm(m.thick, w - m.thick, m.cap * 0.5, m.thin)]
        return self.build("H", parts, m.side - m.reach, m.side - m.reach)

    def I(self):
        m = self.m
        a = self.stem(0)
        return self.build("I", [a.path(), *self.serifs(a)], m.side - m.reach, m.side - m.reach)

    def E(self, name="E", bottom=True, mid=0.505, top_len=0.47, mid_len=0.42, bot_len=0.50):
        m = self.m
        a = self.stem(0)
        t = m.thin * 1.12
        x = m.thick
        parts = [a.path()]
        parts += self.serifs(a, bottom=(1, 0) if bottom else (1, 1), top=(1, 0))
        top_end = m.cap * top_len
        parts += [rect(0, m.cap - t, top_end, m.cap), self.beak(top_end, m.cap - t, True, m.cap * 0.14, m.cap * 0.14)]
        mid_end = m.cap * mid_len
        y = m.cap * mid
        parts += [self.arm(x - 1, mid_end, y, t * 0.95),
                  self.beak(mid_end, y + t * 0.47, False, m.cap * 0.045, m.cap * 0.06, 2),
                  self.beak(mid_end, y - t * 0.47, True, m.cap * 0.045, m.cap * 0.06, 2)]
        right = max(top_end, mid_end)
        if bottom:
            bot_end = m.cap * bot_len
            parts += [rect(0, 0, bot_end, t), self.beak(bot_end, t, False, m.cap * 0.15, m.cap * 0.15)]
            right = max(right, bot_end)
        return self.build(name, parts, m.side - m.reach, m.open_side)

    def F(self):
        return self.E("F", bottom=False, mid=0.48, mid_len=0.40)

    def L(self):
        m = self.m
        a = self.stem(0)
        t = m.thin * 1.12
        end = m.cap * 0.49
        parts = [a.path(), *self.serifs(a, bottom=(1, 0), top=(1, 1)),
                 rect(0, 0, end, t), self.beak(end, t, False, m.cap * 0.15, m.cap * 0.15)]
        return self.build("L", parts, m.side - m.reach, m.open_side)

    def T(self):
        m = self.m
        w = m.cap * 0.76
        t = m.thin * 1.12
        a = self.stem(w / 2 - m.thick / 2)
        parts = [a.path(), *self.serifs(a, top=None), rect(0, m.cap - t, w, m.cap),
                 self.beak(w, m.cap - t, True, m.cap * 0.15, m.cap * 0.13),
                 self.mirror_x(self.beak(w, m.cap - t, True, m.cap * 0.15, m.cap * 0.13), w)]
        return self.build("T", parts, m.open_side, m.open_side)

    def mirror_x(self, p: Path, w: float) -> Path:
        from fontTools.pens.transformPen import TransformPen
        from fontTools.pens.reverseContourPen import ReverseContourPen
        out = Path()
        p.draw(ReverseContourPen(TransformPen(out.getPen(), (-1, 0, 0, 1, w, 0))))
        return out

    def O(self):
        m = self.m
        w = m.cap * 0.98
        return self.build("O", [self.ring(w / 2, w)], m.round_side, m.round_side)

    def Q(self):
        m = self.m
        w = m.cap * 0.98
        ring = self.ring(w / 2, w)
        # The tail: from the bowl's foot, a long thinning sweep out under the baseline to the right.
        pts = [(w * 0.34, m.thin * 1.0), (w * 0.60, -m.cap * 0.04), (w * 0.92, -m.cap * 0.13), (w * 1.18, -m.cap * 0.19)]
        tail = pen_stroke(through(pts, start_dir=(1, -0.35), tension=0.5), Pen(m.thick, 90, 4), steps=40,
                          width_at=lambda i, n, w_: 3.0 + m.thick * 0.95 * (1 - (i / n) ** 1.25))
        g = self.build("Q", [ring, tail], m.round_side, m.round_side)
        g.advance = m.round_side * 2 + w
        return g

    def c_parts(self, a_top: float, a_bot: float, lower_beak: bool) -> tuple[list[Path], Path, float]:
        """C's ring opened between two angles, its ends cut upright, with their beaks; and where the lower end is."""
        m = self.m
        width = m.cap * 0.90
        cx, cy = width / 2, m.cap / 2
        ring = self.ring(cx, width, cy=cy)
        far = width * 3
        ct, st = math.cos(math.radians(a_top)), math.sin(math.radians(a_top))
        cb, sb = math.cos(math.radians(a_bot)), math.sin(math.radians(a_bot))
        wedge = polygon([(cx, cy), (cx + far * cb, cy + far * sb), (cx + far, cy), (cx + far * ct, cy + far * st)])
        shape = minus(ring, wedge)
        xt = self.inner_x(ring, cx, cy, a_top)
        xb = self.inner_x(ring, cx, cy, a_bot)
        shape = minus(shape, rect(xt, cy, far, far))
        shape = minus(shape, rect(xb, -far, far, cy))
        top = self.face(shape, xt, upper=True)
        parts = [shape, self.beak_at(shape, xt, top[0], -1, m.cap * 0.13, m.cap * 0.13, 1)]
        if lower_beak:
            bottom = self.face(shape, xb, upper=False)
            parts.append(self.beak_at(shape, xb, bottom[1], 1, m.cap * 0.06, m.cap * 0.09, 1))
        return parts, shape, xb

    @staticmethod
    def inner_x(ring: Path, cx: float, cy: float, angle: float) -> float:
        """Where a ray from the centre at `angle` first meets the ring: its x."""
        a = math.radians(angle)
        r = 0.0
        while r < 2000 and not ring.contains((cx + r * math.cos(a), cy + r * math.sin(a))):
            r += 0.5
        return cx + r * math.cos(a)

    def C(self):
        m = self.m
        parts, _, _ = self.c_parts(50, -42, True)
        return self.build("C", parts, m.round_side, m.open_side + 14)

    def G(self):
        m = self.m
        parts, shape, xb = self.c_parts(50, -26, False)
        # The spur: an upright on the lower end, rising to a flat top a little below the middle, a small slab on it.
        y_top = m.cap * 0.46
        w = m.thick * 0.92
        parts += [rect(xb - w, m.cap * 0.10, xb, y_top),
                  rect(xb - w - m.reach * 0.35, y_top - m.slab * 1.1, xb + m.reach * 0.30, y_top)]
        return self.build("G", parts, m.round_side, m.open_side + 14)

    def face(self, shape: Path, x: float, upper: bool) -> tuple[float, float]:
        """The low and high y of the flat end a cut at x left on a stroke, above or below the middle."""
        m = self.m
        band = meet(shape, rect(x - 1.5, m.cap / 2 if upper else -m.cap, x + 1.5, m.cap * 2 if upper else m.cap / 2))
        _, y0, _, y1 = band.bounds
        return y0, y1

    def edge_near(self, shape: Path, x: float, y: float, d: int) -> float:
        """The stroke's edge at x nearest to y on the side d points to (d = -1: the lower edge, +1: the upper)."""
        band = meet(shape, rect(x - 1.0, -self.m.cap, x + 1.0, self.m.cap * 2))
        best = None
        for contour in band.contours:
            ys = [pt[1] for seg in contour for pt in seg[1]]
            lo, hi = min(ys), max(ys)
            if best is None or abs((lo + hi) / 2 - y) < abs((best[0] + best[1]) / 2 - y):
                best = (lo, hi)
        if best is None:
            return y
        return best[0] if d < 0 else best[1]

    def beak_at(self, shape: Path, x: float, y0: float, d: int, length: float, base: float, side: int) -> Path:
        """
        A beak serif on a flat end at x: from the end's edge at y0 it runs `length` up (d = 1) or down (d = -1),
        straight along the end, its other side hollow, meeting the stroke's edge `base` back from the end.
        """
        xr = x - side * base
        yr = self.edge_near(shape, xr, y0, d)
        tip = (x + side * 2.0, y0 + d * length)
        p = Path()
        pen = p.getPen()
        pen.moveTo((xr, yr - d * 12))
        pen.lineTo((x, y0 - d * 12))
        pen.lineTo((x + side * 1.0, y0))
        pen.lineTo(tip)
        pen.lineTo((x - side * 5.0, tip[1] - d * 0.5))
        corner = (x, yr)
        pen.curveTo(lerp(tip, corner, 0.28), lerp((xr, yr), corner, 0.28), (xr, yr))
        pen.closePath()
        return p

    def D(self):
        m = self.m
        a = self.stem(0)
        w = m.cap * 0.88
        parts = [a.path(), *self.serifs(a, bottom=(1, 0), top=(1, 0)),
                 self.bowl(0, -m.over * 0.0, m.cap + 0.0, w, m.thin)]
        return self.build("D", parts, m.side - m.reach, m.round_side)

    def B(self):
        m = self.m
        a = self.stem(0)
        y_mid = m.cap * 0.53
        parts = [a.path(), *self.serifs(a, bottom=(1, 0), top=(1, 0)),
                 self.bowl(0, y_mid - m.thin / 2, m.cap, m.cap * 0.50, m.thin),
                 self.bowl(0, 0, y_mid + m.thin / 2, m.cap * 0.58, m.thin)]
        return self.build("B", parts, m.side - m.reach, m.round_side)

    def P(self):
        m = self.m
        a = self.stem(0)
        y_mid = m.cap * 0.44
        bowl = self.bowl(0, y_mid, m.cap, m.cap * 0.57, m.thin)
        # 1.1's capitals and Rome's both leave P's bowl a little open where it comes back to the stem.
        gap = rect(m.thick, y_mid - 2, m.thick + m.cap * 0.07, y_mid + m.thin + 2)
        parts = [a.path(), *self.serifs(a, bottom=(1, 1), top=(1, 0)), minus(bowl, gap)]
        return self.build("P", parts, m.side - m.reach, m.round_side)

    def R(self):
        m = self.m
        a = self.stem(0)
        y_mid = m.cap * 0.47
        parts = [a.path(), *self.serifs(a, bottom=(1, 1), top=(1, 0)),
                 self.bowl(0, y_mid - m.thin / 2, m.cap, m.cap * 0.56, m.thin)]
        # The leg: from the bowl's join out to the right, thick, ending in a lifted foot past the baseline.
        x_join = m.cap * 0.27
        leg = stroke((m.cap * 0.62, 0), (x_join, y_mid), m.thick * 0.98)
        parts.append(minus(leg.path(), rect(-1, y_mid + 1, m.cap, m.cap * 2)))
        foot = Path()
        pen = foot.getPen()
        xr = leg.right.x_at(0)
        pen.moveTo((leg.left.x_at(m.cap * 0.12), m.cap * 0.12))
        pen.curveTo((leg.left.x_at(m.cap * 0.04) + 6, 0), (xr - 4, -2), (xr + m.reach * 1.1, -3))
        pen.lineTo((xr + m.reach * 1.1, 4))
        pen.curveTo((xr + 2, 6), (xr - 2, m.cap * 0.06), (leg.right.x_at(m.cap * 0.14), m.cap * 0.14))
        pen.closePath()
        parts.append(foot)
        return self.build("R", parts, m.side - m.reach, m.open_side)

    def S(self):
        m = self.m
        w = m.cap * 0.58
        h = m.cap
        o = m.over
        t = m.thin
        # Two bowls, the lower a little larger, joined by the spine: the upper's arc from its end at the upper right
        # round the top and down the left, the spine's curve down to the right, the lower's arc round the right,
        # the foot and up to its end at the lower left.
        uc, urx, ury = (w * 0.52, h * 0.745), w * 0.40, h * 0.245 - t / 2 + o / 2
        lc, lrx, lry = (w * 0.48, h * 0.262), w * 0.44, h * 0.262 - t / 2 + o / 2
        upper = arc_segments(uc[0], uc[1], urx, ury, 24, 212)
        lower = arc_segments(lc[0], lc[1], lrx, lry, 38, -158)
        a, b = upper[-1][3], lower[0][0]
        ta = (upper[-1][3][0] - upper[-1][2][0], upper[-1][3][1] - upper[-1][2][1])
        tb = (lower[0][1][0] - lower[0][0][0], lower[0][1][1] - lower[0][0][1])
        d = math.hypot(b[0] - a[0], b[1] - a[1]) * 0.36
        la, lb = math.hypot(*ta) or 1, math.hypot(*tb) or 1
        spine_mid = (a, (a[0] + ta[0] / la * d, a[1] + ta[1] / la * d), (b[0] - tb[0] / lb * d, b[1] - tb[1] / lb * d), b)
        segs_s = [*upper, spine_mid, *lower]

        def ends(i, n, w_):                                # the strokes thin into both terminals
            k = i / n
            if k > 0.86:
                return w_ + (m.thin * 1.5 - w_) * min(1.0, (k - 0.86) / 0.12)
            if k < 0.06:
                return max(w_, m.thin * 1.5)
            return w_
        body = pen_stroke(segs_s, Pen(math.hypot(m.thick, m.thin) * 1.02, 26, m.thin * 0.95), steps=32, width_at=ends)
        # The terminals: each end cut upright a little way in, a short spur on the cut, so they do not read as a
        # stroke through the letter.
        x_top, x_bot = w * 0.95, w * 0.02
        body = minus(body, rect(x_top, h * 0.55, w * 2, h * 2))
        body = minus(body, rect(-w, -h, x_bot, h * 0.45))
        top = self.face(body, x_top, upper=True)
        bottom = self.face(body, x_bot, upper=False)
        parts = [body, self.beak_at(body, x_top, top[0], -1, h * 0.035, h * 0.06, 1),
                 self.beak_at(body, x_bot, bottom[1], 1, h * 0.03, h * 0.05, -1)]
        return self.build("S", parts, m.round_side, m.round_side)

    def vbeak(self, x: float, y: float, down: bool, length: float, left: bool = False) -> Path:
        m = self.m
        d = -1 if down else 1
        w = m.thin * 1.2
        s = -1 if left else 1
        p = Path()
        pen = p.getPen()
        pen.moveTo((x + s * 3, y - d * 18))
        pen.lineTo((x + s * 4, y + d * length))
        pen.curveTo((x - s * w * 0.2, y + d * length), (x - s * w, y + d * length * 0.3), (x - s * w * 1.8, y - d * 8))
        pen.closePath()
        return p

    def A(self):
        m = self.m
        w = m.cap * 0.84
        apex = (w / 2, m.cap + m.point)
        thick_cut = m.thick * 1.12
        thin_cut = m.thin * 1.15
        # The outer edges run from the feet to the apex; each stroke's inner edge is its outer one moved in.
        left = stroke_edges(0, apex[0], thin_cut, apex[0] + thin_cut, 0, apex[1])
        right = stroke_edges(w - thick_cut, apex[0] - thick_cut, w, apex[0], 0, apex[1])
        body = meet(union([left.path(), right.path()]), self.inside((0, 0), apex, (w, 0)))
        flat = rect(-w, m.cap + m.point - 6, 2 * w, 2 * m.cap)
        body = minus(body, flat)
        y_bar = m.cap * 0.30
        bar = rect(left.left.x_at(y_bar), y_bar - m.thin / 2, right.right.x_at(y_bar), y_bar + m.thin / 2)
        bar = meet(bar, self.inside((0, 0), apex, (w, 0)))
        parts = [body, bar, *self.serifs(left, top=None, thin=True), *self.serifs(right, top=None)]
        return self.build("A", parts, m.open_side, m.open_side)

    def V(self):
        m = self.m
        w = m.cap * 0.84
        vertex = (w / 2, -m.point)
        thick_cut = m.thick * 1.12
        thin_cut = m.thin * 1.15
        left = stroke_edges(vertex[0], 0, vertex[0] + thick_cut, thick_cut, vertex[1], m.cap)
        right = stroke_edges(vertex[0] - thin_cut, w - thin_cut, vertex[0], w, vertex[1], m.cap)
        body = meet(union([left.path(), right.path()]), self.inside((0, m.cap), vertex, (w, m.cap)))
        body = minus(body, rect(-w, -2 * m.cap, 2 * w, -m.point + 6))
        parts = [body, *self.serifs(left, bottom=None), *self.serifs(right, bottom=None, thin=True)]
        return self.build("V", parts, m.open_side, m.open_side)

    def W(self):
        m = self.m
        v = self.V()
        x0, _, x1, _ = v.path.bounds
        from fontTools.pens.transformPen import TransformPen
        second = Path()
        v.path.draw(TransformPen(second.getPen(), (1, 0, 0, 1, (x1 - x0) * 0.62, 0)))
        return self.build("W", [v.path, second], m.open_side, m.open_side)

    def M(self):
        m = self.m
        W = m.cap * 1.04
        splay = m.cap * 0.075
        tc = m.thin * 1.1                    # a thin stroke's width along a horizontal
        Tc = m.thick * 1.12                  # a thick diagonal's
        Ht = m.cap + m.point * 0.35
        A1 = (splay, Ht)
        A2 = (W - splay, Ht)
        V = (W / 2 + tc * 0.25, -m.point)
        # The edges, each as two points on its line.
        L1 = ((0, 0), A1)                                       # the left stroke's outer edge
        L1r = ((tc, 0), (A1[0] + tc, Ht))                       # its inner edge
        D1r = (A1, (V[0] + Tc, V[1]))                           # the thick diagonal's right edge
        D1l = ((A1[0] - Tc, Ht), V)                             # its left edge
        D2r = (V, (A2[0] + tc, Ht))                             # the thin diagonal's right edge
        D2l = ((V[0] - tc, V[1]), A2)                           # its left edge
        Rr = (A2, (W, 0))                                       # the right stroke's outer edge
        Rl = ((A2[0] - Tc, Ht), (W - Tc, 0))                    # its inner edge
        U = line_meet(*D1r, *D2l)
        K1 = line_meet(*D1l, *L1r)
        K2 = line_meet(*D2r, *Rl)
        outline = polygon([(0, 0), A1, U, A2, (W, 0), (W - Tc, 0), K2, V, K1, (tc, 0)])
        outline = minus(outline, rect(-W, Ht - 5, 2 * W, 2 * m.cap))
        outline = minus(outline, rect(-W, -2 * m.cap, 2 * W, V[1] + 5))
        left = stroke_edges(0, A1[0], tc, A1[0] + tc, 0, Ht)
        right = stroke_edges(W - Tc, A2[0] - Tc, W, A2[0], 0, Ht)
        parts = [outline, *self.serifs(left, top=None, thin=True), *self.serifs(right, top=None)]
        return self.build("M", parts, m.open_side + 8, m.open_side + 8)

    def N(self):
        m = self.m
        w = m.cap * 0.82
        thin_cut = m.thin * 1.1
        thick_cut = m.thick * 1.12
        a = self.stem(0, thin_cut, 0, m.cap)
        b = self.stem(w - thin_cut, thin_cut, -m.point, m.cap)
        diag = stroke_edges(w - thick_cut, 0, w, thick_cut, -m.point, m.cap)
        body = union([a.path(), b.path(), diag.path()])
        body = minus(body, polygon([(w - thin_cut, -m.point - 1), (w + 50, -m.point - 1), (w + 50, m.cap * 0.25)]))
        body = minus(body, rect(-w, -2 * m.cap, 2 * w, -m.point + 5))
        parts = [body, *self.serifs(a, top=(1, 0), thin=True), *self.serifs(b, bottom=None, thin=True)]
        return self.build("N", parts, m.side - m.reach, m.side - m.reach)

    def K(self):
        m = self.m
        a = self.stem(0)
        x_join = m.thick
        y_join = m.cap * 0.40
        arm = stroke((x_join - m.thin * 0.8, y_join - m.cap * 0.035), (m.cap * 0.62, m.cap), m.thin * 1.1)
        leg = stroke((m.cap * 0.68, 0), (x_join + m.thick * 0.35, y_join + m.cap * 0.12), m.thick * 0.98)
        parts = [a.path(), *self.serifs(a), arm.path(), *self.serifs(arm, bottom=None, thin=True),
                 minus(leg.path(), rect(-1, m.cap * 0.62, m.cap, m.cap * 2)), *self.serifs(leg, top=None, bottom=(0.6, 1))]
        return self.build("K", parts, m.side - m.reach, m.open_side)

    def X(self):
        m = self.m
        w = m.cap * 0.76
        thick = stroke((w - m.thick * 0.6, 0), (m.thick * 0.6, m.cap), m.thick)
        thin = stroke((m.thin * 0.6, 0), (w - m.thin * 0.6, m.cap), m.thin * 1.1)
        parts = [thick.path(), thin.path(), *self.serifs(thick), *self.serifs(thin, thin=True)]
        return self.build("X", parts, m.open_side, m.open_side)

    def Y(self):
        m = self.m
        w = m.cap * 0.78
        y_join = m.cap * 0.44
        cx = w / 2
        st = self.stem(cx - m.thick / 2, m.thick, 0, y_join + 2)
        left = stroke((cx, y_join), (m.thick * 0.55, m.cap), m.thick * 0.98)
        right = stroke((cx + m.thin * 0.2, y_join), (w - m.thin * 0.6, m.cap), m.thin * 1.1)
        parts = [st.path(), *self.serifs(st, top=None), left.path(), *self.serifs(left, bottom=None),
                 right.path(), *self.serifs(right, bottom=None, thin=True)]
        return self.build("Y", parts, m.open_side, m.open_side)

    def Z(self):
        m = self.m
        w = m.cap * 0.68
        t = m.thin * 1.12
        diag = stroke((m.thick * 0.5, t), (w - m.thick * 0.5, m.cap - t), m.thick * 0.95)
        parts = [rect(m.cap * 0.02, m.cap - t, w, m.cap), rect(0, 0, w + m.cap * 0.02, t), diag.path(),
                 self.mirror_x(self.beak(w - m.cap * 0.02, m.cap - t, True, m.cap * 0.10, m.cap * 0.10), w),
                 self.beak(w + m.cap * 0.02, t, False, m.cap * 0.12, m.cap * 0.12)]
        return self.build("Z", parts, m.open_side + 6, m.open_side)

    def U(self):
        m = self.m
        w = m.cap * 0.80
        thin_cut = m.thin * 1.1
        y_turn = m.cap * 0.32
        a = self.stem(0, m.thick, y_turn, m.cap)
        b = self.stem(w - thin_cut, thin_cut, 0, m.cap)
        rx = (w - thin_cut * 0.5 - m.thick * 0.5) / 2
        cx = m.thick / 2 + rx
        segs = arc_segments(cx, y_turn, rx, y_turn + m.over - m.thin / 2, 180, 360)
        # Thick down the left and round the foot, thinning to the thin stem's width as it rises into it.
        def taper(i, n, w_):
            k = i / n
            return w_ if k < 0.55 else w_ + (thin_cut - w_) * min(1.0, (k - 0.55) / 0.35)
        bowl = pen_stroke(segs, Pen(math.hypot(m.thick, m.thin), m.stress, m.thin), steps=40, width_at=taper)
        parts = [a.path(), b.path(), bowl, *self.serifs(a, bottom=None), *self.serifs(b, bottom=None, thin=True),
                 *self.serifs(b, top=None, bottom=(0, 0.8), thin=True)]
        return self.build("U", parts, m.side - m.reach, m.side - m.reach)

    def J(self):
        m = self.m
        a = self.stem(0, m.thick, m.cap * 0.05, m.cap)
        drop = m.cap * 0.20
        segs = [((m.thick / 2, m.cap * 0.05), (m.thick / 2, -drop * 0.6), (-m.cap * 0.06, -drop), (-m.cap * 0.20, -drop * 0.9))]
        tail = pen_stroke(segs, Pen(m.thick * 1.05, 10, 8), steps=40,
                          width_at=lambda i, n, w_: max(6.0, w_ * (1 - 0.8 * (i / n) ** 2)))
        parts = [a.path(), tail, *self.serifs(a, bottom=None)]
        return self.build("J", parts, m.open_side, m.side - m.reach)

    def ankh(self, name: str = "ankh", bar_y: float | None = None, top: float | None = None, width: float | None = None):
        """
        The ankh (U+2625): a stem flaring to its foot, a crossbar with flared ends, a loop above drawn by the pen.
        `bar_y` the crossbar's middle and `top` the loop's top; by default a symbol of the cap height's size, the loop
        reaching it. For the wordmark it stands as a T, its bar at the cap height and its loop above.
        """
        m = self.m
        bar_y = m.cap * 0.56 if bar_y is None else bar_y
        top = m.cap + m.over if top is None else top
        width = m.cap * 0.62 if width is None else width
        cx = width / 2
        t = m.thin * 1.35
        # The stem: thick, a little wider at its foot, with a foot serif.
        stem = stroke_edges(cx - m.thick * 0.62, cx - m.thick * 0.46, cx + m.thick * 0.62, cx + m.thick * 0.46, 0, bar_y)
        # The crossbar: thin in the middle, flaring to square ends.
        flare = m.thin * 0.9
        bar = polygon([(0, bar_y - t / 2 - flare), (width * 0.16, bar_y - t / 2), (width * 0.84, bar_y - t / 2),
                       (width, bar_y - t / 2 - flare), (width, bar_y + t / 2 + flare), (width * 0.84, bar_y + t / 2),
                       (width * 0.16, bar_y + t / 2), (0, bar_y + t / 2 + flare)])
        # The loop: an oval narrower than the bar, standing on it, drawn as a ring with the round letters' pen.
        loop_h = top - bar_y
        loop_w = width * 0.66
        pen = Pen(m.thick * 0.80, 0.0, m.thin * 1.15)               # an upright pen: the loop's sides heavier than its top
        rx = (loop_w - pen.width) / 2
        ry = (loop_h - m.thin * 1.15) / 2 + m.thin * 0.25
        ring = pen_stroke(ellipse_segments(cx, bar_y + loop_h / 2 + m.thin * 0.1, rx, ry), pen, closed=True, steps=64)
        parts = [stem.path(), *self.serifs(stem, top=None), bar, ring]
        return self.build(name, parts, m.open_side, m.open_side)

    def space(self):
        return Glyph("space", Path(), self.m.cap * 0.32)

    def letters(self) -> list[Glyph]:
        names = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
        out = []
        for n in names:
            out.append(getattr(self, n)())
        out.append(self.ankh())
        out.append(self.space())
        return out
