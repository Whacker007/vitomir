// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips (CONVENTIONS,
// Notes, traverse.wall, traverse.corner, turn_deg, root_turns): climbing walls by their holds, and faces with none out of the masquerade
//. faster and flowing, with diagonals — wallClimbRate and wallMoveRate, a held move
// out of the getting-on and out of the move before with no rest at the idle, over the top in one go, diagonals, the catch from a run softened.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>climbing walls</b> on the models repository's clips (<see cref="WallRules"/>; the player's, riding on the hang's corners and leaps). The capsule follows
/// the clips' paths laid from <b>the wall position</b> (the capsule where the body is in <c>wall_idle</c>, its <c>face_fwd_m</c> back from the face), carried by
/// the ledge's state (<see cref="LedgeMove"/>: its clip, time, <see cref="LedgeMove.Place"/> the wall position and <see cref="LedgeMove.In"/> the way in) and
/// the wall's (<see cref="WallMove"/>).
/// <list type="bullet">
/// <item><b>What can be climbed</b>: a face in a level's area marked <c>"climb": "holds"</c> (<see cref="LevelClimb.HoldsAt"/>) by anyone,
/// masquerade or not; any other upright face — not marked not to be climbed, not a ladder's — only out of the masquerade or in a free place (the caller says
/// which: <c>sheer</c>), with the splayed grip. A face must stand under every hand and foot of the idle where the body is to be (rays in at each hold).</item>
/// <item><b>The holds</b>: the level's own where its area gives them (<see cref="LevelClimb.HoldNear"/>, within <see cref="WallRules.HoldReach"/> of each of the
/// clip's: a move whose end has a limb with none is refused; the people's IK moves each limb onto its hold), else <b>a lattice at the clips' spacing</b>: the
/// wall positions a step (<c>step_m</c>, 0.4 m) apart along the face from the level's own origin (its coordinate along the face, rounded to the step) and up from
/// the ground at its foot, the holds where the clips' own are at each — so nothing is left for an IK to do.</item>
/// <item><b>Getting on</b>: a tap of jump at a climbable face whose top is out of the grabs' bands (a ledge grab wins where a lip is within them; a shorter
/// wall is vaulted, mantled or grabbed as before): standing, <c>wall_on_ground</c>; with a run-up, <c>wall_on_jump</c>; the start's difference taken out by the
/// hands' first hold (from the take-off to the contact for the jump). In the air, jumped at it (or use held), <c>wall_on_jump</c> from <see cref="WallCatchLead"/> before its hands' contact.</item>
/// <item><b>Climbing</b>: the move keys as the body faces the wall (forward up, back down, the sides across): the quick two-beat moves by default, the careful
/// four-beat with walk held, the long ones (0.8 m) with sprint held where the holds and the room are there for them; chained with no fade while the wish holds;
/// a move refused is said once a place. At the top, where its edge is within half a step of the clip's own, up goes over it (<c>wall_to_hang_braced</c>) into
/// the braced hang and, held, straight on into the pull-up; at the foot, down gets off (<c>wall_off_ground</c>). Corners (outside and inside) by the side's wish,
/// as the hang's (the earlier build's tolerances).</item>
/// <item><b>Off</b>: cancel or crouch lets go (<c>wall_let_go</c>), jump with back held pushes off (<c>wall_push_off</c>): their paths, then the fall (the earlier build's catch
/// with use held), no wall caught again for <see cref="WallAgain"/>.</item>
/// <item><b>an earlier build</b> (<see cref="WallRules.Flows"/>; the state beside it <see cref="WallFlow"/>): the moves at <see cref="WallRules.Rate"/>, the rest at
/// <see cref="WallRules.MoveRate"/>; the flow (<see cref="FlowOutOfOn"/>, <see cref="EndMove"/>, <see cref="FlowOverTop"/>); diagonals
/// (<see cref="TryWallDiagonal"/>); a velocity carried into a clip (<see cref="CarryInto"/>).</item>
/// </list>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>How far (beyond the clip's own) a standing tap and a running one look for a wall to get on (m).</summary>
    public const float WallStandReach = 0.35f, WallRunReach = 0.6f;

    /// <summary>How long after leaving a wall into the air before one is caught again (s).</summary>
    public const float WallAgain = 0.4f;

    /// <summary>How long before <c>wall_on_jump</c>'s hands' contact a wall caught in the air enters it (s): the body's way laid onto its path by then.</summary>
    public const float WallCatchLead = 0.2f;

    /// <summary>How far a face may stand off its expected place at a hold (m) and still be the wall's.</summary>
    const float WallPlane = 0.12f;

    /// <summary>How far the wish must point a way (its share) for a move on the wall that way.</summary>
    const float WallCone = 0.5f;

    /// <summary>How far the wish must point each way (its share of the wish, sin 22.5°) for a diagonal: forward or back with a side.</summary>
    public const float WallDiagCone = 0.38f;

    /// <summary>Horizon's: how long (ticks) a wish let go is held on — a chain's next move, a diagonal pressed one key after the other.</summary>
    public const int InputHoldTicks = 12;

    /// <summary>Uncharted 4's: how far the leading hand reaches toward a wish whose move is refused (m), and how much of the way it goes a tick.</summary>
    public const float LeanReach = 0.06f, LeanRate = 0.15f;

    /// <summary>How far from the wall position a getting-on's capsule may still be (its path's way left, m) when a move flows out of it.</summary>
    const float FlowNear = 0.08f;

    /// <summary>The limbs that move first in a wall clip: those whose start hold is let go within this long (s of the clip) of the first.</summary>
    const float FirstBeat = 0.12f;

    /// <summary>A catch from the air's capsule difference (and the velocity it comes in with) taken out over this many times its time to the hands' contact.</summary>
    const float CatchSpread = 1.5f;

    /// <summary>The share of the side's move's own pose a diagonal's body takes from the up's or the down's (its spine, hips and head; the limbs are the side's).</summary>
    public const float DiagShare = 0.5f;

    /// <summary>The rate a getting-on plays at (the climb's).</summary>
    static float OnRate(WallRules wr) => wr.Flows ? wr.MoveRate : wr.Rate;

    /// <summary>The rate a getting-off, a drop, the clip over the top and a corner play at (their own).</summary>
    static float MovingRate(WallRules wr) => wr.Flows ? wr.MoveRate : 1f;

    // ---------------------------------------------------------------- the face

    /// <summary>
    /// The face under a point <paramref name="p"/> on the face's expected plane (the way in <paramref name="inward"/>): a level ray in at it from 0.3 m out meets an
    /// upright face looking back within about 25°, within <see cref="WallPlane"/> of the plane.
    /// </summary>
    static bool FaceAt(CollisionWorld level, Vector3 p, Vector2 inward, out CollisionHit face)
    {
        face = default;
        var d = new Vector3(inward, 0f);
        if (level.Ray(p - d * 0.3f, d, 0.3f + WallPlane) is not { } hit || hit.Distance < 0.3f - WallPlane) return false;
        if (MathF.Abs(hit.Normal.Z) > 0.35f || hit.Normal.X * -inward.X + hit.Normal.Y * -inward.Y < 0.9f) return false;
        face = hit;
        return true;
    }

    /// <summary>The point on the face's plane of a wall position <paramref name="place"/>: <paramref name="left"/> to its left and <paramref name="up"/> over its capsule's bottom.</summary>
    static Vector3 OnFace(Vector3 place, Vector2 inward, float faceFwd, float left, float up)
    {
        var l = LeftOf(inward);
        return new Vector3(place.X + inward.X * faceFwd + l.X * left, place.Y + inward.Y * faceFwd + l.Y * left, place.Z + up);
    }

    /// <summary>
    /// Whether the body can be on the wall at <paramref name="place"/>: a face under each hold of the idle (a little under its top), every one climbable — in an
    /// area of walls with holds (the level's own hold within reach where it gives them), or another upright face when <paramref name="sheer"/> allows it, never one
    /// marked not to be climbed — and the capsule fits there. <paramref name="sheerHere"/>: no hold is in an area of walls with holds; <paramref name="given"/>: one is
    /// in an area that gives its holds.
    /// </summary>
    static WallRefusal WallFits(CollisionWorld level, in Locomotion loco, Vector3 place, Vector2 inward, LedgeClip idle, WallRules wr, bool sheer, out bool sheerHere, out bool given)
    {
        sheerHere = true;
        given = false;
        foreach (var h in idle.Holds)
        {
            var p = OnFace(place, inward, idle.WallFace, h.Left, h.Up - 0.03f);
            if (!FaceAt(level, p, inward, out var face)) return WallRefusal.NoHolds;
            if (level.NoClimb(face.Triangle, face.Point)) return WallRefusal.NoClimb;
            if (level.Climb.HoldsAt(face.Point) is { } area)
            {
                sheerHere = false;
                if (area.Holds.Length > 0)
                {
                    given = true;
                    if (!level.Climb.HoldNear(OnFace(place, inward, idle.WallFace, h.Left, h.Up), wr.HoldReach, out _)) return WallRefusal.NoHolds;
                }
            }
            else if (!sheer) return WallRefusal.Sheer;
        }
        if (!Fits(level, place + new Vector3(0f, 0f, Skin), loco.Radius, loco.Height)) return WallRefusal.NoRoom;
        return WallRefusal.None;
    }

    /// <summary>The lattice's step (m): the quick move up's (0.4).</summary>
    static float WallStep(LedgePaths paths) => paths.WallMoveOf(0, 1, quick: true) is { } up && up.StepUp > 0.05f ? up.StepUp : 0.4f;

    /// <summary>
    /// The wall position on a face (its point <paramref name="face"/>, the way in <paramref name="inward"/>) at height <paramref name="z"/>: the idle's
    /// <c>face_fwd_m</c> back from it, along it at the lattice's (the level's coordinate along the face rounded to the step).
    /// </summary>
    static Vector3 WallPlace(Vector3 face, Vector2 inward, float faceFwd, float z, float step)
    {
        var left = LeftOf(inward);
        var p = new Vector2(face.X - inward.X * faceFwd, face.Y - inward.Y * faceFwd);
        float s = Vector2.Dot(p, left);
        float q = MathF.Round(s / step) * step;
        p += left * (q - s);
        return new Vector3(p, z);
    }

    /// <summary>A refusal told, once a place (<see cref="WallMove.Told"/>): not again for the same reason within half a step of where it was last told.</summary>
    static void WallRefuse(ref WallMove w, ref MotorOutcome o, WallAct act, int left, int up, WallRefusal why, Vector3 at)
    {
        if (why == WallRefusal.None || (w.Told == why && Vector3.DistanceSquared(w.ToldAt, at) < 0.04f)) return;
        w.Told = why;
        w.ToldAt = at;
        o.WallRefused = why;
        o.WallRefusedAct = act;
        o.WallLeft = left;
        o.WallUp = up;
        o.WallAt = at;
    }

    /// <summary>
    /// A face ahead along <paramref name="way"/> from <paramref name="feet"/> at the height of a body's middle within <paramref name="reach"/> of the capsule's
    /// middle: upright, facing back within 35° of the way; its point and the way into it.
    /// </summary>
    static bool FaceAhead(CollisionWorld level, Vector3 feet, Vector2 way, float reach, out CollisionHit face, out Vector2 inward)
    {
        face = default;
        inward = default;
        if (way.LengthSquared() < 1e-6f) return false;
        if (level.Ray(feet + new Vector3(0f, 0f, 1.0f), new Vector3(way, 0f), reach) is not { } hit || MathF.Abs(hit.Normal.Z) > 0.35f) return false;
        var n = new Vector2(hit.Normal.X, hit.Normal.Y);
        float nl = n.Length();
        if (nl < 1e-4f || Vector2.Dot(-n / nl, way) < 0.82f) return false;
        face = hit;
        inward = -n / nl;
        return true;
    }

    /// <summary>Whether the face goes on up from <paramref name="from"/> to <paramref name="to"/> (world Z) over <paramref name="point"/> (on it): no top within that (a grab's or a vault's).</summary>
    static bool FaceGoesUp(CollisionWorld level, Vector3 point, Vector2 inward, float from, float to)
    {
        for (float z = from; ; z = MathF.Min(z + 0.25f, to))
        {
            if (!FaceAt(level, new Vector3(point.X, point.Y, z), inward, out _)) return false;
            if (z >= to - 1e-4f) return true;
        }
    }

    // ---------------------------------------------------------------- getting on

    /// <summary>The first hold a getting-on's limbs take (s; its length for none).</summary>
    static float FirstHold(LedgeClip c)
    {
        float t = c.Duration;
        foreach (var h in c.Holds) if (h.On >= 0f && h.On < t) t = h.On;
        return t;
    }

    /// <summary>What a tap on the ground would get onto: the clip, the wall position, the way in, the face met, the face's kind.</summary>
    struct WallOnPlan
    {
        public LedgeClip Clip;
        public Vector3 Place;
        public Vector2 In;
        public CollisionHit Face;
        public bool Running, Sheer, Given;
    }

    /// <summary>
    /// Whether a tap on the ground from <paramref name="feet"/> along <paramref name="way"/> (the body moving at <paramref name="velocity"/>) gets onto a wall:
    /// standing, <c>wall_on_ground</c> where the face is within <see cref="WallStandReach"/> of the clip's own; with a run-up, <c>wall_on_jump</c> within
    /// <see cref="WallRunReach"/>; the face going on up past the grabs' bands, climbable there. <paramref name="why"/>: what stopped one at a tall face.
    /// </summary>
    static bool PlanWallOn(CollisionWorld level, Vector3 feet, Vector3 velocity, Vector2 way, in Locomotion loco, LedgePaths paths, WallRules wr, bool sheer,
                           out WallOnPlan plan, out WallRefusal why)
    {
        plan = default;
        why = WallRefusal.None;
        if (paths.Wall(LedgeRole.WallIdle) is not { } idle || paths.WallOn(jump: false) is not { } onGround) return false;
        var across = new Vector2(velocity.X, velocity.Y);
        var onJump = paths.WallOn(jump: true);
        bool running = across.Length() >= VaultRunning && onJump is not null;
        var c = running ? onJump! : onGround;
        float own = c.WallFace - (c.F.Length > 0 ? c.F[0] : 0f);
        if (!FaceAhead(level, feet, way, own + (running ? WallRunReach : WallStandReach), out var face, out var inward)) return false;
        // A face that goes on up past the grabs' bands (and the getting-on's highest hand a step over): a shorter one is the vault's, the climb's, the grab's.
        float hands = 0f;
        foreach (var h in idle.Holds) hands = MathF.Max(hands, h.Up);
        float need = MathF.Max(paths.HighestGrab + 0.3f, -onGround.WallGround + hands + WallStep(paths));
        if (!FaceGoesUp(level, face.Point, inward, feet.Z + 0.3f, feet.Z + need)) return false;
        var place = WallPlace(face.Point, inward, idle.WallFace, feet.Z - c.WallGround, WallStep(paths));
        why = WallFits(level, loco, place, inward, idle, wr, sheer, out bool sheerHere, out bool given);
        plan = new WallOnPlan { Clip = c, Place = place, In = inward, Face = face, Running = running, Sheer = sheerHere, Given = given };
        return why == WallRefusal.None;
    }

    /// <summary>
    /// <b>a press of jump on the ground</b> by a body given the walls (<see cref="BodySystem"/>, before the jump's squash): true when it gets onto a wall (the
    /// squash not played: the getting-on's own dip is its).
    /// </summary>
    public static bool WallPress(in MoveIntent given, in Body body, in Locomotion loco, CollisionWorld level, in LedgeMove ledge, LedgePaths paths, WallRules wr, bool sheer)
    {
        if (!given.Jump || body.State != BodyState.Ground || loco.CanCharge || body.Crouched != 0 || ledge.Clip != 0 || ledge.Pending != 0) return false;
        return PlanWallOn(level, body.Feet, body.Velocity, WayOf(given, body.Yaw), loco, paths, wr, sheer, out _, out _);
    }

    /// <summary>
    /// <b>a tap of jump on the ground at a wall</b> (after the ledge's grabs passed it over; <see cref="PlanWallOn"/>): its getting-on begun, the start's
    /// difference taken out by the hands' first hold (from the take-off to the contact for the jump). False (nothing changed; a refusal said once a place, but a
    /// face with nothing to hold in the masquerade) for none.
    /// </summary>
    /// <remarks>
    /// <see cref="WallRules.Flows"/>: at <see cref="WallRules.MoveRate"/>, the difference's windows in its time; with a run-up the across difference taken out
    /// from the press, the run's velocity carried into it (<see cref="CarryInto"/>), so the run does not stop dead at the press (the earlier build's 27 mm pop).
    /// </remarks>
    static bool TryWallFromGround(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                                  ref Vector3 feet, in Locomotion loco, Vector2 way, ref MotorOutcome o)
    {
        if (!PlanWallOn(m.Level, feet, body.Velocity, way, loco, paths, wr, sheer, out var p, out var why))
        {
            // A face with nothing to hold in the masquerade is every house's: not said (the jump goes on as ever); the rest once a place.
            if (why is not (WallRefusal.None or WallRefusal.Sheer)) WallRefuse(ref w, ref o, p.Running ? WallAct.OnJump : WallAct.OnGround, 0, 0, why, p.Face.Point);
            return false;
        }
        var c = p.Clip;
        var next = new LedgeMove { Clip = paths.IndexOf(c) + 1, Time = 0f, Age = 0f, Place = p.Place, In = p.In, Top = float.NaN, Floor = feet.Z, Fade = 1 };
        next.Delta = feet - LedgeRaw(next, c, 0f);
        float off = c.Jump && !float.IsNaN(c.TakeoffS) ? c.TakeoffS : 0f, on = c.Jump && !float.IsNaN(c.ContactS) ? c.ContactS : FirstHold(c);
        float rate = OnRate(wr);
        var flow = default(WallFlow);
        if (wr.Flows)
        {
            // The windows in the body's time (the clip at its rate); with a run-up the across difference from the press, the run's velocity carried.
            (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (p.Running ? 0f : off / rate, MathF.Max(off, on) / rate, off / rate, MathF.Max(off, on) / rate);
            flow.Carry = CarryInto(next, flow, paths, c, body.Velocity with { Z = 0f }, rate);
        }
        else (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (off, MathF.Max(off, on), off, MathF.Max(off, on));
        var plan = new WallMove { Sheer = p.Sheer ? 1 : 0, Given = p.Given ? 1 : 0, Rate = rate, Told = w.Told, ToldAt = w.ToldAt };
        if (!WallPathClear(m, loco, next, plan, flow, paths, c, feet, 0f)) { WallRefuse(ref w, ref o, p.Running ? WallAct.OnJump : WallAct.OnGround, 0, 0, WallRefusal.Blocked, p.Face.Point); return false; }
        f = flow;
        BeginWallOn(m, ref body, ref move, ref l, paths, c, next, ref w, ref f, plan, ref feet, loco, p.Face, p.In, p.Running ? WallAct.OnJump : WallAct.OnGround, ref o, on: wr.Flows);
        o.JumpAttempted = true;
        o.AttemptOnGround = true;
        return true;
    }

    /// <summary>
    /// <b>a wall caught in the air</b> — jumped at it, or use held — where the body comes to <c>wall_on_jump</c>'s own distance from a climbable face with a
    /// step of it over the hands (a lip nearer is the ledge's catch): the clip from <see cref="WallCatchLead"/> before its hands' contact, at the lattice's row nearest, the body's way to
    /// the wall laid onto its path by the contact.
    /// </summary>
    /// <remarks>
    /// <see cref="WallRules.Flows"/>: at <see cref="WallRules.MoveRate"/> (its lead and the closing in the body's time); the capsule's difference taken out over
    /// <see cref="CatchSpread"/> times its time to the contact with the velocity it comes in at carried into it (<see cref="CarryInto"/>), so a sprint is not
    /// stopped at the wall in a fifth of a second (the earlier build's 51.5 mm pop); the hands on the holds at the contact by the IK (the body still coming in).
    /// </remarks>
    static bool TryWallFromAir(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                               ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        if (body.State != BodyState.Air || w.Again > 0f || (l.Clip == 0 && l.Again > 0f)) return false;
        var vh = new Vector2(body.Velocity.X, body.Velocity.Y);
        bool jumpedAt = body.Jumped != 0 && vh.Length() > 0.5f;
        if (!jumpedAt && !intent.UseHeld) return false;
        if (paths.Wall(LedgeRole.WallIdle) is not { } idle || paths.WallOn(jump: true) is not { } c || float.IsNaN(c.ContactS) || paths.WallOn(jump: false) is not { } onGround) return false;
        var way = vh.Length() > 0.5f ? Vector2.Normalize(vh) : Facing(body.Yaw);
        // Entered a little before its hands' contact (the body's way to the wall then laid onto its path by the contact, so the hands meet the holds there).
        float lead = MathF.Min(WallCatchLead, c.ContactS - (float.IsNaN(c.TakeoffS) ? 0f : c.TakeoffS));
        float entry = c.ContactS - lead;
        float rate = OnRate(wr);
        if (wr.Flows) lead /= rate;   // the time to the contact, the body's
        var (cf, cu, _) = c.At(entry);
        float closing = MathF.Max(0f, Vector2.Dot(vh, way)) * lead;
        if (!FaceAhead(m.Level, feet, way, c.WallFace - cf + MathF.Max(0.15f, closing), out var face, out var inward)) return false;
        if (jumpedAt && !intent.UseHeld && Vector2.Dot(vh, inward) < 0.5f) return false;   // jumped at it, not along it
        if (Vector2.Dot(new Vector2(face.Point.X - feet.X, face.Point.Y - feet.Y), inward) > c.WallFace - cf + MathF.Max(0.15f, Vector2.Dot(vh, inward) * lead)) return false;   // not yet
        float step = WallStep(paths);
        float z = feet.Z + body.Velocity.Z * lead - cu;
        if (TopUnder(m.Level, new Vector2(face.Point.X - inward.X * idle.WallFace, face.Point.Y - inward.Y * idle.WallFace), z + 0.2f, z - 8f, out _) is var g && !float.IsNaN(g))
        {
            float row0 = g - onGround.WallGround;
            z = row0 + step * MathF.Max(0f, MathF.Round((z - row0) / step));
        }
        var place = WallPlace(face.Point, inward, idle.WallFace, z, step);
        // A step of face over the idle's hands (the edge nearer: the ledge's catch) and the idle's holds.
        if (WallFits(m.Level, loco, place + new Vector3(0f, 0f, step), inward, idle, wr, sheer, out _, out _) != WallRefusal.None) return false;
        var why = WallFits(m.Level, loco, place, inward, idle, wr, sheer, out bool sheerHere, out bool given);
        if (why != WallRefusal.None) { if (why != WallRefusal.Sheer) WallRefuse(ref w, ref o, WallAct.Catch, 0, 0, why, face.Point); return false; }
        var next = new LedgeMove { Clip = paths.IndexOf(c) + 1, Time = entry, Age = 0f, Place = place, In = inward, Top = float.NaN, Floor = float.NegativeInfinity, Fade = 1 };
        next.Delta = feet - LedgeRaw(next, c, entry);
        float by = MathF.Max(2f * World.TickSeconds, wr.Flows ? CatchSpread * lead : lead);
        (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (0f, by, 0f, by);
        var flow = default(WallFlow);
        if (wr.Flows) flow.Carry = CarryInto(next, flow, paths, c, body.Velocity, rate);   // the way in carried, not stopped
        var plan = new WallMove { Sheer = sheerHere ? 1 : 0, Given = given ? 1 : 0, Rate = rate, Told = w.Told, ToldAt = w.ToldAt };
        if (!WallPathClear(m, loco, next, plan, flow, paths, c, feet, entry)) return false;
        f = flow;
        BeginWallOn(m, ref body, ref move, ref l, paths, c, next, ref w, ref f, plan, ref feet, loco, face, inward, WallAct.Catch, ref o, on: wr.Flows);
        return true;
    }

    /// <summary>A getting-on begun: the move, the body's state, its first tick.</summary>
    static void BeginWallOn(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, in LedgeMove next, ref WallMove w, ref WallFlow f,
                            in WallMove plan, ref Vector3 feet, in Locomotion loco, in CollisionHit face, Vector2 inward, WallAct act, ref MotorOutcome o, bool on = false)
    {
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = new Traversal
        {
            Kind = TraversalKind.WallOn, Progress = 0f, Seconds = MathF.Max((c.Duration - next.Time) / MathF.Max(1f, plan.Rate), World.TickSeconds), From = feet,
            To = LedgeRaw(next, c, c.Duration), Lip = face.Point, LipNormal = new Vector3(-inward, 0f),
            // The masquerade's measure (MasqueradeRisk): nought on a wall with holds (anyone may climb it), 1 on a face with nothing to hold (no human can).
            Height = plan.Sheer != 0 ? 1f : 0f,
        };
        l = next;
        w = plan;
        o.MoveStarted = TraversalKind.WallOn;
        o.WallStarted = act;
        o.WallSheer = plan.Sheer != 0;
        o.WallFrom = feet;
        o.WallTo = next.Place;
        body.State = BodyState.Traversing;
        body.Stoop = 0f;
        body.Crouched = 0;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.Rising = 0; body.Jumped = 0; body.Charging = 0; body.Powered = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1; body.GroundSeconds = 0f;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(inward.Y, inward.X));
        // On: its first tick a tick along the clip's way, with what the body came with carried, not held where it was for a tick (a run stopped dead).
        WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o, first: !on);
    }

    // ---------------------------------------------------------------- the path

    /// <summary>
    /// Where the feet are in a wall clip at <paramref name="t"/> s of it (<paramref name="age"/> s after it began): its path laid, the start's difference and the end's
    /// taken out; a diagonal's second move's path added (<see cref="DiagOffset"/>) and the velocity it was begun with carried and taken
    /// out with the start's difference (<see cref="CarryOffset"/>).
    /// </summary>
    public static Vector3 WallPoint(in LedgeMove l, in WallMove w, in WallFlow f, LedgePaths paths, LedgeClip c, float t, float age)
    {
        var p = LedgePoint(l, c, t, age) + w.End * Window(t, w.EndFrom, w.EndTo);
        if (f.Diag > 0 && c.Role == LedgeRole.WallMove && paths.At(f.Diag - 1) is { } d) p += DiagOffset(l, c, d, t);
        if (f.Carry != Vector3.Zero) p += CarryOffset(l, f.Carry, age);
        return p;
    }

    /// <summary>A diagonal's second move's (<paramref name="d"/>, the up's or the down's) path at the side's <paramref name="t"/> (their lengths in step), laid along the wall.</summary>
    static Vector3 DiagOffset(in LedgeMove l, LedgeClip c, LedgeClip d, float t)
    {
        var (fw, u, s) = d.At(DiagTime(c, d, t));
        var left = LeftOf(l.In);
        return new Vector3(l.In.X * fw + left.X * s, l.In.Y * fw + left.Y * s, u);
    }

    /// <summary>A diagonal's second move's time at the side's <paramref name="t"/> (the two played in step from the same first frame to the same last).</summary>
    public static float DiagTime(LedgeClip c, LedgeClip d, float t) => c.Duration > 1e-4f ? t * d.Duration / c.Duration : t;

    /// <summary>
    /// The velocity <paramref name="carry"/> a clip was begun with beyond its path's own, carried on and brought to nought with the start's difference (its
    /// across part over the across window, its up part over the up window, each from the clip's start): with the difference's smoothstep, the inertialized join's
    /// cubic (<see cref="Inertial"/>, Holden's), which leaves the capsule's velocity whole at the seam.
    /// </summary>
    static Vector3 CarryOffset(in LedgeMove l, Vector3 carry, float age)
    {
        // The velocity's part of the inertialized join (Inertial: the difference's part is LedgeMove.Delta's smoothstep, Inertial.H0).
        float ta = l.AcrossTo - l.AcrossFrom, tu = l.UpTo - l.UpFrom;
        float a = ta > 1e-5f ? ta * Inertial.H1((age - l.AcrossFrom) / ta) : 0f, u = tu > 1e-5f ? tu * Inertial.H1((age - l.UpFrom) / tu) : 0f;
        return new Vector3(carry.X * a, carry.Y * a, carry.Z * u);
    }

    /// <summary>The clip's path with a diagonal's second move's, at <paramref name="t"/> (no difference taken out): its velocity's reckoning.</summary>
    static Vector3 WallRaw(in LedgeMove l, in WallFlow f, LedgePaths paths, LedgeClip c, float t)
    {
        var p = LedgeRaw(l, c, t);
        if (f.Diag > 0 && c.Role == LedgeRole.WallMove && paths.At(f.Diag - 1) is { } d) p += DiagOffset(l, c, d, t);
        return p;
    }

    /// <summary>
    /// The velocity a clip <paramref name="next"/> (<paramref name="c"/> from its time, at <paramref name="rate"/>) is begun with beyond its path's own, the body
    /// coming at <paramref name="v"/>, to carry (<see cref="CarryOffset"/>): along each window only where it starts with the clip, and no more than takes the body
    /// twice its difference and 10 cm on over the window (so it does not run past where the clip has it).
    /// </summary>
    static Vector3 CarryInto(in LedgeMove next, in WallFlow f, LedgePaths paths, LedgeClip c, Vector3 v, float rate)
    {
        const float h = 1f / 60f;
        var pathV = (WallRaw(next, f, paths, c, MathF.Min(c.Duration, next.Time + h)) - WallRaw(next, f, paths, c, next.Time)) * (rate / h);
        var d = v - pathV;
        float ta = next.AcrossTo - next.AcrossFrom, tu = next.UpTo - next.UpFrom;
        var across = new Vector2(d.X, d.Y);
        if (next.AcrossFrom > 1e-5f || ta <= 1e-5f) across = Vector2.Zero;
        else
        {
            float most = (2f * new Vector2(next.Delta.X, next.Delta.Y).Length() + 0.1f) / ta, len = across.Length();
            if (len > most) across *= most / len;
        }
        float up = d.Z;
        if (next.UpFrom > 1e-5f || tu <= 1e-5f) up = 0f;
        else up = Math.Clamp(up, -(2f * MathF.Abs(next.Delta.Z) + 0.1f) / tu, (2f * MathF.Abs(next.Delta.Z) + 0.1f) / tu);
        return new Vector3(across, up);
    }

    /// <summary>The body's facing at <paramref name="t"/> s of a wall corner: its start's, the clip's turn so far, the end's difference over its window.</summary>
    static float WallYaw(in WallMove w, LedgeClip c, float t) => DetMath.WrapAngle(w.Yaw0 + c.TurnAt(t) + w.YawEnd * Window(t, w.EndFrom, w.EndTo));

    /// <summary>A body of the clip's room (a corner's: its own clearance) moving from <paramref name="from"/> to <paramref name="to"/> meets anything, kept over the floor it starts on.</summary>
    static bool WallSwept(in Mover m, in Locomotion loco, in LedgeMove l, LedgeClip c, Vector3 from, Vector3 to, out float share)
    {
        if (from.Z < l.Floor + Skin) from.Z = l.Floor + Skin;
        if (to.Z < l.Floor + Skin) to.Z = l.Floor + Skin;
        float r = c.Role == LedgeRole.WallCorner && !float.IsNaN(c.Room) ? MathF.Min(m.R, c.Room) : m.R;
        return HangSwept(m, loco, r, loco.Height, from, to, out share);
    }

    /// <summary>Whether a wall clip's path from <paramref name="t0"/> s of it is clear (<see cref="LedgeSamples"/> pieces).</summary>
    static bool WallPathClear(in Mover m, in Locomotion loco, in LedgeMove l, in WallMove w, in WallFlow f, LedgePaths paths, LedgeClip c, Vector3 feet, float t0)
    {
        var prev = feet;
        float rate = w.Rate > 0f ? w.Rate : 1f;
        for (int i = 1; i <= LedgeSamples; i++)
        {
            float t = t0 + (c.Duration - t0) * i / LedgeSamples;
            var p = WallPoint(l, w, f, paths, c, t, l.Age + (t - t0) / rate);
            if (WallSwept(m, loco, l, c, prev, p, out _)) return false;
            prev = p;
        }
        return true;
    }

    /// <summary>A tick of a wall clip: its time at the move's rate (a loop round), the feet on its path (swept: met by something, the body falls from there).</summary>
    static bool WallAdvance(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref WallMove w, ref WallFlow f, ref Vector3 feet,
                            in Locomotion loco, ref MotorOutcome o, bool first = false)
    {
        const float dt = World.TickSeconds;
        if (!first)
        {
            float rate = c.Role == LedgeRole.WallIdle || !(w.Rate > 0f) ? 1f : w.Rate;
            l.Age += dt;
            l.Time += dt * rate;
            if (c.Loops) { if (l.Time >= c.Duration) l.Time -= c.Duration; }
            else if (l.Time > c.Duration - 1e-4f) l.Time = c.Duration;
        }
        var p = WallPoint(l, w, f, paths, c, l.Time, l.Age);
        if (c.Role != LedgeRole.WallIdle && WallSwept(m, loco, l, c, feet, p, out float share))
        {
            feet += (p - feet) * share;
            l = default;
            Leave(ref w, ref f);
            Abort(m, ref body, ref move, ref feet, loco, ref o);
            return false;
        }
        body.Velocity = (p - feet) / dt;
        feet = p;
        if (move.Seconds > 0f && !float.IsInfinity(move.Seconds)) move.Progress = MathF.Min(1f, l.Age / move.Seconds);
        body.Yaw = c.Role == LedgeRole.WallCorner ? WallYaw(w, c, l.Time) : DetMath.WrapAngle(DetMath.Atan2(l.In.Y, l.In.X));
        return true;
    }

    /// <summary>The wall left (into the air, onto the ground, into the hang): its state cleared but for what is told and the moment before another is caught (and the flow's).</summary>
    static void Leave(ref WallMove w, ref WallFlow f, float again = 0f)
    {
        w = new WallMove { Told = w.Told, ToldAt = w.ToldAt, Again = again };
        f = default;
    }

    // ---------------------------------------------------------------- a tick

    /// <summary>
    /// <b>one tick of a body on a wall</b> (a wall clip carries the capsule): getting on, to the idle; on the wall, its controls (the moves, the corners,
    /// over the top, off at the foot, the let-go and the push-off); a move, to its end (the next while the wish holds); off, to the ground or the fall; over the
    /// top, into the braced hang (and on into the pull-up while held). <paramref name="rules"/>: the commit point's (its pull-up's flow), null for none.
    /// </summary>
    /// <remarks>
    /// <see cref="WallRules.Flows"/>, the flow: a move held starts out of the getting-on once the limbs it moves first have their holds (<see cref="FlowOutOfOn"/>),
    /// the next out of a move as its last limb lands (<see cref="EndMove"/>), and over the top with up or jump held the pull-up is crossed in over the clip's last
    /// part (<see cref="FlowOverTop"/>); released, each goes on to its end as before.
    /// </remarks>
    static void WallTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                         ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o, AirCommitRules? rules)
    {
        if (!wr.Flows)
        {
            WallTickOf(m, ref body, ref move, ref l, paths, c, ref w, ref f, wr, sheer, ref feet, loco, intent, intent, ref o, rules);
            return;
        }
        // The wish held on a moment after it is let go (for a chain and a diagonal), and the intent shown at a refused move eased in and out.
        if (intent.Direction.LengthSquared() >= 0.09f) { f.Wish = intent.Direction; f.WishAge = 0; }
        else if (f.WishAge < 1 << 20) f.WishAge++;
        var (wish, age, lean) = (f.Wish, f.WishAge, f.Lean);
        var held = intent;
        if (held.Direction.LengthSquared() < 0.09f && wish != Vector2.Zero && age <= InputHoldTicks) held.Direction = wish;
        f.LeanTo = Vector2.Zero;
        WallTickOf(m, ref body, ref move, ref l, paths, c, ref w, ref f, wr, sheer, ref feet, loco, intent, held, ref o, rules);
        bool on = l.Clip > 0 && paths.At(l.Clip - 1) is { OnWall: true };
        var to = on ? f.LeanTo : Vector2.Zero;
        (f.Wish, f.WishAge) = on ? (wish, age) : (Vector2.Zero, 0);
        f.Lean = on ? lean + (to - lean) * LeanRate : Vector2.Zero;
        if (f.Lean.LengthSquared() < 1e-8f) f.Lean = Vector2.Zero;
        f.LeanTo = Vector2.Zero;
    }

    /// <summary>A tick of the wall by its clip's part (<paramref name="held"/>: the wish held on a moment, for a chain's next move; the intent itself).</summary>
    static void WallTickOf(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                           ref Vector3 feet, in Locomotion loco, in MoveIntent intent, in MoveIntent held, ref MotorOutcome o, AirCommitRules? rules)
    {
        switch (c.Role)
        {
            case LedgeRole.WallIdle:
                IdleTick(m, ref body, ref move, ref l, paths, c, ref w, ref f, wr, sheer, ref feet, loco, intent, ref o);
                return;
            case LedgeRole.WallMove:
                // Let go, or pushed off, in a move too (the hands are on holds through it but for a long move's flight).
                if (intent.Cancel || (intent.Crouch && body.CrouchWas == 0)) { WallDrop(m, ref body, ref move, ref l, paths, ref w, ref f, wr, ref feet, loco, push: false, intent, ref o); return; }
                // A straight quick move begun a moment ago becomes the diagonal when the other key comes within the wish's hold (pressed one after the other).
                if (wr.Flows && wr.Diagonals && f.Diag == 0 && !c.Long && intent.Gait != Gait.Walk && l.Age <= InputHoldTicks * World.TickSeconds && l.Age > 0f
                    && WallWish(intent, l.In, diagonals: true) is var (dl, du) && dl != 0 && du != 0
                    && ((MathF.Abs(c.StepLeft) > 0.05f && MathF.Sign(c.StepLeft) == dl) || (MathF.Abs(c.StepUp) > 0.05f && MathF.Sign(c.StepUp) == du))
                    && TryWallDiagonal(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, dl, du, fromEnd: false, wr.FlowBlend, ref o)) return;
                // The next move as this one's last limb lands, overlapping (held), instead of idle to idle; taken before this tick's advance, so the body
                // moves once this tick (on the new move's path). The wish held on a moment (a brief let-go does not break the chain).
                if (wr.Flows && l.Time < c.Duration - 1e-4f && l.Time >= LastOn(c) + w.Rate * World.TickSeconds - 1e-4f
                    && EndMove(m, ref body, ref move, ref l, paths, c, ref w, ref f, wr, sheer, ref feet, loco, held, ref o, wr.FlowBlend)) return;
                if (!WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o)) return;
                if (l.Time < c.Duration - 1e-4f) return;
                EndMove(m, ref body, ref move, ref l, paths, c, ref w, ref f, wr, sheer, ref feet, loco, wr.Flows ? held : intent, ref o, 0f);
                return;
            case LedgeRole.WallCorner:
                if (!WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o)) return;
                if (l.Time < c.Duration - 1e-4f) return;
                EndCorner(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, ref o);
                return;
            case LedgeRole.WallOn:
                if (wr.Flows && l.Time < c.Duration - 1e-4f && l.Age > 0f && FlowOutOfOn(m, ref body, ref move, ref l, paths, c, ref w, ref f, wr, sheer, ref feet, loco, held, ref o)) return;
                if (!WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o)) return;
                if (l.Time < c.Duration - 1e-4f) return;
                ToIdle(ref body, ref move, ref l, paths, feet, loco, ref o);
                return;
            case LedgeRole.WallToHang:
                if (wr.Flows && l.Time < c.Duration - 1e-4f && l.Age > 0f && FlowOverTop(m, ref body, ref move, ref l, paths, c, ref w, ref f, wr, ref feet, loco, intent, ref o, rules)) return;
                if (!WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o)) return;
                if (l.Time < c.Duration - 1e-4f) return;
                IntoHang(m, ref body, ref move, ref l, paths, ref w, ref f, wr, ref feet, loco, intent, ref o, rules);
                return;
            case LedgeRole.WallOff:
                if (!WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o)) return;
                if (l.Time < c.Duration - 1e-4f) return;
                o.MoveEnded = move.Kind; o.MoveCompleted = true;
                move = default;
                l = default;
                Leave(ref w, ref f);
                SetDown(m, ref body, ref feet, loco, intent, Vector3.Zero);
                return;
            case LedgeRole.WallDrop:
                WallDropTick(m, ref body, ref move, ref l, c, ref w, ref f, ref feet, loco, intent, ref o);
                return;
            default:
                l = default;
                Leave(ref w, ref f);
                Abort(m, ref body, ref move, ref feet, loco, ref o);
                return;
        }
    }

    /// <summary>Where a wall clip's last limb takes its hold (s of it; nought for none): a move's, as it lands the next may begin.</summary>
    static float LastOn(LedgeClip c)
    {
        float t = 0f;
        foreach (var h in c.Holds) if (h.On >= 0f && h.On < c.Duration) t = MathF.Max(t, h.On);
        return t;
    }

    /// <summary>
    /// Whether the limbs <paramref name="next"/> moves first (those whose start hold it lets go within <see cref="FirstBeat"/> of the first) have their holds in
    /// <paramref name="from"/> at <paramref name="t"/> s of it (taken by then, or held from before; a limb it has no hold for counts as held).
    /// </summary>
    static bool FirstHeld(LedgeClip from, float t, LedgeClip next)
    {
        float first = float.PositiveInfinity;
        foreach (var h in next.Holds) if (h.On < 0f && h.Off < first) first = h.Off;
        if (float.IsInfinity(first)) return true;
        foreach (var h in next.Holds)
        {
            if (h.On >= 0f || h.Off > first + FirstBeat) continue;
            bool any = false, held = false;
            foreach (var g in from.Holds)
            {
                if (g.Limb != h.Limb) continue;
                any = true;
                if ((g.On < 0f || t >= g.On - 1e-4f) && t < g.Off) held = true;
            }
            if (any && !held) return false;
        }
        return true;
    }

    /// <summary>The clip a move asked for would most likely start with (over the top, off at the foot, else the move at the pace; a diagonal's side), for <see cref="FirstHeld"/>.</summary>
    static LedgeClip? WouldMove(in Mover m, in LedgeMove l, LedgePaths paths, in MoveIntent intent, int left, int up)
    {
        if (up > 0 && left == 0 && TopNear(m, l.Place, l.In, paths, out _, out _, out _)) return paths.Wall(LedgeRole.WallToHang);
        if (up < 0 && left == 0 && AtFoot(m, l.Place, paths, out _)) return paths.Wall(LedgeRole.WallOff);
        bool careful = intent.Gait == Gait.Walk;
        if (left != 0 && up != 0) return careful ? paths.WallMoveOf(left, 0, quick: false) ?? paths.WallMoveOf(0, up, quick: false) : paths.WallMoveOf(left, 0, quick: true);
        if (intent.Gait >= Gait.Sprint && paths.WallMoveOf(left, up, quick: true, @long: true) is { } lc) return lc;
        return paths.WallMoveOf(left, up, quick: !careful);
    }

    /// <summary>
    /// A getting-on with a move held: once the limbs that move first in it have their holds (by the clips' <c>on_s</c> and <c>off_s</c>), the body is on the wall
    /// and the move begins from here (no rest at <c>wall_idle</c>), the capsule's difference, the velocity and the animation's fade over the flow's blend; true when
    /// it did (nothing changed else).
    /// </summary>
    static bool FlowOutOfOn(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref WallMove w, ref WallFlow f, WallRules wr,
                            bool sheer, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        if (intent.Cancel || intent.Crouch || intent.Jump) return false;
        var (left, up) = WallCarried(ref w, intent) ? (w.Carry, 0) : WallWish(intent, l.In, wr.Diagonals);
        if (left == 0 && up == 0) return false;
        if (WouldMove(m, l, paths, intent, left, up) is not { } next || !FirstHeld(c, l.Time - w.Rate * World.TickSeconds, next)) return false;
        // And the body near the wall position (the getting-on's way left at most FlowNear): a move's first hand reaching up a step from a body still pulling itself
        // up would be short of its hold.
        var (ef, eu, es) = c.At(c.Duration);
        var (nf, nu, ns) = c.At(l.Time);
        if (new Vector3(ef - nf, eu - nu, es - ns).Length() > FlowNear) return false;
        var (b0, mv0, l0, w0, f0, feet0, o0) = (body, move, l, w, f, feet, o);
        var v = body.Velocity;
        OnTheWall(ref body, ref move, l, feet, ref o);
        body.Velocity = v;   // the velocity it comes with, carried into the move
        if (TryWallMove(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, left, up, fromEnd: false, wr.FlowBlend, ref o)) return true;
        (body, move, l, w, f, feet) = (b0, mv0, l0, w0, f0, feet0);
        // What the attempt told (a refusal, said once a place) stays told; nothing else of it.
        var told = o;
        o = o0;
        (o.WallRefused, o.WallRefusedAct, o.WallLeft, o.WallUp, o.WallAt) = (told.WallRefused, told.WallRefusedAct, told.WallLeft, told.WallUp, told.WallAt);
        (w.Told, w.ToldAt) = told.WallRefused != WallRefusal.None ? (told.WallRefused, told.WallAt) : (w.Told, w.ToldAt);
        return false;
    }

    /// <summary>On the wall at the wall position: the idle from its first frame (a getting-on, a move and a corner end on it), the move's kind <see cref="TraversalKind.Wall"/>.</summary>
    static void ToIdle(ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, Vector3 feet, in Locomotion loco, ref MotorOutcome o)
    {
        _ = loco;
        var idle = paths.Wall(LedgeRole.WallIdle)!;
        Switch(ref l, paths, idle, 0f, feet, LedgeBlend, fade: false);
        l.Way = 0;
        l.Floor = float.NegativeInfinity;
        OnTheWall(ref body, ref move, l, feet, ref o);
    }

    /// <summary>The body on the wall (a getting-on over): the move's kind <see cref="TraversalKind.Wall"/>, hanging, still.</summary>
    static void OnTheWall(ref Body body, ref Traversal move, in LedgeMove l, Vector3 feet, ref MotorOutcome o)
    {
        if (move.Kind != TraversalKind.Wall)
        {
            o.MoveEnded = move.Kind; o.MoveCompleted = true;
            move = new Traversal { Kind = TraversalKind.Wall, Seconds = float.PositiveInfinity, From = feet, To = feet, Lip = move.Lip, LipNormal = new Vector3(-l.In, 0f), Height = 0f };
            o.MoveStarted = TraversalKind.Wall;
        }
        move.From = feet; move.To = feet; move.Progress = 0f;
        body.State = BodyState.Hanging;
        body.Velocity = Vector3.Zero;
        body.AirSeconds = 0f;
        body.Crouched = 0;
        body.Shimmy = 0;
    }

    /// <summary>
    /// The wish as a move on the wall (a body facing it: forward up, back down, its left and right across): the way (+1 left, −1 right) and up (+1, −1), the stronger
    /// alone; both where each is at least <see cref="WallDiagCone"/> of it (a diagonal).
    /// </summary>
    static (int Left, int Up) WallWish(in MoveIntent intent, Vector2 inward, bool diagonals = false)
    {
        var d = intent.Direction;
        if (d.LengthSquared() < 0.09f) return (0, 0);
        float up = Vector2.Dot(d, inward), left = Vector2.Dot(d, LeftOf(inward));
        if (diagonals)
        {
            float n = d.Length();
            if (MathF.Abs(up) >= WallDiagCone * n && MathF.Abs(left) >= WallDiagCone * n) return (MathF.Sign(left), MathF.Sign(up));
        }
        if (MathF.Abs(up) >= MathF.Abs(left)) return MathF.Abs(up) > WallCone ? (0, MathF.Sign(up)) : (0, 0);
        return MathF.Abs(left) > WallCone ? (MathF.Sign(left), 0) : (0, 0);
    }

    /// <summary>After a corner, whether the wish that went round it still holds (within about 30° of it): the move's way it keeps; else forgotten.</summary>
    static bool WallCarried(ref WallMove w, in MoveIntent intent)
    {
        if (w.Carry == 0) return false;
        float wl = intent.Direction.Length();
        if (wl > 0.3f && Vector2.Dot(intent.Direction / wl, w.Wish) > 0.85f) return true;
        w.Carry = 0;
        w.Wish = Vector2.Zero;
        return false;
    }

    /// <summary>The idle: cancel or crouch lets go, jump with back held pushes off, the wish moves (the pace by the gait asked); else the idle's loop.</summary>
    static void IdleTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip idle, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                         ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        body.State = BodyState.Hanging;
        body.AirSeconds = 0f;
        if (intent.Cancel || (intent.Crouch && body.CrouchWas == 0))
        {
            o.Cancelled = intent.Cancel;
            WallDrop(m, ref body, ref move, ref l, paths, ref w, ref f, wr, ref feet, loco, push: false, intent, ref o);
            return;
        }
        if (intent.Jump && BackFrom(intent, new Vector3(-l.In, 0f)))
        {
            WallDrop(m, ref body, ref move, ref l, paths, ref w, ref f, wr, ref feet, loco, push: true, intent, ref o);
            return;
        }
        var (left, up) = WallCarried(ref w, intent) ? (w.Carry, 0) : WallWish(intent, l.In, wr.Diagonals);
        if ((left != 0 || up != 0) && TryWallMove(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, left, up, fromEnd: false, 0f, ref o)) return;
        // A move asked and refused shows the intent: the leading hand reaches toward the wish (eased in while it is held, back after; WallTick).
        if (wr.Flows && (left != 0 || up != 0)) f.LeanTo = Vector2.Normalize(new Vector2(left, up)) * LeanReach;
        WallAdvance(m, ref body, ref move, ref l, paths, idle, ref w, ref f, ref feet, loco, ref o);
        move.From = feet; move.To = feet;
    }

    /// <summary>
    /// A clip of the wall's begun from where the body is (<paramref name="time"/> s of it): from its first frame with no fade at a clip's end or the idle's first
    /// frame (<paramref name="still"/>), else faded over <see cref="LedgeBlend"/>; flowing (<paramref name="flow"/> above nought): the capsule's difference
    /// and the animation's fade over it, the clip faded from going on at its own rate (<see cref="LedgeMove.Fade"/> 5, its rate in <see cref="LedgeMove.Rate"/>).
    /// </summary>
    static void WallSwitch(ref LedgeMove next, LedgePaths paths, LedgeClip c, Vector3 feet, bool still, float flow, float fromRate)
    {
        if (flow > 0f && !still)
        {
            Switch(ref next, paths, c, 0f, feet, flow, fade: true);
            next.Fade = 5;
            next.Rate = fromRate > 0f ? fromRate : 1f;
        }
        else Switch(ref next, paths, c, 0f, feet, LedgeBlend, fade: !still);
    }

    /// <summary>
    /// A move asked from the idle (or at a move's end, <paramref name="fromEnd"/>: no fade, from its first frame): over the top where its edge is, off at the
    /// foot, round a corner to a side; else the move that way at the pace asked (the long one with sprint where it fits, the careful one with walk, the quick one
    /// else), where the wall goes on and there is room. False (a refusal said once a place) for none.
    /// </summary>
    /// <remarks>
    /// <paramref name="flow"/> above nought, flowing out of the clip under way before its end (a getting-on, a move; <see cref="WallSwitch"/>); the velocity it
    /// comes with carried (<see cref="CarryInto"/>). A diagonal (<paramref name="left"/> and <paramref name="up"/> both) where its hold and room are there
    /// (<see cref="TryWallDiagonal"/>), else the straight move the wish's stronger part asks for.
    /// </remarks>
    static bool TryWallMove(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                            ref Vector3 feet, in Locomotion loco, in MoveIntent intent, int left, int up, bool fromEnd, float flow, ref MotorOutcome o)
    {
        if (left != 0 && up != 0)
        {
            if (intent.Gait == Gait.Walk)
            {
                // A careful diagonal is the up's (or the down's) and the side's careful moves by turns, each flowing out of the other (the four-beat moves
                // blended put the leading hand 13 cm short of its diagonal hold: it reaches before the body has moved). The other where the one is refused.
                bool sideWas = paths.At(l.Clip - 1) is { Role: LedgeRole.WallMove } was && MathF.Abs(was.StepLeft) > 0.05f;
                var (a, b) = sideWas ? ((0, up), (left, 0)) : ((left, 0), (0, up));
                var (b0, mv0, l0, w0, f0, feet0) = (body, move, l, w, f, feet);
                if (TryWallMove(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, a.Item1, a.Item2, fromEnd, flow, ref o)) return true;
                (body, move, l, w, f, feet) = (b0, mv0, l0, w0, f0, feet0);
                return TryWallMove(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, b.Item1, b.Item2, fromEnd, flow, ref o);
            }
            if (TryWallDiagonal(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, left, up, fromEnd, flow, ref o)) return true;
            (left, up) = WallWish(intent, l.In);   // the straight move the stronger part asks for
            if (left == 0 && up == 0) return false;
        }
        var idle = paths.Wall(LedgeRole.WallIdle)!;
        var lv = LeftOf(l.In);
        float step = WallStep(paths);
        var at = OnFace(l.Place, l.In, idle.WallFace, 0.4f * left, 1.0f + 0.4f * up);
        if (up > 0 && TopNear(m, l.Place, l.In, paths, out float dz, out var lip, out float top))
            return TryToHang(m, ref body, ref move, ref l, paths, ref w, ref f, wr, ref feet, loco, dz, lip, top, fromEnd, flow, ref o);
        if (up < 0 && AtFoot(m, l.Place, paths, out float gz))
            return TryWallOff(m, ref body, ref move, ref l, paths, ref w, ref f, wr, ref feet, loco, gz, fromEnd, flow, ref o);
        if (left != 0)
        {
            var corner = TryWallCorner(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, left, fromEnd, ref o);
            if (corner != 0) return corner > 0;
        }
        bool careful = intent.Gait == Gait.Walk;
        LedgeClip? c = null;
        var to = l.Place;
        WallRefusal why = WallRefusal.None;
        bool sheerHere = false, given = false;
        // The long move (sprint held) where its end and its middle are on the wall, and not past the top's or the foot's places.
        if (intent.Gait >= Gait.Sprint && paths.WallMoveOf(left, up, quick: true, @long: true) is { } lc)
        {
            var end = l.Place + new Vector3(lv * lc.StepLeft, lc.StepUp);
            var mid = l.Place + new Vector3(lv * (0.5f * lc.StepLeft), 0.5f * lc.StepUp);
            bool pastTop = up > 0 && TopNear(m, mid, l.In, paths, out _, out _, out _);
            bool pastFoot = up < 0 && AtFoot(m, mid, paths, out _);
            if (!pastTop && !pastFoot && WallFits(m.Level, loco, mid, l.In, idle, wr, sheer, out _, out _) == WallRefusal.None
                && WallFits(m.Level, loco, end, l.In, idle, wr, sheer, out sheerHere, out given) == WallRefusal.None)
                (c, to) = (lc, end);
        }
        if (c is null)
        {
            if (paths.WallMoveOf(left, up, quick: !careful) is not { } sc) { WallRefuse(ref w, ref o, WallAct.Move, left, up, WallRefusal.NoClip, at); return false; }
            to = l.Place + new Vector3(lv * sc.StepLeft, sc.StepUp);
            why = WallFits(m.Level, loco, to, l.In, idle, wr, sheer, out sheerHere, out given);
            if (why != WallRefusal.None) { WallRefuse(ref w, ref o, WallAct.Move, left, up, up > 0 && why == WallRefusal.NoHolds ? WallRefusal.Top : why, at); return false; }
            c = sc;
        }
        return BeginMove(m, ref body, ref move, ref l, paths, c, 0, ref w, ref f, wr, ref feet, loco, left, up, to, sheerHere, given, fromEnd, flow, at, ref o);
    }

    /// <summary>
    /// A move begun (<paramref name="c"/>; with a diagonal's second move <paramref name="diag"/>, 1 + its index, 0 none), its path swept first (blocked: refused,
    /// said once a place).
    /// </summary>
    static bool BeginMove(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, int diag, ref WallMove w, ref WallFlow f, WallRules wr,
                          ref Vector3 feet, in Locomotion loco, int left, int up, Vector3 to, bool sheerHere, bool given, bool fromEnd, float flow, Vector3 at, ref MotorOutcome o)
    {
        var next = l;
        bool still = fromEnd || l.Time < 0.5f * World.TickSeconds;
        WallSwitch(ref next, paths, c, feet, still, flow, w.Rate);   // the capsule's millimetres out (the move's first row against where it is)
        // Flowing out of a clip with a way still to go up or down (a getting-on still rising), that difference over what was left of it, not the blend.
        if (flow > 0f && !still && paths.At(l.Clip - 1) is { } was && w.Rate > 0f) next.UpTo = MathF.Max(next.UpTo, MathF.Min(0.6f, (was.Duration - l.Time) / w.Rate));
        next.Way = left != 0 ? left : up;
        var plan = w;
        plan.Rate = wr.Rate;
        // A diagonal at the speed of a straight move (its path the longer by the steps' sum: √2 for a step up and one across), so slower in time.
        if (diag > 0 && paths.At(diag - 1) is { } dv)
        {
            float straight = MathF.Max(MathF.Abs(c.StepLeft), MathF.Abs(dv.StepUp)), len = MathF.Sqrt(c.StepLeft * c.StepLeft + dv.StepUp * dv.StepUp);
            if (len > 1e-4f) plan.Rate *= straight / len;
        }
        plan.End = Vector3.Zero;
        plan.Shift = Vector3.Zero;
        (plan.EndFrom, plan.EndTo) = (0f, 0f);
        var fl = new WallFlow { Diag = diag };
        if (wr.Flows && !still) fl.Carry = CarryInto(next, fl, paths, c, body.Velocity, plan.Rate);   // the velocity at the seam kept
        if (!WallPathClear(m, loco, next, plan, fl, paths, c, feet, 0f))
        {
            if (diag == 0) WallRefuse(ref w, ref o, WallAct.Move, left, up, WallRefusal.Blocked, at);
            return false;
        }
        l = next;
        w = plan;
        f = fl;
        w.Sheer = sheerHere ? 1 : 0;
        w.Given = given ? 1 : 0;
        move.Seconds = c.Duration / MathF.Max(1f, w.Rate);
        move.Progress = 0f;
        o.WallStarted = WallAct.Move;
        o.WallLeft = left;
        o.WallUp = up;
        o.WallPace = c.Long ? WallEvents.Long : c.Quick ? WallEvents.Quick : WallEvents.Careful;
        o.WallSheer = w.Sheer != 0;
        o.WallFrom = feet;
        o.WallTo = to;
        WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o);
        return true;
    }

    /// <summary>
    /// <b>a diagonal</b> (forward or back with a side held): to the hold one step up or down and one across, where the wall is there with its holds and room
    /// (no refusal said: else the straight move the wish's stronger part asks for). No diagonal clips: the side's move (quick, or careful with walk held) carries
    /// the limbs and their timing, the up's or the down's of the same pace is played in step with it (from the same first frame to the same last): its path added
    /// to the side's (the path the sum of the two), and its body (the spine, hips and head) laid half over the side's (<see cref="DiagShare"/>); the people's IK
    /// carries each hand and foot up or down its step over its own flight onto the diagonal hold. Long moves stay straight. Not over the top or off at the foot.
    /// </summary>
    static bool TryWallDiagonal(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                                ref Vector3 feet, in Locomotion loco, in MoveIntent intent, int left, int up, bool fromEnd, float flow, ref MotorOutcome o)
    {
        if (!wr.Diagonals) return false;
        if (up > 0 && TopNear(m, l.Place, l.In, paths, out _, out _, out _)) return false;
        if (up < 0 && AtFoot(m, l.Place, paths, out _)) return false;
        bool careful = intent.Gait == Gait.Walk;
        if (paths.WallMoveOf(left, 0, quick: !careful) is not { Long: false } side || paths.WallMoveOf(0, up, quick: !careful) is not { Long: false } vert) return false;
        var idle = paths.Wall(LedgeRole.WallIdle)!;
        var to = l.Place + new Vector3(LeftOf(l.In) * side.StepLeft, vert.StepUp);
        if (WallFits(m.Level, loco, to, l.In, idle, wr, sheer, out bool sheerHere, out bool given) != WallRefusal.None) return false;
        var at = OnFace(l.Place, l.In, idle.WallFace, 0.4f * left, 1.0f + 0.4f * up);
        return BeginMove(m, ref body, ref move, ref l, paths, side, paths.IndexOf(vert) + 1, ref w, ref f, wr, ref feet, loco, left, up, to, sheerHere, given, fromEnd, flow, at, ref o);
    }

    /// <summary>
    /// A move at its end: the wall position a step on; the next move while the wish holds that way (and the pace), else the idle from its first frame. with
    /// <paramref name="flow"/> above nought, before its end as its last limb lands: the next move the wish asks for (any way) flowing out of it; none, nothing
    /// changed (false) and the move goes on to its end. At its end any way the wish asks for (the same way only).
    /// </summary>
    static bool EndMove(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                        ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o, float flow)
    {
        var lv = LeftOf(l.In);
        var d = f.Diag > 0 && paths.At(f.Diag - 1) is { } dc ? dc : null;
        var was = l.Place;
        l.Place += new Vector3(lv * c.StepLeft, c.StepUp + (d?.StepUp ?? 0f));
        int left = MathF.Abs(c.StepLeft) > 0.05f ? MathF.Sign(c.StepLeft) : 0, up = MathF.Abs(c.StepUp + (d?.StepUp ?? 0f)) > 0.05f ? MathF.Sign(c.StepUp + (d?.StepUp ?? 0f)) : 0;
        var idle = paths.Wall(LedgeRole.WallIdle)!;
        var (wl, wu) = WallCarried(ref w, intent) ? (w.Carry, 0) : WallWish(intent, l.In, wr.Diagonals);
        if (flow > 0f)
        {
            // Flowing on as the last limb lands; else back as it was, to go on to the end.
            if ((wl != 0 || wu != 0) && !(intent.Cancel || intent.Crouch || intent.Jump)
                && TryWallMove(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, wl, wu, fromEnd: false, flow, ref o)) return true;
            l.Place = was;
            return false;
        }
        // The next move from the idle's first frame (the move ends on it) at once while the wish holds the same way (any way); else the idle from there.
        if ((wr.Flows ? wl != 0 || wu != 0 : wl == left && wu == up)
            && TryWallMove(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, wl, wu, fromEnd: true, 0f, ref o)) return true;
        f = default;
        Switch(ref l, paths, idle, 0f, feet, LedgeBlend, fade: false);   // the capsule's millimetres out (the idle's own row against the move's last)
        move.Seconds = float.PositiveInfinity;
        move.Progress = 0f;
        move.From = feet; move.To = feet;
        body.Velocity = Vector3.Zero;
        return true;
    }

    // ---------------------------------------------------------------- the top and the foot

    /// <summary>
    /// Whether the face's top is over the wall position <paramref name="place"/> where the clip over it takes it: a level top within half a step of its own
    /// (<c>top.edge_up_m</c> over the wall position, <c>edge_fwd_m</c> ahead) with the face under it and the lip going on under both hands; its difference, the
    /// lip's point and the top.
    /// </summary>
    static bool TopNear(in Mover m, Vector3 place, Vector2 inward, LedgePaths paths, out float dz, out Vector3 lip, out float top)
    {
        dz = 0f; lip = default; top = float.NaN;
        if (paths.Wall(LedgeRole.WallToHang) is not { } c || float.IsNaN(c.TopUp)) return false;
        float half = 0.5f * WallStep(paths);
        float fwd = float.IsNaN(c.TopFwd) ? c.WallFace : c.TopFwd;
        var edge = new Vector3(place.X + inward.X * fwd, place.Y + inward.Y * fwd, place.Z + c.TopUp);
        var inside = new Vector2(edge.X + inward.X * 0.04f, edge.Y + inward.Y * 0.04f);
        float z = TopUnder(m.Level, inside, edge.Z + half + 0.02f, edge.Z - half - 0.02f, out _);
        if (float.IsNaN(z) || z > edge.Z + half || z < edge.Z - half) return false;
        if (!FaceAt(m.Level, new Vector3(edge.X, edge.Y, z - 0.15f), inward, out var face)) return false;
        var n = new Vector3(face.Normal.X, face.Normal.Y, 0f);
        float nl = n.Length();
        if (nl < 1e-4f) return false;
        n /= nl;
        var at = new Vector3(face.Point.X, face.Point.Y, z);
        var lv = new Vector3(LeftOf(inward), 0f);
        if (!LipGoesOn(m.Level, at + lv * ShimmyHand, z, n) || !LipGoesOn(m.Level, at - lv * ShimmyHand, z, n)) return false;
        dz = z - edge.Z;
        lip = at;
        top = z;
        return true;
    }

    /// <summary>Whether the wall position <paramref name="place"/> is at the foot: the ground under it where the getting-off ends within half a step; its height.</summary>
    static bool AtFoot(in Mover m, Vector3 place, LedgePaths paths, out float ground)
    {
        ground = float.NaN;
        if (paths.Wall(LedgeRole.WallOff) is not { } off) return false;
        float half = 0.5f * WallStep(paths);
        float end = place.Z + off.At(off.Duration).Up;
        float g = TopUnder(m.Level, new Vector2(place.X, place.Y), end + half + 0.02f, end - half - 0.02f, out _);
        if (float.IsNaN(g) || MathF.Abs(g - end) > half + 1e-4f) return false;
        ground = g;
        return true;
    }

    /// <summary>Over the top: <c>wall_to_hang_braced</c> laid on the real edge (its height's difference taken out along its rise), where the braced hang fits there.</summary>
    /// <remarks>At <see cref="WallRules.MoveRate"/> (its windows in the body's time); flowing into it (<paramref name="flow"/>) as <see cref="WallSwitch"/>.</remarks>
    static bool TryToHang(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, ref Vector3 feet,
                          in Locomotion loco, float dz, Vector3 lip, float top, bool fromEnd, float flow, ref MotorOutcome o)
    {
        var at = lip;
        if (paths.Wall(LedgeRole.WallToHang) is not { } c || paths.Of(LedgeRole.Hang, braced: true) is not { } hang)
        { WallRefuse(ref w, ref o, WallAct.ToHang, 0, 1, WallRefusal.NoClip, at); return false; }
        var n = new Vector3(-l.In, 0f);
        var target = HangPlace(paths, lip, n, top);
        if (!HangFits(m.Level, loco, target, lip, n)) { WallRefuse(ref w, ref o, WallAct.ToHang, 0, 1, WallRefusal.NoRoom, at); return false; }
        var next = l;
        bool still = fromEnd || l.Time < 0.5f * World.TickSeconds;
        float rate = MovingRate(wr);
        WallSwitch(ref next, paths, c, feet, still, flow, w.Rate);
        next.Place += new Vector3(0f, 0f, dz);
        next.Delta = feet - LedgeRaw(next, c, 0f);
        float rise = 0.6f * c.Duration / (wr.Flows ? rate : 1f);
        (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (0f, still ? rise : flow > 0f ? flow : LedgeBlend, 0f, rise);
        var plan = w;
        plan.Rate = rate;
        plan.Shift = new Vector3(0f, 0f, dz);
        var ends = new LedgeMove { Place = target, In = l.In };
        plan.End = LedgeRaw(ends, hang, 0f) - LedgeRaw(next, c, c.Duration);
        (plan.EndFrom, plan.EndTo) = (0.5f * c.Duration, c.Duration);
        plan.Lip = lip;
        plan.LipNormal = n;
        plan.Top = top;
        var fl = default(WallFlow);
        if (wr.Flows && !still) fl.Carry = CarryInto(next, fl, paths, c, body.Velocity, rate) with { Z = 0f };   // the across velocity at the seam kept
        if (!WallPathClear(m, loco, next, plan, fl, paths, c, feet, 0f)) { WallRefuse(ref w, ref o, WallAct.ToHang, 0, 1, WallRefusal.Blocked, at); return false; }
        l = next;
        w = plan;
        f = fl;
        move.Seconds = c.Duration / rate;
        move.Progress = 0f;
        move.Lip = lip;
        move.LipNormal = n;
        o.WallStarted = WallAct.ToHang;
        o.WallUp = 1;
        o.WallSheer = w.Sheer != 0;
        o.WallFrom = feet;
        o.WallTo = LedgeRaw(ends, hang, 0f);
        WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o);
        return true;
    }

    /// <summary>
    /// Over the top's end: the braced hang from its first frame at the hang's place from the real edge (the clip ends on it); and, with forward (up) or jump held,
    /// straight on into the pull-up (the commit point's flow: no wait at the hang).
    /// </summary>
    /// <remarks>The pull-up at the commit point's rate times <see cref="WallRules.MoveRate"/> (at most 3).</remarks>
    static void IntoHang(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, ref Vector3 feet,
                         in Locomotion loco, in MoveIntent intent, ref MotorOutcome o, AirCommitRules? rules)
    {
        var hang = paths.Of(LedgeRole.Hang, braced: true)!;
        var lip = w.Lip;
        var n = w.LipNormal;
        var inward = l.In;
        l = new LedgeMove
        {
            Clip = paths.IndexOf(hang) + 1, Time = 0f, Age = 0f, Braced = 1, Place = HangPlace(paths, lip, n, w.Top), In = inward, Top = w.Top,
            Floor = float.NegativeInfinity,
        };
        l.Delta = feet - LedgeRaw(l, hang, 0f);
        (l.AcrossFrom, l.AcrossTo, l.UpFrom, l.UpTo) = (0f, LedgeBlend, 0f, LedgeBlend);
        Leave(ref w, ref f);
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        body.State = BodyState.Hanging;
        body.Crouched = 0; body.Velocity = Vector3.Zero; body.ForwardArmed = 0; body.AirSeconds = 0f; body.Shimmy = 0;
        move = new Traversal
        {
            Kind = TraversalKind.Hang, Seconds = loco.GripSeconds > 0f ? loco.GripSeconds : float.PositiveInfinity,
            From = feet, To = feet, Lip = lip, LipNormal = n, Height = lip.Z - feet.Z,
        };
        o.MoveStarted = TraversalKind.Hang;
        bool held = ForwardTo(intent, n) || intent.JumpHeld || intent.Jump;
        if (!held) return;
        bool flowed = rules is { Climb: true }
            ? TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o, braced: true, PullUpOnTop(rules, wr), FlowBlend)
            : TryLedgePullUp(m, ref body, ref move, ref l, paths, ref feet, loco, ref o);
        if (flowed) o.MoveEnded = TraversalKind.Hang;
    }

    /// <summary>The pull-up's rate over the top of a wall: the commit point's (the earlier build's flow), times <see cref="WallRules.MoveRate"/>, at most 3 (the commit point's).</summary>
    static float PullUpOnTop(AirCommitRules rules, WallRules wr) => wr.Flows ? MathF.Min(3f, rules.PullUpRate * wr.MoveRate) : rules.PullUpRate;

    /// <summary>
    /// <b>the top as one motion</b> — over the top with up (forward) or jump held, the pull-up crossed in over the last <see cref="FlowBlend"/> of the clip over
    /// the top (no braced hang held between): the hang at the hang's place taken at once and the pull-up from it (the top's room checked as ever), the capsule's
    /// difference from the pull-up's path taken out over that, the animation's fade too with the clip over the top going on to its end (<see cref="LedgeMove.Fade"/>
    /// 5). True when it did; nothing changed else (no room on the top, or released: on to the end, into the braced hang).
    /// </summary>
    static bool FlowOverTop(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, LedgeClip c, ref WallMove w, ref WallFlow f, WallRules wr,
                            ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o, AirCommitRules? rules)
    {
        if (rules is not { Climb: true } || (c.Duration - l.Time) / MathF.Max(1f, w.Rate) > FlowBlend + 1e-4f) return false;
        if (!(ForwardTo(intent, w.LipNormal) || intent.JumpHeld || intent.Jump)) return false;
        var (b0, mv0, l0, w0, f0, feet0, o0) = (body, move, l, w, f, feet, o);
        IntoHang(m, ref body, ref move, ref l, paths, ref w, ref f, wr, ref feet, loco, intent, ref o, rules);
        if (l.Clip > 0 && paths.At(l.Clip - 1) is { Role: LedgeRole.PullUp })
        {
            l.Fade = 5;   // the clip over the top going on (at the pull-up's rate, l.Rate) as the pull-up fades in
            return true;
        }
        (body, move, l, w, f, feet, o) = (b0, mv0, l0, w0, f0, feet0, o0);
        return false;
    }

    /// <summary>Off at the foot: <c>wall_off_ground</c> laid onto the real ground (its difference taken out along its fall), where the body stands there.</summary>
    /// <remarks>At <see cref="WallRules.MoveRate"/> (its windows in the body's time); flowing into it (<paramref name="flow"/>) as <see cref="WallSwitch"/>.</remarks>
    static bool TryWallOff(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, ref Vector3 feet,
                           in Locomotion loco, float ground, bool fromEnd, float flow, ref MotorOutcome o)
    {
        var at = l.Place;
        if (paths.Wall(LedgeRole.WallOff) is not { } c) { WallRefuse(ref w, ref o, WallAct.Off, 0, -1, WallRefusal.NoClip, at); return false; }
        var next = l;
        bool still = fromEnd || l.Time < 0.5f * World.TickSeconds;
        float rate = MovingRate(wr);
        WallSwitch(ref next, paths, c, feet, still, flow, w.Rate);
        float dz = ground - (l.Place.Z + c.At(c.Duration).Up);
        next.Place += new Vector3(0f, 0f, dz);
        next.Delta = feet - LedgeRaw(next, c, 0f);
        float fall = 0.6f * c.Duration / (wr.Flows ? rate : 1f);
        (next.AcrossFrom, next.AcrossTo, next.UpFrom, next.UpTo) = (0f, still ? fall : flow > 0f ? flow : LedgeBlend, 0f, fall);
        next.Floor = ground;   // the swept capsule kept over the ground it comes down onto
        var end = LedgeRaw(next, c, c.Duration);
        if (!Fits(m.Level, end + new Vector3(0f, 0f, Skin), loco.Radius, loco.Height)) { WallRefuse(ref w, ref o, WallAct.Off, 0, -1, WallRefusal.NoRoom, at); return false; }
        var plan = w;
        plan.Rate = rate;
        plan.End = Vector3.Zero;
        plan.Shift = new Vector3(0f, 0f, dz);
        var fl = default(WallFlow);
        if (wr.Flows && !still) fl.Carry = CarryInto(next, fl, paths, c, body.Velocity, rate) with { Z = 0f };   // the across velocity at the seam kept
        if (!WallPathClear(m, loco, next, plan, fl, paths, c, feet, 0f)) { WallRefuse(ref w, ref o, WallAct.Off, 0, -1, WallRefusal.Blocked, at); return false; }
        l = next;
        w = plan;
        f = fl;
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        move = new Traversal
        {
            Kind = TraversalKind.WallOff, Progress = 0f, Seconds = c.Duration / rate, From = feet, To = end, Lip = move.Lip, LipNormal = new Vector3(-l.In, 0f), Height = feet.Z - end.Z,
        };
        o.MoveStarted = TraversalKind.WallOff;
        o.WallStarted = WallAct.Off;
        o.WallUp = -1;
        o.WallSheer = w.Sheer != 0;
        o.WallFrom = feet;
        o.WallTo = end;
        body.State = BodyState.Traversing;
        WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o);
        return true;
    }

    // ---------------------------------------------------------------- off into the air

    /// <summary>The hands let go (<paramref name="push"/> false: <c>wall_let_go</c>) or a push off backwards (<c>wall_push_off</c>): the clip's path, then the fall.</summary>
    /// <remarks>At <see cref="WallRules.MoveRate"/> (its path faster: the fall after it at that speed).</remarks>
    static void WallDrop(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, ref Vector3 feet,
                         in Locomotion loco, bool push, in MoveIntent intent, ref MotorOutcome o)
    {
        if (paths.WallDrop(push) is not { } c)
        {
            l = default;
            Leave(ref w, ref f, WallAgain);
            if (push) PushOff(m, ref body, ref move, feet, loco, ref o); else LetGo(m, ref body, ref move, feet, ref o);
            return;
        }
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        var n = new Vector3(-l.In, 0f);
        var lip = move.Lip;
        Switch(ref l, paths, c, 0f, feet, LedgeBlend, fade: true);
        l.Floor = feet.Z;   // its fall's height from here (BodyMotor.ToAir)
        l.Want = intent.UseHeld ? DropCatches : 0;   // let go with use held: a lip caught on the way down, at once
        w.Rate = MovingRate(wr);
        w.End = Vector3.Zero;
        w.Shift = Vector3.Zero;
        f = default;
        move = new Traversal
        {
            Kind = TraversalKind.Drop, Progress = 0f, Seconds = MathF.Max(c.Duration / w.Rate, World.TickSeconds), From = feet, To = LedgeRaw(l, c, c.Duration), Lip = lip, LipNormal = n,
            Height = 0f,
        };
        o.MoveStarted = TraversalKind.Drop;
        o.WallStarted = push ? WallAct.PushOff : WallAct.LetGo;
        o.WallSheer = w.Sheer != 0;
        o.WallFrom = feet;
        o.WallTo = move.To;
        body.State = BodyState.Traversing;
        body.Shimmy = 0;
        WallDropTick(m, ref body, ref move, ref l, c, ref w, ref f, ref feet, loco, intent, ref o, first: true);
    }

    /// <summary>A drop off the wall along its path (swept); at its end, or where it meets something, the fall from there at the path's speed.</summary>
    static void WallDropTick(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgeClip c, ref WallMove w, ref WallFlow f, ref Vector3 feet, in Locomotion loco,
                             in MoveIntent intent, ref MotorOutcome o, bool first = false)
    {
        const float dt = World.TickSeconds;
        _ = intent;
        float hangZ = l.Floor;
        float rate = w.Rate > 0f ? w.Rate : 1f;   // wallMoveRate
        if (!first) { l.Age += dt; l.Time = MathF.Min(c.Duration, l.Time + dt * rate); }
        var p = LedgePoint(l, c, l.Time, l.Age);
        var v = (p - feet) / dt;
        var floorless = l;
        floorless.Floor = float.NegativeInfinity;
        bool met = WallSwept(m, loco, floorless, c, feet, p, out float share);
        if (met) p = feet + (p - feet) * share;
        feet = p;
        body.Velocity = first ? Vector3.Zero : v;
        move.Progress = MathF.Min(1f, l.Time / MathF.Max(c.Duration, World.TickSeconds));
        if (!met && l.Time < c.Duration - 1e-4f) return;
        o.MoveEnded = TraversalKind.Drop; o.MoveCompleted = !met;
        move = default;
        bool catches = l.Want == DropCatches;
        l = default;
        l.Again = catches ? 0f : LedgeAgain;
        Leave(ref w, ref f, WallAgain);
        body.State = BodyState.Air;
        body.Crouched = 0;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.AirSeconds = c.Duration / rate;
        body.TakeOffZ = hangZ; body.HighestZ = MathF.Max(hangZ, feet.Z); body.Rising = 0; body.Jumped = 0; body.Powered = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1;
    }

    // ---------------------------------------------------------------- corners

    /// <summary>
    /// A corner of the wall <paramref name="way"/> (+1 left) within its clip's reach from the wall position (as the hang's, outside where the face
    /// stops going on, inside where another face comes across, square within the tolerance; the corner within half a step beyond the clip's own
    /// <c>corner_along_m</c> and no nearer than the slack short of it): round it, 1; one that cannot be taken, −1 (told why); none within reach, 0 (the move).
    /// </summary>
    /// <remarks>At <see cref="WallRules.MoveRate"/> (its lay-in window in the body's time).</remarks>
    static int TryWallCorner(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                             ref Vector3 feet, in Locomotion loco, in MoveIntent intent, int way, bool fromEnd, ref MotorOutcome o)
    {
        var outsideClip = paths.WallCornerOf(true, way);
        var insideClip = paths.WallCornerOf(false, way);
        if (outsideClip is null && insideClip is null) return 0;
        var idle = paths.Wall(LedgeRole.WallIdle)!;
        var in1 = l.In;
        var along = LeftOf(in1) * way;
        var a3 = new Vector3(along, 0f);
        float halfStep = 0.5f * WallStep(paths);
        float reach = MathF.Max(outsideClip?.CornerAlong ?? 0f, insideClip?.CornerAlong ?? 0f) + halfStep + 0.02f;
        // At the body's middle and at its feet's holds: the face's line on the wall.
        var e1 = OnFace(l.Place, in1, idle.WallFace, 0f, 1.0f);
        float dIn = float.PositiveInfinity;
        Vector2 nIn = default;
        Vector3 hitIn = default;
        if (insideClip is not null && m.Level.Ray(new Vector3(e1.X - in1.X * 0.05f, e1.Y - in1.Y * 0.05f, e1.Z), a3, reach) is { } across && MathF.Abs(across.Normal.Z) < 0.35f)
        {
            var an = new Vector2(across.Normal.X, across.Normal.Y);
            if (an.Length() > 1e-4f) { dIn = across.Distance; nIn = Vector2.Normalize(an); hitIn = across.Point; }
        }
        float dOut = float.PositiveInfinity;
        if (outsideClip is not null)
            for (float d = 0f; d <= MathF.Min(reach, dIn) + 1e-4f; d += 0.02f)
                if (!FaceAt(m.Level, e1 + a3 * d, in1, out _))
                {
                    float lo = MathF.Max(0f, d - 0.02f), hi = d;
                    if (d > 0f)
                        for (int i = 0; i < 4; i++)
                        {
                            float mid = 0.5f * (lo + hi);
                            if (FaceAt(m.Level, e1 + a3 * mid, in1, out _)) lo = mid; else hi = mid;
                        }
                    dOut = 0.5f * (lo + hi);
                    break;
                }
        bool outside = dOut < dIn - 0.05f;
        float dist = outside ? dOut : dIn;
        if (float.IsInfinity(dist)) return 0;
        var c = outside ? outsideClip : insideClip;
        if (c is null) return 0;
        if (dist - c.CornerAlong > halfStep + 1e-3f) return 0;   // not yet: the move toward it
        var at = e1 + a3 * dist;
        if (dist < c.CornerAlong - wr.CornerSlack - 1e-3f) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, WallRefusal.TooNear, at); return -1; }
        Vector2 n2;
        Vector3 on2;
        if (outside)
        {
            var probe = at + new Vector3(in1 * 0.15f, 0f) + a3 * 0.3f;
            if (m.Level.Ray(probe, -a3, 0.45f) is not { } face || face.Distance < 0.1f) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, WallRefusal.ShortFace, at); return -1; }
            var fn = new Vector2(face.Normal.X, face.Normal.Y);
            if (fn.Length() < 1e-4f) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, WallRefusal.ShortFace, at); return -1; }
            n2 = Vector2.Normalize(fn);
            on2 = face.Point;
            if (Vector2.Dot(n2, along) < DetMath.Cos(wr.AngleTolerance)) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, WallRefusal.Angle, at); return -1; }
        }
        else
        {
            n2 = nIn;
            on2 = hitIn;
            if (Vector2.Dot(n2, -along) < DetMath.Cos(wr.AngleTolerance)) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, WallRefusal.Angle, at); return -1; }
        }
        // The corner: where the two faces' lines meet.
        var d2 = new Vector2(-n2.Y, n2.X);
        if (outside ? Vector2.Dot(d2, in1) < 0f : Vector2.Dot(d2, in1) > 0f) d2 = -d2;   // along the other face away from the corner
        float den = along.X * d2.Y - along.Y * d2.X;
        if (MathF.Abs(den) < 0.3f) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, WallRefusal.Angle, at); return -1; }
        var wv = new Vector2(on2.X - e1.X, on2.Y - e1.Y);
        float t1 = (wv.X * d2.Y - wv.Y * d2.X) / den;
        var corner = new Vector3(e1.X + along.X * t1, e1.Y + along.Y * t1, e1.Z);
        dist = t1;
        if (dist < c.CornerAlong - wr.CornerSlack - 1e-3f) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, WallRefusal.TooNear, corner); return -1; }
        // The end's wall position on the other face (the clip's own distance from the corner), its holds and room.
        var in2 = -n2;
        var endFace = corner + new Vector3(d2 * c.EndsAlong, 0f);
        var target = new Vector3(endFace.X - in2.X * idle.WallFace, endFace.Y - in2.Y * idle.WallFace, l.Place.Z);
        var why = WallFits(m.Level, loco, target, in2, idle, wr, sheer, out bool sheerHere, out bool given);
        if (why != WallRefusal.None) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, why == WallRefusal.NoHolds ? WallRefusal.ShortFace : why, corner); return -1; }
        // Laid with its own corner on the real one, as the hang's: the start's difference along the face out as the hands go to the corner, the end's as
        // the body comes round.
        float turnIn = 0.3f, turnHalf = 0.5f * c.Duration;
        for (float t = 0f; t <= c.Duration; t += World.TickSeconds)
            if (MathF.Abs(c.TurnAt(t)) >= 0.05f * MathF.Abs(c.Turn)) { turnIn = Math.Clamp(t, 0.3f, 0.5f * c.Duration); break; }
        for (float t = 0f; t <= c.Duration; t += World.TickSeconds)
            if (MathF.Abs(c.TurnAt(t)) >= 0.5f * MathF.Abs(c.Turn)) { turnHalf = t; break; }
        var next = l;
        next.Place = l.Place + a3 * (dist - c.CornerAlong);
        bool still = fromEnd || l.Time < 0.5f * World.TickSeconds;
        float rate = MovingRate(wr);
        Switch(ref next, paths, c, 0f, feet, wr.Flows ? turnIn / rate : turnIn, fade: !still);
        next.Way = way;
        var ends = new LedgeMove { Place = target, In = in2 };
        var plan = w;
        plan.Rate = rate;
        plan.Shift = Vector3.Zero;
        plan.End = LedgeRaw(ends, idle, 0f) - LedgeRaw(next, c, c.Duration);
        (plan.EndFrom, plan.EndTo) = (MathF.Min(turnHalf, c.Duration - World.TickSeconds), c.Duration);
        plan.Yaw0 = DetMath.Atan2(in1.Y, in1.X);
        plan.YawEnd = DetMath.WrapAngle(DetMath.Atan2(in2.Y, in2.X) - (plan.Yaw0 + c.TurnAt(c.Duration)));
        plan.Place = target;
        plan.In = in2;
        var fl = default(WallFlow);
        if (!WallPathClear(m, loco, next, plan, fl, paths, c, feet, 0f)) { WallRefuse(ref w, ref o, WallAct.Corner, way, 0, WallRefusal.Blocked, corner); return -1; }
        l = next;
        w = plan;
        f = fl;
        w.Sheer = sheerHere ? 1 : 0;
        w.Given = given ? 1 : 0;
        move.Seconds = c.Duration / rate;
        move.Progress = 0f;
        o.WallStarted = WallAct.Corner;
        o.WallLeft = way;
        o.WallOutside = outside;
        o.WallSheer = w.Sheer != 0;
        o.WallFrom = feet;
        o.WallTo = target;
        WallAdvance(m, ref body, ref move, ref l, paths, c, ref w, ref f, ref feet, loco, ref o);
        return 1;
    }

    /// <summary>A corner at its end: the idle on the other face from its first frame; the move on that way while the wish that went in holds.</summary>
    static void EndCorner(in Mover m, ref Body body, ref Traversal move, ref LedgeMove l, LedgePaths paths, ref WallMove w, ref WallFlow f, WallRules wr, bool sheer,
                          ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        int way = l.Way;
        l.Place = w.Place;
        l.In = w.In;
        body.Yaw = DetMath.WrapAngle(DetMath.Atan2(w.In.Y, w.In.X));
        w.End = Vector3.Zero;
        w.Shift = Vector3.Zero;
        (w.EndFrom, w.EndTo) = (0f, 0f);
        var idle = paths.Wall(LedgeRole.WallIdle)!;
        Switch(ref l, paths, idle, 0f, feet, LedgeBlend, fade: false);   // the capsule's millimetres out (the idle's own row against the corner's end)
        move.LipNormal = new Vector3(-l.In, 0f);
        move.Seconds = float.PositiveInfinity;
        move.Progress = 0f;
        float wl = intent.Direction.Length();
        if (wl <= 0.3f) return;
        w.Carry = way;
        w.Wish = intent.Direction / wl;
        f = default;
        TryWallMove(m, ref body, ref move, ref l, paths, ref w, ref f, wr, sheer, ref feet, loco, intent, way, 0, fromEnd: true, 0f, ref o);
    }
}
