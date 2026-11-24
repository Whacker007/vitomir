// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>Ladders, and the hang's shimmy and push-off</b> (ours — 1.1 has neither;
/// <i>"hanging: shimmy left and right, push off backwards; ladders (use at the foot, forward
/// and back to climb, jump or crouch to leave)"</i>).
///
/// <list type="bullet">
/// <item><b>On</b>: <b>use</b> pressed standing at a ladder's foot — on its climbing side, facing it (within 60°), the
/// capsule within <see cref="LadderUseReach"/> of its face and <see cref="LadderUseAside"/> past its sides, the feet
/// within <see cref="LadderUseHeight"/> of the foot's height — moves the body in <see cref="LadderOnSeconds"/> to the
/// ladder's line, facing it (<see cref="TraversalKind.LadderOn"/>). Use standing on the top floor at the top — behind
/// its face, within <see cref="LadderUseReach"/> of the edge, facing the drop or away from it — moves it out over the
/// edge and down onto the ladder's top in <see cref="LadderFromTopSeconds"/>. The nearest ladder that qualifies is taken
/// (the lower index on a tie); a way that is not clear takes none.</item>
/// <item><b>On it</b> (<see cref="BodyState.Ladder"/>) the feet are held to a line <see cref="LadderStandOff"/> plus the
/// radius out from the face, from the foot's height to <see cref="LadderOverStep"/> under the top. <b>Forward</b> (the
/// wish's forward part as the view faces — <see cref="MoveIntent.Facing"/>, the player's view — more than
/// <see cref="ClimbCone"/>, so W climbs wherever the camera looks) climbs at <see cref="Locomotion.ClimbUpSpeed"/>,
/// <b>back</b> goes down at <see cref="Locomotion.ClimbDownSpeed"/>, each times that part; something in the way
/// stops it. The body turns to face the ladder; the camera works as ever; nothing else moves it.</item>
/// <item><b>Off</b>: forward at the top carries the body over onto the top floor (a pull-up, when there is floor and
/// room); back at the foot sets it down on the ground; <b>jump</b> pushes off backwards — <see cref="PushOffSpeed"/>
/// away and a small rise of <see cref="PushOffHeight"/>, the hands shown letting go for a moment (no lip is taken in
/// it); <b>crouch</b> (pressed) or <b>cancel</b> lets go and it falls.</item>
/// <item><b>The shimmy</b>: hanging from a lip, the wish's sideways part (more than <see cref="ShimmyCone"/>) moves the
/// body along the lip at <see cref="Locomotion.ShimmySpeed"/> times it, while the lip goes on under the leading hand
/// (<see cref="ShimmyHand"/> ahead: a level top at the lip's height with an upright face under it, not marked not to be
/// climbed) and the hanging body has room; it stops at the lip's end or a corner (corners are not turned).
/// <b>Jump with back held</b> pushes off backwards as from a ladder; jump alone or forward pulls up, crouch or cancel
/// lets go, as before.</item>
/// </list>
/// Route-followers never use ladders: only the player's control sets <see cref="MoveIntent.Use"/> (links come later).
/// </summary>
public static partial class BodyMotor
{
    /// <summary>How far from a ladder's face (from the capsule at the foot; from the middle at the top) use reaches (m).</summary>
    public const float LadderUseReach = 0.5f;

    /// <summary>How far past a ladder's sides the body's middle may be for use to reach it (m).</summary>
    public const float LadderUseAside = 0.3f;

    /// <summary>How far above or below a ladder's foot (or top) the feet may be for use to reach it (m).</summary>
    public const float LadderUseHeight = 0.5f;

    /// <summary>The least share of the facing towards a ladder (at the foot) or along its normal (at the top) for use: the cosine of 60°.</summary>
    public const float LadderUseCone = 0.5f;

    /// <summary>How far the capsule on a ladder is kept off its face (m).</summary>
    public const float LadderStandOff = 0.05f;

    /// <summary>How far under a ladder's top the feet are at the top of the climb, where forward carries the body over (m).</summary>
    public const float LadderOverStep = 1.0f;

    /// <summary>How high above the feet the hands hold a ladder's face (m; never above its top).</summary>
    public const float LadderHands = 1.6f;

