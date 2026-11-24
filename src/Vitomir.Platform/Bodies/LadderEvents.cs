// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Bodies;

/// <summary>Where a body got onto a ladder from.</summary>
public enum LadderEnd : byte
{
    None = 0,
    /// <summary>Use at its foot.</summary>
    Foot = 1,
    /// <summary>Use on the top floor at its top.</summary>
    Top = 2,
    /// <summary>Use on a landing beside it (its clips: <c>ladder_on_side_*</c>).</summary>
    Side = 3,
    /// <summary>From a braced hang on a ledge beside it, the wish toward it (<c>ladder_v_from_hang_braced_*</c>).</summary>
    Hang = 4,
}

/// <summary>How a body left a ladder.</summary>
public enum LadderLeave : byte
{
    None = 0,
    /// <summary>Forward at the top: over onto the top floor.</summary>
    Top = 1,
    /// <summary>Back at the foot: set down on the ground.</summary>
    Foot = 2,
    /// <summary>Jump: pushed off backwards.</summary>
    PushOff = 3,
    /// <summary>Crouch or cancel: let go, falling.</summary>
    LetGo = 4,
    /// <summary>The ladder was lost (a body on a ladder the level does not have, or its moves stopped): it falls.</summary>
    Stopped = 5,
    /// <summary>The wish to a side, onto a landing beside it level with a rung (<c>ladder_off_side_*</c>).</summary>
    Side = 6,
    /// <summary>The wish to a side, into the braced hang on a ledge beside it (<c>ladder_v_to_hang_braced_*</c>).</summary>
    Hang = 7,
}

/// <summary>Why a shimmy along a lip stopped.</summary>
public enum ShimmyStop : byte
{
    None = 0,
    /// <summary>The sideways wish let go (or turned the other way).</summary>
    Released = 1,
    /// <summary>The lip's end, a corner not turned, a lip marked not to be climbed, or no room to hang.</summary>
    End = 2,
    /// <summary>The hang ended: pulled up, let go or pushed off.</summary>
    Left = 3,
}

/// <summary>A body got onto ladder <paramref name="Ladder"/> (its index in the level's list, <paramref name="Name"/>) from its foot or its top, its feet at <paramref name="Feet"/>.</summary>
public readonly record struct LadderMounted(EntityId Entity, long Tick, int Ladder, string Name, LadderEnd From, Vector3 Feet);

/// <summary>A body left ladder <paramref name="Ladder"/>: how, and its feet where it left.</summary>
public readonly record struct LadderLeft(EntityId Entity, long Tick, int Ladder, string Name, LadderLeave How, Vector3 Feet);

/// <summary>Hanging, a body began to shimmy along the lip: +1 to its right, −1 to its left as it faces the wall.</summary>
public readonly record struct ShimmyStarted(EntityId Entity, long Tick, int Way, Vector3 Lip, Vector3 Feet);

/// <summary>A shimmy stopped, and why, at <paramref name="Lip"/> with the feet at <paramref name="Feet"/>.</summary>
public readonly record struct ShimmyStopped(EntityId Entity, long Tick, ShimmyStop Why, Vector3 Lip, Vector3 Feet);

/// <summary>
/// Use met a ladder the clips do not fit (its lean off both sets' own by more than their tolerance, or its top too far off the climb's rungs): climbed on
/// The earlier build's stand-in; its lean (degrees) and its top's floor off the rungs (m).
/// </summary>
public readonly record struct LadderStandIn(EntityId Entity, long Tick, int Ladder, string Name, float LeanDegrees, float TopOff);

/// <summary>Hanging, jump with back held pushed the body off the wall backwards.</summary>
public readonly record struct LipPushedOff(EntityId Entity, long Tick, Vector3 Lip, Vector3 Feet);

/// <summary>
/// <b>The ladders' and the shimmy's events</b> (for the log): published by <see cref="BodySystem"/> from the
/// motor's outcome — on a ladder and off it (how: the top, the foot, a push-off, let go), the shimmy's start and stop,
/// and a push-off from a lip.
/// </summary>
public static class LadderEvents
{
    /// <summary>Registers <see cref="LadderMounted"/>, <see cref="LadderLeft"/>, <see cref="ShimmyStarted"/>, <see cref="ShimmyStopped"/> and <see cref="LipPushedOff"/>.</summary>
    public static WorldBuilder Register(WorldBuilder b) =>
        b.Event<LadderMounted>().Event<LadderLeft>().Event<ShimmyStarted>().Event<ShimmyStopped>().Event<LipPushedOff>().Event<LadderStandIn>();

    /// <summary>The events of one body's tick, from its outcome (nothing is looked up when there are none).</summary>
    public static void Publish(World world, EntityId e, in MotorOutcome o)
    {
        if (o.LadderStandIn > 0)
            world.Events<LadderStandIn>().Publish(new LadderStandIn(e, world.Tick, o.LadderStandIn - 1, NameOf(world, o.LadderStandIn - 1), o.LadderLean, o.LadderTopOff));
        if (o.LadderOn != LadderEnd.None)
            world.Events<LadderMounted>().Publish(new LadderMounted(e, world.Tick, o.LadderIndex, NameOf(world, o.LadderIndex), o.LadderOn, o.LadderFeet));
        if (o.LadderOff != LadderLeave.None)
            world.Events<LadderLeft>().Publish(new LadderLeft(e, world.Tick, o.LadderIndex, NameOf(world, o.LadderIndex), o.LadderOff, o.LadderFeet));
        // A stop before a start: a shimmy turned round in one tick is told as the one stopping and the other starting.
        if (o.ShimmyStopped != ShimmyStop.None) world.Events<ShimmyStopped>().Publish(new ShimmyStopped(e, world.Tick, o.ShimmyStopped, o.ShimmyLip, o.ShimmyFeet));
        if (o.ShimmyStarted != 0) world.Events<ShimmyStarted>().Publish(new ShimmyStarted(e, world.Tick, o.ShimmyStarted, o.ShimmyLip, o.ShimmyFeet));
        if (o.PushedOffLip) world.Events<LipPushedOff>().Publish(new LipPushedOff(e, world.Tick, o.GripLip, o.GripFeet));
    }

    static string NameOf(World world, int index)
    {
        var ladders = world.Service<Space.LevelSpace>().Collision.Ladders;
        return index >= 0 && index < ladders.Count ? ladders[index].Name : $"ladder {index}";
    }
}
