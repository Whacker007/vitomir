// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The put-down a click on the ground gives, and the ring it leaves at the point.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// **A click on the ground with something on the pointer puts it down there** (<see
/// cref="Actions.Put"/>), and a put-down that walks leaves **the walk-target ring** at
/// the point, as a move order does: at the end of the order, for the character the player is steering,
/// at the point — in single player once the walk is given, and
/// on a player's machine once the order is on its way to the host. Only a put-down that
/// walks: one done at once with space held (the `0x8` arm) returns before it, and so does a
/// refusal or a busy character. The ring lies in the clicked surface's plane, like the move order's
/// (<see cref="Pick.NormalAt"/>), and goes when the steered walker stops or its second is up
/// (<see cref="WalkTarget"/>).
/// </summary>
public static class PutDownOrder
{
    /// <summary>
    /// The put-down of <paramref name="item"/> at the struck surface <paramref name="at"/> by the
    /// steered character <paramref name="who"/>, and the ring when it walks. Answers the order's result.
    /// </summary>
    public static OrderResult Click(Simulation sim, WorldThing who, Item item, Hit at, int nowMs,
                                    InputModifier mods = InputModifier.None)
    {
        var got = Actions.Put(who, item, at.Point, nowMs, mods, node: at.PathNode);
        if (got == OrderResult.Started)
            sim.WalkTarget.Set(at.Point, who.Place is { } place ? Pick.NormalAt(place.Level, at) : null);
        return got;
    }
}
