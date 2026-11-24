// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>The moves</b> (ours). Each is <b>a timed path</b> from where it
/// starts to where it ends (<see cref="Traversal.From"/>, <see cref="Traversal.To"/>), checked against the
/// level and the other bodies with the <b>crouched</b> capsule (the body tucks) before it starts — refused, as
/// if it had not been asked for, when the capsule would meet anything on the way — and swept again tick by
/// tick as it runs (a body stepping into the way ends it, and the body falls from where it got to).
///
/// <para>Which move, from what the probe reads (<see cref="TraversalProbe"/>) and from jump —
/// <b>a tap does them all</b>: the obstacle decides, with no hold (a hold charges a higher jump instead,
/// <c>BodyMotor.Jump.cs</c>). Heights are measured from <b>the ground the body left</b> (under the feet, a
/// <see cref="Skin"/> below them), so a block of 0.6 m is 0.6 m, not 0.598:</para>
/// <list type="bullet">
/// <item><b>Hop up</b>: a tap on the ground, moving towards a top that can be stood on whose face is
/// within <see cref="CloseReach"/> of the body and which the tap's jump reaches (above the stride, to the tap's
/// height and <see cref="HopTuck"/>, within <see cref="Within"/>): instead of the jump.</item>
/// <item><b>The sprint vault</b> (<see cref="TraversalKind.Hurdle"/>) and <b>the run's step-over</b>
/// (<see cref="TraversalKind.StepOver"/>): by themselves, from the pace (BodyMotor.Pace.cs).</item>
/// <item><b>Vault</b>, <b>climb onto</b>, <b>mantle</b>: in the air from a jump (tapped or charged), facing (or
/// moving towards) an obstacle within <see cref="CloseReach"/>, its height measured from the ground jumped from: a
/// thin one (shallower than a top that can be stood on, <see cref="Locomotion.MantleMinDepth"/>, the hurdle's
/// threshold too; ground beyond) above the stride to <see cref="Locomotion.VaultMax"/> is vaulted; one that can be
/// stood on from <see cref="Locomotion.VaultMin"/> to <see cref="Locomotion.VaultMax"/> climbed onto; above that to
/// <see cref="Locomotion.MantleMax"/> mantled onto. The bands are the body's numbers now (the masquerade holds them to
/// the human's in public; free, an elder mantles 4 m with a tap) — or, from a charged jump, towards the body's own
/// whatever the mode, as far as the charge went (<see cref="MovesReach"/>).</item>
/// <item><b>Grab</b>: in the air from a jump — or falling with jump pressed or held (with <see cref="HeldGrace"/>) —,
/// the hands (at <see cref="Locomotion.StandingReach"/> above the feet) at a holdable lip within
/// <see cref="GrabReach"/> — or, from a jump, over a lip from the mantle band's top (<see cref="Locomotion.MantleMax"/>)
/// to what the hands reach at the jump's top, so no lip between the mantle and the grab is out of both —, and room to
/// hang there. At the grab's end, <b>forward or jump held</b> (jump with the grace) <b>pulls up</b> when there is room
/// above; nothing held, the body <b>hangs</b>.</item>
/// <item><b>Hanging</b>: jump pressed, or forward (once the wish has been off forward since the hang began, so
/// the forward held through the grab does not pull up again and again where there is no room), pulls up; crouch
/// pressed or cancel — or the grip running out — <b>drops</b>.</item>
/// </list>
/// <para>A move that would start at a face marked not to be climbed (<see cref="Obstacle.NoClimb"/>) does not, and
/// is told (<see cref="MotorOutcome.Refused"/>, <see cref="MoveRefused"/>).</para>
/// </summary>
public static partial class BodyMotor
{
    /// <summary>Jump counts as held this long after it is let go (s; the movement design's "about 0.15 s").</summary>
    public const float HeldGrace = 0.15f;

    /// <summary>A press of jump this long or longer is a hold, not a tap (s).</summary>
    public const float HoldSeconds = 0.15f;

    /// <summary>How far above an obstacle's top the feet's path goes over it (m).</summary>
    public const float Clearance = 0.1f;

    /// <summary>How far past the nearest place beyond a vaulted or hurdled thing the feet come down (m).</summary>
    public const float LandRun = 0.3f;

    /// <summary>How much higher than the jump lifts the feet a hop up reaches (m: the knees drawn up).</summary>
    public const float HopTuck = 0.1f;

