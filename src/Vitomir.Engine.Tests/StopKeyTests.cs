// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Space stops the character you steer**,
/// found by a comparison with 1.1: the game camera's key handler calls it on every Space press, in a
/// conversation or not, and it cancels the order unforced (0) and stops the walk
/// whatever the cancel did. Earlier, Vitomir's Space only skipped a line, so a click-walk
/// carried on.
/// </summary>
public class StopKeyTests
{
    static (Location Place, Vfs.GameFiles Files) Load()
    {
        var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", sky: false, scene: "OTDY_4_1", player: "Christof");
        place.Awaken(new AiClasses(files), 1);
        return (place, files);
    }

    /// <summary>A click-walk under way is stopped by the press, and the move order with it.</summary>
    [Fact]
    public void Space_stops_a_walk_and_its_move_order()
    {
        var (place, files) = Load();
        using var __ = files;
        var player = place.Player!;
        var me = player.Thing.Actor!;
        var goal = player.Position + new Vector3(0f, 200f, 0f);
        Assert.True(player.WalkTo(goal, place.Walk!.Find(goal)));
        MoveOrder.Taken(player.Thing);
        Assert.True(player.Walking);
        Assert.Equal(ActionMode.Move, me.Action);

        Assert.Same(player.Thing, StopKey.Press(place));
        Assert.False(player.Walking);
        Assert.Equal(ActionMode.None, me.Action);
    }

    /// <summary>
    /// The cancel is unforced, so a busy character keeps its order — but the walk is stopped anyway,
    /// because the stop calls the walker's stop after the cancel whatever it did.
    /// </summary>
    [Fact]
    public void A_busy_character_keeps_its_order_but_stops_walking()
    {
        var (place, files) = Load();
        using var __ = files;
        var player = place.Player!;
        var me = player.Thing.Actor!;
        var goal = player.Position + new Vector3(0f, 200f, 0f);
        Assert.True(player.WalkTo(goal, place.Walk!.Find(goal)));
        MoveOrder.Taken(player.Thing);
        me.BusyUntil = place.Milliseconds + 500;

        StopKey.Press(place);
        Assert.False(player.Walking);
        Assert.Equal(ActionMode.Move, me.Action);   // the unforced cancel refused
    }
}
