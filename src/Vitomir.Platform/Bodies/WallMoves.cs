// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository's clips: climbing walls
// by their holds. The motor's side: BodyMotor.Wall.cs.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>climbing walls</b> for the motor (the people's <c>PersonWallSettings</c>, riding on the hang's corners and leaps): the numbers it is found and
/// played by. Null on <see cref="MovesRules.Wall"/>: the earlier build's (no wall is climbed).
/// </summary>
public sealed class WallRules
{
    /// <summary>How far a hand or a foot may be moved to a level's own hold (m), and a move whose end has a limb with none within it is refused.</summary>
    public float HoldReach { get; init; } = 0.25f;

    /// <summary>The rate the wall's clips play at (1: their own; the capsule on their paths at it).</summary>
    public float Rate { get; init; } = 1f;

    /// <summary>As the hang's corners (the earlier build's <see cref="HangRules.CornerSlack"/>, <see cref="HangRules.AngleTolerance"/>): how much nearer than its clip's own a corner may be, how far off square.</summary>
    public float CornerSlack { get; init; } = 0.15f;
    public float AngleTolerance { get; init; } = 15f * DetMath.DegToRad;

    /// <summary>
    /// "smoother ... transition through them faster": the rate the getting-on, the getting-off, the let-go and the push-off, the clip over
    /// the top and the pull-up after it, and the corners play at (1: their own). The moves are at <see cref="Rate"/>.
    /// </summary>
    public float MoveRate { get; init; } = 1f;

    /// <summary>
    /// The flow — a held move starts out of the getting-on as soon as the limbs it moves first have their holds, the next move out of the last as its last
    /// limb lands, and over the top straight on into the pull-up — and its blend (s; the capsule's difference and the animation's fade over it). 0: the earlier build's (no
    /// flow, no diagonals, no softened catch: every an earlier build thing off).
    /// </summary>
    public float FlowBlend { get; init; }

    /// <summary>Forward or back with a side climbs diagonally (the side's and the up's or down's quick or careful moves blended).</summary>
    public bool Diagonals { get; init; }

    /// <summary>Whether any of the earlier build's is on (the flow's state, <see cref="WallFlow"/>, kept).</summary>
    public bool Flows => FlowBlend > 0f;

    /// <summary>These numbers with the earlier build's off, for a body without the flow's state.</summary>
    public static WallRules NoFlow(WallRules r) => new() { HoldReach = r.HoldReach, Rate = r.Rate, CornerSlack = r.CornerSlack, AngleTolerance = r.AngleTolerance };
}

/// <summary>
/// <b>the wall's flow</b> — the motor's state beside its <see cref="WallMove"/> (a store of its own, <c>PersonWallFlow</c>, kept only with the earlier build's on, so a
/// tape of the earlier build's digests as before): a diagonal move's second clip (the up's or the down's, blended into the side's), and the velocity a clip was begun with
/// that its path does not have, carried and taken out with the capsule's difference (a catch from a run, a getting-on from a run-up, a flowing seam).
/// </summary>
public struct WallFlow
{
    /// <summary>A diagonal move: 1 + the index of the up's or the down's move (<see cref="LedgePaths.Clips"/>) whose path is added to the side's, 0 for none.</summary>
    public int Diag;
    /// <summary>The velocity (m/s, the level's axes) the clip under way was begun with beyond its path's own: across taken out over the ledge state's across window, up over its up window.</summary>
    public Vector3 Carry;
    /// <summary>The wish last held (the level's axes) and the ticks since it was (held on for <c>BodyMotor.InputHoldTicks</c>, so a brief release does not break a chain).</summary>
    public Vector2 Wish;
    public int WishAge;
    /// <summary>
    /// The intent shown when a move is refused — the leading hand's reach toward the wish (m, on the wall: to the left, up), eased toward it while the wish is
    /// held and refused and back to nought after; and where it is eased toward this tick.
    /// </summary>
    public Vector2 Lean, LeanTo;
}

/// <summary>What a wall's move is (in the log, and <see cref="WallMoveStarted"/>).</summary>
public enum WallAct : byte
{
    None = 0,
    /// <summary>Onto the wall from the ground, standing (<c>wall_on_ground</c>).</summary>
    OnGround = 1,
    /// <summary>Onto the wall with a jump: a tap with a run-up (<c>wall_on_jump</c>).</summary>
    OnJump = 2,
    /// <summary>Caught in the air: jumped at, or use held (<c>wall_on_jump</c> from its hands' contact).</summary>
    Catch = 3,
    /// <summary>A move on the wall (careful, quick or long; up, down, to a side).</summary>
    Move = 4,
    /// <summary>Round a corner of the wall.</summary>
    Corner = 5,
    /// <summary>Over the top into the braced hang (<c>wall_to_hang_braced</c>).</summary>
    ToHang = 6,
    /// <summary>Down off it at its foot (<c>wall_off_ground</c>).</summary>
    Off = 7,
    /// <summary>The hands let go: the fall (<c>wall_let_go</c>).</summary>
    LetGo = 8,
    /// <summary>A push off it backwards: the fall (<c>wall_push_off</c>).</summary>
    PushOff = 9,
}

