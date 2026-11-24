// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// What the probe read ahead of a body (<see cref="TraversalProbe.Read"/>): the obstacle's face and the
/// edge at its top, how high and how deep the top is, whether it can be stood on and held, and the ground
/// beyond it. Heights are the world's (metres, Z up); distances are across, along the way probed.
/// </summary>
public struct Obstacle
{
    /// <summary>Something above the stride stands within reach along the way.</summary>
    public bool Found;
    /// <summary>A top was found within the height looked at (false: a wall taller than that, a slope, or a lip not to be climbed, <see cref="NoClimb"/>).</summary>
    public bool HasTop;
    /// <summary>
    /// The top was found but the level says it is not to be climbed (ours): its top or its face is a
    /// <see cref="Formats.L2.L2Surface.NoClimb"/> triangle or the lip is inside an area marked <c>"climb": "none"</c>
    /// (<see cref="Space.LevelClimb"/>). The probe then reads it as a face with no top (<see cref="HasTop"/> false), so
    /// no grab, mantle, climb onto, hop up, vault or hurdle starts from it; <see cref="Lip"/> and <see cref="Top"/>
    /// keep what was measured.
    /// </summary>
    public bool NoClimb;
    /// <summary>From the body's middle to the lip, along the way (m).</summary>
    public float Distance;
    /// <summary>The point of the top's near edge the body meets, and the face's outward normal there, level.</summary>
    public Vector3 Lip, LipNormal;
    /// <summary>The top's height (world Z) and its normal.</summary>
    public float Top;
    public Vector3 TopNormal;
    /// <summary>
    /// How far the top goes on at about its height <b>square to the face</b>, from the lip (m;
    /// <see cref="TraversalProbe.DeepEnough"/> or more is "deep"). Square to the face rather than along the
    /// way: a wall of 0.3 m met at a slant was once read as deep enough to stand on along its length.
    /// </summary>
    public float Depth;
    /// <summary>
    /// How far the top goes on <b>across</b>, along the face, through where the feet would go (m, to
    /// <see cref="TraversalProbe.DeepEnough"/>): a top narrower than <see cref="Locomotion.MantleMinDepth"/>
    /// cannot be stood on whichever way it is met (a beam met end-on). Measured only on a top deep enough to stand on.
    /// </summary>
    public float Width;
    /// <summary>The hands can hold the lip: a near-level top meeting a near-upright face, open above.</summary>
    public bool Holdable;
    /// <summary>The top can be stood on: deep, wide and flat enough, with a crouched body's headroom (<see cref="Onto"/>).</summary>
    public bool Standable;
    /// <summary>A standing body fits at <see cref="Onto"/> (else it ends a move there crouched).</summary>
    public bool Headroom;
    /// <summary>Where the feet go on the top.</summary>
    public Vector3 Onto;
    /// <summary>Ground was found beyond a top that is not deep (<see cref="Over"/>).</summary>
    public bool Beyond;
    /// <summary>The nearest the feet can be put down beyond the top: the middle a radius and a little past its far side, on the ground there.</summary>
    public Vector3 Over;
    /// <summary>
    /// The top is deep and flat enough to stand on but the level marks it not to be stood on (ours): the
    /// top at the lip or where the feet would go is a <see cref="Formats.L2.L2Surface.NoStand"/> triangle. It is then not
    /// <see cref="Standable"/>, so no hop up, climb onto, mantle or pull-up ends on it; its lip is still held (a grab
    /// hangs) and a vault still goes over it.
    /// </summary>
    public bool NoStand;
}

/// <summary>
/// <b>Reading what is ahead</b> (ours): a deterministic measure of the obstacle in
/// front of a body along a way — its top's height, its depth and the ground beyond (over it or onto it),
/// whether its top can be stood on (deep and flat enough, headroom at least the crouched height), and
/// whether it has a lip the hands can hold. It is a handful of rays and capsule tests against the level
/// in a fixed order, made only when a move could start (a jump pressed, a run or a sprint, a body in the air
/// from a jump), and allocates nothing.
///
/// <para>The face is found by sweeping the body's own radius, lifted clear of the stride
/// (<see cref="Locomotion.StepHeight"/>), forwards; the top by a ray down just inside the face from the
/// height looked at (a ray that starts inside a solid, or meets a face turned down first, means the face
/// goes higher: a wall); the lip by a level ray just under the top; the top's depth by rays down every
/// <see cref="DepthStep"/> square to the face while the top stays within <see cref="TopTolerance"/> of its
/// height, and its width the same way along the face, both ways from where the feet would go; whether it can be
/// stood on by its depth and width (each at least <see cref="Locomotion.MantleMinDepth"/>) and by putting the
/// crouched capsule there; and the ground beyond by a ray down past the far side, along the way.</para>
///
/// <para><b>Square to the face, and across</b>: the depth was once measured along the way, so a wall of 0.3 m
/// met at 45° read 0.42 m and was stood on along its length, and a beam of 0.2 m met end-on read deep and was climbed
/// onto. Now a top is as deep as it is square to the face it is met at, and one narrower across than a top stood on
/// cannot be stood on whichever way it is met: met across it is thin (vaulted, hurdled), met end-on nothing starts
/// from it and a body walking into it stops.</para>
/// </summary>
public static class TraversalProbe
{
    /// <summary>The steps of the depth's look along the top (m) and how far it looks (m): a top this deep is "deep".</summary>
    public const float DepthStep = 0.05f, DeepEnough = 1.0f;

