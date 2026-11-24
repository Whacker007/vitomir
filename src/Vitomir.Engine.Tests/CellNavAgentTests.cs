// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The navigation mesh baked for a given agent** (ours): the cook's
/// <c>--nav-agent</c> names the agent (<see cref="NavSettings.ParseAgent"/>: <c>human</c> or four numbers), the
/// package records it in every cell's <c>NAVM</c> (level.json does not say it), and Recast counts the human's
/// numbers as whole voxels without losing one to rounding: climb 0.35 m is 7 layers of 0.05 m, not 6.
///
/// Pinned on a small floor with a dais in it: a dais 0.37 m high (8 layers over the floor's top, the datum
/// rounding its top up) is climbed by the default agent's mesh (0.4 m, 8 layers) and not by the human's
/// (0.35 m, 7); one 0.34 m high (7 layers) by both — and, the layers rounding a top up, one 0.36 m high too.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellNavAgentTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;

    [Fact]
    public void The_switch_reads_human_or_four_numbers_and_refuses_anything_else()
    {
        Assert.Equal(NavSettings.Human, NavSettings.ParseAgent("human", out _));
        Assert.Equal(NavSettings.Human, NavSettings.ParseAgent(" Human ", out _));
        Assert.Equal(NavSettings.Default, NavSettings.ParseAgent("default", out _));
        Assert.Equal(NavSettings.Human, NavSettings.ParseAgent("0.3,1.8,0.35,40", out _));
        Assert.Equal(new NavSettings(0.4f, 2f, 0.5f, 50f, 0.15f, 0.05f), NavSettings.ParseAgent("0.4,2,0.5,50", out _));
        Assert.Equal((0.3f, 1.8f, 0.35f, 40f, 0.15f, 0.05f),
            (NavSettings.Human.Radius, NavSettings.Human.Height, NavSettings.Human.Climb, NavSettings.Human.MaxSlope, NavSettings.Human.CellSize, NavSettings.Human.CellHeight));
        foreach (var bad in new[] { null, "", "elder", "0.3,1.8,0.35", "0.3,1.8,0.35,40,1", "0.3,1.8,x,40", "0,1.8,0.35,40", "0.3,0.3,0.35,40", "0.3,1.8,0.35,90", "0.3,1.8,0.35,-5", "0.3;1.8;0.35;40", "NaN,1.8,0.35,40" })
        {
            Assert.Null(NavSettings.ParseAgent(bad, out string error));
            Assert.StartsWith("--nav-agent", error);
        }
    }

    [Fact]
    public void Recast_counts_the_human_as_two_voxels_round_thirty_six_tall_and_seven_of_climb()
    {
        static RcConfig Config(NavSettings n) => new(RcPartition.WATERSHED, n.CellSize, n.CellHeight, n.MaxSlope, n.Height, n.Radius, n.Climb,
            8, 20, 12f, 1.3f, 6, 6f, 1f, true, true, true, new RcAreaModification(RcRecast.RC_WALKABLE_AREA), true);
        var human = Config(NavSettings.Human);
        Assert.Equal((2, 36, 7), (human.WalkableRadius, human.WalkableHeight, human.WalkableClimb));
        var old = Config(NavSettings.Default);
        Assert.Equal((2, 34, 8), (old.WalkableRadius, old.WalkableHeight, old.WalkableClimb));
    }

    /// <summary>A 12 m × 12 m floor at height 0 with a dais 3 m × 4 m, <paramref name="rise"/> metres high, cut into it (its sides are walls, not ground).</summary>
    static (Vector3[] V, int[] I, int[] Mat) FloorWithDais(float rise)
    {
        var v = new List<Vector3>(); var idx = new List<int>(); var mat = new List<int>();
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
        {
            int at = v.Count;
            v.AddRange([a * M, b * M, c * M, d * M]);
            idx.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]); mat.AddRange([m, m]);
        }
        for (int x = -6; x < 6; x++)
            for (int y = -6; y < 6; y++)
            {
                float z = x is >= 1 and < 4 && y is >= -2 and < 2 ? rise : 0f;
                Quad(new(x, y, z), new(x + 1, y, z), new(x + 1, y + 1, z), new(x, y + 1, z), 0);
            }
        Quad(new(1, -2, 0), new(1, 2, 0), new(1, 2, rise), new(1, -2, rise), 1);       // the four sides, facing out
        Quad(new(4, 2, 0), new(4, -2, 0), new(4, -2, rise), new(4, 2, rise), 1);
        Quad(new(4, -2, 0), new(1, -2, 0), new(1, -2, rise), new(4, -2, rise), 1);
        Quad(new(1, 2, 0), new(4, 2, 0), new(4, 2, rise), new(1, 2, rise), 1);
        return ([.. v], [.. idx], [.. mat]);
    }

    /// <summary>Cooks the floor for <paramref name="agent"/> (null: the cook's default) and answers whether Detour finds a route from the floor onto the dais, and the agents the cells recorded.</summary>
    static (bool Reaches, List<L2NavMesh> Recorded, string Manifest) Cooked(float rise, NavSettings? agent)
    {
        var (v, idx, mat) = FloorWithDais(rise);
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = new CellCookInput
            {
                Name = "dais", Vertices = v, Indices = idx, TriangleMaterials = mat, Materials = ["ground", "wall"],
                Ground = m => m == "ground", Passable = _ => false, CellSize = 0f,
            };
            if (agent is not null) input = new CellCookInput
            {
                Name = "dais", Vertices = v, Indices = idx, TriangleMaterials = mat, Materials = ["ground", "wall"],
                Ground = m => m == "ground", Passable = _ => false, CellSize = 0f, Nav = agent,
            };
            string package = L2Package.PathFor(root, "dais");
            CellCook.Cook(input, package);
            var opened = L2Package.Open(package);
            var recorded = new List<L2NavMesh>();
            foreach (var entry in opened.Manifest.Cells)
                if (L2Cell.ReadFile(Path.Combine(opened.Folder, entry.File), t => t is L2NavMesh.Tag).Find(L2NavMesh.Tag) is { } chunk)
                    recorded.Add(L2NavMesh.Decode(chunk));
            using var stream = new CellStream(opened, new CellStreamOptions { Everything = true });
            stream.LoadNow(Vector3.Zero);
            var filter = new DtQueryDefaultFilter();
            var half = new DotRecast.Core.Numerics.RcVec3f(0.5f, 1f, 0.5f);
            var a = CellCook.ToRecast(new Vector3(-4f, 0f, 0f)); var b = CellCook.ToRecast(new Vector3(2.5f, 0f, rise));
            stream.Query.FindNearestPoly(a, half, filter, out long start, out _, out _);
            stream.Query.FindNearestPoly(b, half, filter, out long goal, out _, out _);
            Assert.NotEqual(0L, start);
            bool reaches = false;
            if (goal != 0)
            {
                Span<long> polys = new long[256];
                var status = stream.Query.FindPath(start, goal, a, b, filter, polys, out int count, 256);
                reaches = !status.Failed() && count > 0 && polys[count - 1] == goal;
            }
            return (reaches, recorded, File.ReadAllText(Path.Combine(package, L2Package.ManifestFile)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_riser_of_0_37_m_is_climbed_by_the_default_agents_mesh_and_not_the_humans()
    {
        var (byDefault, defaultRecorded, _) = Cooked(0.37f, null);
        var (byHuman, humanRecorded, manifest) = Cooked(0.37f, NavSettings.Human);
        Assert.True(byDefault, "the default agent's mesh should climb a 0.37 m riser");
        Assert.False(byHuman, "the human's mesh should not climb a 0.37 m riser");
        Assert.All(defaultRecorded, n => Assert.Equal((0.25f, 1.7f, 0.4f, 45f), (n.AgentRadius, n.AgentHeight, n.AgentClimb, n.MaxSlope)));
        Assert.NotEmpty(humanRecorded);
        Assert.All(humanRecorded, n => Assert.Equal((0.3f, 1.8f, 0.35f, 40f, 0.15f, 0.05f), (n.AgentRadius, n.AgentHeight, n.AgentClimb, n.MaxSlope, n.CellSize, n.CellHeight)));
        Assert.DoesNotContain("agent", manifest, StringComparison.OrdinalIgnoreCase);     // the cells say it, level.json does not
    }

    [Fact]
    public void A_riser_of_0_34_m_is_climbed_by_both_and_one_of_0_36_m_too_for_the_layers_round_a_top_up()
    {
        Assert.True(Cooked(0.34f, null).Reaches);
        Assert.True(Cooked(0.34f, NavSettings.Human).Reaches);
        // 0.36 m off a floor at 0 is still 7 layers (its top lifted to the boundary at 0.3625): the human's mesh joins
        // it though the body steps only 0.35 m. A known gap of whole voxels, pinned so it is seen.
        Assert.True(Cooked(0.36f, NavSettings.Human).Reaches);
    }
}
