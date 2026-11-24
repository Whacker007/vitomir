// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Ours: the models repository (CONVENTIONS, Notes, traverse.side_step): squeezing
// sideways through a gap and the back to a wall in cover. The motor's side: BodyMotor.Side.cs.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Bodies;

/// <summary>
/// <b>squeezing and cover</b> for the motor (the people's <c>PersonStealthSettings</c>, riding on the walls): the numbers they are found and played by.
/// Null for none: the earlier build's (a gap stops the body, nothing presses its back to a wall).
/// </summary>
public sealed class SideRules
{
    /// <summary>Whether a gap narrower than the body is squeezed through.</summary>
    public bool Squeeze { get; init; } = true;

    /// <summary>Whether a body in stealth presses its back to a wall behind it.</summary>
    public bool Cover { get; init; } = true;

    /// <summary>How far the back may be off a wall behind (m, the capsule's skin to the face) for the body in stealth to take cover against it (<c>coverReach</c>).</summary>
    public float CoverReach { get; init; } = 0.3f;

    /// <summary>How long a body in stealth stands still with a wall behind before it takes cover (s; backing into it, at once).</summary>
    public float CoverDwell { get; init; } = 0.2f;

    /// <summary>Squeezes quick: the rate the quarter turns into and out of a squeeze are played at (their notes' <c>quickens</c>; <c>squeezeTurnRate</c>).</summary>
    public float TurnRate { get; init; } = 1.5f;

    /// <summary>
    /// No locked-in squeeze clip, the side-step stops when the key does: the rate a side-step's cycle let go of runs to its nearer end at (back to its
    /// start or on to its end, both the narrow stance; <c>sideStopRate</c>).
    /// </summary>
    public float StopRate { get; init; } = 2f;

    /// <summary>Horizon's three frames at 30 Hz: how long the keys' way along a squeeze or a cover is kept after they let go (s; <c>sideHold</c>).</summary>
    public float Hold { get; init; } = 0.1f;
}

/// <summary>What a squeeze's or a cover's move is (in the log, and <see cref="SideMoveStarted"/>).</summary>
public enum SideAct : byte
{
    None = 0,
    /// <summary>The quarter turn on the spot into a squeeze (to face the near wall).</summary>
    SqueezeIn = 1,
    /// <summary>A side-step's cycle in a squeeze, to the left or the right.</summary>
    SqueezeStep = 2,
    /// <summary>The quarter turn on the spot out of a squeeze (to face the way it went), back to standing and the body's own capsule.</summary>
    SqueezeOut = 3,
    /// <summary>Into cover: two steps back to the wall (<c>cover_in</c>).</summary>
    CoverIn = 4,
    /// <summary>A side-step's cycle along cover.</summary>
    CoverStep = 5,
    /// <summary>Out of cover: up and two steps forward (<c>cover_out</c>).</summary>
    CoverOut = 6,
    /// <summary>The way into cover let go of before the back met the wall (forward, stealth off, crouch or cancel): standing (or walking) on from there.</summary>
    CoverLetGo = 7,
}

/// <summary>Why a squeeze or a cover was not taken (told once a place).</summary>
public enum SideRefusal : byte
{
    None = 0,
    /// <summary>A gap narrower than the squeeze's clips allow (<c>gap_min_m</c>).</summary>
    Narrow = 1,
    /// <summary>A gap that bends (its walls not straight and parallel along it), or is closed before its far end.</summary>
    Bend = 2,
    /// <summary>A gap with less room overhead than the body standing.</summary>
    Low = 3,
    /// <summary>Crouched: the squeeze's clips are made standing.</summary>
    Crouched = 4,
    /// <summary>A gap with no way out within the length looked along.</summary>
    Long = 5,
    /// <summary>The wall of the cover ends (a corner or its end: the body stops there; no turning round it).</summary>
    WallEnds = 6,
    /// <summary>The way is blocked (the clip's path swept before it starts).</summary>
    Blocked = 7,
    /// <summary>No room for the body there.</summary>
    NoRoom = 8,
}

