// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using DotRecast.Detour;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The stairs' ledge filter** (ours, for cell levels; <see cref="CellLedges"/>, <see cref="NavSettings.StairLedges"/>):
/// Recast's ledge filter marks a span whose accessible neighbours spread more than the climb a ledge, and so loses
/// the treads of steep stairs; a bake with <c>--nav-agent</c> judges that spread against twice the climb, and keeps
/// Recast's drop test. Pinned on small cooks for the human: an open-riser flight (0.2 m risers on 0.22 m treads, the
/// East Gate towers') and a closed one (0.35 m risers on 0.30 m treads) route from the floor to the landing at the
/// top with the filter and not without it; a single riser of 0.40 m and a 45° bank do not route either way; and the
/// manifest says <c>"navigation": { "ledges": "stairs" }</c> when the filter was used and nothing otherwise.
/// </summary>
[Collection(LevelCooksCollection.Name)]
public class CellStairLedgeTests
{
    const float M = L2Manifest.EngineUnitsPerMetre;
    static readonly NavSettings Stairs = NavSettings.Human with { StairLedges = true };

    /// <summary>Triangles in metres (scaled to engine units as the cook is given them); material 0 is ground, 1 a wall.</summary>
    sealed class Scene
    {
        public readonly List<Vector3> V = [];
        public readonly List<int> I = [];
        public readonly List<int> Mat = [];

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int m)
        {
            int at = V.Count;
            V.AddRange([a * M, b * M, c * M, d * M]);
            I.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]); Mat.AddRange([m, m]);
        }

        public void Triangle(Vector3 a, Vector3 b, Vector3 c, int m)
        {
            int at = V.Count;
            V.AddRange([a * M, b * M, c * M]);
            I.AddRange([at, at + 1, at + 2]); Mat.Add(m);
        }

        /// <summary>A box from (x0, y0, z0) to (x1, y1, z1): its top ground, its other faces walls, all facing out.</summary>
        public void Box(float x0, float x1, float y0, float y1, float z0, float z1)
        {
            Quad(new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1), 0);
            Quad(new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0), 1);
            Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1), 1);
            Quad(new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), 1);
            Quad(new(x0, y1, z0), new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1), 1);
            Quad(new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1), 1);
        }

        /// <summary>The floor at height 0 from x −6 to 0 (metre squares), and a landing at <paramref name="top"/> from <paramref name="from"/> 4 m on.</summary>
        public Scene FloorAndLanding(float from, float top)
        {
            for (int x = -6; x < 0; x++)
                for (int y = -3; y < 3; y++)
                    Quad(new(x, y, 0), new(x + 1, y, 0), new(x + 1, y + 1, 0), new(x, y + 1, 0), 0);
            Box(from, from + 4f, -1.5f, 1.5f, 0f, top);
            return this;
        }
    }

    const float Y0 = -1.5f, Y1 = 1.5f;

    /// <summary>An open-riser flight: treads 0.05 m thick, nothing between them, from x 0; the landing after the last.</summary>
    static (Scene S, float Top, float Landing) OpenFlight(float riser, float tread, int steps)
    {
        var s = new Scene();
        float x = 0;
        for (int k = 1; k < steps; k++) { s.Box(x, x + tread, Y0, Y1, k * riser - 0.05f, k * riser); x += tread; }
        return (s.FloorAndLanding(x, steps * riser), steps * riser, x);
    }

    /// <summary>A closed flight: each step solid to the floor, from x 0; the landing after the last.</summary>
    static (Scene S, float Top, float Landing) ClosedFlight(float riser, float tread, int steps)
    {
        var s = new Scene();
        float x = 0;
        for (int k = 1; k < steps; k++) { s.Box(x, x + tread, Y0, Y1, 0f, k * riser); x += tread; }
        return (s.FloorAndLanding(x, steps * riser), steps * riser, x);
    }

    /// <summary>A 45° bank from x 0 up to <paramref name="top"/>, and the landing at its top.</summary>
    static (Scene S, float Top, float Landing) Bank(float top)
    {
        var s = new Scene();
        s.Quad(new(0, Y0, 0), new(top, Y0, top), new(top, Y1, top), new(0, Y1, 0), 0);
        s.Triangle(new(0, Y0, 0), new(top, Y0, 0), new(top, Y0, top), 1);
        s.Triangle(new(0, Y1, 0), new(top, Y1, top), new(top, Y1, 0), 1);
        return (s.FloorAndLanding(top, top), top, top);
    }

    /// <summary>Cooks <paramref name="scene"/> for <paramref name="agent"/> and answers whether Detour routes from the floor to the landing's middle, with the manifest.</summary>
    static (bool Reaches, string Manifest) Cooked((Scene S, float Top, float Landing) scene, NavSettings agent)
    {
        var root = Path.Combine(Path.GetTempPath(), "vitomir-celltest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var input = new CellCookInput
            {
                Name = "stairs", Vertices = [.. scene.S.V], Indices = [.. scene.S.I], TriangleMaterials = [.. scene.S.Mat],
                Materials = ["ground", "wall"], Ground = m => m == "ground", Passable = _ => false, CellSize = 0f, Nav = agent,
            };
            string package = L2Package.PathFor(root, "stairs");
            CellCook.Cook(input, package);
            var opened = L2Package.Open(package);
            using var stream = new CellStream(opened, new CellStreamOptions { Everything = true });
            stream.LoadNow(Vector3.Zero);
            var filter = new DtQueryDefaultFilter();
            var half = new DotRecast.Core.Numerics.RcVec3f(0.5f, 0.3f, 0.5f);
            var a = CellCook.ToRecast(new Vector3(-4f, 0f, 0f));
            var b = CellCook.ToRecast(new Vector3(scene.Landing + 2f, 0f, scene.Top));
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
            return (reaches, File.ReadAllText(Path.Combine(package, L2Package.ManifestFile)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_open_riser_flight_of_0_2_m_risers_on_0_22_m_treads_routes_with_the_filter_and_not_without()
    {
        var flight = OpenFlight(0.2f, 0.22f, 8);
        Assert.False(Cooked(flight, NavSettings.Human).Reaches, "Recast's own ledge filter should lose the flight");
        var (reaches, manifest) = Cooked(flight, Stairs);
        Assert.True(reaches, "the stairs' filter should route the open-riser flight end to end");
        Assert.Contains("\"ledges\": \"stairs\"", manifest);
    }

    [Fact]
    public void A_closed_flight_of_0_35_m_risers_on_0_30_m_treads_routes_with_the_filter()
    {
        var flight = ClosedFlight(0.35f, 0.30f, 5);
        Assert.True(Cooked(flight, Stairs).Reaches, "the stairs' filter should route the closed flight end to end");
    }

    [Fact]
    public void A_single_riser_of_0_40_m_and_a_45_degree_bank_do_not_route_with_the_filter()
    {
        Assert.False(Cooked(ClosedFlight(0.40f, 0.30f, 1), Stairs).Reaches, "a 0.40 m riser is over the human's climb");
        Assert.False(Cooked(Bank(1.0f), Stairs).Reaches, "a 45° bank is steeper than the human walks");
    }

    [Fact]
    public void Without_the_filter_the_manifest_says_nothing_of_the_navigation()
    {
        var (_, manifest) = Cooked(ClosedFlight(0.15f, 0.30f, 4), NavSettings.Human);
        Assert.DoesNotContain("\"navigation\"", manifest);
        Assert.Equal(NavSettings.Human, NavSettings.ParseAgent("human", out _));
        Assert.False(NavSettings.Human.StairLedges);
    }
}
