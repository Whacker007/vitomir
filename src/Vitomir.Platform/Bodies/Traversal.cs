// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// The moves a body makes over, onto or up what is ahead of it (ours): each a
/// timed path the simulation runs, and each a named motion slot the pose fills.
/// </summary>
public enum TraversalKind
{
    /// <summary>No move: the body walks, runs, stands or flies as usual.</summary>
    None = 0,
    /// <summary>A hop up onto a top a jump reaches that can be stood on (jump tapped, close, moving towards it).</summary>
    HopUp = 1,
    /// <summary>
    /// <b>The sprint vault</b> (the first hurdle, whose name it keeps for the motion slot, the reach
    /// highlight and the log): sprinting over a thing to <see cref="Locomotion.SprintVaultMax"/> high and
    /// <see cref="Locomotion.SprintVaultDepth"/> deep, by itself, never onto.
    /// </summary>
    Hurdle = 2,
    /// <summary>Over a thin obstacle (a jump into it; tapped or charged).</summary>
    Vault = 3,
    /// <summary>Up onto a deep obstacle of a vault's height (a jump into it).</summary>
    ClimbOnto = 4,
    /// <summary>Up onto a top the hands reach standing (a jump into it).</summary>
    Mantle = 5,
    /// <summary>The hands taking a lip in the air and the body coming to hang from it.</summary>
    Grab = 6,
    /// <summary>Hanging from a lip (no end of its own).</summary>
    Hang = 7,
    /// <summary>From hanging up onto the top.</summary>
    PullUp = 8,
    /// <summary>Letting go of a lip: the body falls (the kind lasts a moment or until it lands).</summary>
    Drop = 9,
    /// <summary>
    /// Crouch held as the body steps off an edge whose lip the hands can hold: out over the edge and
    /// down to hang from it, ending in <see cref="Hang"/>. The lip is the edge stepped off.
    /// </summary>
    LowerToHang = 10,
    /// <summary>
    /// <b>The run's step-over</b>: moving at a run or faster, a higher step over a thin thing to
    /// <see cref="Locomotion.StepOverMax"/>, by itself, never onto (BodyMotor.Pace.cs). It plays the hurdle's slot.
    /// </summary>
    StepOver = 11,
    /// <summary>
    /// <b>Getting onto a ladder</b>: use at its foot moves the body a short way to its line, facing it; use at its
    /// top out over the edge and down onto it. Ends in <see cref="Ladder"/>. It plays the grab's slot.
    /// </summary>
    LadderOn = 12,
    /// <summary>
    /// <b>On a ladder</b> (<see cref="BodyState.Ladder"/>): <see cref="Traversal.From"/> and <see cref="Traversal.To"/>
    /// are where the feet are at its foot and at its top, <see cref="Traversal.Progress"/> how far up between them,
    /// <see cref="Traversal.Lip"/> where the hands hold its face, <see cref="Traversal.LipNormal"/> the way its climbing side
    /// looks out, <see cref="Traversal.Height"/> its rise. No end of its own. It plays the hang's slot.
    /// </summary>
    Ladder = 13,
    /// <summary>
    /// <b>Getting off a ladder on its clips</b>: down onto the ground at its foot (<c>ladder_off_bottom</c>), onto a landing beside it
    /// (<c>ladder_off_side_*</c>) or into the braced hang on a ledge beside it (<c>ladder_v_to_hang_braced_*</c>). Plays the drop's slot (its clip chosen
    /// by the people's ladder).
    /// </summary>
    LadderOff = 14,
    /// <summary>
    /// <b>Getting onto a wall on its clips</b>: from the ground (<c>wall_on_ground</c>, <c>wall_on_jump</c>) or caught in the air; ends in
    /// <see cref="Wall"/>. Plays the grab's slot (its clip chosen by the people's ledge). Its <see cref="Traversal.Height"/> is the masquerade's measure:
    /// nought on a wall with holds, 1 on a face with nothing to hold.
    /// </summary>
    WallOn = 15,
    /// <summary><b>On a wall</b> (<see cref="BodyState.Hanging"/>): the idle, a move, a corner, over the top. Plays the hang's slot.</summary>
    Wall = 16,
    /// <summary><b>Down off a wall at its foot</b> (<c>wall_off_ground</c>), standing. Plays the drop's slot.</summary>
    WallOff = 17,
    /// <summary>
    /// <b>Squeezing sideways through a gap</b> (the models repository): the quarter turn on the spot to face the near wall, the side-steps
    /// (<c>squeeze_left</c>, <c>_right</c>) and <c>squeeze_idle</c> with the slimmer capsule, the quarter turn out. Plays the hang's slot (the people's ledge chooses the clip).
    /// </summary>
    Squeeze = 18,
    /// <summary><b>In cover</b>: the back to a wall — <c>cover_in</c>, <c>cover_idle</c>, the side-steps along it, <c>cover_out</c>. Plays the hang's slot.</summary>
    Cover = 19,
}

/// <summary>
/// <b>The move a body is making</b> (ours), written by <see cref="BodyMotor"/> on
/// every body every tick — <see cref="TraversalKind.None"/> when it makes none — for the pose to follow:
/// which move, how far through it (0 to 1) and how long it is in all, where the feet's path starts and
/// ends, the edge the hands hold and which way its face looks, and how high the obstacle's top is above
/// where the move started. The motor runs the move from it too (the path is a function of these fields),
/// so it is the move's whole state. Plain data, digested byte for byte.
/// </summary>
public struct Traversal
{
    public TraversalKind Kind;
    /// <summary>How far through the move (0 at its start, 1 at its end; 0 throughout a hang without a grip limit).</summary>
    public float Progress;
    /// <summary>The move's whole length (s); a hang's is the grip's (infinite while grip is unlimited).</summary>
    public float Seconds;
    /// <summary>The feet where the move starts and where it ends (m).</summary>
    public Vector3 From, To;
    /// <summary>The point of the edge the hands hold or go over (m); zero when there is none.</summary>
    public Vector3 Lip;
    /// <summary>The obstacle's face's outward normal at the lip, level (zero when there is none).</summary>
    public Vector3 LipNormal;
    /// <summary>The obstacle's top above <see cref="From"/>'s feet (m).</summary>
    public float Height;
}