    /// <summary>How long getting onto a ladder takes from its foot, and from its top (s).</summary>
    public const float LadderOnSeconds = 0.3f, LadderFromTopSeconds = 0.6f;

    /// <summary>How far over the top floor the feet are lifted going out onto a ladder from above (m).</summary>
    public const float LadderLift = 0.05f;

    /// <summary>How far onto the top floor past the face the feet go over a ladder's top, beyond the radius (m).</summary>
    public const float LadderOverInto = 0.2f;

    /// <summary>The least forward (or back) part of the wish, as the view faces, that climbs (or goes down).</summary>
    public const float ClimbCone = 0.3f;

    /// <summary>A push-off's speed away from the ladder or the wall (m/s) and how high it lifts the feet (m).</summary>
    public const float PushOffSpeed = 2.5f, PushOffHeight = 0.3f;

    /// <summary>The least sideways part of the wish that shimmies.</summary>
    public const float ShimmyCone = 0.3f;

    /// <summary>How far ahead of the lip's point the leading hand is: the lip must go on to there (m).</summary>
    public const float ShimmyHand = 0.2f;

    // ---------------------------------------------------------------- getting on

    /// <summary>Use on the ground: onto the nearest ladder whose foot or top the body is at; false when there is none.</summary>
    static bool TryUseLadder(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        var ladders = m.Level.Ladders;
        if (ladders.Count == 0) return false;
        var face = Facing(body.Yaw);
        int best = -1;
        bool bestTop = false;
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
            {
                best = i; bestTop = false; bestD = d.Length();
            }
            d = new Vector2(feet.X - l.Top.X, feet.Y - l.Top.Y);
            off = Vector2.Dot(d, outv);
            if (MathF.Abs(feet.Z - l.Top.Z) <= LadderUseHeight && off <= 0.05f && off >= -(m.R + LadderUseReach)
                && MathF.Abs(Vector2.Dot(d, side)) <= aside && MathF.Abs(Vector2.Dot(face, outv)) >= LadderUseCone && d.Length() < bestD)
            {
                best = i; bestTop = true; bestD = d.Length();
            }
        }
        if (best < 0) return false;
        ref readonly var ladder = ref ladders[best];
        var (lo, hi) = ClimbRange(ladder);
        float z = bestTop ? hi : Math.Clamp(feet.Z, lo, hi);
        var to = OnLine(ladder, z, m.R);
        if (!Fits(m.Level, to, loco.Radius, loco.Height)) return false;
        var next = new Traversal
        {
            Kind = TraversalKind.LadderOn, Seconds = bestTop ? LadderFromTopSeconds : LadderOnSeconds,
            From = feet, To = to, Lip = HandsAt(ladder, z), LipNormal = ladder.Out, Height = ladder.Rise,
        };
        var tucked = new Mover(m.Level, m.Others, m.Self, loco, TuckedHeight(loco));
        if (!PathClear(tucked, next)) return false;
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = next;
        o.MoveStarted = TraversalKind.LadderOn;
        o.LadderOn = bestTop ? LadderEnd.Top : LadderEnd.Foot;
        o.LadderIndex = best;
        o.LadderFeet = feet;
        body.State = BodyState.Traversing;
        body.Ladder = best + 1;
        body.Crouched = 0; body.Stoop = 0f;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.Rising = 0; body.Jumped = 0; body.Charging = 0; body.Powered = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1; body.GroundSeconds = 0f;
        Advance(tucked, ref body, ref move, ref feet, loco, intent, ref o);
        return true;
    }

    /// <summary>The getting-on ended: the body is on the ladder, its feet on the line at the height they reached.</summary>
    static void BeginLadder(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, ref MotorOutcome o)
    {
        int index = body.Ladder - 1;
        if (index < 0 || index >= m.Level.Ladders.Count)
        {
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        ref readonly var l = ref m.Level.Ladders[index];
        var (lo, hi) = ClimbRange(l);
        float s = hi > lo + 1e-5f ? Math.Clamp((feet.Z - lo) / (hi - lo), 0f, 1f) : 0f;
        var from = OnLine(l, lo, m.R);
        var to = OnLine(l, hi, m.R);
        move = new Traversal
        {
            Kind = TraversalKind.Ladder, Progress = s, Seconds = LadderSeconds(from, to, loco),
            From = from, To = to, Lip = HandsAt(l, feet.Z), LipNormal = l.Out, Height = l.Rise,
        };
        feet = Vector3.Lerp(from, to, s);
        o.MoveStarted = TraversalKind.Ladder;
        body.State = BodyState.Ladder;
        body.Velocity = Vector3.Zero;
        body.Crouched = 0;
        body.AirSeconds = 0f; body.GroundSeconds = 0f;
    }

    // ---------------------------------------------------------------- on it

    /// <summary>One tick on a ladder: let go, push off, climb, go down, over the top or off at the foot.</summary>
    static void LadderTick(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        int index = body.Ladder - 1;
        if (index < 0 || index >= m.Level.Ladders.Count)
        {
            o.LadderOff = LadderLeave.Stopped; o.LadderIndex = index; o.LadderFeet = feet;
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        ref readonly var l = ref m.Level.Ladders[index];
        feet = Vector3.Lerp(move.From, move.To, move.Progress);
        body.Velocity = Vector3.Zero;
        body.AirSeconds = 0f; body.GroundSeconds = 0f;
        var inward = new Vector2(-l.Out.X, -l.Out.Y);
        body.Yaw = DetMath.WrapAngle(TurnToward(body.Yaw, DetMath.Atan2(inward.Y, inward.X), loco.TurnDegreesPerSecond * DetMath.DegToRad * dt));

        if (intent.Cancel || (intent.Crouch && body.CrouchWas == 0))
        {
            o.Cancelled = intent.Cancel;
            LeaveLadder(ref body, index, feet, LadderLeave.LetGo, ref o);
            LetGo(m, ref body, ref move, feet, ref o);
            o.Dropped = false;
            return;
        }
        if (intent.Jump)
        {
            LeaveLadder(ref body, index, feet, LadderLeave.PushOff, ref o);
            PushOff(m, ref body, ref move, feet, loco, ref o);
            return;
        }

        // Forward and back are the stick's, as the view faces (the intent's facing: the player's view), not the world's:
        // forward climbs whichever way the camera looks at the ladder.
        var (fs, fc) = DetMath.SinCos(intent.Facing);
        float heading = Vector2.Dot(intent.Direction, new Vector2(fc, fs));
        float pace = MathF.Min(1f, MathF.Abs(heading));
        float len = Vector3.Distance(move.From, move.To);
        float s = move.Progress;
        if (heading > ClimbCone)
        {
            if (s >= 1f - 1e-5f)
            {
                if (TryOverTop(m, ref body, ref move, ref feet, loco, intent, l, index, ref o)) return;
            }
            else s = len > 1e-5f ? MathF.Min(1f, s + loco.ClimbUpSpeed * pace * dt / len) : 1f;
        }
        else if (heading < -ClimbCone)
        {
            if (s <= 1e-5f)
            {
                LeaveLadder(ref body, index, feet, LadderLeave.Foot, ref o);
                o.MoveEnded = TraversalKind.Ladder; o.MoveCompleted = true;
                move = default;
                SetDown(m, ref body, ref feet, loco, intent, Vector3.Zero);
                return;
            }
            s = len > 1e-5f ? MathF.Max(0f, s - loco.ClimbDownSpeed * pace * dt / len) : 0f;
        }
        var next = Vector3.Lerp(move.From, move.To, s);
        var d = next - feet;
        float dl = d.Length();
        if (dl > 1e-7f && !(Sweep(m, feet, d / dl, dl + Skin, out var hit) && hit.Distance < dl - 1e-4f))
        {
            body.Velocity = d / dt;
            feet = next;
            move.Progress = s;
        }
        move.Lip = HandsAt(l, feet.Z);
        move.Seconds = LadderSeconds(move.From, move.To, loco);
    }

    /// <summary>Forward at the top: over onto the top floor as a pull-up, when there is floor there (not marked not to be stood on) and room; false (still on the ladder) else.</summary>
    static bool TryOverTop(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent,
                           in Ladder l, int index, ref MotorOutcome o)
    {
        var onto = l.Top - l.Out * (m.R + LadderOverInto);
        if (m.Level.Ray(onto + new Vector3(0f, 0f, 0.5f), -Vector3.UnitZ, 1f) is not { } under || under.Normal.Z < m.MinN
            || m.Level.NoStand(under.Triangle)) return false;   // no floor, too steep, or marked not to be stood on
        onto.Z = under.Point.Z;
        if (!Fits(m.Level, onto, loco.Radius, TuckedHeight(loco))) return false;
        var next = new Traversal
        {
            Kind = TraversalKind.PullUp, Seconds = MathF.Max(loco.PullUpSeconds, World.TickSeconds),
            From = feet, To = onto, Lip = l.Top, LipNormal = l.Out, Height = l.Top.Z - feet.Z,
        };
        var tucked = new Mover(m.Level, m.Others, m.Self, loco, TuckedHeight(loco));
        if (!PathClear(tucked, next)) return false;
        LeaveLadder(ref body, index, feet, LadderLeave.Top, ref o);
        o.MoveEnded = TraversalKind.Ladder; o.MoveCompleted = true;
        move = next;
        o.MoveStarted = TraversalKind.PullUp;
        body.State = BodyState.Traversing;
        Advance(tucked, ref body, ref move, ref feet, loco, intent, ref o);
        return true;
    }

    /// <summary>The body leaves its ladder: said in the outcome, and the body no longer on one.</summary>
    static void LeaveLadder(ref Body body, int index, Vector3 feet, LadderLeave how, ref MotorOutcome o)
    {
        o.LadderOff = how;
        o.LadderIndex = index;
        o.LadderFeet = feet;
        body.Ladder = 0;
    }

    /// <summary>
    /// A push-off backwards from a ladder or a lip: away along the move's <see cref="Traversal.LipNormal"/> at
    /// <see cref="PushOffSpeed"/> with a small rise, showing the hands letting go (a drop) for its moment, in which no lip
    /// is taken.
    /// </summary>
    static void PushOff(in Mover m, ref Body body, ref Traversal move, Vector3 feet, in Locomotion loco, ref MotorOutcome o)
    {
        var n = move.LipNormal;
        LetGo(m, ref body, ref move, feet, ref o);
        o.Dropped = false;
        body.Velocity = new Vector3(n.X * PushOffSpeed, n.Y * PushOffSpeed, Locomotion.ImpulseFor(PushOffHeight, loco.Gravity));
    }

    // ---------------------------------------------------------------- the hang's shimmy and push-off

    /// <summary>Whether the wish heads out from a lip whose face's outward normal is <paramref name="lipNormal"/>: back, to push off.</summary>
    static bool BackFrom(in MoveIntent intent, Vector3 lipNormal)
    {
        float wl = intent.Direction.Length();
        return wl > 0.3f && Vector2.Dot(intent.Direction / wl, new Vector2(lipNormal.X, lipNormal.Y)) > 0.5f;
    }

    /// <summary>Hanging, jump with back held: off the wall backwards.</summary>
    static void PushOffLip(in Mover m, ref Body body, ref Traversal move, Vector3 feet, in Locomotion loco, ref MotorOutcome o)
    {
        StopShimmy(ref body, move, feet, ShimmyStop.Left, ref o);
        o.PushedOffLip = true;
        o.GripLip = move.Lip;
        o.GripFeet = feet;
        PushOff(m, ref body, ref move, feet, loco, ref o);
    }

    /// <summary>
    /// Hanging: the wish's sideways part moves the body along the lip, while the lip goes on under the leading hand and
    /// the hanging body has room; the start and the stop told once each (<see cref="Body.Shimmy"/>).
    /// </summary>
    static void Shimmy(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        var n = move.LipNormal;
        var side = new Vector2(-n.Y, n.X);   // to the right of a body facing the wall
        float lateral = intent.Direction.LengthSquared() > 0.01f ? Vector2.Dot(intent.Direction, side) : 0f;
        int way = lateral > ShimmyCone ? 1 : lateral < -ShimmyCone ? -1 : 0;
        if (way == 0 || side.LengthSquared() < 0.5f || loco.ShimmySpeed <= 0f)
        {
            StopShimmy(ref body, move, feet, ShimmyStop.Released, ref o);
            return;
        }
        var along = new Vector3(side * way, 0f);
        float step = loco.ShimmySpeed * MathF.Min(1f, MathF.Abs(lateral)) * dt;
        var lip = move.Lip + along * step;
        if (!LipGoesOn(m.Level, lip + along * ShimmyHand, move.Lip.Z, n)
            || (Sweep(m, feet, along, step + Skin, out var hit) && hit.Distance < step - 1e-4f))
        {
            StopShimmy(ref body, move, feet, ShimmyStop.End, ref o);
            return;
        }
        if (body.Shimmy != way)
        {
            if (body.Shimmy != 0) StopShimmy(ref body, move, feet, ShimmyStop.Released, ref o);
            o.ShimmyStarted = way;
            o.ShimmyLip = move.Lip;
            o.ShimmyFeet = feet;
            body.Shimmy = way;
        }
        feet += along * step;
        move.Lip = lip;
        move.From = feet; move.To = feet;
        body.Velocity = along * (step / dt);
    }

    /// <summary>A shimmy under way stops (told once, with why); nothing when the body was still.</summary>
    static void StopShimmy(ref Body body, in Traversal move, Vector3 feet, ShimmyStop why, ref MotorOutcome o)
    {
        if (body.Shimmy == 0) return;
        body.Shimmy = 0;
        o.ShimmyStopped = why;
        o.ShimmyLip = move.Lip;
        o.ShimmyFeet = feet;
    }

    /// <summary>
    /// Whether a lip goes on at <paramref name="p"/>: a level top at about <paramref name="z"/> just inside the face (not
    /// marked not to be climbed), and an upright face looking out along <paramref name="n"/> under it, in the face's plane.
    /// </summary>
    static bool LipGoesOn(CollisionWorld level, Vector3 p, float z, Vector3 n)
    {
        var inside = new Vector3(p.X - n.X * 0.04f, p.Y - n.Y * 0.04f, z + 0.15f);
        if (level.Ray(inside, -Vector3.UnitZ, 0.3f) is not { } top || top.Normal.Z < 0.7f || MathF.Abs(top.Point.Z - z) > 0.08f) return false;
        if (level.NoClimb(top.Triangle, top.Point)) return false;
        var outside = new Vector3(p.X + n.X * 0.25f, p.Y + n.Y * 0.25f, z - 0.08f);
        if (level.Ray(outside, new Vector3(-n.X, -n.Y, 0f), 0.4f) is not { } face) return false;
        return face.Normal.X * n.X + face.Normal.Y * n.Y >= 0.7f && MathF.Abs(face.Distance - 0.25f) <= 0.1f;
    }

    // ---------------------------------------------------------------- the ladder's line

    /// <summary>The feet's lowest and highest heights on a ladder: its foot, and <see cref="LadderOverStep"/> under its top (never under the foot).</summary>
    static (float Lo, float Hi) ClimbRange(in Ladder l) => (l.Foot.Z, MathF.Max(l.Foot.Z, l.Top.Z - LadderOverStep));

    /// <summary>Where the feet are on a ladder at height <paramref name="z"/>: its face's line, out by the radius and <see cref="LadderStandOff"/>.</summary>
    static Vector3 OnLine(in Ladder l, float z, float radius) => l.FaceAt(z) + l.Out * (radius + LadderStandOff);

    /// <summary>Where the hands hold a ladder with the feet at <paramref name="feetZ"/>: its face's line <see cref="LadderHands"/> higher, never over its top.</summary>
    static Vector3 HandsAt(in Ladder l, float feetZ) => l.FaceAt(MathF.Min(feetZ + LadderHands, l.Top.Z));

    /// <summary>How long climbing the whole ladder up takes (s; the traversal's length).</summary>
    static float LadderSeconds(Vector3 from, Vector3 to, in Locomotion loco) =>
        loco.ClimbUpSpeed > 1e-3f ? MathF.Max(Vector3.Distance(from, to) / loco.ClimbUpSpeed, World.TickSeconds) : float.PositiveInfinity;
}
