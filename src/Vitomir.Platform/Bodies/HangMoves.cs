// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips: the hang's leaps and its corners.
// The motor's side: BodyMotor.Hang.cs.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>the hang's corners and leaps</b> for the motor (the people's <c>PersonHangSettings</c>, riding on tier 1's moves): whether they are taken and the
/// numbers they are found by. Null on <see cref="MovesRules.Hang"/>: the earlier build's (corners are a stop, no leaps).
/// </summary>
public sealed class HangRules
{
    /// <summary>Shimmying into a corner of the lip goes round it by itself where it can be taken.</summary>
    public bool Corners { get; init; } = true;

    /// <summary>The leaps from the braced hang (to a side, up, down, back to a facing ledge).</summary>
    public bool Leaps { get; init; } = true;

    /// <summary>How far a leap's target lip may be off the clip's own offset (m; along the lip, up or down, and the leap back's distance) and still be leapt to, warped onto it.</summary>
    public float LeapTolerance { get; init; } = 0.25f;

    /// <summary>How far the corner may be off the corner clip's own from the hang's middle (m), nearer than it; further, within half a shimmy's step (the cycle that reaches it).</summary>
    public float CornerSlack { get; init; } = 0.15f;

    /// <summary>How far the lip's turn at a corner, or a facing ledge's face, may be off the clip's own (a quarter turn, square behind) and still be taken (radians).</summary>
    public float AngleTolerance { get; init; } = 15f * DetMath.DegToRad;

    /// <summary>
    /// A side leap only where the lip <b>breaks</b> between the hands and the target (a gap at least
    /// <see cref="LeapTolerance"/> wide along it, or a different lip); on a lip that just goes on, jump with a side held does as with no lip (the pull-up
    /// where there is a top, else the shimmy while the side is held). False: the earlier build's (the leap along a lip that goes on).
    /// </summary>
    public bool Breaks { get; init; }
}

/// <summary>What the hang's move is (<see cref="HangMove.Kind"/>).</summary>
public enum HangMoveKind : byte
{
    None = 0,
    /// <summary>A leap from the braced hang to the next ledge.</summary>
    Leap = 1,
    /// <summary>Round a corner of the lip.</summary>
    Corner = 2,
}

/// <summary>Which leap (<see cref="HangMove.Way"/>, and in the log).</summary>
public enum HangLeap : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Up = 3,
    Down = 4,
    Back = 5,
}

/// <summary>Why a corner or a leap was not taken (the log says it).</summary>
public enum HangRefusal : byte
{
    None = 0,
    /// <summary>No lip to leap to within the tolerance of the clip's own offset (the leap's control falls back to today's: the shimmy, the pull-up, the drop, the push-off).</summary>
    NoLip = 1,
    /// <summary>A corner's other face has no lip long enough to hang on (its end's hang and half the hands' spacing beyond).</summary>
    ShortLip = 2,
    /// <summary>The target has no wall under its edge for the feet (a leap's, a braced corner's end).</summary>
    NoWall = 3,
    /// <summary>A free corner's end has a wall under it (the free clips' legs swing under: they assume none).</summary>
    Wall = 4,
    /// <summary>The body does not hang at the target (no room).</summary>
    NoRoom = 5,
    /// <summary>The clip's way is blocked (its path swept before it starts).</summary>
    Blocked = 6,
    /// <summary>The corner is nearer the hang's middle than the clip can start from.</summary>
    TooNear = 7,
    /// <summary>The lip turns too far off a quarter turn, or the facing ledge is not square to this one.</summary>
    Angle = 8,
    /// <summary>The leaps start braced (the feet push off the wall): none from a free hang.</summary>
    FreeHang = 9,
    /// <summary>The set has no clip for it.</summary>
    NoClip = 10,
    /// <summary>A side leap's lip goes on from the hands to the target, no break between (the shimmy's, not a leap's).</summary>
    Continuous = 11,
}

/// <summary>
/// <b>the hang's move under way</b> (the motor's; the people's digested <c>PersonHang</c>, only with the corners and leaps on): a leap's or a corner's
/// difference at its end taken out over its window (the clip laid on the real lip), the body's facing at its start and the difference of the turn at its end,
/// and the hang it ends in (its place, the way in, the top, the lip and its face's normal). A corner's wish carried after it: the way the shimmy goes on and the
/// wish that asked for it, while that wish holds.
/// </summary>
public struct HangMove
{
    public HangMoveKind Kind;
    /// <summary>A leap's <see cref="HangLeap"/>, a corner's way (+1 to the left, −1 to the right).</summary>
    public int Way;
    /// <summary>The difference at the end (m) taken out between <see cref="EndFrom"/> and <see cref="EndTo"/> (s of the clip).</summary>
    public Vector3 End;
    public float EndFrom, EndTo;
    /// <summary>The body's facing at the start (radians), and the turn's difference from the clip's own at the end (radians, over the end's window).</summary>
    public float Yaw0, YawEnd;
    /// <summary>The hang it ends in: its place (the free hang's, from the real edge), the level unit way in, the top (world Z), the lip and its face's outward normal.</summary>
    public Vector3 Place;
    public Vector2 In;
    public float Top;
    public Vector3 Lip, LipNormal;
    /// <summary>1 when the hang it ends in is braced.</summary>
    public int Braced;
    /// <summary>After a corner: the shimmy's way the held wish keeps (+1 left, −1 right; 0 none) and that wish (level unit), while it holds.</summary>
    public int Carry;
    public Vector2 Wish;
}

/// <summary>A leap or a corner began (<paramref name="Leap"/> for a leap; a corner's <paramref name="Outside"/> and way), from the feet at <paramref name="From"/> to the hang at <paramref name="To"/>.</summary>
public readonly record struct HangMoveStarted(EntityId Entity, long Tick, HangMoveKind Kind, HangLeap Leap, bool Outside, int Way, bool Braced, Vector3 From, Vector3 To, Vector3 Lip);

/// <summary>A leap or a corner asked for (by its control, or a shimmy into a corner) and not taken, and why, about <paramref name="At"/>.</summary>
public readonly record struct HangMoveRefused(EntityId Entity, long Tick, HangMoveKind Kind, HangLeap Leap, bool Outside, int Way, HangRefusal Why, Vector3 At);

/// <summary>The hang's corners and leaps for the log.</summary>
public static class HangEvents
{
    public static WorldBuilder Register(WorldBuilder b) => b.Event<HangMoveStarted>().Event<HangMoveRefused>();

    public static void Publish(World world, EntityId e, in MotorOutcome o)
    {
        if (o.HangStarted != HangMoveKind.None)
            world.Events<HangMoveStarted>().Publish(new HangMoveStarted(e, world.Tick, o.HangStarted, o.HangLeap, o.HangOutside, o.HangWay, o.HangBraced, o.HangFrom, o.HangTo, o.HangLip));
        if (o.HangRefused != HangRefusal.None)
            world.Events<HangMoveRefused>().Publish(new HangMoveRefused(e, world.Tick, o.HangRefusedKind, o.HangLeap, o.HangOutside, o.HangWay, o.HangRefused, o.HangAt));
    }
}
