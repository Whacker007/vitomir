// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The collision trees: the last field of the NIL format that had no meaning. These tests are the
/// evidence for the decode, so they run over the whole game rather than over a sample.
/// </summary>
public class CollisionTests
{
    static string[] Levels(Vfs.GameFiles files) =>
        [.. files.List("levels/").Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                 .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()];

    [Fact]
    public void Every_sector_tree_in_the_game_is_a_binary_tree()
    {
        using var files = Install.Open();
        long internals = 0, leaves = 0, trees = 0, badShape = 0, badPlane = 0, badChild = 0;
        var kinds = new Dictionary<int, long>();
        foreach (var name in Levels(files))
            foreach (var sector in NilLevel.Read(files.Read($"levels/{name}.nil"), name).Sectors)
            {
                int count = sector.BspCount;
                if (count == 0) continue;
                trees++;
                long here = 0, leafHere = 0;
                for (int i = 0; i < count; i++)
                {
                    var node = sector.Bsp(i);
                    kinds[node.Kind] = kinds.GetValueOrDefault(node.Kind) + 1;
                    if (node.IsLeaf) { leafHere++; continue; }
                    here++;
                    if (node.Plane >= sector.SurfacePlanes.Length) badPlane++;
                    // Pre-order: a child always sits later in the array, so the tree cannot loop.
                    if (node.Front <= i || node.Front >= count || node.Back <= i || node.Back >= count) badChild++;
                }
                if (leafHere != here + 1) badShape++;
                internals += here; leaves += leafHere;
            }
        Assert.Equal(0, badShape);                      // one more leaf than internal nodes, every time
        Assert.Equal(0, badPlane);
        Assert.Equal(0, badChild);
        Assert.Equal((7_567L, 744_098L, 751_665L), (trees, internals, leaves));
        // Kind 0 is an internal node; every other value is a leaf, and bit 1 says it is solid.
        Assert.Equal([0, 1, 3, 7, 11], kinds.Keys.Order());
        Assert.Equal(744_098L, kinds[0]);
        Assert.Equal(398_474L, kinds[1]);               // open
        Assert.Equal(340_857L, kinds[3]);               // solid
    }

    [Fact]
    public void Above_a_floor_is_open_and_below_it_is_solid()
    {
        using var files = Install.Open();
        long above = 0, aboveOpen = 0, below = 0, belowSolid = 0;
        foreach (var name in Levels(files))
        {
            var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
            var map = new WalkMap(level);
            var solid = new Collision(level);
            foreach (var node in map.Nodes)
            {
                if (node.Outline.Length == 0) continue;
                var middle = node.Outline.Aggregate(Vector2.Zero, (a, v) => a + v) / node.Outline.Length;
                if (!node.Contains(middle)) continue;
                float z = node.Height(middle.X, middle.Y);
                above++; if (!solid.Solid(new Vector3(middle.X, middle.Y, z + 48f))) aboveOpen++;
                // Below the floor is rock, which is outside every sector's volume — so ask the
                // owning sector's tree directly rather than through the level-wide lookup, which
                // deliberately calls the space between sectors "not solid".
                below++;
                if (Collision.Solid(level.Sectors[node.Sector], new Vector3(middle.X, middle.Y, z - 48f)))
                    belowSolid++;
            }
        }
        // Where a person can stand, the air above them is open and the ground below them is not.
        Assert.True(aboveOpen > above * 0.98, $"only {aboveOpen} of {above} floors have air above them");
        Assert.True(belowSolid > below * 0.90, $"only {belowSolid} of {below} floors have ground below them");
    }

    [Fact]
    public void Every_player_start_in_the_game_has_room_for_a_body()
    {
        // The player's radius is 32 (his template's tag 13). If the decode were wrong — the wrong
        // leaf bit, the children the wrong way round — the starts would be inside walls.
        using var files = Install.Open();
        int starts = 0, roomy = 0;
        foreach (var name in Levels(files))
        {
            var level = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
            if (!files.TryRead($"scenes/{name}.nsd", out var data)) continue;
            var map = new WalkMap(level);
            var solid = new Collision(level);
            foreach (var start in NsdScene.Read(data, name).Starts)
            {
                int node = map.Find(start.Position);
                if (node < 0) continue;
                starts++;
                var feet = start.Position with { Z = map.Nodes[node].Height(start.Position.X, start.Position.Y) };
                if (solid.Fits(feet, 32f)) roomy++;
            }
        }
        Assert.True(starts > 1_000);
        Assert.True(roomy > starts * 0.98, $"only {roomy} of {starts} player starts have room to stand");
    }

    [Fact]
    public void The_collision_trees_agree_with_the_walkable_ground_about_where_the_walls_are()
    {
        // Two structures built by the level compiler for different purposes, decoded separately.
        // Walking outward from a player start, they should run out at the same place.
        using var files = Install.Open();
        var level = NilLevel.Read(files.Read("levels/P1_OTDY.nil"), "P1_OTDY");
        var map = new WalkMap(level);
        var solid = new Collision(level);
        var scene = NsdScene.Read(files.Read("scenes/P1_OTDY.nsd"), "P1_OTDY");
        var start = scene.Starts[0].Position;
        int n0 = map.Find(start);
        start = start with { Z = map.Nodes[n0].Height(start.X, start.Y) };
        for (int a = 0; a < 8; a++)
        {
            float angle = a * MathF.Tau / 8f;
            var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            float toSolid = -1, offGround = -1;
            for (float d = 0; d < 4000 && (toSolid < 0 || offGround < 0); d += 4)
            {
                var p = new Vector3(start.X + direction.X * d, start.Y + direction.Y * d, start.Z);
                if (toSolid < 0 && solid.Solid(p with { Z = p.Z + 48f })) toSolid = d;
                if (offGround < 0 && map.Find(p, 64f) < 0) offGround = d;
            }
            Assert.True(toSolid > 0 && offGround > 0, $"nothing found at {angle * 180 / MathF.PI:F0} degrees");
            Assert.True(MathF.Abs(toSolid - offGround) <= 16f,
                        $"at {angle * 180 / MathF.PI:F0} degrees the wall is at {toSolid} but the ground ends at {offGround}");
        }
    }

    [Fact]
    public void A_walker_stops_short_of_a_wall_rather_than_standing_in_it()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var town = Location.Load(files, "P1_OTDY", scene: set.ForLevel("P1_OTDY")?.Entry?.SceneName,
                                 player: "Christof");
        var player = town.Player!;
        // Straight ahead until something stops him — the far side of the square.
        for (int i = 0; i < 60 * 8; i++) player.Move(player.Facing, 1f / 60f);
        Assert.True(player.Blocked, "he crossed the whole square without meeting anything");
        // What stops him is the walkable ground running out, not a body test: the body test is a
        // Fix now and it is off (see DrivingTests). This asserts the thing that actually matters —
        // his centre is not inside the level — and then notes that in this particular spot his whole
        // body happens to be clear too, which is the case the body test was added for.
        Assert.False(town.Solid!.Solid(player.Position with { Z = player.Position.Z + Collision.Body[1] }),
                     "he stopped with his middle inside the level");
        Assert.True(player.Fits(player.Position), "he stopped inside something solid");
        Assert.True(town.Solid!.Fits(player.Position, player.Radius, player.Sector));
    }
}
