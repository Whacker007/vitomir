// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The ground you can stand on, and someone standing on it. These run against the shipped levels
/// because the whole model — which surfaces are walkable, how they join up, how high the floor is —
/// is the level compiler's work, not ours.
/// </summary>
public class WalkTests
{
    static Location Load(string level, bool player = false)
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel(level);
        // Only Prague.nmf names a PLAYER; the other chronicles carry the key with an empty value,
        // which `??` does not catch. Every test below happens to use a Prague level, but a London
        // one would silently get no player at all.
        var named = where?.Chronicle.Meta?.Player;
        return Location.Load(files, level, scene: where?.Entry?.SceneName,
                             player: player ? (string.IsNullOrEmpty(named) ? "Christof" : named) : null);
    }

    [Fact]
    public void A_level_s_walkable_ground_is_its_path_graph_minus_the_walls()
    {
        var town = Load("P1_OTDY");
        var walk = town.Walk!;
        Assert.Equal(town.Level.PathNodes.Length, walk.Nodes.Length);
        Assert.True(walk.Count > 0, "nowhere to stand in Old Town");
        // Walk is a brush flag, so a floor brush's sides get a path node too; those are dropped.
        Assert.True(walk.Vertical > 0, "no sideways-facing walkables at all is suspicious");
        Assert.Equal(walk.Nodes.Length, walk.Count + walk.Vertical);
        Assert.All(walk.Nodes.Where(n => n.Outline.Length > 0),
                   n => Assert.True(n.Plane.Normal.Z >= WalkMap.MinFloorNormalZ));
    }

    [Fact]
    public void Every_player_start_in_the_game_has_ground_under_it()
    {
        // 1,132 starts across the 112 levels. One of them — a single start in P2_OLDT — has
        // nothing walkable beneath it, which is the level builder's, not ours.
        using var files = Install.Open();
        var levels = files.List("levels/")
                          .Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                          .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToArray();
        int starts = 0, grounded = 0; var misses = new List<string>();
        foreach (var name in levels)
        {
            var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
            if (!files.TryRead($"scenes/{name}.nsd", out var data)) continue;
            var map = new WalkMap(level);
            foreach (var start in NsdScene.Read(data, name).Starts)
            {
                starts++;
                if (map.Find(start.Position) >= 0) grounded++;
                else misses.Add($"{name} {start.Position}");
            }
        }
        Assert.Equal(1_132, starts);
        Assert.Equal(1_131, grounded);
        Assert.Single(misses);
        Assert.StartsWith("P2_OLDT", misses[0]);
    }

    [Fact]
    public void The_floor_is_where_the_surface_says_it_is()
    {
        var town = Load("P1_OTDY");
        var walk = town.Walk!;
        foreach (var node in walk.Nodes.Where(n => n.Outline.Length > 0).Take(200))
        {
            // Every corner of a surface lies on its own plane, so the height there is the corner's.
            foreach (var corner in node.Outline)
            {
                float z = node.Height(corner.X, corner.Y);
                Assert.True(float.IsFinite(z));
            }
            var middle = node.Outline.Aggregate(Vector2.Zero, (a, v) => a + v) / node.Outline.Length;
            Assert.True(float.IsFinite(node.Height(middle.X, middle.Y)));
        }
    }

    [Fact]
    public void A_walker_crosses_from_one_piece_of_ground_to_the_next()
    {
        var town = Load("P1_OTDY");
        var walk = town.Walk!;
        var node = walk.Nodes.First(n => n.Outline.Length > 0 && n.Neighbours.Any(walk.IsGround));
        var from = Middle(node);
        int neighbour = node.Neighbours.First(walk.IsGround);
        var to = Middle(walk.Nodes[neighbour]);
        // A long step straight at the middle of the neighbour crosses onto it.
        var step = walk.Move(node.Index, from, new Vector3(to.X, to.Y, from.Z));
        Assert.True(step.Node == neighbour || step.Node == node.Index,
                    "a step towards a neighbour landed somewhere else entirely");
        Assert.True(walk.IsGround(step.Node));
    }

    [Fact]
    public void Walking_into_a_wall_stops_rather_than_leaving_the_world()
    {
        var town = Load("P1_OTDY");
        var walk = town.Walk!;
        var node = walk.Nodes.First(n => n.Outline.Length > 0);
        var from = Middle(node);
        // A mile in one go lands on nothing; the walker keeps its footing instead.
        var step = walk.Move(node.Index, from, from + new Vector3(100_000f, 0, 0));
        Assert.True(walk.IsGround(step.Node));
        Assert.True(step.Blocked);
        Assert.True(Vector3.Distance(step.Position, from) < 100_000f);
    }

    [Fact]
    public void The_player_stands_where_the_scene_starts_them()
    {
        var town = Load("P1_OTDY", player: true);
        var player = town.Player!;
        Assert.Equal("Christof", player.Thing.Template);
        Assert.True(player.Node >= 0, "the player is not on any walkable ground");
        Assert.Equal(ThingType.Player, player.Thing.Type);
        // The chronicle's own numbers, not ours.
        Assert.Equal(115f, player.WalkSpeed);
        Assert.Equal(250f, player.RunSpeed);
        Assert.Equal(32f, player.Thing.Radius);
        // And they are the last thing in the list, after every scene thing, so no CODEXPARAMS
        // index can reach them — but numbered first, as 1.1 makes the player before any place:
        // the kit's three numbers and the fallback weapon's, then Christof's 5.
        Assert.Same(player.Thing, town.Things[^1]);
        Assert.Equal(5, player.Thing.Guid);
        Assert.All(town.Things.Where(t => t != player.Thing), t => Assert.True(t.Guid > 5));
    }

    [Fact]
    public void Walking_covers_the_character_s_own_speed()
    {
        var town = Load("P1_OTDY", player: true);
        var player = town.Player!;
        var from = player.Position;
        for (int i = 0; i < 60; i++) player.Move(player.Facing, 1f / 60f);
        Assert.False(player.Blocked, "the start faces a wall in this level, so this test cannot measure speed");
        Assert.Equal(player.WalkSpeed, Vector3.Distance(from, player.Position), 0);
        Assert.Equal("walk", player.Thing.Animation!.Mode!.Name);

        player.Running = true;
        from = player.Position;
        for (int i = 0; i < 60; i++) player.Move(player.Facing, 1f / 60f);
        Assert.Equal(player.RunSpeed, Vector3.Distance(from, player.Position), 0);
        Assert.Equal("run", player.Thing.Animation!.Mode!.Name);

        player.Move(Vector2.Zero, 1f / 60f);
        Assert.Equal(0f, player.Speed);
        Assert.Equal("stand", player.Thing.Animation!.Mode!.Name);
    }

    [Fact]
    public void A_character_turns_towards_where_it_is_going()
    {
        var town = Load("P1_OTDY", player: true);
        var player = town.Player!;
        float before = player.Heading;
        // Straight backwards: it should turn rather than moon-walk, and take a moment to do it.
        player.Move(-player.Facing, 1f / 60f);
        Assert.NotEqual(before, player.Heading);
        Assert.InRange(MathF.Abs(player.Heading - before), 0f, player.TurnRate / 60f + 0.01f);
        for (int i = 0; i < 120; i++) player.Move(new Vector2(1, 0), 1f / 60f);
        Assert.Equal(-90f, player.Heading, 1);              // facing +X
    }

    [Fact]
    public void A_place_with_nobody_in_it_has_no_player_and_still_loads()
    {
        var town = Load("P1_OTDY");
        Assert.Null(town.Player);
        Assert.NotNull(town.Walk);
        Assert.DoesNotContain(town.Things, t => t.Type == ThingType.Player);
    }

    /// <summary>A point in the middle of a piece of ground, on its floor.</summary>
    static Vector3 Middle(WalkNode n)
    {
        var middle = n.Outline.Aggregate(Vector2.Zero, (a, v) => a + v) / n.Outline.Length;
        return new Vector3(middle.X, middle.Y, n.Height(middle.X, middle.Y));
    }
}