    /// <summary>How far a top may rise or fall along its depth and still be the same top (m).</summary>
    public const float TopTolerance = 0.1f;

    /// <summary>How far below the feet the ground beyond is looked for (m).</summary>
    public const float BeyondDrop = 1.5f;

    /// <summary>How far the far side is cleared by the feet put down beyond it (m, beyond the radius).</summary>
    public const float BeyondClear = 0.05f;

    /// <summary>A face whose normal's upward part is at most this is upright enough for a lip (about 20°).</summary>
    const float UprightZ = 0.35f;

    /// <summary>A top whose normal's upward part is at least this is level enough to hold (about 30°).</summary>
    const float LevelZ = 0.85f;

    const float Skin = BodyMotor.Skin;

    /// <summary>
    /// Reads the obstacle ahead of a body whose feet are at <paramref name="feet"/>, along the level unit
    /// <paramref name="way"/>, whose face is no more than <paramref name="reach"/> from the body's middle,
    /// looking for its top up to <paramref name="above"/> over the feet. Returns <see cref="Obstacle.Found"/>.
    /// </summary>
    public static bool Read(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, float reach, float above, out Obstacle o)
    {
        o = default;
        float wl = way.Length();
        if (wl < 1e-4f || reach <= loco.Radius) return false;
        way /= wl;
        var way3 = new Vector3(way, 0f);
        float r = loco.Radius;
        float low = MathF.Max(0f, loco.StepHeight) + 0.02f;
        if (above <= low + 0.05f) return false;

        // ---- the face: the body's radius, lifted clear of the stride, swept forwards (at least a ball of it)
        // Swept as far as the reach itself and the contact then held to it: a top just over the stride meets the
        // probe's rounded bottom nearer the middle than its radius, and was once seen only from closer.
        var probe = new Capsule(feet + new Vector3(0, 0, low + r), MathF.Max(0f, above - low - 2f * r), r);
        if (level.CapsuleSweep(probe, way3, reach) is not { } hit) return false;
        var nh = new Vector2(hit.Normal.X, hit.Normal.Y);
        float nhl = nh.Length();
        if (nhl < 0.3f) return false;                          // a floor or a ceiling grazed, not a face
        nh /= nhl;
        if (Vector2.Dot(nh, way) > -0.2f) return false;        // met side-on: nothing ahead
        var contact = hit.Point;
        if (Vector2.Dot(new Vector2(contact.X - feet.X, contact.Y - feet.Y), way) > reach + 1e-4f) return false;

        // ---- the top: a ray down just inside the face, from the height looked at
        var inside = new Vector2(contact.X, contact.Y) - nh * 0.02f;
        float z0 = feet.Z + above;
        float span = above - low + 0.1f;
        if (level.Ray(new Vector3(inside, z0), -Vector3.UnitZ, span) is not { } down)
        {
            return false;
        }
        o.Found = true;
        o.LipNormal = new Vector3(nh, 0f);
        o.Lip = new Vector3(contact.X, contact.Y, z0);
        o.Distance = Vector2.Dot(new Vector2(contact.X - feet.X, contact.Y - feet.Y), way);
        if (level.Normal(down.Triangle).Z <= 0.1f)
        {
            // Inside a solid, or under something turned down: the face goes on above what is looked at.
            return true;
        }
        float top = down.Point.Z;
        if (top < feet.Z + loco.StepHeight - 0.02f) { o.Found = false; return false; }
        o.HasTop = true;
        o.Top = top;
        o.TopNormal = down.Normal;

        // ---- the lip: a level ray just under the top, from the body's middle
        bool upright = false;
        int faceTriangle = -1;
        var lipXY = inside + nh * 0.02f;
        if (level.Ray(new Vector3(feet.X, feet.Y, top - 0.03f), way3, reach + 0.2f) is { } face)
        {
            var fh = new Vector2(face.Normal.X, face.Normal.Y);
            float fhl = fh.Length();
            if (fhl > 0.3f && Vector2.Dot(fh / fhl, way) < -0.2f)
            {
                lipXY = new Vector2(face.Point.X, face.Point.Y);
                o.LipNormal = new Vector3(fh / fhl, 0f);
                upright = MathF.Abs(face.Normal.Z) <= UprightZ;
                faceTriangle = face.Triangle;
            }
        }
        else upright = true;   // a thin top (a plate, a roof's edge) with nothing under its edge: its edge is the lip
        o.Lip = new Vector3(lipXY, top);
        o.Distance = Vector2.Dot(new Vector2(lipXY.X - feet.X, lipXY.Y - feet.Y), way);

        // ---- not to be climbed (ours): the top's or the face's triangle flagged, or the lip in a marked
        // area: read as a face with no top, so no move takes this lip; walking and stepping never ask the probe.
        if (level.NoClimb(down.Triangle, o.Lip) || (faceTriangle >= 0 && level.NoClimb(faceTriangle, o.Lip)))
        {
            o.NoClimb = true;
            o.HasTop = false;
            return true;
        }
        o.Holdable = upright && o.TopNormal.Z >= LevelZ;

        // ---- the depth: rays down square to the face while the top stays at about its height (not along the way)
        float minStand = loco.MinStandNormalZ - 2e-5f;
        var inward = new Vector2(-o.LipNormal.X, -o.LipNormal.Y);
        float depth = Extent(level, lipXY, inward, top, minStand, DeepEnough, refine: false);
        o.Depth = depth;

        // ---- the width: the same along the face, both ways from where the feet would go
        if (depth >= loco.MantleMinDepth - 1e-4f)
        {
            var mid = lipXY + inward * MathF.Min(depth * 0.5f, r + 0.1f);
            var along = new Vector2(-inward.Y, inward.X);
            o.Width = Extent(level, mid, along, top, minStand, DeepEnough * 0.5f, refine: true)
                    + Extent(level, mid, -along, top, minStand, DeepEnough * 0.5f, refine: true);
        }

        // ---- standing on it: deep and wide enough, not marked no-stand, and the crouched capsule put on the top
        if (depth >= loco.MantleMinDepth - 1e-4f && o.Width >= loco.MantleMinDepth - 1e-4f && o.TopNormal.Z >= minStand
            && !NoStandTop(level, down.Triangle, ref o))
        {
            float into = MathF.Min(depth * 0.5f, r + 0.1f);
            var onto = lipXY + way * into;
            if (level.Ray(new Vector3(onto, top + TopTolerance), -Vector3.UnitZ, 2f * TopTolerance + 0.05f) is { } under
                && level.Normal(under.Triangle).Z >= minStand && !NoStandTop(level, under.Triangle, ref o))
            {
                var feetOn = new Vector3(onto, under.Point.Z + Skin);
                float crouched = MathF.Max(MathF.Min(loco.CrouchHeight, loco.Height), 2f * r);
                if (!level.Overlaps(Capsule.OfBody(feetOn + new Vector3(0, 0, Skin), r, crouched), 1e-4f))
                {
                    o.Standable = true;
                    o.Onto = feetOn;
                    o.Headroom = !level.Overlaps(Capsule.OfBody(feetOn + new Vector3(0, 0, Skin), r, loco.Height), 1e-4f);
                }
            }
        }

        // ---- the ground beyond a top that is not deep, along the way: its far side square to the face is cleared by
        // the radius and a little, which along a way met at a slant is that much further on
        if (depth < DeepEnough - 1e-4f)
        {
            float cos = MathF.Max(0.2f, Vector2.Dot(way, inward));
            var over = lipXY + way * ((depth + DepthStep * 0.5f + r + BeyondClear) / cos);
            float from = top + TopTolerance;
            if (level.Ray(new Vector3(over, from), -Vector3.UnitZ, from - (feet.Z - BeyondDrop)) is { } g
                && level.Normal(g.Triangle).Z >= minStand && g.Point.Z < top - TopTolerance)
            {
                o.Beyond = true;
                o.Over = new Vector3(over, g.Point.Z + Skin);
            }
        }
        return true;
    }

