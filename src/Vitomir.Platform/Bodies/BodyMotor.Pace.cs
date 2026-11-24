// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>Over small things by pace</b> (ours): the moves a body makes by itself,
/// on the ground, from how fast it goes — <i>"at a run a higher step over thin things to 0.5 m; at a sprint a vault
/// over things to 1.0 m high and 0.7 m deep; never onto"</i>, to be tuned later (the numbers are the
/// profile's).
/// <list type="bullet">
/// <item><b>The run's step-over</b> (<see cref="TraversalKind.StepOver"/>): moving at a run or faster — at least
/// <see cref="PaceShare"/> of the run's speed — into a <b>thin</b> thing (shallower than
/// <see cref="Locomotion.MantleMinDepth"/>) above the stride to <see cref="Locomotion.StepOverMax"/>: a higher step
/// over it, a short move of its own (at least <see cref="StepOverSeconds"/>, at the body's own pace).</item>
/// <item><b>The sprint vault</b> (<see cref="TraversalKind.Hurdle"/>, the name kept from the first hurdle, which it
/// replaces — the motion slot, the reach highlight's colour and the log name it so): sprinting — at least
/// <see cref="PaceShare"/> of the sprint's speed — into a thing above the stride to
/// <see cref="Locomotion.SprintVaultMax"/> high and <see cref="Locomotion.SprintVaultDepth"/> deep, thin or not (a low
/// fence, a bench, a trough): over it in <see cref="Locomotion.HurdleSeconds"/>.</item>
/// </list>
/// Both go <b>over, never onto</b>: only where the thing ends in a face (<see cref="FarFaceDrop"/>) down to standable
/// ground beyond within the stride (<see cref="Locomotion.StepHeight"/>) of the feet — so never onto the side of a ramp
/// going on up or down (the finding: a sprint hurdled onto a 40° ramp's side, landing 0.5 m up) — and only a face
/// <b>rising from the ground the feet stand on</b> (<see cref="RisesFromFeet"/>): the second riser of a stair starts
/// on the first tread, within the stride, and a stair is walked in stride, never vaulted or stepped over (a walk in play:
/// a sprint diagonally across a stair's foot vaulted its corner). A sprint tries the
/// vault, then the step-over; a run the step-over. At a walk nothing starts: the body stops at it, as at anything above
/// the stride. <b>A body following a route starts neither</b> (<see cref="FollowsRoute"/>; <i>"NPCs
/// just walking shouldn't ever hop vault mantle or fall off anything"</i>).
/// </summary>
public static partial class BodyMotor
{
    /// <summary>The share of the gait's speed the body must be going at for the step-over (of the run's) or the sprint vault (of the sprint's).</summary>
    public const float PaceShare = 0.7f;

    /// <summary>The step-over's least length (s; "about 0.35 s"); longer when the path is longer than the body's pace covers in it.</summary>
    public const float StepOverSeconds = 0.35f;

    /// <summary>How far ahead a run looks for a step-over (s of its speed, beyond the radius and 0.1 m): a stride's lift, close.</summary>
    public const float StepOverLead = 0.05f;

    /// <summary>
    /// How far below an obstacle's top the ground just past its far side must be for the far side to be a face (m): a
    /// slope going on down falls a little at a time and is not one, and a vault or a step-over never lands on it.
    /// </summary>
    public const float FarFaceDrop = 0.25f;

    /// <summary>
    /// How far the foot of a face may be from the ground the body stands on for a pace move to take it (m; "within
    /// a couple of centimetres"), and how far in front of the face its foot is looked for (m).
    /// </summary>
    public const float FootTolerance = 0.03f, FootLook = 0.05f;

    /// <summary>
    /// Whether an intent is a body following a route (<see cref="EdgeGuard.Footing"/>, which the route steering sets,
    /// <c>Routes.Toward</c>): such a body starts no move by itself — no step-over, no sprint vault — and asks for no
    /// hop (it never presses jump); 3.11, DH, an earlier build.
    /// </summary>
    public static bool FollowsRoute(in MoveIntent intent) => intent.Guard == EdgeGuard.Footing;

    /// <summary>The moves that go over a thing and come down beyond it (their path's shape and the pace they keep after).</summary>
    static bool GoesOver(TraversalKind kind) => kind is TraversalKind.Hurdle or TraversalKind.Vault or TraversalKind.StepOver;

