// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The approach's ground ray, cast for the thing
/// being walked to, on the thing it was found wrong on: the gate to the Judith Bridge in the Golden
/// Lane by day.
///
/// In play a click on that gate from outside Unorna's did not move Christof. The
/// gate is a box centred high over its doorstep, and the doorstep is 48 over the low end of the lane.
/// Vitomir asked every rung of the approach through a window of its own (64 down, 16 up), which found
/// no ground from the box's height nor from Christof's; nearer the gate, on its own level, it did,
/// which is why the fault hid for the whole play-through. 1.1 casts its ray for the target — a box's
/// is longer by the box's lowest point — and meets the doorstep on the first rung.
/// </summary>
public class ExitApproachTests
{
    static (Location Where, GameFiles Files) Enter(string level)
    {
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel(level);
        var loc = Location.Load(files, level, sky: false, scene: where?.Entry?.SceneName, player: "Christof");
        loc.Awaken(new AiClasses(files));
        return (loc, files);
    }

    /// <summary>The level's westernmost exit box: the gate to the Judith Bridge.</summary>
    static WorldThing JudithGate(Location where) =>
        where.Things.Where(t => t.Template.Equals("tracker_thing", StringComparison.OrdinalIgnoreCase)).MinBy(t => t.Position.X)!;

    [Fact]
    public void The_ground_ray_is_as_long_as_the_things_body_makes_it()
    {
        // The ground ray's branch on the body: a sphere's radius more, a box's lowest point more.
        using var install = Install.Open();
        var (where, files) = Enter("P1_GOLDD");
        using var _ = files;
        var gate = JudithGate(where);
        Assert.Equal(CollideShape.Box, gate.Collide);
        Assert.True(gate.BoxMin.Z < 0f, $"the gate's box does not hang below its position: {gate.BoxMin}");
        Assert.Equal(WalkMap.RayLength - gate.BoxMin.Z, WalkMap.RayLengthOf(gate));
        Assert.Equal(WalkMap.RayLength, WalkMap.RayLengthOf(where.Player!.Thing));          // a cylinder
    }

    [Fact]
    public void The_gate_to_the_Judith_Bridge_is_walked_to_from_the_low_end_of_the_Golden_Lane()
    {
        using var install = Install.Open();
        var (where, files) = Enter("P1_GOLDD");
        using var _ = files;
        var gate = JudithGate(where);
        var player = where.Player!;
        player.Thing.Position = new Vector3(-95f, -1216f, 0f);          // where the scene's first line leaves him

        // The premise: too far over the doorstep for the old window, at either height.
        Assert.True(where.Walk!.Find(gate.Position) < 0);
        Assert.True(where.Walk!.Find(gate.Position with { Z = player.Thing.Position.Z }) < 0);

        var (to, node) = Actions.Toward(player.Thing, gate);
        Assert.True(node >= 0, "nowhere to stand to reach the gate");
        // The first rung: its own doorstep, at the doorstep's height, further under the
        // box's middle than the old window reached and inside the box's own ray.
        Assert.Equal(gate.Position.X, to.X);
        Assert.Equal(gate.Position.Y, to.Y);
        Assert.Equal(where.Walk!.Nodes[node].Height(to.X, to.Y), to.Z, 3);
        Assert.InRange(gate.Position.Z - to.Z, WalkMap.Reach, WalkMap.RayLengthOf(gate) - WalkMap.RayAbove);

        Assert.Equal(OrderResult.Started, Actions.Use(player.Thing, gate, nowMs: 0));
        Assert.True(player.Walking, "the use order started but nobody is walking");
        Assert.Equal(ActionMode.Use, player.Thing.Actor!.Action);
    }
}