    /// <summary>Whether <paramref name="triangle"/> is a no-stand top, noted on <paramref name="o"/> (<see cref="Obstacle.NoStand"/>).</summary>
    static bool NoStandTop(CollisionWorld level, int triangle, ref Obstacle o) => o.NoStand |= level.NoStand(triangle);

    /// <summary>
    /// How far a top at <paramref name="top"/> goes on from <paramref name="from"/> along the level unit
    /// <paramref name="dir"/> while it stays at about its height and flat enough to stand on (rays down every
    /// <see cref="DepthStep"/>, to <paramref name="most"/>); with <paramref name="refine"/> the last step is halved
    /// down to a few millimetres, so a stool's 0.4 m is read as 0.4 m and not 0.35.
    /// </summary>
    static float Extent(CollisionWorld level, Vector2 from, Vector2 dir, float top, float minStand, float most, bool refine)
    {
        int steps = (int)(most / DepthStep + 0.5f);
        float found = 0f;
        for (int k = 1; k <= steps; k++)
        {
            if (!OnTop(level, from + dir * (k * DepthStep), top, minStand)) break;
            found = k * DepthStep;
        }
        if (!refine || found >= most - 1e-4f) return found;
        float lo = found, hi = found + DepthStep;
        for (int i = 0; i < 4; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (OnTop(level, from + dir * mid, top, minStand)) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>Whether the top at <paramref name="top"/> is under (x, y): a ray down meets flat enough ground within <see cref="TopTolerance"/> of it.</summary>
    static bool OnTop(CollisionWorld level, Vector2 at, float top, float minStand) =>
        level.Ray(new Vector3(at, top + TopTolerance), -Vector3.UnitZ, 2f * TopTolerance + 0.05f) is { } t
        && level.Normal(t.Triangle).Z >= minStand && MathF.Abs(t.Point.Z - top) <= TopTolerance;
}
