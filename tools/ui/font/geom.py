# Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
# Free for noncommercial use, never to be sold, never shipped with the original game's files.
"""
The shapes our letters are built from (ours): straight strokes, curved strokes of a width that follows a
pen's angle, rings, and the bracketed serifs of the Roman inscriptional capital. Every shape is a closed outline
(skia-pathops `Path`, font units, y up); a letter is their union. Nothing here is traced from any font: the
proportions are the classical capital's, written down as numbers in `faces.py`.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

from pathops import Path, PathOp, op

Pt = tuple[float, float]


def lerp(a: Pt, b: Pt, k: float) -> Pt:
    return (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k)


def polygon(points: list[Pt]) -> Path:
    p = Path()
    pen = p.getPen()
    pen.moveTo(points[0])
    for q in points[1:]:
        pen.lineTo(q)
    pen.closePath()
    return p


def union(paths: list[Path]) -> Path:
    out = Path()
    for p in paths:
        out = op(out, p, PathOp.UNION)
    return out


def minus(a: Path, b: Path) -> Path:
    return op(a, b, PathOp.DIFFERENCE)


def meet(a: Path, b: Path) -> Path:
    return op(a, b, PathOp.INTERSECTION)


def rect(x0: float, y0: float, x1: float, y1: float) -> Path:
    return polygon([(x0, y0), (x1, y0), (x1, y1), (x0, y1)])


@dataclass(frozen=True)
class Edge:
    """A straight edge of a stroke, as a line through two points; asked for its x at a height."""
    a: Pt
    b: Pt

    def x_at(self, y: float) -> float:
        (x0, y0), (x1, y1) = self.a, self.b
        if abs(y1 - y0) < 1e-9:
            return x0
        return x0 + (x1 - x0) * (y - y0) / (y1 - y0)


@dataclass(frozen=True)
class Stroke:
    """A straight stroke between two horizontal lines: its left and right edges, cut level at both ends."""
    left: Edge
    right: Edge
    y0: float
    y1: float

    def path(self) -> Path:
        return polygon([(self.left.x_at(self.y0), self.y0), (self.right.x_at(self.y0), self.y0),
                        (self.right.x_at(self.y1), self.y1), (self.left.x_at(self.y1), self.y1)])


def stroke(bottom: Pt, top: Pt, width: float) -> Stroke:
    """A stroke whose centre runs from `bottom` to `top`, `width` its true (perpendicular) width."""
    (x0, y0), (x1, y1) = bottom, top
    dx, dy = x1 - x0, y1 - y0
    cut = width * math.hypot(dx, dy) / abs(dy)          # its width along a horizontal line
    h = cut / 2
    return Stroke(Edge((x0 - h, y0), (x1 - h, y1)), Edge((x0 + h, y0), (x1 + h, y1)), min(y0, y1), max(y0, y1))


def stroke_edges(left_bottom: float, left_top: float, right_bottom: float, right_top: float, y0: float, y1: float) -> Stroke:
    """A stroke given by where its edges cross its two ends."""
    return Stroke(Edge((left_bottom, y0), (left_top, y1)), Edge((right_bottom, y0), (right_top, y1)), y0, y1)


def fillet(corner: Pt, along_slab: Pt, along_edge: Pt, pull: float) -> Path:
    """A bracket: the hollow-sided wedge in the corner between a serif's slab and a stroke's edge."""
    c1 = lerp(along_slab, corner, pull)
    c2 = lerp(along_edge, corner, pull)
    p = Path()
    pen = p.getPen()
    pen.moveTo(along_slab)
    pen.lineTo(corner)
    pen.lineTo(along_edge)
    pen.curveTo(c2, c1, along_slab)
    pen.closePath()
    return p


@dataclass(frozen=True)
class SerifStyle:
    reach: float        # how far the slab runs out beyond the stroke's edge
    slab: float         # the slab's thickness at its tip
    bracket: float      # how far up the stroke the bracket climbs
    pull: float = 0.72  # how hollow the bracket is (0 a straight chamfer, 1 a square corner)
    cup: float = 0.0    # how far the slab's foot is hollowed between its tips


def serif(s: Stroke, at_bottom: bool, style: SerifStyle, left: float = 1.0, right: float = 1.0) -> Path:
    """
    A serif at one end of a stroke: a slab along the end's line, bracketed into the stroke's edges. `left` and
    `right` scale the reach on each side (0 for none, as where a stroke meets another at an apex).
    """
    d = 1.0 if at_bottom else -1.0
    y = s.y0 if at_bottom else s.y1
    ys = y + d * style.slab
    yb = y + d * (style.slab + style.bracket)
    xl, xr = s.left.x_at(y), s.right.x_at(y)
    xls, xrs = s.left.x_at(ys), s.right.x_at(ys)
    rl, rr = style.reach * left, style.reach * right
    parts: list[Path] = []
    # The slab, its foot cupped a little between its tips.
    a, b = (xl - rl, y), (xr + rr, y)
    top_l, top_r = (xls - rl, ys), (xrs + rr, ys)
    slab = Path()
    pen = slab.getPen()
    pen.moveTo(a)
    if style.cup > 0:
        mid = ((a[0] + b[0]) / 2, y + d * style.cup)
        pen.curveTo(lerp(a, mid, 0.55), lerp(b, mid, 0.55), b)
    else:
        pen.lineTo(b)
    pen.lineTo(top_r)
    pen.lineTo(top_l)
    pen.closePath()
    parts.append(slab)
    if rl > 0:
        parts.append(fillet((xls, ys), (xls - rl, ys), (s.left.x_at(yb), yb), style.pull))
    if rr > 0:
        parts.append(fillet((xrs, ys), (xrs + rr, ys), (s.right.x_at(yb), yb), style.pull))
    return union(parts)


# ---- curves of a pen ------------------------------------------------------------------------------------------

def cubic(p0: Pt, p1: Pt, p2: Pt, p3: Pt, t: float) -> tuple[Pt, Pt]:
    """The point and the tangent of a cubic at t."""
    u = 1 - t
    x = u * u * u * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t * t * t * p3[0]
    y = u * u * u * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t * t * t * p3[1]
    dx = 3 * u * u * (p1[0] - p0[0]) + 6 * u * t * (p2[0] - p1[0]) + 3 * t * t * (p3[0] - p2[0])
    dy = 3 * u * u * (p1[1] - p0[1]) + 6 * u * t * (p2[1] - p1[1]) + 3 * t * t * (p3[1] - p2[1])
    return (x, y), (dx, dy)


@dataclass(frozen=True)
class Pen:
    """A broad pen: its width, its angle (degrees), and the least width it ever draws."""
    width: float
    angle: float
    least: float

    def across(self, tangent: Pt) -> float:
        phi = math.atan2(tangent[1], tangent[0])
        w = self.width * abs(math.sin(phi - math.radians(self.angle)))
        return max(self.least, w)


def spine(segments: list[tuple[Pt, Pt, Pt, Pt]], steps: int = 48) -> list[tuple[Pt, Pt]]:
    out: list[tuple[Pt, Pt]] = []
    for i, seg in enumerate(segments):
        for k in range(0 if i == 0 else 1, steps + 1):
            out.append(cubic(*seg, k / steps))
    return out


def pen_stroke(segments: list[tuple[Pt, Pt, Pt, Pt]], pen: Pen, closed: bool = False,
               width_at=None, steps: int = 48, smooth: int = 6) -> Path:
    """
    A curved stroke: the spine's cubics drawn with a broad pen held at one angle, as an outline (each side offset
    by half the pen's width across the spine there). `width_at(i, n, w)` may change the width at sample i of n.
    """
    pts = spine(segments, steps)
    if closed:
        pts = pts[:-1]
    n = len(pts)
    widths = [pen.across(tan) for _, tan in pts]
    if smooth > 0:                       # a running mean: a spine's joins change direction faster than a hand would
        k = smooth
        widths = [sum(widths[(j % n) if closed else min(max(j, 0), n - 1)] for j in range(i - k, i + k + 1)) / (2 * k + 1)
                  for i in range(n)]
    lefts: list[Pt] = []
    rights: list[Pt] = []
    for i, (p, tan) in enumerate(pts):
        w = widths[i]
        if width_at is not None:
            w = width_at(i, n, w)
        L = math.hypot(*tan) or 1.0
        nx, ny = -tan[1] / L, tan[0] / L
        lefts.append((p[0] + nx * w / 2, p[1] + ny * w / 2))
        rights.append((p[0] - nx * w / 2, p[1] - ny * w / 2))
    if closed:                                           # a ring: the larger side less the smaller
        outer, inner = (lefts, rights) if abs(area(lefts)) > abs(area(rights)) else (rights, lefts)
        return minus(op(polygon(outer), Path(), PathOp.UNION), op(polygon(inner), Path(), PathOp.UNION))
    outline = polygon(lefts + rights[::-1])
    return op(outline, Path(), PathOp.UNION)            # resolves any self-crossing of a tight curve


def area(pts: list[Pt]) -> float:
    s = 0.0
    for i in range(len(pts)):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % len(pts)]
        s += x0 * y1 - x1 * y0
    return s / 2


def ellipse_segments(cx: float, cy: float, rx: float, ry: float, start_deg: float = 0.0) -> list[tuple[Pt, Pt, Pt, Pt]]:
    """A whole ellipse as four cubics, anticlockwise, from `start_deg`."""
    k = 0.5522847498
    segs = []
    for q in range(4):
        a0 = math.radians(start_deg + 90 * q)
        a1 = math.radians(start_deg + 90 * (q + 1))
        p0 = (cx + rx * math.cos(a0), cy + ry * math.sin(a0))
        p3 = (cx + rx * math.cos(a1), cy + ry * math.sin(a1))
        t0 = (-rx * math.sin(a0), ry * math.cos(a0))
        t1 = (-rx * math.sin(a1), ry * math.cos(a1))
        segs.append((p0, (p0[0] + k * t0[0], p0[1] + k * t0[1]), (p3[0] - k * t1[0], p3[1] - k * t1[1]), p3))
    return segs


def arc_segments(cx: float, cy: float, rx: float, ry: float, from_deg: float, to_deg: float) -> list[tuple[Pt, Pt, Pt, Pt]]:
    """An elliptical arc, anticlockwise when to > from, as cubics of at most 90 degrees."""
    n = max(1, math.ceil(abs(to_deg - from_deg) / 90))
    step = (to_deg - from_deg) / n
    segs = []
    for i in range(n):
        a0 = math.radians(from_deg + step * i)
        a1 = math.radians(from_deg + step * (i + 1))
        k = 4 / 3 * math.tan((a1 - a0) / 4)
        p0 = (cx + rx * math.cos(a0), cy + ry * math.sin(a0))
        p3 = (cx + rx * math.cos(a1), cy + ry * math.sin(a1))
        t0 = (-rx * math.sin(a0), ry * math.cos(a0))
        t1 = (-rx * math.sin(a1), ry * math.cos(a1))
        segs.append((p0, (p0[0] + k * t0[0], p0[1] + k * t0[1]), (p3[0] - k * t1[0], p3[1] - k * t1[1]), p3))
    return segs


def through(points: list[Pt], start_dir: Pt | None = None, end_dir: Pt | None = None,
            tension: float = 0.5) -> list[tuple[Pt, Pt, Pt, Pt]]:
    """
    A smooth spine through points (a Catmull-Rom curve as cubics): each point's tangent from its neighbours,
    `start_dir` and `end_dir` (any length) setting the ends' directions.
    """
    n = len(points)
    tangents: list[Pt] = []
    for i in range(n):
        if i == 0:
            if start_dir is not None:
                L = math.hypot(*start_dir) or 1
                d = math.hypot(points[1][0] - points[0][0], points[1][1] - points[0][1])
                tangents.append((start_dir[0] / L * d, start_dir[1] / L * d))
            else:
                tangents.append((points[1][0] - points[0][0], points[1][1] - points[0][1]))
        elif i == n - 1:
            if end_dir is not None:
                L = math.hypot(*end_dir) or 1
                d = math.hypot(points[-1][0] - points[-2][0], points[-1][1] - points[-2][1])
                tangents.append((end_dir[0] / L * d, end_dir[1] / L * d))
            else:
                tangents.append((points[-1][0] - points[-2][0], points[-1][1] - points[-2][1]))
        else:
            tangents.append(((points[i + 1][0] - points[i - 1][0]) * tension, (points[i + 1][1] - points[i - 1][1]) * tension))
    segs = []
    for i in range(n - 1):
        p0, p3 = points[i], points[i + 1]
        t0, t1 = tangents[i], tangents[i + 1]
        segs.append((p0, (p0[0] + t0[0] / 3, p0[1] + t0[1] / 3), (p3[0] - t1[0] / 3, p3[1] - t1[1] / 3), p3))
    return segs


def line_meet(a0: Pt, a1: Pt, b0: Pt, b1: Pt) -> Pt:
    """Where the line through a0 and a1 crosses the line through b0 and b1."""
    x1, y1 = a0
    x2, y2 = a1
    x3, y3 = b0
    x4, y4 = b1
    d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4)
    px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / d
    py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / d
    return (px, py)


def bounds(p: Path) -> tuple[float, float, float, float]:
    return p.bounds