/// <summary>Why a wall's move was not taken (the log says it, once a place).</summary>
public enum WallRefusal : byte
{
    None = 0,
    /// <summary>A face with no holds, in the masquerade in a public place (only out of the masquerade, or in a free place).</summary>
    Sheer = 1,
    /// <summary>The level says not to be climbed (a no-climb mesh or area), or a ladder's face.</summary>
    NoClimb = 2,
    /// <summary>The face (or the level's own holds) does not go on under every hand and foot where the move ends.</summary>
    NoHolds = 3,
    /// <summary>The body does not fit there.</summary>
    NoRoom = 4,
    /// <summary>The clip's way is blocked (its path swept before it starts).</summary>
    Blocked = 5,
    /// <summary>At the top of the face with no edge the clip over the top can take (no lip within half a step of its own).</summary>
    Top = 6,
    /// <summary>A corner nearer the wall position's middle than its clip can start from.</summary>
    TooNear = 7,
    /// <summary>A corner whose faces are not square to each other within the tolerance.</summary>
    Angle = 8,
    /// <summary>A corner whose other face is too short (the end's holds, or its wall position, off it).</summary>
    ShortFace = 9,
    /// <summary>The set has no clip for it.</summary>
    NoClip = 10,
}

/// <summary>
/// <b>a person on a wall</b> — the motor's state beside its <see cref="LedgeMove"/> (which carries the clip, its time and the wall position: its
/// <see cref="LedgeMove.Place"/>, the capsule in <c>wall_idle</c>, and <see cref="LedgeMove.In"/>, the way into the face); part of the people's digested
/// <c>PersonWall</c>. The face's kind (holds, the level's own or the lattice, or sheer), a corner's (or the clip over the top's) difference at its end taken out
/// over its window, the facing at its start and its turn's difference, the wall position it ends at; a corner's wish carried; the edge over the top; the
/// moment after leaving a wall before one is caught again; the move's rate; and the last refusal told (said once a place).
/// </summary>
public struct WallMove
{
    /// <summary>1 on a face with nothing to hold (the splayed grip), 0 on a wall with holds.</summary>
    public int Sheer;
    /// <summary>1 where the level gives its own holds (the area's list: the limbs moved onto them), 0 the lattice at the clips' spacing.</summary>
    public int Given;
    /// <summary>The difference at the end (m) taken out between <see cref="EndFrom"/> and <see cref="EndTo"/> (s of the clip).</summary>
    public Vector3 End;
    public float EndFrom, EndTo;
    /// <summary>A corner's: the facing at its start, its turn's difference at its end (radians), the wall position and the way in it ends at.</summary>
    public float Yaw0, YawEnd;
    public Vector3 Place;
    public Vector2 In;
    /// <summary>After a corner: the way the held wish keeps (+1 left, −1 right; 0 none) and that wish (level unit), while it holds.</summary>
    public int Carry;
    public Vector2 Wish;
    /// <summary>Over the top: the edge (its point on the face at its top), the face's outward normal and the top (world Z).</summary>
    public Vector3 Lip, LipNormal;
    public float Top;
    /// <summary>After leaving a wall into the air, the seconds before a wall is caught again.</summary>
    public float Again;
    /// <summary>The rate the clip under way plays at (1 its own).</summary>
    public float Rate;
    /// <summary>How far the clip under way is laid off the wall position (the clip over the top onto the real edge, the getting-off onto the real ground: up).</summary>
    public Vector3 Shift;
    /// <summary>The last refusal told and about where (a refusal again there is not told again).</summary>
    public WallRefusal Told;
    public Vector3 ToldAt;
}

/// <summary>A wall's move began (<paramref name="Act"/>; a move's way, <paramref name="Left"/> and <paramref name="Up"/> (both for a diagonal), its pace; a corner's outside and way), from <paramref name="From"/> to <paramref name="To"/> (where the capsule ends).</summary>
public readonly record struct WallMoveStarted(EntityId Entity, long Tick, WallAct Act, int Left, int Up, byte Pace, bool Outside, bool Sheer, Vector3 From, Vector3 To);

/// <summary>A wall's move asked for and not taken, and why, about <paramref name="At"/> (once a place).</summary>
public readonly record struct WallMoveRefused(EntityId Entity, long Tick, WallAct Act, int Left, int Up, WallRefusal Why, Vector3 At);

/// <summary>The walls for the log.</summary>
public static class WallEvents
{
    /// <summary>A move's pace (<see cref="WallMoveStarted.Pace"/>): the quick two-beat, the careful four-beat (walk held), the long one (sprint held).</summary>
    public const byte Quick = 0, Careful = 1, Long = 2;

    public static WorldBuilder Register(WorldBuilder b) => b.Event<WallMoveStarted>().Event<WallMoveRefused>();

    public static void Publish(World world, EntityId e, in MotorOutcome o)
    {
        if (o.WallStarted != WallAct.None)
            world.Events<WallMoveStarted>().Publish(new WallMoveStarted(e, world.Tick, o.WallStarted, o.WallLeft, o.WallUp, o.WallPace, o.WallOutside, o.WallSheer, o.WallFrom, o.WallTo));
        if (o.WallRefused != WallRefusal.None)
            world.Events<WallMoveRefused>().Publish(new WallMoveRefused(e, world.Tick, o.WallRefusedAct, o.WallLeft, o.WallUp, o.WallRefused, o.WallAt));
    }
}
