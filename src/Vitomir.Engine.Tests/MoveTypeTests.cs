// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// `MOVETYPE`, the word that decides which half of the engine a thing belongs to. Four kinds, and
/// each of them gates something: the walker, the keyframe track, physics, or nothing at all.
///
/// These are corpus tests because the gate they check is one Vitomir did not have until it was
/// found in 1.1, and adding a gate to shipped data is exactly the kind of change
/// that silently stops a door working. The measurement is the safety net.
/// </summary>
public class MoveTypeTests
{
    static readonly string[] Levels = ["P1_OTDY", "P1_MON1", "P1_MON2"];

    static Location Load(string level)
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel(level);
        return Location.Load(files, level, sky: false, scene: where?.Entry?.SceneName);
    }

    [Fact]
    public void Everything_the_level_builders_gave_keyframes_is_a_track_thing()
    {
        // The whole justification for the gate MoveToFrame now applies. If a single placed thing
        // anywhere carried frames without being MOVETYPE 2, the gate would have stopped it moving.
        int framed = 0;
        foreach (string level in Levels)
        {
            var place = Load(level);
            foreach (var thing in place.Things.Where(t => t.Keyframes.Length > 0))
            {
                framed++;
                Assert.Equal(WorldThing.MoveTrack, thing.MoveType);
            }
        }
        Assert.True(framed >= 13, $"only {framed} things with keyframes — the survey lost its subject");
    }

    [Fact]
    public void A_track_thing_with_no_frames_is_a_thing_a_script_is_expected_to_fill_in()
    {
        // The other half of the same fact, and why AllocateFrames exists at all: Old Town alone
        // places ten MOVETYPE 2 things with nothing to travel between. They are the trackers — the
        // invisible things a scene moves the camera and its set pieces along.
        var town = Load("P1_OTDY");
        var empty = town.Things.Where(t => t.MoveType == WorldThing.MoveTrack && t.Keyframes.Length == 0).ToArray();
        Assert.NotEmpty(empty);
        Assert.All(empty, t => Assert.Empty(t.Keyframes));
    }

    [Fact]
    public void Nothing_placed_in_a_level_has_physics()
    {
        // Worth recording because it is surprising: **not one placed thing in these levels is
        // MOVETYPE 3**. Everything with velocity in this game arrives later — gore, projectiles,
        // things a script spawns — which is why the physics natives looked unreachable and are not.
        foreach (string level in Levels)
            Assert.DoesNotContain(Load(level).Things, t => t.MoveType == WorldThing.MovePhysics);
    }

    [Fact]
    public void A_thing_that_is_not_on_a_track_will_not_travel_along_one()
    {
        var place = Load("P1_MON1");
        var door = place.Things.First(t => t.MoveType == WorldThing.MoveTrack && t.Keyframes.Length > 1);
        var prop = place.Things.First(t => t.MoveType == WorldThing.MoveNone);
        prop.Keyframes = door.Keyframes;                      // frames, but still not a track thing

        Assert.False(prop.MoveToFrame(1, 100f));
        Assert.Null(prop.Motion);
        // RotatePivot's native answers `mov al, 1` whatever the worker did, so the
        // refusal is invisible to the script — the thing simply does not turn.
        Assert.True(prop.RotatePivot(1, 2f));
        Assert.Null(prop.Motion);

        Assert.True(door.MoveToFrame(1, 100f));
        Assert.NotNull(door.Motion);
    }

    [Fact]
    public void A_swing_of_no_duration_is_no_swing_at_all()
    {
        // Duration == 0 returns true before the worker is called, so there is no motion
        // and no `arrived`. Vitomir used to make a zero-length motion, which completed on the next
        // tick and told the script it had arrived — a keyframe event the original never sends.
        var place = Load("P1_MON1");
        var door = place.Things.First(t => t.MoveType == WorldThing.MoveTrack && t.Keyframes.Length > 1);
        var pose = (door.Position, door.Orientation);

        Assert.True(door.RotatePivot(1, 0f));
        Assert.Null(door.Motion);
        Assert.Equal(-1, door.Advance(1f));
        Assert.Equal(pose, (door.Position, door.Orientation));
    }

    [Fact]
    public void Stopping_a_track_thing_leaves_it_where_it_stopped()
    {
        var place = Load("P1_MON1");
        var door = place.Things.First(t => t.MoveType == WorldThing.MoveTrack && t.Keyframes.Length > 1);
        var start = door.Position;
        Assert.True(door.MoveToFrame(1, 20f));

        door.Advance(0.1f);
        var caught = door.Position;
        Assert.NotEqual(start, caught);

        door.StopMoving();
        Assert.Null(door.Motion);
        Assert.Equal(-1, door.Advance(100f));                 // `arrived` never comes
        Assert.Equal(caught, door.Position);
        Assert.NotEqual(door.Keyframes[1].Position, door.Position);
    }

    [Fact]
    public void A_commanded_speed_beats_the_walk_and_run_pair_until_the_route_ends()
    {
        // What SendActorToPos needs from the walker, and the engine's own third argument to
        // simWalker::WalkTo: a speed in units a second, which is what the step distance
        // and the animation are both taken from.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1",
                                  player: "Christof");
        var walker = place.Player!;
        var far = Reachable(place, walker, 600f);

        Assert.True(walker.WalkTo(far.Point, far.Node));
        walker.Follow(0.001f);
        float usual = walker.Speed;
        Assert.True(usual is Player.DefaultWalkSpeed or Player.DefaultRunSpeed
                    || usual == walker.WalkSpeed || usual == walker.RunSpeed);

        Assert.True(walker.WalkTo(far.Point, far.Node, speed: 77f));
        walker.Follow(0.001f);
        Assert.Equal(77f, walker.Speed);
        Assert.False(walker.Running);                          // 77 is below a walk, so it is a walk

        // A fast order runs, and says so — the animation is chosen from the speed, not the flag.
        Assert.True(walker.WalkTo(far.Point, far.Node, speed: 400f));
        walker.Follow(0.001f);
        Assert.Equal(400f, walker.Speed);
        Assert.True(walker.Running);

        // And it belongs to that route alone: the next ordinary order is back on the usual rule.
        walker.Stop();
        Assert.True(walker.WalkTo(far.Point, far.Node));
        walker.Follow(0.001f);
        Assert.Equal(usual, walker.Speed);
    }

    /// <summary>A walkable spot at least <paramref name="away"/> off, with a route to it.</summary>
    static (Vector3 Point, int Node) Reachable(Location place, Player walker, float away)
    {
        var walk = place.Walk!;
        for (int i = 0; i < walk.Nodes.Length; i++)
        {
            var node = walk.Nodes[i];
            if (node.Outline.Length == 0) continue;
            var mid = Vector2.Zero;
            foreach (var v in node.Outline) mid += v;
            mid /= node.Outline.Length;
            if (!node.Contains(mid)) mid = Vector2.Lerp(mid, node.Outline[0], 0.5f);
            var p = new Vector3(mid, node.Height(mid.X, mid.Y));
            if (Vector3.Distance(p, walker.Position) < away) continue;
            if (!walker.WalkTo(p, i)) continue;
            walker.Stop();
            return (p, i);
        }
        throw new InvalidOperationException($"nowhere reachable {away} from {walker.Position}");
    }
}