    /// <summary>On the ground, moving: the sprint vault at a sprint, else the run's step-over at a run or faster (not for a route-follower).</summary>
    /// <remarks>
    /// <paramref name="clipVaults"/> (the player's vault clips, with the run's own vault on): a thin thing the clips vault (in their bands) is theirs,
    /// by the run's own vault square to it (<c>BodyMotor.AutoVault</c>), never the sprint vault's — met at a slant beyond its cone, nothing; the sprint
    /// vault keeps what the clips do not take (a bench, a trough, a rail of 0.5 m).
    /// </remarks>
    /// <remarks>
    /// <paramref name="low"/> (tier 1's low vaults, with <paramref name="clipVaults"/>): what <c>hurdle_low</c> or <c>vault_speed</c> serves is theirs too
    /// (<see cref="OverPaths.LowFor"/>), neither the sprint vault's nor the step-over's; they keep the rest (a deep top over 0.65 m at a sprint).
    /// </remarks>
    static bool TryPaceMove(in Mover m, ref Body body, ref Traversal move, ref Vector3 feet, in Locomotion loco, in MoveIntent intent,
                            Gait gait, Vector2 vh, ref MotorOutcome o, OverPaths? clipVaults = null, bool low = false)
    {
        if (gait < Gait.Run || FollowsRoute(intent)) return false;
        float speed = vh.Length();
        if (speed < 1e-4f) return false;
        var way = vh / speed;
        if (gait >= Gait.Sprint && SprintVaultAhead(m.Level, feet, way, speed, loco, out var ob, out var to)
            && !(clipVaults is not null && ob.Depth < loco.MantleMinDepth - 1e-4f && clipVaults.For(TraversalKind.Vault, ob.Top - (feet.Z - Skin)) is not null)
            && !(clipVaults is not null && low && clipVaults.LowFor(ob.Top - (feet.Z - Skin), ob.Depth, ob.Depth < loco.MantleMinDepth - 1e-4f) is not null)
            && Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.Hurdle, ob, to, loco.HurdleSeconds, ref o))
            return true;
        if (!StepOverAhead(m.Level, feet, way, speed, loco, out ob, out to)) return false;
        if (clipVaults is not null && low && clipVaults.LowFor(ob.Top - (feet.Z - Skin), ob.Depth, true) is not null) return false;   // the hurdle's
        float d = Vector2.Distance(new Vector2(feet.X, feet.Y), new Vector2(to.X, to.Y));
        return Start(m, ref body, ref move, ref feet, loco, intent, TraversalKind.StepOver, ob, to, MathF.Max(StepOverSeconds, d / speed), ref o);
    }

    /// <summary>
    /// Whether a body at <paramref name="feet"/> going along the unit <paramref name="way"/> at <paramref name="speed"/>
    /// meets a thing it <b>sprint-vaults</b> (above the stride to <see cref="Locomotion.SprintVaultMax"/>, at most
    /// <see cref="Locomotion.SprintVaultDepth"/> deep, its face rising from the feet's ground, ending in a face down to
    /// ground within the stride of the feet),
    /// and where its feet come down (<paramref name="to"/>). Pure: the reach highlight asks it too.
    /// </summary>
    public static bool SprintVaultAhead(CollisionWorld level, Vector3 feet, Vector2 way, float speed, in Locomotion loco, out Obstacle ob, out Vector3 to)
    {
        ob = default; to = default;
        if (speed < PaceShare * loco.SprintSpeed || loco.SprintVaultMax <= loco.StepHeight) return false;
        if (!TraversalProbe.Read(level, feet, way, loco, loco.Radius + 0.1f + speed * HurdleLead, loco.SprintVaultMax + 0.25f, out ob) || !ob.HasTop) return false;
        float h = ob.Top - feet.Z;
        if (h <= loco.StepHeight || h > loco.SprintVaultMax || ob.Depth > loco.SprintVaultDepth + 1e-4f) return false;
        return RisesFromFeet(level, feet, ob) && Beyond(level, feet, way, loco, ob, out to);
    }

    /// <summary>
    /// Whether a body at <paramref name="feet"/> going along the unit <paramref name="way"/> at <paramref name="speed"/>
    /// meets a <b>thin</b> thing it <b>steps over</b> at a run (above the stride to <see cref="Locomotion.StepOverMax"/>,
    /// shallower than <see cref="Locomotion.MantleMinDepth"/>, its face and the ground beyond as for the vault), and where its feet come
    /// down (<paramref name="to"/>). Pure: the reach highlight asks it too.
    /// </summary>
    public static bool StepOverAhead(CollisionWorld level, Vector3 feet, Vector2 way, float speed, in Locomotion loco, out Obstacle ob, out Vector3 to)
    {
        ob = default; to = default;
        if (speed < PaceShare * loco.RunSpeed || loco.StepOverMax <= loco.StepHeight) return false;
        if (!TraversalProbe.Read(level, feet, way, loco, loco.Radius + 0.1f + speed * StepOverLead, loco.StepOverMax + 0.25f, out ob) || !ob.HasTop) return false;
        float h = ob.Top - feet.Z;
        if (h <= loco.StepHeight || h > loco.StepOverMax || ob.Depth >= loco.MantleMinDepth - 1e-4f) return false;
        return RisesFromFeet(level, feet, ob) && Beyond(level, feet, way, loco, ob, out to);
    }

    /// <summary>
    /// Whether the face the probe met (<paramref name="ob"/>) rises from the ground the body stands on: the ground
    /// <see cref="FootLook"/> in front of the face, under its lip, is within <see cref="FootTolerance"/> of the ground
    /// under the feet — level with it, or on the plane of the slope the feet stand on, carried on to the face. A face
    /// whose foot is higher (the second riser of a stair, on the first tread within the stride) or lower (a face over a
    /// dip) is not one a pace move takes, nor one the reach highlight shows in the hurdle's orange. Two rays, made only
    /// once the thing is in the pace's band. Pure.
    /// </summary>
    public static bool RisesFromFeet(CollisionWorld level, Vector3 feet, in Obstacle ob)
    {
        var at = new Vector2(ob.Lip.X + ob.LipNormal.X * FootLook, ob.Lip.Y + ob.LipNormal.Y * FootLook);
        float from = ob.Top - 0.03f, ground = feet.Z - Skin;
        if (level.Ray(new Vector3(at, from), -Vector3.UnitZ, from - (ground - 0.5f)) is not { } foot) return false;
        if (MathF.Abs(foot.Point.Z - ground) <= FootTolerance) return true;
        // On a slope: the plane under the feet carried on to the foot.
        if (level.Ray(new Vector3(feet.X, feet.Y, feet.Z + 0.1f), -Vector3.UnitZ, 0.1f + Skin + 0.1f) is not { } under) return false;
        var n = level.Normal(under.Triangle);
        if (n.Z < 0.3f) return false;
        float plane = under.Point.Z - (n.X * (foot.Point.X - under.Point.X) + n.Y * (foot.Point.Y - under.Point.Y)) / n.Z;
        return MathF.Abs(foot.Point.Z - plane) <= FootTolerance;
    }

    /// <summary>
    /// Over, never onto: the probe found standable ground beyond (<see cref="Obstacle.Beyond"/>) within the stride of the
    /// feet's height, and the thing's far side is a face — the ground just past its top's end at least
    /// <see cref="FarFaceDrop"/> below the top and within the stride of the feet. The feet come down
    /// <see cref="LandRun"/> past the nearest place beyond.
    /// </summary>
    static bool Beyond(CollisionWorld level, Vector3 feet, Vector2 way, in Locomotion loco, in Obstacle ob, out Vector3 to)
    {
        to = default;
        float stride = MathF.Max(0f, loco.StepHeight);
        if (!ob.Beyond || MathF.Abs(ob.Over.Z - feet.Z) > stride) return false;
        var past = new Vector2(ob.Lip.X, ob.Lip.Y) + way * (ob.Depth + 1.5f * TraversalProbe.DepthStep);
        float from = ob.Top + TraversalProbe.TopTolerance;
        if (level.Ray(new Vector3(past, from), -Vector3.UnitZ, from - (feet.Z - TraversalProbe.BeyondDrop)) is not { } g) return false;
        if (ob.Top - g.Point.Z < FarFaceDrop || g.Point.Z > feet.Z + stride) return false;
        to = ob.Over + new Vector3(way * LandRun, 0f);
        return true;
    }
}
