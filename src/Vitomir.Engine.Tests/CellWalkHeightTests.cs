// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A walker's height on a cell level is the ground's, never a navigation polygon's plane** (ours; the
/// Cell levels: a walker that came off a stair sank through the ground along the plane
/// of a polygon it was no longer over, 23 m under the road at its goal, and was reported as arrived).
///
/// Recast cuts the ground into polygons by their outlines in plan and keeps heights at the corners
/// only, so the plane through a polygon's corners can be far from the ground inside it. Two generated
/// levels show the two halves of it. **The hollow**: a 12 m floor with a smooth hollow 1.5 m deep
/// along its middle, which Recast makes one polygon with the floor round it, its four corners all at
/// the floor's height: the plane is 1.5 m over the hollow's bottom, further than the 40 units the
/// collision floor is looked for round a guess, so until then a walker crossed the hollow in the air.
/// **The ramp to nowhere**: a ramp that ends in a drop; 1.1's rule for a walker on a route whose
/// ground ray finds nothing is to keep the surface it had, and until then its height then
/// came off that polygon's plane carried on past its edge -- up this ramp's slope for ever, and in the levels report
/// down a tipped sliver's.
///
/// What is pinned: the height inside a polygon is the detail mesh's and then the collision's
/// (<see cref="CellStream.HeightOn"/>); a walk across the hollow stays on the ground at every step,
/// by the route step itself (<see cref="WalkMap.Stride"/>) and by Christof walking a route with the
/// game's own loader; and off a polygon's outline the height is its edge's, not its plane's.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellWalkHeightTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    /// <summary>The hollow's depth (metres) and half its width; it runs the length of the floor along y.</summary>
    const float Depth = 1.5f, HalfWidth = 3f;

    /// <summary>The floor's height at x (metres): 0, and a smooth hollow <see cref="Depth"/> deep for |x| under <see cref="HalfWidth"/> (steepest 38°).</summary>
    static float Hollow(float x) => MathF.Abs(x) >= HalfWidth ? 0f : -Depth / 2f * (1f + MathF.Cos(MathF.PI * x / HalfWidth));

    /// <summary>A floor from x0 to x1 and y −6 to 6 m in half-metre squares at height <paramref name="z"/>(x), cooked as one cell under a fresh folder.</summary>
    static string Cook(string name, float x0, float x1, Func<float, float> z)
    {
        var v = new List<Vector3>(); var idx = new List<int>();
        for (float x = x0; x < x1 - 1e-3f; x += 0.5f)
            for (float y = -6; y < 6 - 1e-3f; y += 0.5f)
            {
                int at = v.Count;
                v.AddRange([new Vector3(x, y, z(x)) * M, new Vector3(x + 0.5f, y, z(x + 0.5f)) * M,
                            new Vector3(x + 0.5f, y + 0.5f, z(x + 0.5f)) * M, new Vector3(x, y + 0.5f, z(x)) * M]);
                idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            }
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        CellCook.Cook(new CellCookInput
        {
            Name = name, Vertices = [.. v], Indices = [.. idx], TriangleMaterials = new int[idx.Count / 3], Materials = ["ground"],
            Ground = _ => true, Passable = _ => false,
            Anchors = [new L2Anchor { Id = $"{name}/starts/0", Kind = "start", Scene = name, Name = "playerstart00",
                                      Position = [x0 + 2, 0, 1], Rotation = [0, 0, -90], Group = 0 }],
        }, L2Package.PathFor(root, name));
        return root;
    }

    [Fact]
    public void The_hollow_is_one_polygon_with_the_floor_and_its_plane_is_far_over_the_hollow_s_bottom()
    {
        // The premise of the tests below, pinned so that a change in the cook that splits the hollow off says so here.
        var root = Cook("hollow", -6, 6, Hollow);
        try
        {
            var level = CellLevel.Load(L2Package.PathFor(root, "hollow"));
            var walk = new WalkMap(level.Shell);
            int node = walk.Nearest(new Vector3(0, 0, -Depth * M));
            Assert.True(node >= 0, "no ground over the hollow's bottom");
            var plane = walk.Nodes[node].Plane;
            float onPlane = plane.Dist / plane.Normal.Z;                       // the plane's height at (0, 0)
            Assert.True(onPlane - -Depth * M > CellLevel.FloorWindow,
                        $"the polygon's plane at the hollow's bottom is at {onPlane:0}, within the floor window of the ground at {-Depth * M:0}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_walk_polygon_s_height_in_a_hollow_is_the_ground_s_not_its_plane_s()
    {
        var root = Cook("hollow", -6, 6, Hollow);
        try
        {
            var level = CellLevel.Load(L2Package.PathFor(root, "hollow"));
            var walk = new WalkMap(level.Shell);
            for (float x = -5; x <= 5; x += 0.25f)
            {
                var at = new Vector3(x * M, 1.3f * M, Hollow(x) * M);
                int node = walk.Find(at);
                Assert.True(node >= 0, $"no ground under x {x} m at the ground's own height");
                float truth = level.Collision.Floor(at.X, at.Y, at.Z, 4f)!.Value;
                Assert.True(MathF.Abs(walk.Nodes[node].Height(at.X, at.Y) - truth) < 0.5f,
                            $"at x {x} m the walk node's height is {walk.Nodes[node].Height(at.X, at.Y):0.0} and the ground is at {truth:0.0}");
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_route_s_steps_across_the_hollow_stay_on_the_ground()
    {
        var root = Cook("hollow", -6, 6, Hollow);
        try
        {
            var level = CellLevel.Load(L2Package.PathFor(root, "hollow"));
            var walk = new WalkMap(level.Shell);
            var at = new Vector3(-5 * M, 0.2f * M, 0);
            int node = walk.Find(at);
            Assert.True(node >= 0);
            // One straight leg to (5, 0.2, 0) m, as the walker steps it: 4 units at a time along the leg's own line.
            float worst = 0f;
            while (at.X < 5 * M)
            {
                var step = walk.Stride(node, at, at + new Vector3(4f, 0, 0));
                Assert.False(step.Blocked);
                (node, at) = (step.Node, step.Position);
                float truth = level.Collision.Floor(at.X, at.Y, Hollow(at.X / M) * M, 8f)!.Value;
                worst = MathF.Max(worst, MathF.Abs(at.Z - truth));
            }
            Assert.True(worst < 0.5f, $"the walker was {worst:0.0} units off the ground on the way");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>A floor from x −12 to 0 and a ramp rising 30° from x 0 to 6 m, where everything ends in a drop.</summary>
    static float Ramp(float x) => x <= 0 ? 0f : x * 0.57735f;

    [Fact]
    public void A_walker_left_on_a_polygon_it_is_no_longer_over_keeps_its_edge_s_height_not_its_plane_s()
    {
        var root = Cook("ramp", -12, 6, Ramp);
        try
        {
            var level = CellLevel.Load(L2Package.PathFor(root, "ramp"));
            var walk = new WalkMap(level.Shell);
            // Up the ramp to the last of the navigation mesh (it ends a body's radius short of the drop).
            var at = new Vector3(3 * M, 0.2f * M, Ramp(3) * M);
            int node = walk.Find(at);
            Assert.True(node >= 0);
            Assert.True(walk.Nodes[node].Plane.Normal.X < -0.1f, "the polygon under the ramp is not sloped");
            float lastOnIt = at.Z;
            // On over the edge and 3 m past the drop: no ground is under the point, so the surface is kept.
            for (int i = 0; i < 6 * 18; i++)
            {
                var step = walk.Stride(node, at, at + new Vector3(4f, 0, 0));
                (node, at) = (step.Node, step.Position);
                if (walk.Nodes[node].Contains(new Vector2(at.X, at.Y))) lastOnIt = at.Z;
            }
            Assert.True(at.X > 8.5f * M, "the walk did not go past the drop");
            Assert.False(walk.Nodes[node].Contains(new Vector2(at.X, at.Y)));
            // The plane carried on would be 1.7 m higher there; the ramp's own top is 0.14 m over the mesh's edge.
            Assert.True(MathF.Abs(at.Z - lastOnIt) < 0.25f * M,
                        $"3 m past the drop the walker is at {at.Z:0}, and it left its polygon at {lastOnIt:0}");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Christof_sent_across_the_hollow_walks_down_into_it_and_arrives_on_the_ground()
    {
        using var files = Install.Open();
        var root = Cook("hollowplay", -6, 6, Hollow);
        var was = L2Levels.Folder;
        try
        {
            L2Levels.Folder = root;
            var place = Location.Load(files, "hollowplay", player: "Christof");
            var level = CellLevel.Of(place.Level)!;
            var player = place.Player!;
            var goal = new Vector3(4.5f * M, 2 * M, 0);
            int node = place.Walk!.Find(goal);
            Assert.True(player.WalkTo(goal, node));
            var aim = player.Destination;
            var sim = new Simulation(place);
            float worst = 0f, lowest = 0f;
            for (int i = 0; i < 60 * 60 && player.Walking; i++)
            {
                sim.Step(1f / 60f);
                var p = player.Position;
                float truth = level.Collision.Floor(p.X, p.Y, Hollow(p.X / M) * M, 8f) ?? float.NaN;
                worst = MathF.Max(worst, MathF.Abs(p.Z - truth));
                lowest = MathF.Min(lowest, p.Z);
            }
            Assert.True(worst < 1f, $"Christof was {worst:0.0} units off the ground on the way");
            Assert.True(lowest < -(Depth - 0.1f) * M, $"Christof never went down into the hollow: his lowest was {lowest:0}");
            var outcome = WalkOutcome.Of(player, place.Walk, aim, node);
            Assert.Equal(WalkOutcome.Kind.Arrived, outcome.Kind);
            Assert.True(MathF.Abs(outcome.Above) < 1f && outcome.Short < 1f);
        }
        finally
        {
            L2Levels.Folder = was;
            Directory.Delete(root, recursive: true);
        }
    }
}
