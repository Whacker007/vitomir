// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Engine.World;

/// <summary>
/// **Driving over a riser's gap** — ours, like driving itself.
/// The keyboard's step (<see cref="Move"/>) lands only on ground: the piece it stands on or
/// one linked to it. But a step in the shipped levels is not always two treads meeting at an edge.
/// Where the riser between them slopes rather than stands upright, or the lower tread's edge is set
/// back from the upper's, there is a strip of **no ground at all** between two linked pieces — the
/// riser is Walk-flagged but steeper than <see cref="MinFloorNormalZ"/> (the Silver Mines' rough
/// steps: normal Z 0.45 and 0.55, eight units deep, eighteen and twenty-one high), or there is
/// nothing there (a ten-unit step down in the same mines, six units across; a sixteen-unit tread in
/// the Old Town, six). The keyboard's step stopped at the strip's edge, every frame, for good
/// (in play, driving had numerous troubles over some short steps
/// and other areas, and click-to-move was needed to get past them).
///
/// A click walk has no such strip, because 1.1's walker never asks about the ground: its step goes
/// wherever the leg goes, and `SetPosition` keeps the surface it had when the
/// ray from 50 above finds nothing under the new point, the height taken off that
/// surface's plane however far outside it the point is (<see cref="Stride"/>). So a
/// route crosses the strip on the tread it came from and lands on the next when the ray finds it.
///
/// Driving now does the same over such a strip and nowhere else: a step that is over no ground
/// at all is taken, on the plane of the ground it came from, when **a piece of ground linked to
/// that one** lies straight ahead within <see cref="DriveGap"/> — where the next tread is. A step
/// over a drop to ground not linked to it (a ledge, the floor below a walkway) is refused as it
/// was, and so is a step into a wall (nothing ahead at all), because a click walk would not take
/// either: it follows the level's own links, which is all a neighbour here is.
///
/// The same play's other stop is here too: <see cref="Along"/>, the slide along a slanting edge.
/// </summary>
public sealed partial class WalkMap
{
    /// <summary>
    /// How far ahead the keyboard's step looks for the linked ground beyond a riser's gap: two cells
    /// of the levels' 8-unit grid. Ours. Of the 113,510 linked pairs of ground in the shipped
    /// levels (bridged risers included), 58,236 touch and 902 are 1 to 16 units apart — 170 of them
    /// 4, 93 of them 8, 241 of them 16; the rest are farther apart than any step, and a gap that
    /// wide is not something to drive over.
    /// </summary>
    public const float DriveGap = 16f;

    /// <summary>How finely that look ahead is taken (a riser is never thinner than this).</summary>
    const float GapProbe = 2f;

    /// <summary>
    /// The keyboard's step onto a riser's gap: <paramref name="to"/> on the plane of
    /// <paramref name="node"/> (the engine's rule for a walker over no ground), when the
    /// point is over no ground at all and linked ground lies straight on within
    /// <see cref="DriveGap"/>; null otherwise. See the class summary.
    /// </summary>
    Step? Across(int node, Vector3 from, Vector3 to, Func<Vector3, bool>? fits)
    {
        var flat = new Vector2(to.X - from.X, to.Y - from.Y);
        float length = flat.Length();
        if (length < 1e-4f) return null;
        var here = to with { Z = Nodes[node].Height(to.X, to.Y) };
        // Over ground of any sort — ground not linked to this, or a floor below — is not a gap.
        if (Find(here, Reach, RayAbove) >= 0) return null;
        var unit = flat / length;
        for (float ahead = GapProbe; ahead <= DriveGap; ahead += GapProbe)
        {
            if (Land(node, here + new Vector3(unit * ahead, 0f)) is not { } beyond) continue;
            // The same piece further on is a notch in its outline, not a tread beyond a riser.
            if (beyond.Node == node) return null;
            if (!(fits?.Invoke(here) ?? true)) return null;
            return new Step(node, here, false);
        }
        return null;
    }

    /// <summary>
    /// **The keyboard's slide along a slanting edge** (ours). The slide <see cref="Move"/> tries first
    /// is one axis at a time, which runs along a wall that lies along an axis and nowhere else: pressed
    /// along a ledge whose edge slants a few degrees off the axis, both axes leave the ground and the
    /// driver stops dead where a click walk runs along the same edge (the Silver Mines, the ledge
    /// above the lower gallery, seen in play: the route's first leg lies along it). So when
    /// both axes are refused, the move is tried along the edge it is leaving by — the part of it that
    /// runs along the edge nearest the walker's feet, a hair inside the ground.
    /// </summary>
    Step? Along(int node, Vector3 from, Vector3 to, Func<Vector3, bool>? fits)
    {
        var o = Nodes[node].Outline;
        var p = new Vector2(from.X, from.Y);
        var move = new Vector2(to.X - from.X, to.Y - from.Y);
        if (move.LengthSquared() < 1e-8f || o.Length < 3) return null;
        int best = -1;
        float nearest = EdgeReach;
        for (int i = 0; i < o.Length; i++)
        {
            var a = o[i]; var b = o[(i + 1) % o.Length];
            if (Vector2.DistanceSquared(a, b) < 1e-4f) continue;
            float d = Vector2.Distance(p, Nearest(a, b, p));
            if (d < nearest) { nearest = d; best = i; }
        }
        if (best < 0) return null;
        var edge = Vector2.Normalize(o[(best + 1) % o.Length] - o[best]);
        var slide = edge * Vector2.Dot(move, edge);
        if (slide.LengthSquared() < 1e-6f) return null;
        var across = new Vector2(-edge.Y, edge.X);
        var foot = Nearest(o[best], o[(best + 1) % o.Length], p);
        if (!Nodes[node].Contains(foot + across * 0.5f)) across = -across;   // inwards
        var at = p + slide + across * EdgeInset;
        if (Land(node, new Vector3(at, from.Z)) is not { } landed) return null;
        if (!(fits?.Invoke(landed.Position) ?? true)) return null;
        return new Step(landed.Node, landed.Position, true);
    }

    /// <summary>How near the feet an edge has to be to be the one slid along, and how far inside it the slide keeps.</summary>
    const float EdgeReach = 2f, EdgeInset = 0.05f;
}