    /// <summary>How far over a band's top a height still counts as in it (m): a millimetre, for the level's own rounding.</summary>
    public const float Within = 1e-3f;

    /// <summary>How far beyond the body's radius a face may be for a hop up, a vault, a climb or a mantle (m).</summary>
    public const float CloseReach = 0.45f;

    /// <summary>How far beyond the body's radius the hands take a lip (m).</summary>
    public const float GrabReach = 0.3f;

    /// <summary>How far ahead a sprint looks for a sprint vault (s of its speed, beyond the radius and 0.1 m).</summary>
    public const float HurdleLead = 0.12f;

    /// <summary>How long a drop from a lip shows as a drop (s), unless the body lands first.</summary>
    public const float DropSeconds = 0.3f;

    /// <summary>The straight pieces a path is checked in before a move starts.</summary>
    const int PathSamples = 16;

    // ---------------------------------------------------------------- starting moves

    /// <summary>The highest top a tap hops up onto (m above the ground): the tap's jump and the knees drawn up.</summary>
    public static float HopReach(in Locomotion loco) => loco.TapHeight + HopTuck;

    /// <summary>A hop up instead of a tap's jump, when the way leads close to a top the tap reaches that can be stood on.</summary>
    static bool TryHopUp(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, Vector2 way, ref MotorOutcome o)
    {
        if (way == Vector2.Zero) return false;
        float hop = HopReach(loco);
        if (hop <= loco.StepHeight) return false;
        if (!TraversalProbe.Read(m.Level, feet, way, loco, m.R + CloseReach, hop + 0.25f, out var ob)) return false;
        float h = ob.Top - (feet.Z - Skin);
        if (ob.NoClimb)
        {
            if (h > loco.StepHeight && h <= hop + Within) Refuse(ref body, TraversalKind.HopUp, ob, h, ref o, once: false);
            return false;
        }
        if (!ob.HasTop || !ob.Standable || h <= loco.StepHeight || h > hop + Within) return false;
        if (!Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.HopUp, ob, ob.Onto, loco.HopUpSeconds, ref o, h)) return false;
        o.JumpAttempted = intent.Jump;
        o.AttemptOnGround = true;
        return true;
    }

    /// <summary>
    /// In the air: from a jump, a vault, a climb onto or a mantle, else a grab; falling with jump pressed or held, a grab.
    /// From a charged jump with a reach towards the body's own as far as the charge went (<see cref="MovesReach"/>).
    /// </summary>
    /// <remarks>
    /// <paramref name="arc"/> (the velocity, with the commit point on: BodyMotor.Commit.cs): no grab here (the commit's catch takes the lips), and no vault,
    /// climb or mantle at a top the arc clears (<see cref="Clears"/>; the body lands on it, or flies over a thin one).
    /// </remarks>
    static bool TryFromAir(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, Vector2 way, ref MotorOutcome o,
                           OverPaths? paths = null, LedgePaths? ledges = null, Vector3? arc = null)
    {
        if (move.Kind == TraversalKind.Drop) return false;
        bool fromJump = body.Jumped != 0;
        if (!fromJump && body.JumpGrace <= Eps) return false;
        var reach = MovesReach(body, loco);   // from a charge, as far towards the body's own as the charge went
        float ground = body.TakeOffZ - Skin;
        if (fromJump)
        {
            // Read from the ground jumped from (under the feet as they rise), not from the feet: risen by the time the
            // obstacle is in reach, the stride's clearance over the feet would hide a top of 0.6–0.7 m.
            var from = new Vector3(feet.X, feet.Y, MathF.Min(feet.Z, ground));
            float above = ground + reach.MantleMax + 0.2f - from.Z;
            if (TraversalProbe.Read(m.Level, from, way, reach, m.R + CloseReach, above, out var ob))
            {
                float h = ob.Top - ground;
                if (ob.NoClimb)
                {
                    if (h >= reach.VaultMin - Within && h <= reach.MantleMax + Within)
                        Refuse(ref body, h <= reach.VaultMax + Within ? TraversalKind.ClimbOnto : TraversalKind.Mantle, ob, h, ref o, once: true);
                }
                else if (ob.HasTop && !(arc is { } v && Clears(feet, v, loco.Gravity, ob.Lip, ob.LipNormal, ob.Top)))
                {
                    // With the clip-carried moves, no vault below the vault clips' band (their bands rule; BodyMotor.Over.cs).
                    bool under = paths?.Any(TraversalKind.Vault) is { } lowest && h < lowest.Low - Within;
                    if (h > loco.StepHeight && h <= reach.VaultMax + Within && ob.Depth < loco.MantleMinDepth - 1e-4f && ob.Beyond && !under
                        && Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.Vault, ob, ob.Over + new Vector3(way * LandRun, 0f), loco.VaultSeconds, ref o, h))
                        return true;
                    if (ob.Standable && h >= reach.VaultMin - Within && h <= reach.VaultMax + Within
                        && Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.ClimbOnto, ob, ob.Onto, loco.ClimbOntoSeconds, ref o, h))
                        return true;
                    if (ob.Standable && h > reach.VaultMax + Within && h <= reach.MantleMax + Within
                        && Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.Mantle, ob, ob.Onto, loco.MantleSeconds, ref o, h))
                        return true;
                }
            }
        }
        if (reach.StandingReach > 0f && arc is null
            && TraversalProbe.Read(m.Level, feet, way, reach, m.R + GrabReach, reach.StandingReach + 0.3f, out var lip))
        {
            // The hands at the lip (not more than a little over it), or — from a jump, from the mantle band's top to
            // what the hands reach at the jump's top — over it already: a lip of 1.9–2.3 m is under the hands as the
            // body leaves the ground, and was once neither mantled nor grabbed.
            float hands = feet.Z + reach.StandingReach;
            float h = lip.Top - ground;
            bool band = fromJump && h > reach.MantleMax - 1e-4f && h <= reach.StandingReach + body.JumpRise + 0.05f;
            bool meets = hands >= lip.Top - 0.05f && (hands <= lip.Top + 0.25f || band);
            if (lip.NoClimb)
            {
                if (meets) Refuse(ref body, TraversalKind.Grab, lip, h, ref o, once: true);
            }
            else if (lip.HasTop && lip.Holdable && meets) return TryGrab(m, ref body, ref move, ref feet, reach, intent, lip, ref o, ledges);
        }
        return false;
    }

    /// <summary>The hands take the lip: a short move to hang from it, when a hanging body fits there.</summary>
    static bool TryGrab(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, in Obstacle lip, ref MotorOutcome o,
                        LedgePaths? ledges = null)
    {
        // Hanging at arm's length; where that would put the feet into the ground or something under the lip
        // (a lip only a little above the reach), with the arms bent at the height the body is at now.
        // For a body given the ledge's clips, at the clip's hang place where it fits (BodyMotor.Ledge.cs).
        var place = ledges is not null ? OwnMoveHang(ledges, loco, lip.Lip, lip.LipNormal, lip.Top) : default;
        bool clip = ledges is not null && Fits(m.Level, place, loco.Radius, loco.Height);
        var hang = clip ? place : new Vector3(lip.Lip.X + lip.LipNormal.X * (m.R + 0.01f), lip.Lip.Y + lip.LipNormal.Y * (m.R + 0.01f), lip.Top - loco.StandingReach);
        if (!clip && !Fits(m.Level, hang, loco.Radius, loco.Height))
        {
            hang.Z = MathF.Max(hang.Z, feet.Z);
            if (!Fits(m.Level, hang, loco.Radius, loco.Height)) return false;
        }
        float left = body.TakeOffZ - Skin;
        var at = feet;
        if (!Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.Grab, lip, hang, loco.GrabSeconds, ref o, lip.Top - left)) return false;
        o.Grabbed = true;
        o.GripLip = lip.Lip;
        o.GripFeet = at;
        o.GripHeight = lip.Top - left;
        return true;
    }

    /// <summary>From hanging (or the end of a grab), up onto the top when it can be stood on and the way up is clear.</summary>
    static bool TryPullUp(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        var way = new Vector2(-move.LipNormal.X, -move.LipNormal.Y);
        float reach = MovesReach(body, loco).StandingReach;
        if (!TraversalProbe.Read(m.Level, feet, way, loco, m.R + GrabReach, reach + 0.3f, out var ob) || !ob.HasTop || !ob.Standable) return false;
        return Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.PullUp, ob, ob.Onto, loco.PullUpSeconds, ref o);
    }

    /// <summary>
    /// Starts a move from the feet to <paramref name="to"/> when its path is clear for the crouched capsule,
    /// ending whatever move was showing (a drop, a grab, a hang), and runs its first tick. <paramref name="height"/>
    /// is the obstacle's top above the ground the move starts from, when the caller measured it; else above the feet.
    /// </summary>
    static bool Start(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent,
                      TraversalKind kind, in Obstacle ob, Vector3 to, float seconds, ref MotorOutcome o, float height = float.NaN)
    {
        var next = new Traversal
        {
            Kind = kind, Progress = 0f, Seconds = MathF.Max(seconds, World.TickSeconds),
            From = feet, To = to, Lip = ob.Lip, LipNormal = ob.LipNormal, Height = float.IsNaN(height) ? ob.Top - feet.Z : height,
        };
        var tucked = new Mover(m.Level, m.Others, m.Self, loco, TuckedHeight(loco));
        if (!PathClear(tucked, next)) return false;
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = true; }
        move = next;
        o.MoveStarted = kind;
        body.State = BodyState.Traversing;
        body.Stoop = 0f;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.Rising = 0; body.Jumped = 0; body.Charging = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1; body.GroundSeconds = 0f;
        Advance(tucked, ref body, ref move, ref feet, loco, intent, ref o);
        return true;
    }

    /// <summary>The capsule's height through a move: the crouched one.</summary>
    static float TuckedHeight(in Locomotion loco) => MathF.Max(MathF.Min(loco.CrouchHeight, loco.Height), 2f * loco.Radius);

    // ---------------------------------------------------------------- the path

    /// <summary>
    /// Where a move's feet are at <paramref name="t"/> (0 to 1) through it (<paramref name="r"/> the body's
    /// radius). A grab eases straight to where the body hangs. The others go in three parts along the level
    /// line from <see cref="Traversal.From"/> to <see cref="Traversal.To"/>: <b>up</b> — rising to
    /// <see cref="Clearance"/> over the lip while closing on the face no nearer than the radius — <b>across</b>
    /// at that height, and <b>down</b>: onto the top at the end (a hop up, a climb, a mantle, a pull-up) or,
    /// over a thin thing (a hurdle, a vault), once past it by <see cref="LandRun"/> before the end.
    /// </summary>
    public static Vector3 PathAt(in Traversal move, float t, float r)
    {
        t = Math.Clamp(t, 0f, 1f);
        switch (move.Kind)
        {
            case TraversalKind.None or TraversalKind.Hang or TraversalKind.Drop: return move.To;
            case TraversalKind.Grab: return Vector3.Lerp(move.From, move.To, t * t * (3f - 2f * t));
            case TraversalKind.LowerToHang:
            {
                // Out over the edge a little above where the feet stood, then down to the hang.
                var outAt = new Vector3(move.To.X, move.To.Y, move.From.Z + LowerLift);
                if (t < LowerOut)
                {
                    float f = t / LowerOut;
                    var p = Vector3.Lerp(move.From, outAt, f * f * (3f - 2f * f));
                    return new Vector3(p.X, p.Y, move.From.Z + LowerLift * MathF.Min(1f, 4f * f));
                }
                float g = (t - LowerOut) / (1f - LowerOut);
                return Vector3.Lerp(outAt, move.To, g * g * (3f - 2f * g));
            }
            case TraversalKind.LadderOn:
            {
                // From a ladder's top, out over the edge a little above the floor, then down onto the ladder (as a
                // lowering); from its foot, an eased step to its line.
                if (move.From.Z <= move.To.Z + 0.3f) return Vector3.Lerp(move.From, move.To, t * t * (3f - 2f * t));
                var outAt = new Vector3(move.To.X, move.To.Y, move.From.Z + LadderLift);
                if (t < LowerOut)
                {
                    float f = t / LowerOut;
                    var p = Vector3.Lerp(move.From, outAt, f * f * (3f - 2f * f));
                    return new Vector3(p.X, p.Y, move.From.Z + LadderLift * MathF.Min(1f, 4f * f));
                }
                float g = (t - LowerOut) / (1f - LowerOut);
                return Vector3.Lerp(outAt, move.To, g * g * (3f - 2f * g));
            }
            case TraversalKind.Ladder: return Vector3.Lerp(move.From, move.To, move.Progress);
        }
        var a = new Vector2(move.From.X, move.From.Y);
        var ab = new Vector2(move.To.X, move.To.Y) - a;
        float d = ab.Length();
        var inward = new Vector2(-move.LipNormal.X, -move.LipNormal.Y);
        var u = d > 1e-5f ? ab / d : inward;
        float cos = MathF.Max(0.3f, Vector2.Dot(u, inward));
        float sLip = Vector2.Dot(new Vector2(move.Lip.X, move.Lip.Y) - a, u);
        float sNear = Math.Clamp(sLip - (r + 2f * Skin) / cos, 0f, d);
        bool over = GoesOver(move.Kind);
        float sDrop = over ? MathF.Max(sNear, d - LandRun) : d;
        float peak = MathF.Max(move.Lip.Z + Clearance, MathF.Max(move.From.Z, over ? move.From.Z : move.To.Z));
        var (ta, tb) = move.Kind switch
        {
            TraversalKind.Hurdle or TraversalKind.Vault or TraversalKind.StepOver => (0.35f, 0.65f),
            TraversalKind.Mantle or TraversalKind.PullUp => (0.6f, 0.9f),
            _ => (0.5f, 0.88f),
        };
        float s, z;
        if (t < ta)
        {
            float f = t / ta;
            s = sNear * f;
            z = move.From.Z + (peak - move.From.Z) * (1f - (1f - f) * (1f - f));
        }
        else if (t < tb)
        {
            float f = (t - ta) / (tb - ta);
            s = sNear + (sDrop - sNear) * f;
            z = peak;
        }
        else
        {
            float f = (t - tb) / (1f - tb);
            s = sDrop + (d - sDrop) * f;
            z = peak + (move.To.Z - peak) * f * f;
        }
        if (t >= 1f) return move.To;
        return new Vector3(a + u * s, z);
    }

    /// <summary>Whether the capsule follows the whole path without meeting anything (checked in <see cref="PathSamples"/> straight pieces).</summary>
    static bool PathClear(in Mover m, in Traversal move)
    {
        var prev = move.From;
        for (int i = 1; i <= PathSamples; i++)
        {
            var p = PathAt(move, i / (float)PathSamples, m.R);
            var d = p - prev;
            float len = d.Length();
            if (len > 1e-6f && Sweep(m, prev, d / len, len + Skin, out var hit) && hit.Distance < len - 1e-4f) return false;
            prev = p;
        }
        return true;
    }

    // ---------------------------------------------------------------- running them

    /// <summary>One tick along the move's path; at its end, what follows (standing, landing, hanging or pulling up).</summary>
    /// <remarks><paramref name="clipHang"/>: a grab or a lowering ending in the clip's hang place (BodyMotor.Ledge.cs) hangs, and the caller puts it on the clip.</remarks>
    static void Advance(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o, bool clipHang = false)
    {
        const float dt = World.TickSeconds;
        float t = MathF.Min(1f, (move.Progress * move.Seconds + dt) / move.Seconds);
        var p = PathAt(move, t, m.R);
        var d = p - feet;
        float len = d.Length();
        if (len > 1e-7f)
        {
            var dir = d / len;
            if (Sweep(m, feet, dir, len + Skin, out var hit) && hit.Distance < len - 1e-4f)
            {
                feet += dir * MathF.Max(0f, hit.Distance - Skin);
                Abort(m, ref body, ref move, ref feet, loco, ref o);
                return;
            }
        }
        body.Velocity = d / dt;
        feet = p;
        move.Progress = t;
        var way = new Vector2(move.To.X - move.From.X, move.To.Y - move.From.Y);
        // Lowering to hang, the body turns to face the edge it lowers from (it steps off backwards, as a person does).
        if (way.LengthSquared() < 1e-4f || move.Kind is TraversalKind.LowerToHang or TraversalKind.LadderOn) way = new Vector2(-move.LipNormal.X, -move.LipNormal.Y);
        // Getting onto a ladder, the body turns to face it whatever the view (it is on the ladder facing it).
        body.Yaw = DetMath.WrapAngle(intent.Faces && move.Kind != TraversalKind.LadderOn ? intent.Facing
            : way.LengthSquared() > 1e-8f ? TurnToward(body.Yaw, DetMath.Atan2(way.Y, way.X), loco.TurnDegreesPerSecond * DetMath.DegToRad * dt) : body.Yaw);
        if (t < 1f) return;

        var kind = move.Kind;
        if (kind == TraversalKind.Grab)
        {
            // Forward or jump held pulls up at once (running at a wall of 2.4 m with forward held, a tap
            // goes up and over); nothing held, the body hangs.
            if (!clipHang && (body.JumpGrace > Eps || ForwardTo(intent, move.LipNormal)) && TryPullUp(m, ref body, ref move, ref feet, loco, intent, ref o)) return;
            o.MoveEnded = TraversalKind.Grab; o.MoveCompleted = true;
            BeginHang(ref body, ref move, feet, loco, ref o);
            return;
        }
        if (kind == TraversalKind.LadderOn)
        {
            // On the ladder (BodyMotor.Ladder.cs).
            o.MoveEnded = kind; o.MoveCompleted = true;
            BeginLadder(m, ref body, ref move, ref feet, loco, ref o);
            return;
        }
        if (kind == TraversalKind.LowerToHang)
        {
            o.MoveEnded = kind; o.MoveCompleted = true;
            // A drop less than the hands' reach is climbed down, standing at its foot (BodyMotor.Edges.cs).
            if (!clipHang && ClimbsDown(move, loco)) { EndClimbDown(m, ref body, ref move, ref feet, loco, intent, ref o); return; }
            BeginHang(ref body, ref move, feet, loco, ref o);
            return;
        }
        var v = Vector3.Zero;
        if (GoesOver(kind))
        {
            var across = new Vector2(move.To.X - move.From.X, move.To.Y - move.From.Y);
            v = new Vector3(across / move.Seconds, 0f);
        }
        o.MoveEnded = kind; o.MoveCompleted = true;
        move = default;
        SetDown(m, ref body, ref feet, loco, intent, v);
    }

    /// <summary>A move ended: the body crouched if it cannot stand there (or crouch is held), on the ground it finds under it or else in the air.</summary>
    static void SetDown(in Mover m, ref Body body, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, Vector3 velocity)
    {
        body.Crouched = intent.Crouch || !Fits(m.Level, feet, loco.Radius, loco.Height) ? 1 : 0;
        body.State = BodyState.Air;
        var after = new Mover(m.Level, m.Others, m.Self, loco, body.CapsuleHeight(loco));
        body.Velocity = velocity;
        body.HighestZ = feet.Z; body.TakeOffZ = feet.Z; body.Rising = 0; body.Jumped = 0; body.Powered = 0;
        body.AirSeconds = 0f; body.GroundSeconds = 0f;
        if (Probe(after, feet, 4f * Skin + 0.02f, keeping: false, out var s) && s.FeetZ >= feet.Z - 4f * Skin - 0.02f && s.FeetZ <= feet.Z + m.R)
        {
            feet.Z = s.FeetZ > feet.Z ? RaiseTo(after, feet, s.FeetZ) : s.FeetZ;
            body.State = BodyState.Ground;
            body.GroundNormal = s.Normal;
            body.GroundTriangle = s.Triangle;
            body.Coyote = 0f;
        }
        else
        {
            body.Coyote = loco.CoyoteSeconds;
            body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1;
        }
    }

    /// <summary>The body hangs from the lip where the grab left it.</summary>
    static void BeginHang(ref Body body, ref Traversal move, Vector3 feet, in Locomotion loco, ref MotorOutcome o)
    {
        var lip = move.Lip; var n = move.LipNormal;
        body.State = BodyState.Hanging;
        body.Crouched = 0;
        body.Velocity = Vector3.Zero;
        body.ForwardArmed = 0;
        body.AirSeconds = 0f;
        body.Shimmy = 0;
        move = new Traversal
        {
            Kind = TraversalKind.Hang, Seconds = loco.GripSeconds > 0f ? loco.GripSeconds : float.PositiveInfinity,
            From = feet, To = feet, Lip = lip, LipNormal = n, Height = lip.Z - feet.Z,
        };
        o.MoveStarted = TraversalKind.Hang;
    }

    /// <summary>One tick hanging: pulling up on jump (or a fresh forward), letting go on crouch or when the grip runs out.</summary>
    static void HangTick(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent, ref MotorOutcome o)
    {
        const float dt = World.TickSeconds;
        feet = move.To;
        body.Velocity = Vector3.Zero;
        body.AirSeconds += dt;
        bool limited = loco.GripSeconds < float.PositiveInfinity && loco.GripSeconds > 0f;
        move.Seconds = loco.GripSeconds > 0f ? loco.GripSeconds : float.PositiveInfinity;
        move.Progress = limited ? MathF.Min(1f, body.AirSeconds / loco.GripSeconds) : 0f;
        var inward = new Vector2(-move.LipNormal.X, -move.LipNormal.Y);
        body.Yaw = DetMath.WrapAngle(intent.Faces ? intent.Facing
            : inward.LengthSquared() > 1e-8f ? TurnToward(body.Yaw, DetMath.Atan2(inward.Y, inward.X), loco.TurnDegreesPerSecond * DetMath.DegToRad * dt) : body.Yaw);
        bool forward = ForwardTo(intent, move.LipNormal);
        if (!forward) body.ForwardArmed = 1;
        if ((intent.Crouch && body.CrouchWas == 0) || (limited && body.AirSeconds >= loco.GripSeconds))
        {
            StopShimmy(ref body, move, feet, ShimmyStop.Left, ref o);
            LetGo(m, ref body, ref move, feet, ref o);
            return;
        }
        // Jump with back held pushes off backwards (BodyMotor.Ladder.cs).
        if (intent.Jump && BackFrom(intent, move.LipNormal))
        {
            PushOffLip(m, ref body, ref move, feet, loco, ref o);
            return;
        }
        var lip = move.Lip;
        if ((intent.Jump || (forward && body.ForwardArmed != 0)) && TryPullUp(m, ref body, ref move, ref feet, loco, intent, ref o))
        {
            if (body.Shimmy != 0) { body.Shimmy = 0; o.ShimmyStopped = ShimmyStop.Left; o.ShimmyLip = lip; o.ShimmyFeet = move.From; }
            return;
        }
        // The wish's sideways part shimmies along the lip (BodyMotor.Ladder.cs).
        Shimmy(m, ref body, ref move, ref feet, loco, intent, ref o);
    }

    /// <summary>Whether the wish heads in at a lip whose face's outward normal is <paramref name="lipNormal"/>: forward, to pull up.</summary>
    static bool ForwardTo(in MoveIntent intent, Vector3 lipNormal)
    {
        float wl = intent.Direction.Length();
        return wl > 0.3f && Vector2.Dot(intent.Direction / wl, new Vector2(-lipNormal.X, -lipNormal.Y)) > 0.5f;
    }

    /// <summary>The hands let go: the body falls from where it hung, showing a drop for a moment.</summary>
    static void LetGo(in Mover m, ref Body body, ref Traversal move, Vector3 feet, ref MotorOutcome o)
    {
        o.Dropped = true;
        o.GripLip = move.Lip;
        o.GripFeet = feet;
        o.MoveEnded = move.Kind; o.MoveCompleted = true;
        var to = m.Level.Ray(feet + new Vector3(0, 0, 0.01f), -Vector3.UnitZ, 50f) is { } under ? under.Point : feet;
        move = new Traversal
        {
            Kind = TraversalKind.Drop, Seconds = DropSeconds, From = feet, To = to,
            Lip = move.Lip, LipNormal = move.LipNormal, Height = move.Lip.Z - feet.Z,
        };
        o.MoveStarted = TraversalKind.Drop;
        body.State = BodyState.Air;
        body.Velocity = Vector3.Zero;
        body.Coyote = 0f; body.JumpBuffer = 0f; body.AirSeconds = 0f;
        body.TakeOffZ = feet.Z; body.HighestZ = feet.Z; body.Rising = 0; body.Jumped = 0; body.Powered = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1;
    }

    /// <summary>A move stopped on the way (or the body was left in a move with none to follow): it falls from where it is.</summary>
    static void Abort(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, ref MotorOutcome o)
    {
        if (move.Kind != TraversalKind.None) { o.MoveEnded = move.Kind; o.MoveCompleted = false; }
        move = default;
        body.Ladder = 0; body.Shimmy = 0;
        body.State = BodyState.Air;
        body.Crouched = Fits(m.Level, feet, loco.Radius, loco.Height) ? 0 : 1;
        body.Velocity = Vector3.Zero;
        body.Coyote = 0f; body.AirSeconds = 0f;
        body.TakeOffZ = feet.Z; body.HighestZ = feet.Z; body.Rising = 0; body.Jumped = 0; body.Powered = 0;
        body.GroundNormal = Vector3.UnitZ; body.GroundTriangle = -1;
    }
}