/// <summary>
/// <b>a person squeezing or in cover</b> — the motor's state beside its <see cref="LedgeMove"/> (which carries the clip, its time, its place and the way the
/// body faces: <see cref="LedgeMove.In"/>, the near wall for a squeeze, out from the wall for a cover); part of the people's digested <c>PersonStealth</c>.
/// </summary>
public struct SideMove
{
    /// <summary>1 squeezing, 2 in cover, 0 neither.</summary>
    public byte Kind;
    /// <summary>1 turning into a squeeze, 2 in it (or in cover), 3 turning out of it; 4 the way into cover let go of (<c>cover_in</c> run back to standing).</summary>
    public byte Phase;
    /// <summary>A squeeze's: 1 once a cycle has ended where the body's own capsule does not fit (it is in the gap).</summary>
    public byte Inside;
    /// <summary>The way of the cycle under way and the next asked (+1 to the body's left, −1 its right, 0 none).</summary>
    public int Way;
    /// <summary>A squeeze's gap: its way along (level unit, as first gone into it), a point of its middle line at the floor, its width (m).</summary>
    public Vector2 Axis;
    public Vector3 Mid;
    public float Width;
    /// <summary>A cover's wall: a point of its face and its outward level normal.</summary>
    public Vector3 Wall;
    public Vector2 Out;
    /// <summary>A turn's or <c>cover_in</c>'s facing at its start and its difference at its end (radians), taken out between <see cref="EndFrom"/> and <see cref="EndTo"/> (s of the clip).</summary>
    public float Yaw0, YawEnd;
    public float EndFrom, EndTo;
    /// <summary>
    /// The body's speed as a squeeze's turn or <c>cover_in</c> began (m/s, level) and how long it is brought to rest in (s): carried into the clip's start so the
    /// body slows rather than stops in a tick (a Hermite's velocity term, nought at its end).
    /// </summary>
    public Vector3 Brake;
    public float BrakeFor;
    /// <summary>How long the body (in stealth) has stood still with a wall behind it (s).</summary>
    public float Still;
    /// <summary>The quarter turn out of a squeeze just ended (1 + its clip's index in the character's clips) and the seconds since, while its fade lasts (its root's turn still carried).</summary>
    public int Turned;
    public float TurnedSeconds;
    /// <summary>1 after leaving cover until the body moves (standing still there does not take it again).</summary>
    public byte Rearm;
    /// <summary>The last refusal told and about where (a refusal again there is not told again).</summary>
    public SideRefusal Told;
    public Vector3 ToldAt;
    /// <summary>The keys' way kept (+1 to the body's left, −1 its right, 0 none) and for how much longer after they let go (s; <see cref="SideRules.Hold"/>).</summary>
    public int Held;
    public float HeldFor;
    /// <summary>A side-step's cycle's rate now (signed: below nought it runs back toward its start), brought to the one asked at <c>BodyMotor.SideSlew</c>.</summary>
    public float Rate;
    /// <summary>A side-step let go of: the end its cycle runs to (+1 on to its end, −1 back to its start), chosen once by which it reaches sooner; 0 while asked.</summary>
    public sbyte Stopping;
}

/// <summary>A squeeze's or a cover's move began (<paramref name="Act"/>; a step's way <paramref name="Way"/>), from <paramref name="From"/>; a squeeze's gap <paramref name="Width"/> wide.</summary>
public readonly record struct SideMoveStarted(EntityId Entity, long Tick, SideAct Act, int Way, Vector3 From, float Width);

/// <summary>A squeeze or a cover not taken, and why, about <paramref name="At"/> (once a place); a gap's width, NaN for none.</summary>
public readonly record struct SideMoveRefused(EntityId Entity, long Tick, SideAct Act, SideRefusal Why, Vector3 At, float Width);

/// <summary>Stealth turned on or off (the key's toggle; <paramref name="On"/>), where the body was.</summary>
public readonly record struct StealthChanged(EntityId Entity, long Tick, bool On, Vector3 Feet);

/// <summary>Squeezing, cover and stealth for the log.</summary>
public static class SideEvents
{
    public static WorldBuilder Register(WorldBuilder b) => b.Event<SideMoveStarted>().Event<SideMoveRefused>().Event<StealthChanged>();

    public static void Publish(World world, EntityId e, in MotorOutcome o)
    {
        if (o.SideStarted != SideAct.None)
            world.Events<SideMoveStarted>().Publish(new SideMoveStarted(e, world.Tick, o.SideStarted, o.SideWay, o.SideFrom, o.SideWidth));
        if (o.SideRefused != SideRefusal.None)
            world.Events<SideMoveRefused>().Publish(new SideMoveRefused(e, world.Tick, o.SideRefusedAct, o.SideRefused, o.SideAt, o.SideWidth));
    }
}
