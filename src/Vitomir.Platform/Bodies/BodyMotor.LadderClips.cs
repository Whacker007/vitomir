// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips (ladders; the upright ladder into a braced hang on a ledge beside it, and back)
// carry their capsule from the ladder position (CONVENTIONS, Notes, traverse.ladder, top, landing, hang).
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>ladders on clips</b> (the models repository's <see cref="LadderPaths"/>), for a body given them (the player's <c>PersonLadder</c>, with tier 1's
/// ladders on): <b>the capsule follows the clips' paths</b>, laid from the <b>ladder position</b> (the capsule where the body is in the idle, its left foot on
/// rung j and its right on j + 1), not the earlier build's line.
/// <list type="bullet">
/// <item><b>The set</b> by the level's ladder's lean (its foot to its top against upright): the upright <c>ladder_v_*</c> within
/// <see cref="MovesRules.LeanTolerance"/> of 0°, the leaning <c>ladder_*</c> within it of 15°; anything else, or a ladder too short for the top's clip, keeps
/// The earlier build's stand-in (said once). The level gives a foot, a top, a width and a way out, no rungs: <b>the rungs are taken to be the clips' own</b> (rung 1 at
/// <c>first_rung_m</c> over the foot, <c>rung_m</c> apart along the rails), and the ladder position for rung j is laid from them — so the hands and feet are on
/// the level's rungs wherever its spacing is the clips'.</item>
/// <item><b>The climb</b> goes by whole cycles of two rungs (j = 1, 3, 5 … from the foot), chained with no fade while the wish holds, each from the idle's
/// first frame to it; let go, the cycle under way finishes and the idle follows. Sprint held plays them <see cref="MovesRules.LadderSprintRate"/> faster.</item>
/// <item><b>The top</b>: the floor is at a rung's top for the clips (<c>traverse.top.rung</c> over the top position); <b>a level's floor off the climb's
/// rungs</b> is met at the top position nearest it (the highest odd j whose floor is within a rung of the top's) and the difference (up to a rung, 0.3 m)
/// taken out by the clip over the top (and the getting-on from it) along its rise, so the climb's own rungs stay where they are. Beyond a rung off, the stand-in.</item>
/// <item><b>A side landing</b> (a floor beside the ladder, <c>traverse.landing</c>): looked for at each idle, level with rung j + 1 within
/// <see cref="LandingTolerance"/>, where the clip ends (its <c>stand_left_m</c> and <c>stand_fwd_m</c>), a floor that can be stood on with room; the wish to that
/// side takes it. A landing level with an even rung is between the climb's places and is not offered.</item>
/// <item><b>The ladder to a braced ledge</b> (upright ladders: <c>traverse.hang</c>): the ledge's top level with rung j + 5 within the tolerance, its edge in the
/// wall the ladder stands against, a lip under both hands' places and a wall under it; the wish to that side. Back from the hang with the wish toward the
/// ladder where one stands so (the hang's middle within <see cref="HangAside"/> of the clip's along the edge).</item>
/// <item><b>Collision</b>: the ends are checked before a clip starts (a floor and room at the top or the landing, the hang's room), not the way between (the
/// ladder's own rails and rungs are in the level's collision and the leaning set's capsule stands closer to its rungs than its radius).</item>
/// </list>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>How near a landing's floor, a ledge's top or the hang must be to a rung's for the clip (m).</summary>
    public const float LandingTolerance = 0.1f;

    /// <summary>How far along the edge the hang's middle may be from the ladder-to-hang clip's own for it to go back onto the ladder (m): a braced shimmy's cycle and a little.</summary>
    public const float HangAside = 0.2f;

    /// <summary>The longest the start's difference is taken out over in getting on (s; by the first hand's hold when sooner).</summary>
    const float LadderDeltaMost = 0.6f, LadderDeltaLeast = 0.2f;

    /// <summary>The level unit way into the ladder (its climbing side's way out, turned about).</summary>
    static Vector2 InOf(in Ladder l)
    {
        var n = new Vector2(-l.Out.X, -l.Out.Y);
        float nl = n.Length();
        return nl > 1e-5f ? n / nl : new Vector2(1f, 0f);
    }

    /// <summary>A level ladder's lean off upright (radians; positive with its top in toward the wall): its foot to its top.</summary>
    public static float LeanOf(in Ladder l)
    {
        var d = l.Top - l.Foot;
        float across = Vector2.Dot(new Vector2(d.X, d.Y), InOf(l));
        return DetMath.Atan2(across, MathF.Max(1e-4f, d.Z));
    }

    /// <summary>The clips' set a ladder is climbed on (its number in <paramref name="set"/>: 1 upright, 2 leaning), or null when its lean fits neither.</summary>
    public static LadderSet? SetOf(in Ladder l, LadderPaths paths, MovesRules tier, out int set)
    {
        float lean = LeanOf(l);
        set = 0;
        if (paths.Vertical is { } v && MathF.Abs(lean - v.Tilt) <= tier.LeanTolerance) { set = 1; return v; }
        if (paths.Leaning is { } w && MathF.Abs(lean - w.Tilt) <= tier.LeanTolerance) { set = 2; return w; }
        return null;
    }

    /// <summary>The ladder position for rung <paramref name="rung"/> (the capsule's bottom: rung j's front <c>rung_fwd_m</c> ahead of it, its top <c>lower_rung_up_m</c> over it).</summary>
    public static Vector3 LadderPosition(in Ladder l, LadderSet s, int rung)
    {
        float z = l.Foot.Z + s.FirstRung + (rung - 1) * s.RungUp;
        var face = l.FaceAt(z);
        var inward = InOf(l);
        return new Vector3(face.X - inward.X * s.RungFwd, face.Y - inward.Y * s.RungFwd, z - s.LowerRungUp);
    }

    /// <summary>
    /// The top position (its rung, odd, from 1) whose floor (the clip over the top's <c>floor_up_m</c>) is nearest the ladder's top, and how far the level's
    /// floor is over it (m; positive higher); −1 when the ladder is too short for it.
    /// </summary>
    public static int TopPosition(in Ladder l, LadderSet s, out float off)
    {
        float rungs = (l.Top.Z - l.Foot.Z - s.FirstRung) / MathF.Max(1e-4f, s.RungUp) + 1f;   // the floor at this rung's top
        float jf = rungs - (s.OffTop.TopRung - 1);
        int j = 1 + 2 * (int)MathF.Round((jf - 1f) * 0.5f);
        if (j < 1) j = 1;
        off = l.Top.Z - (LadderPosition(l, s, j).Z + s.OffTop.FloorUp);
        return MathF.Abs(off) <= s.Rung + 1e-3f ? j : -1;
    }

    /// <summary>The nearest odd rung (from 1) whose ladder position is <paramref name="up"/> under <paramref name="z"/>, and how far off (m).</summary>
    static int RungFor(in Ladder l, LadderSet s, float z, float up, out float off)
    {
        float jf = 1f + (z - up + s.LowerRungUp - l.Foot.Z - s.FirstRung) / MathF.Max(1e-4f, s.RungUp);
        int j = 1 + 2 * (int)MathF.Round((jf - 1f) * 0.5f);
        if (j < 1) j = 1;
        off = z - (LadderPosition(l, s, j).Z + up);
        return j;
    }

    // ---------------------------------------------------------------- the path

    /// <summary>The clip's path at <paramref name="t"/> s laid from the move's place: forward into the ladder, to the left, up.</summary>
    internal static Vector3 LadderRaw(in LadderClipMove l, LadderClip c, float t)
    {
        var (f, u, s) = c.At(t);
        var left = LeftOf(l.In);
        return new Vector3(l.Place.X + l.In.X * f + left.X * s, l.Place.Y + l.In.Y * f + left.Y * s, l.Place.Z + u);
    }

    /// <summary>
    /// <b>where the feet are</b> in a ladder clip at <paramref name="t"/> s of it, <paramref name="age"/> s after it began: its path laid from the place, what
    /// makes it end where it must (<see cref="LadderClipMove.Fix"/>) laid on across by the clip's time and up by its rise, the start's difference taken out by
    /// <see cref="LadderClipMove.BlendTo"/>.
    /// </summary>
    public static Vector3 LadderPoint(in LadderClipMove l, LadderClip c, float t, float age)
    {
        var p = LadderRaw(l, c, t);
        float ts = c.Duration > 1e-5f ? Math.Clamp(t / c.Duration, 0f, 1f) : 1f;
        float u0 = c.At(0f).Up, u1 = c.At(c.Duration).Up, u = c.At(t).Up;
        float us = MathF.Abs(u1 - u0) > 0.05f ? Math.Clamp((u - u0) / (u1 - u0), 0f, 1f) : ts;
        p += new Vector3(l.Fix.X * ts, l.Fix.Y * ts, l.Fix.Z * us);
        float w = l.BlendTo > 1e-5f ? Smooth(age / l.BlendTo) : 1f;
        return p + l.Delta * (1f - w);
    }

    /// <summary>The ladder's line for the earlier build's traversal (its feet's way from the foot to the top position): kept so the pose's ladder slot and the log read as before.</summary>
    /// <summary>The most a ladder's clips play faster than their own (the climb's rate times the sprint's capped here).</summary>
    public const float MaxLadderRate = 3f;

    static Traversal LadderTraversal(in Ladder l, LadderSet s, TraversalKind kind, Vector3 feet, float seconds)
    {
        int top = TopPosition(l, s, out _);
        var from = LadderPosition(l, s, 1);
        var to = top > 0 ? LadderPosition(l, s, top) : from;
        float span = to.Z - from.Z;
        return new Traversal
        {
            Kind = kind, Progress = span > 1e-4f ? Math.Clamp((feet.Z - from.Z) / span, 0f, 1f) : 0f, Seconds = MathF.Max(seconds, World.TickSeconds),
            From = from, To = to, Lip = l.FaceAt(MathF.Min(feet.Z + LadderHands, l.Top.Z)), LipNormal = l.Out, Height = l.Rise,
        };
    }

    // ---------------------------------------------------------------- getting on

    /// <summary>
    /// <b>use on the ground</b> by a body given the ladder's clips: at a ladder's foot or top (the earlier build's reach) or on a landing beside one (where its getting-on
    /// starts, level with a rung), the nearest; its clip from where the body stands, the start's difference taken out by its first hold. False when there is
    /// none, or the ladder fits neither set (<see cref="MotorOutcome.LadderStandIn"/>: the earlier build's then).
    /// </summary>
    static bool TryUseLadderClips(in Mover m, ref Body body, ref Traversal move, ref LadderClipMove climb, LadderPaths paths, ref Vector3 feet, in Locomotion loco,
                                  MovesRules tier, ref MotorOutcome o)
    {
        var ladders = m.Level.Ladders;
        if (ladders.Count == 0) return false;
        var face = Facing(body.Yaw);
        int best = -1, bestSide = 0, bestRung = 0;
        var from = LadderEnd.None;
        float bestD = float.PositiveInfinity;
        for (int i = 0; i < ladders.Count; i++)
        {
            ref readonly var l = ref ladders[i];
            var outv = new Vector2(l.Out.X, l.Out.Y);
            var side = new Vector2(l.Side.X, l.Side.Y);
            float aside = l.Width * 0.5f + LadderUseAside;
            var d = new Vector2(feet.X - l.Foot.X, feet.Y - l.Foot.Y);
            float off = Vector2.Dot(d, outv);
            if (MathF.Abs(feet.Z - l.Foot.Z) <= LadderUseHeight && off >= 0f && off <= m.R + LadderUseReach
                && MathF.Abs(Vector2.Dot(d, side)) <= aside && Vector2.Dot(face, -outv) >= LadderUseCone && d.Length() < bestD)
                (best, from, bestD) = (i, LadderEnd.Foot, d.Length());
            d = new Vector2(feet.X - l.Top.X, feet.Y - l.Top.Y);
            off = Vector2.Dot(d, outv);
            if (MathF.Abs(feet.Z - l.Top.Z) <= LadderUseHeight && off <= 0.05f && off >= -(m.R + LadderUseReach)
                && MathF.Abs(Vector2.Dot(d, side)) <= aside && MathF.Abs(Vector2.Dot(face, outv)) >= LadderUseCone && d.Length() < bestD)
                (best, from, bestD) = (i, LadderEnd.Top, d.Length());
            // A landing beside it: where a getting-on from that side starts, level with the floor stood on.
            if (SetOf(l, paths, tier, out _) is not { } s) continue;
            int topJ = TopPosition(l, s, out _);
            for (int k = 0; k < 2; k++)
            {
                if (s.OnSide[k] is not { } c || float.IsNaN(c.LandUp)) continue;
                int j = RungFor(l, s, feet.Z, c.LandUp, out float zOff);
                if (MathF.Abs(zOff) > LandingTolerance || (topJ > 0 && j > topJ)) continue;
                var place = new LadderClipMove { Place = LadderPosition(l, s, j), In = InOf(l) };
                var start = LadderRaw(place, c, 0f);
                float dd = Vector2.Distance(new Vector2(start.X, start.Y), new Vector2(feet.X, feet.Y));
                if (dd <= LadderUseReach && dd < bestD) (best, from, bestD, bestSide, bestRung) = (i, LadderEnd.Side, dd, c.Side, j);
            }
        }
        if (best < 0) return false;
        ref readonly var ladder = ref ladders[best];
        if (SetOf(ladder, paths, tier, out int setNo) is not { } set)
        {
            o.LadderStandIn = best + 1;
            o.LadderLean = LeanOf(ladder) * DetMath.RadToDeg;
            return false;
        }
        int top = TopPosition(ladder, set, out float topOff);
        LadderClip clip;
        int rung;
        var next = new LadderClipMove { Set = setNo, In = InOf(ladder), Rate = tier.LadderMoveRate, Fade = 1 };   // the getting-on at ladderMoveRate
        switch (from)
        {
            case LadderEnd.Top:
                if (top < 0)
                {
                    o.LadderStandIn = best + 1;
                    o.LadderLean = LeanOf(ladder) * DetMath.RadToDeg;
                    o.LadderTopOff = topOff;
                    return false;
                }
                clip = Rails(m.Level, ladder) && set.OnTopRails is { } rails ? rails : set.OnTop;
                rung = top;
                // From the real floor down to the top position: the path laid a floor's difference higher and that much taken out along its rise.
                next.Place = LadderPosition(ladder, set, rung) + new Vector3(0f, 0f, topOff);
                next.Fix = new Vector3(0f, 0f, -topOff);
                next.Fade = Vector2.Dot(face, next.In) >= 0f ? 1 : 2;   // facing the drop: the clip begins facing away from it, the turn faded in longer
                o.LadderTopOff = topOff;
                break;
            case LadderEnd.Side:
                clip = set.OnSide[LadderSet.SideIndex(bestSide)]!;
                rung = bestRung;
                next.Place = LadderPosition(ladder, set, rung);
                next.Way = bestSide;
                break;
            default:
                clip = set.OnBottom;
                rung = 1;
                next.Place = LadderPosition(ladder, set, rung);
                break;
        }
        next.Rung = rung;
        next.Clip = paths.IndexOf(clip) + 1;
        next.Delta = feet - LadderRaw(next, clip, 0f);
        next.BlendTo = Math.Clamp(clip.FirstContact / next.Rate, LadderDeltaLeast, LadderDeltaMost);
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = LadderTraversal(ladder, set, TraversalKind.LadderOn, feet, clip.Duration / next.Rate);
        climb = next;
        o.MoveStarted = TraversalKind.LadderOn;
        o.LadderOn = from;
        o.LadderIndex = best;
        o.LadderFeet = feet;
        body.State = BodyState.Traversing;
        body.Ladder = best + 1;
        body.Crouched = 0; body.Stoop = 0f;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.Rising = 0; body.Jumped = 0; body.Charging = 0; body.Powered = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1; body.GroundSeconds = 0f;
        LadderClipStep(ref body, ref climb, clip, ref feet, first: true);
        return true;
    }

    /// <summary>Whether a ladder's rails go on above its top (rays across where they would stand, half a metre over the floor).</summary>
    static bool Rails(CollisionWorld level, in Ladder l)
    {
        var inward = InOf(l);
        var side = new Vector2(l.Side.X, l.Side.Y);
        for (int k = -1; k <= 1; k += 2)
        {
            var at = new Vector2(l.Top.X, l.Top.Y) + side * (k * (l.Width * 0.5f - 0.03f)) - inward * 0.3f;
            if (level.Ray(new Vector3(at, l.Top.Z + 0.5f), new Vector3(inward, 0f), 0.45f) is null) return false;
        }
        return true;
    }

    /// <summary>A tick of a ladder clip's time (at its rate; a loop round) and its age, and the feet on its path.</summary>
    static void LadderClipStep(ref Body body, ref LadderClipMove l, LadderClip c, ref Vector3 feet, bool first = false)
    {
        const float dt = World.TickSeconds;
        if (!first)
        {
            l.Age += dt;
            l.Time += dt * (l.Rate > 0f ? l.Rate : 1f);
            if (c.Loops) { if (l.Time >= c.Duration) l.Time -= c.Duration; }
            else if (l.Time > c.Duration - 1e-4f) l.Time = MathF.Min(l.Time, c.Duration + dt * 4f);
        }
        var p = LadderPoint(l, c, MathF.Min(l.Time, c.Duration), l.Age);
        body.Velocity = (p - feet) / dt;
        feet = p;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(l.In.Y, l.In.X));   // the clip is drawn facing the ladder
    }

    // ---------------------------------------------------------------- a tick

    /// <summary>
    /// <b>one tick of a body in a ladder clip</b> (<see cref="LadderClipMove.Clip"/>): on the ladder the idle and the climb's cycles with the ladder's
    /// controls (forward and back as the view faces, jump pushes off, crouch or cancel lets go; the wish to a side onto a landing or into a hang);
    /// getting on, to the idle; getting off, to the ground, the floor, the landing or the hang.
    /// </summary>
    static void LadderClipTick(in Mover m, ref Body body, ref Traversal move, ref LadderClipMove l, LadderPaths paths, ref Vector3 feet, in Locomotion loco,
                               in MoveIntent intent, ref MotorOutcome o, MovesRules tier, ref LedgeMove ledge, LedgePaths? ledges)
    {
        if (paths.At(l.Clip - 1) is not { } c || paths.Set(l.Set) is not { } set)
        {
            l = default;
            o.LadderOff = LadderLeave.Stopped; o.LadderIndex = body.Ladder - 1; o.LadderFeet = feet;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        int index = body.Ladder - 1;
        bool onIt = c.Role is LadderRole.Idle or LadderRole.Up or LadderRole.Down;
        if ((onIt || c.Role is LadderRole.OnBottom or LadderRole.OnTop or LadderRole.OnSide or LadderRole.FromHang) && (index < 0 || index >= m.Level.Ladders.Count))
        {
            l = default;
            o.LadderOff = LadderLeave.Stopped; o.LadderIndex = index; o.LadderFeet = feet;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        if (onIt) { OnLadderTick(m, ref body, ref move, ref l, paths, set, c, ref feet, loco, intent, ref o, tier, ledges); return; }
        LadderClipStep(ref body, ref l, c, ref feet);
        move.Progress = c.Duration > 0f ? Math.Clamp(l.Time / c.Duration, 0f, 1f) : 1f;
        if (l.Time < c.Duration - 1e-4f) return;
        switch (c.Role)
        {
            case LadderRole.OnBottom or LadderRole.OnTop or LadderRole.OnSide or LadderRole.FromHang:
            {
                // On: the idle at the ladder position from its first frame (each getting-on ends on it exactly).
                ref readonly var ladder = ref m.Level.Ladders[index];
                var at = LadderPosition(ladder, set, l.Rung);
                var keep = l;
                l = new LadderClipMove { Clip = paths.IndexOf(set.Idle) + 1, Set = keep.Set, Rung = keep.Rung, Place = at, In = keep.In, Rate = 1f, Fade = 0,
                                         Delta = feet - at, BlendTo = LedgeBlend };
                o.MoveEnded = move.Kind; o.MoveCompleted = true;
                move = LadderTraversal(ladder, set, TraversalKind.Ladder, feet, 0f);
                o.MoveStarted = TraversalKind.Ladder;
                body.State = BodyState.Ladder;
                body.Velocity = Vector3.Zero;
                body.AirSeconds = 0f; body.GroundSeconds = 0f;
                return;
            }
            case LadderRole.ToHang:
            {
                // Into the braced hang from its first frame (the clip ends on it), at the hang's place from the ledge's edge.
                var keep = l;
                l = default;
                o.MoveEnded = move.Kind; o.MoveCompleted = true;
                if (ledges is null || System.Runtime.CompilerServices.Unsafe.IsNullRef(ref ledge) || ledges.Of(LedgeRole.Hang, braced: true) is not { } hang)
                {
                    Abort(m, ref body, ref move, ref feet, loco, ref o);
                    return;
                }
                var lipNormal = new Vector3(-keep.In.X, -keep.In.Y, 0f);
                ledge = new LedgeMove
                {
                    Clip = ledges.IndexOf(hang) + 1, Time = 0f, Age = 0f, Braced = 1, Place = HangPlace(ledges, keep.Lip, lipNormal, keep.Top), In = keep.In, Top = keep.Top,
                    Floor = float.NegativeInfinity,
                };
                ledge.Delta = feet - LedgeRaw(ledge, hang, 0f);
                (ledge.AcrossFrom, ledge.AcrossTo, ledge.UpFrom, ledge.UpTo) = (0f, LedgeBlend, 0f, LedgeBlend);
                body.State = BodyState.Hanging;
                body.Crouched = 0; body.Velocity = Vector3.Zero; body.ForwardArmed = 0; body.AirSeconds = 0f; body.Shimmy = 0; body.Ladder = 0;
                move = new Traversal
                {
                    Kind = TraversalKind.Hang, Seconds = loco.GripSeconds > 0f ? loco.GripSeconds : float.PositiveInfinity,
                    From = feet, To = feet, Lip = keep.Lip, LipNormal = lipNormal, Height = keep.Lip.Z - feet.Z,
                };
                o.MoveStarted = TraversalKind.Hang;
                return;
            }
            default:
                // Off onto the ground, the floor at the top or a landing: standing there.
                o.MoveEnded = move.Kind; o.MoveCompleted = true;
                move = default;
                l = default;
                body.Ladder = 0;
                SetDown(m, ref body, ref feet, loco, intent, Vector3.Zero);
                return;
        }
    }

    /// <summary>On the ladder: the idle (its controls read) or a climb's cycle (finished, then the next while the wish holds that way, else the idle).</summary>
    static void OnLadderTick(in Mover m, ref Body body, ref Traversal move, ref LadderClipMove l, LadderPaths paths, LadderSet set, LadderClip c, ref Vector3 feet,
                             in Locomotion loco, in MoveIntent intent, ref MotorOutcome o, MovesRules tier, LedgePaths? ledges)
    {
        int index = body.Ladder - 1;
        ref readonly var ladder = ref m.Level.Ladders[index];
        body.State = BodyState.Ladder;
        body.AirSeconds = 0f; body.GroundSeconds = 0f;
        if (intent.Cancel || (intent.Crouch && body.CrouchWas == 0))
        {
            o.Cancelled = intent.Cancel;
            l = default;
            LeaveLadder(ref body, index, feet, LadderLeave.LetGo, ref o);
            LetGo(m, ref body, ref move, feet, ref o);
            o.Dropped = false;
            return;
        }
        if (intent.Jump)
        {
            l = default;
            LeaveLadder(ref body, index, feet, LadderLeave.PushOff, ref o);
            PushOff(m, ref body, ref move, feet, loco, ref o);
            return;
        }
        // The earlier build's controls: forward and back as the view faces; the wish's sideways part as the body faces the ladder.
        var (fs, fc) = DetMath.SinCos(intent.Facing);
        float heading = Vector2.Dot(intent.Direction, new Vector2(fc, fs));
        int want = heading > ClimbCone ? 1 : heading < -ClimbCone ? -1 : 0;
        var right = new Vector2(l.In.Y, -l.In.X);
        float lateral = intent.Direction.LengthSquared() > 0.01f ? Vector2.Dot(intent.Direction, right) : 0f;
        int side = lateral > ShimmyCone ? -1 : lateral < -ShimmyCone ? 1 : 0;   // +1 to the left
        if (want != 0 && MathF.Abs(lateral) > MathF.Abs(heading)) want = 0;
        else if (side != 0 && MathF.Abs(heading) >= MathF.Abs(lateral)) side = 0;
        int top = TopPosition(ladder, set, out float topOff);
        // The climb's cycles at ladderClimbRate, sprint held that times ladderSprintRate (at most 3); the getting-off at ladderMoveRate.
        float rate = MathF.Min(MaxLadderRate, tier.LadderClimbRate * (intent.Gait >= Gait.Sprint ? tier.LadderSprintRate : 1f));
        float moveRate = tier.LadderMoveRate;
        if (c.Role == LadderRole.Idle)
        {
            LadderClipStep(ref body, ref l, c, ref feet);
            bool fresh = l.Age < 1.5f * World.TickSeconds;   // the idle only just begun: on its first frame
            if (want > 0 && top > 0 && l.Rung + 2 <= top) StartLadderCycle(ref l, paths, set, set.Up, ladder, 1, rate, fade: !fresh, ref feet, ref body);
            else if (want > 0 && top > 0 && l.Rung == top) TryOffTop(m, ref body, ref move, ref l, paths, set, ladder, index, topOff, ref feet, loco, ref o, moveRate);
            else if (want < 0 && l.Rung - 2 >= 1) StartLadderCycle(ref l, paths, set, set.Down, ladder, -1, rate, fade: !fresh, ref feet, ref body);
            else if (want < 0 && l.Rung <= 1) OffBottom(m, ref body, ref move, ref l, paths, set, ladder, index, ref feet, ref o, moveRate);
            else if (side != 0 && !TryOffSide(m, ref body, ref move, ref l, paths, set, ladder, index, side, ref feet, loco, ref o, moveRate))
                TryToHang(m, ref body, ref move, ref l, paths, set, ladder, index, side, ref feet, loco, ref o, ledges, moveRate);
        }
        else
        {
            l.Rate = rate;
            LadderClipStep(ref body, ref l, c, ref feet);
            if (l.Time >= c.Duration - 1e-4f)
            {
                int way = c.Role == LadderRole.Up ? 1 : -1;
                float carry = MathF.Max(0f, l.Time - c.Duration);
                l.Rung += 2 * way;
                var at = LadderPosition(ladder, set, l.Rung);
                bool more = want == way && (way > 0 ? top > 0 && l.Rung + 2 <= top : l.Rung - 2 >= 1);
                var keep = l;
                l = new LadderClipMove { Set = keep.Set, Rung = keep.Rung, Place = at, In = keep.In, Rate = more ? rate : 1f, Fade = 0, Way = more ? way : 0 };
                if (more)
                {
                    var next = way > 0 ? set.Up : set.Down;
                    l.Clip = paths.IndexOf(next) + 1;
                    l.Fix = LadderPosition(ladder, set, l.Rung + 2 * way) - LadderRaw(l, next, next.Duration);
                }
                else l.Clip = paths.IndexOf(set.Idle) + 1;
                l.Time = carry;
                l.Age = carry;
                // The cycle's end laid on the next ladder position (its fix): the next clip from its first frame there, nothing to take out.
                feet = LadderPoint(l, paths.At(l.Clip - 1)!, l.Time, l.Age);
                // At the end of the climb with the wish still that way: off there at once (the cycle ends on the idle's first frame, where they begin).
                if (!more && want == way && way > 0 && top > 0 && l.Rung == top) TryOffTop(m, ref body, ref move, ref l, paths, set, ladder, index, topOff, ref feet, loco, ref o, moveRate);
                else if (!more && want == way && way < 0 && l.Rung <= 1) OffBottom(m, ref body, ref move, ref l, paths, set, ladder, index, ref feet, ref o, moveRate);
                if (move.Kind != TraversalKind.Ladder) return;
            }
        }
        if (move.Kind == TraversalKind.Ladder)
        {
            var span = move.To.Z - move.From.Z;
            move.Progress = span > 1e-4f ? Math.Clamp((feet.Z - move.From.Z) / span, 0f, 1f) : 0f;
            move.Lip = ladder.FaceAt(MathF.Min(feet.Z + LadderHands, ladder.Top.Z));
        }
    }

    /// <summary>A climb's cycle from the idle (faded when the idle is part way through its loop), its end laid on the next ladder position.</summary>
    static void StartLadderCycle(ref LadderClipMove l, LadderPaths paths, LadderSet set, LadderClip cycle, in Ladder ladder, int way, float rate, bool fade,
                                 ref Vector3 feet, ref Body body)
    {
        var keep = l;
        l = new LadderClipMove { Clip = paths.IndexOf(cycle) + 1, Set = keep.Set, Rung = keep.Rung, Place = LadderPosition(ladder, set, keep.Rung), In = keep.In,
                                 Rate = rate, Fade = fade ? 1 : 0, Way = way };
        l.Fix = LadderPosition(ladder, set, keep.Rung + 2 * way) - LadderRaw(l, cycle, cycle.Duration);
        l.Delta = feet - LadderPoint(l, cycle, 0f, 0f);
        l.BlendTo = fade ? LedgeBlend : 0f;
        LadderClipStep(ref body, ref l, cycle, ref feet, first: true);
    }

    /// <summary>Forward at the top position: over onto the floor, when there is one where the clip stands and room; the floor's difference from the clip's taken out along its rise.</summary>
    static bool TryOffTop(in Mover m, ref Body body, ref Traversal move, ref LadderClipMove l, LadderPaths paths, LadderSet set, in Ladder ladder, int index,
                          float topOff, ref Vector3 feet, in Locomotion loco, ref MotorOutcome o, float rate = 1f)
    {
        var c = Rails(m.Level, ladder) && set.OffTopRails is { } rails ? rails : set.OffTop;
        var next = l;
        next.Clip = paths.IndexOf(c) + 1; next.Time = 0f; next.Age = 0f; next.Rate = rate; next.Fade = 0; next.Way = 0; next.Delta = Vector3.Zero; next.BlendTo = 0f;
        next.Place = LadderPosition(ladder, set, l.Rung);
        var end = LadderRaw(next, c, c.Duration) + new Vector3(0f, 0f, topOff);
        if (m.Level.Ray(end + new Vector3(0f, 0f, 0.5f), -Vector3.UnitZ, 0.5f + LandingTolerance) is not { } under || under.Normal.Z < m.MinN
            || m.Level.NoStand(under.Triangle)) return false;
        end.Z = under.Point.Z;
        if (!Fits(m.Level, end + new Vector3(0f, 0f, Skin), loco.Radius, TuckedHeight(loco))) return false;
        next.Fix = end - LadderRaw(next, c, c.Duration);
        LeaveLadder(ref body, index, feet, LadderLeave.Top, ref o);
        o.LadderTopOff = topOff;
        o.MoveEnded = TraversalKind.Ladder; o.MoveCompleted = true;
        move = new Traversal
        {
            Kind = TraversalKind.PullUp, Seconds = MathF.Max(c.Duration / rate, World.TickSeconds), From = feet, To = end, Lip = ladder.Top, LipNormal = ladder.Out,
            Height = ladder.Top.Z - feet.Z,
        };
        o.MoveStarted = TraversalKind.PullUp;
        body.State = BodyState.Traversing;
        l = next;
        LadderClipStep(ref body, ref l, c, ref feet, first: true);
        return true;
    }

    /// <summary>Back at the bottom position: down onto the ground at the foot.</summary>
    static void OffBottom(in Mover m, ref Body body, ref Traversal move, ref LadderClipMove l, LadderPaths paths, LadderSet set, in Ladder ladder, int index,
                          ref Vector3 feet, ref MotorOutcome o, float rate = 1f)
    {
        var c = set.OffBottom;
        var next = l;
        next.Clip = paths.IndexOf(c) + 1; next.Time = 0f; next.Age = 0f; next.Rate = rate; next.Fade = 0; next.Way = 0; next.Delta = Vector3.Zero; next.BlendTo = 0f;
        next.Place = LadderPosition(ladder, set, l.Rung);
        var end = LadderRaw(next, c, c.Duration);
        if (m.Level.Ray(end + new Vector3(0f, 0f, 0.3f), -Vector3.UnitZ, 0.6f) is { } under && under.Normal.Z >= m.MinN) end.Z = under.Point.Z;
        next.Fix = end - LadderRaw(next, c, c.Duration);
        LeaveLadder(ref body, index, feet, LadderLeave.Foot, ref o);
        o.MoveEnded = TraversalKind.Ladder; o.MoveCompleted = true;
        move = new Traversal
        {
            Kind = TraversalKind.LadderOff, Seconds = MathF.Max(c.Duration / rate, World.TickSeconds), From = feet, To = end, Lip = ladder.Foot, LipNormal = ladder.Out,
            Height = ladder.Rise,
        };
        o.MoveStarted = TraversalKind.LadderOff;
        body.State = BodyState.Traversing;
        l = next;
        LadderClipStep(ref body, ref l, c, ref feet, first: true);
    }

    /// <summary>
    /// <b>a side landing</b> at the ladder position: a floor that can be stood on where the clip onto the landing to <paramref name="side"/> ends (level with
    /// rung j + 1 within <see cref="LandingTolerance"/>) and room there: onto it; false (still on the ladder) else.
    /// </summary>
    static bool TryOffSide(in Mover m, ref Body body, ref Traversal move, ref LadderClipMove l, LadderPaths paths, LadderSet set, in Ladder ladder, int index, int side,
                           ref Vector3 feet, in Locomotion loco, ref MotorOutcome o, float rate = 1f)
    {
        if (set.OffSide[LadderSet.SideIndex(side)] is not { } c) return false;
        var next = l;
        next.Clip = paths.IndexOf(c) + 1; next.Time = 0f; next.Age = 0f; next.Rate = rate; next.Fade = 0; next.Way = side; next.Delta = Vector3.Zero; next.BlendTo = 0f;
        next.Place = LadderPosition(ladder, set, l.Rung);
        if (!LandingAt(m, loco, LadderRaw(next, c, c.Duration), out var end)) return false;
        next.Fix = end - LadderRaw(next, c, c.Duration);
        LeaveLadder(ref body, index, feet, LadderLeave.Side, ref o);
        o.MoveEnded = TraversalKind.Ladder; o.MoveCompleted = true;
        move = new Traversal
        {
            Kind = TraversalKind.LadderOff, Seconds = MathF.Max(c.Duration / rate, World.TickSeconds), From = feet, To = end, Lip = end, LipNormal = ladder.Out,
            Height = end.Z - ladder.Foot.Z,
        };
        o.MoveStarted = TraversalKind.LadderOff;
        body.State = BodyState.Traversing;
        l = next;
        LadderClipStep(ref body, ref l, c, ref feet, first: true);
        return true;
    }

    /// <summary>Whether there is a landing at <paramref name="at"/> (a floor that can be stood on within <see cref="LandingTolerance"/> of its height, room for the tucked body), and where its feet stand.</summary>
    static bool LandingAt(in Mover m, in Locomotion loco, Vector3 at, out Vector3 stand)
    {
        stand = at;
        if (m.Level.Ray(at + new Vector3(0f, 0f, 0.4f), -Vector3.UnitZ, 0.4f + LandingTolerance) is not { } under || under.Normal.Z < m.MinN
            || m.Level.NoStand(under.Triangle) || MathF.Abs(under.Point.Z - at.Z) > LandingTolerance) return false;
        stand = new Vector3(at.X, at.Y, under.Point.Z);
        return Fits(m.Level, stand + new Vector3(0f, 0f, Skin), loco.Radius, TuckedHeight(loco));
    }

    /// <summary>
    /// <b>the ladder to a braced ledge</b> beside it (the upright set): at the ladder position, the ledge to <paramref name="side"/> where the clip has
    /// it — a lip under both hands' places with its top level with rung j + 5 (within the tolerance) and its edge in the wall's plane, the hang's room and a
    /// wall under it for the feet: into the hang; false (still on the ladder) else.
    /// </summary>
    static bool TryToHang(in Mover m, ref Body body, ref Traversal move, ref LadderClipMove l, LadderPaths paths, LadderSet set, in Ladder ladder, int index, int side,
                          ref Vector3 feet, in Locomotion loco, ref MotorOutcome o, LedgePaths? ledges, float rate = 1f)
    {
        if (ledges is null || set.ToHang[LadderSet.SideIndex(side)] is not { } c || float.IsNaN(c.EdgeUp)) return false;
        var next = l;
        next.Clip = paths.IndexOf(c) + 1; next.Time = 0f; next.Age = 0f; next.Rate = rate; next.Fade = 0; next.Way = side; next.Delta = Vector3.Zero; next.BlendTo = 0f;
        next.Place = LadderPosition(ladder, set, l.Rung);
        if (!LedgeBeside(m, loco, next, c, ladder, ledges, out var lip, out float top)) return false;
        var lipNormal = new Vector3(-next.In.X, -next.In.Y, 0f);
        var hangAt = new LedgeMove { Place = HangPlace(ledges, lip, lipNormal, top), In = next.In };
        if (ledges.Of(LedgeRole.Hang, braced: true) is not { } hang) return false;
        next.Fix = LedgeRaw(hangAt, hang, 0f) - LadderRaw(next, c, c.Duration);
        next.Lip = lip;
        next.Top = top;
        LeaveLadder(ref body, index, feet, LadderLeave.Hang, ref o);
        o.MoveEnded = TraversalKind.Ladder; o.MoveCompleted = true;
        move = new Traversal
        {
            Kind = TraversalKind.LadderOff, Seconds = MathF.Max(c.Duration / rate, World.TickSeconds), From = feet, To = LedgeRaw(hangAt, hang, 0f), Lip = lip,
            LipNormal = lipNormal, Height = top - feet.Z,
        };
        o.MoveStarted = TraversalKind.LadderOff;
        body.State = BodyState.Traversing;
        l = next;
        LadderClipStep(ref body, ref l, c, ref feet, first: true);
        return true;
    }

    /// <summary>
    /// Whether a ladder clip into a hang (or out of one) at its place has its ledge: a lip under each hand's place along the edge (<c>hands_left_m</c>) at the
    /// edge's height (within <see cref="LandingTolerance"/> of the clip's) in the wall's plane, the hang fitting and a wall under it for the feet; the lip's
    /// middle point (the hang's: <c>edge_left_m</c>) and its top.
    /// </summary>
    static bool LedgeBeside(in Mover m, in Locomotion loco, in LadderClipMove at, LadderClip c, in Ladder ladder, LedgePaths ledges, out Vector3 lip, out float top)
    {
        var left = LeftOf(at.In);
        var lipNormal = new Vector3(-at.In.X, -at.In.Y, 0f);
        var edge = new Vector3(at.Place.X + at.In.X * c.EdgeFwd, at.Place.Y + at.In.Y * c.EdgeFwd, at.Place.Z + c.EdgeUp);
        lip = new Vector3(edge.X + left.X * c.EdgeLeft, edge.Y + left.Y * c.EdgeLeft, edge.Z);
        top = edge.Z;
        // The lip's real top under the hang's middle (a ray down just inside the edge).
        var inside = new Vector3(lip.X + at.In.X * 0.04f, lip.Y + at.In.Y * 0.04f, edge.Z + LandingTolerance + 0.05f);
        if (m.Level.Ray(inside, -Vector3.UnitZ, 2f * LandingTolerance + 0.05f) is not { } hit || hit.Normal.Z < 0.7f) return false;
        top = hit.Point.Z;
        lip.Z = top;
        foreach (float h in c.HandsLeft)
            if (!LipGoesOn(m.Level, new Vector3(edge.X + left.X * h, edge.Y + left.Y * h, top), top, lipNormal)) return false;
        var place = HangPlace(ledges, lip, lipNormal, top);
        return HangFits(m.Level, loco, place, lip, lipNormal) && WallUnder(m.Level, ledges, place, at.In);
    }

    /// <summary>
    /// <b>back onto a ladder from a braced hang</b> (the upright set): hanging braced (in the hang's loop), the wish to the side a ladder stands
    /// on, its ladder position where the clip from the hang starts within <see cref="HangAside"/> along the edge and the tolerance up and across: its clip, the
    /// hang's difference from the clip's start taken out over its first hold. False (the shimmy's) else.
    /// </summary>
    static bool TryFromHang(in Mover m, ref Body body, ref Traversal move, ref LedgeMove ledge, LedgePaths ledges, ref LadderClipMove climb, LadderPaths paths,
                            ref Vector3 feet, in Locomotion loco, in MoveIntent intent, MovesRules tier, ref MotorOutcome o)
    {
        if (ledges.At(ledge.Clip - 1) is not { Role: LedgeRole.Hang, Braced: true }) return false;
        int way = SideWish(intent, move.LipNormal);   // +1 to the left of the body facing the wall
        if (way == 0) return false;
        var ladders = m.Level.Ladders;
        var hangLip = new Vector3(ledge.Place.X + ledge.In.X * ledges.EdgeFwd, ledge.Place.Y + ledge.In.Y * ledges.EdgeFwd, ledge.Top);
        int best = -1, bestRung = 0;
        float bestD = float.PositiveInfinity;
        LadderClip? clip = null;
        for (int i = 0; i < ladders.Count; i++)
        {
            ref readonly var l = ref ladders[i];
            if (SetOf(l, paths, tier, out int setNo) is not { Vertical: true } s || setNo != 1) continue;
            var inward = InOf(l);
            if (Vector2.Dot(inward, ledge.In) < 0.98f) continue;
            // The ledge is to the side the clip comes from: a wish to the left goes from a ledge on the ladder's right.
            if (s.FromHang[LadderSet.SideIndex(-way)] is not { } c || float.IsNaN(c.EdgeUp)) continue;
            int j = RungFor(l, s, ledge.Top, c.EdgeUp, out float zOff);
            int topJ = TopPosition(l, s, out _);
            if (MathF.Abs(zOff) > LandingTolerance || (topJ > 0 && j > topJ)) continue;
            var at = new LadderClipMove { Place = LadderPosition(l, s, j), In = inward };
            var left = LeftOf(inward);
            var expect = new Vector3(at.Place.X + inward.X * c.EdgeFwd + left.X * c.EdgeLeft, at.Place.Y + inward.Y * c.EdgeFwd + left.Y * c.EdgeLeft, at.Place.Z + c.EdgeUp);
            var d = new Vector2(hangLip.X - expect.X, hangLip.Y - expect.Y);
            float fwd = Vector2.Dot(d, inward), along = Vector2.Dot(d, left);
            if (MathF.Abs(fwd) > LandingTolerance || MathF.Abs(along) > HangAside) continue;
            float dd = MathF.Abs(along) + MathF.Abs(fwd);
            if (dd < bestD) (best, bestRung, bestD, clip) = (i, j, dd, c);
        }
        if (best < 0 || clip is null) return false;
        ref readonly var ladder = ref ladders[best];
        var set = SetOf(ladder, paths, tier, out int no)!;
        var next = new LadderClipMove { Clip = paths.IndexOf(clip) + 1, Set = no, Rung = bestRung, Place = LadderPosition(ladder, set, bestRung), In = InOf(ladder),
                                        Rate = tier.LadderMoveRate, Fade = 1, Way = -way };   // at ladderMoveRate
        next.Delta = feet - LadderRaw(next, clip, 0f);
        next.BlendTo = Math.Clamp(clip.FirstContact / next.Rate, LadderDeltaLeast, LadderDeltaMost);
        StopShimmy(ref body, move, feet, ShimmyStop.Left, ref o);
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        ledge = default;
        move = LadderTraversal(ladder, set, TraversalKind.LadderOn, feet, clip.Duration / next.Rate);
        climb = next;
        o.MoveStarted = TraversalKind.LadderOn;
        o.LadderOn = LadderEnd.Hang;
        o.LadderIndex = best;
        o.LadderFeet = feet;
        body.State = BodyState.Traversing;
        body.Ladder = best + 1;
        LadderClipStep(ref body, ref climb, clip, ref feet, first: true);
        return true;
    }
}
