// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The level loader's ceilings, measured against the shipped levels**. 1.1's
/// world and sector loaders assert a handful of per-sector counts, the
/// collision walk marks planes in a table of 4,096, the portal walk's visible list holds 255
/// sectors and a sector's number is the low sixteen bits of its id. These pin how
/// close the 112 shipped levels come to each, with the largest values the doc quotes, so a bigger level
/// knows its headroom and the doc stays true.
///
/// It reads all 112 shipped levels, so the class runs on its own: beside the rest of the suite it
/// starved timed tests
/// (the convent's sound read-ahead, which waits at most 60 s, timed out on the laptop's Debug run).
/// </summary>
[Collection(nameof(LevelLimitsTests))]
[CollectionDefinition(nameof(LevelLimitsTests), DisableParallelization = true)]
public class LevelLimitsTests
{
    static string[] Levels(Vfs.GameFiles files) =>
        [.. files.List("levels/").Where(n => n.EndsWith(".nil", StringComparison.OrdinalIgnoreCase))
                 .Select(Path.GetFileNameWithoutExtension).OfType<string>().Order()];

    /// <summary>The largest value of one count over the game, and where it is.</summary>
    sealed class Peak(string name)
    {
        public string Name { get; } = name;
        public int Value { get; private set; } = -1;
        public string Where { get; private set; } = "";
        public void See(int value, string where) { if (value > Value) (Value, Where) = (value, where); }
        public override string ToString() => $"{Name} {Value} ({Where})";
    }

    /// <summary>
    /// Every shipped level is inside every ceiling the loader asserts or a fixed table imposes, and the
    /// largest values are these: 437 sectors (N1_WAR2), 3,195 vertices,
    /// 12,702 sector vertices and 2,640 surfaces in one sector (P2_VYC1's sector 17), 81 groups (V1_EAST),
    /// 2,814 surface planes (P1_ARC2's sector 63, the nearest any level comes to a table's end), 26
    /// portals (P1_HILL), 166 lights listed in one sector (L1_EAST), 4 lightmap layers, 32 lightmap pages
    /// (L1_TOL1) of at most 256 × 256, 138 materials (N1_UTWN) and 1,880 path nodes (N1_SEW2).
    /// </summary>
    [Fact]
    public void Every_shipped_level_stays_inside_the_loaders_ceilings()
    {
        using var files = Install.Open();
        var sectors = new Peak("sectors"); var verts = new Peak("vertices"); var sectorVerts = new Peak("sector vertices");
        var surfaces = new Peak("surfaces"); var groups = new Peak("groups"); var planes = new Peak("surface planes");
        var portals = new Peak("portals"); var lights = new Peak("lights"); var layers = new Peak("layers");
        var pages = new Peak("lightmap pages"); var side = new Peak("lightmap side"); var materials = new Peak("materials");
        var nodes = new Peak("path nodes"); var links = new Peak("links per node"); var nodePlanes = new Peak("planes per node");
        int levels = 0;
        foreach (var name in Levels(files))
        {
            var nil = NilLevel.Read(files.Read($"levels/{name}.nil"), name);
            levels++;
            string lvl = name.ToUpperInvariant();
            sectors.See(nil.Sectors.Length, lvl);
            pages.See(nil.Lightmaps.Length, lvl);
            foreach (var page in nil.Lightmaps) side.See(Math.Max(page.Width, page.Height), lvl);
            materials.See(nil.Materials.Length, lvl);
            nodes.See(nil.PathNodes.Length, lvl);
            foreach (var node in nil.PathNodes) { links.See(node.Links.Length, lvl); nodePlanes.See(node.Planes.Length, lvl); }
            for (int i = 0; i < nil.Sectors.Length; i++)
            {
                var s = nil.Sectors[i];
                string at = $"{lvl} sector {i}";
                verts.See(s.Vertices.Length, at);
                sectorVerts.See(s.SectorVertices.Length, at);
                surfaces.See(s.Surfaces.Length, at);
                groups.See(s.Groups.Length, at);
                planes.See(s.SurfacePlanes.Length, at);
                portals.See(s.Portals.Length, at);
                lights.See(s.Lights.Length, at);
                foreach (var g in s.Groups) layers.See(g.Layers.Length, at);
                for (int n = 0; n < s.BspCount; n++)
                {
                    var b = s.Bsp(n);
                    if ((b.Kind & NilBspNode.LeafBit) == 0) Assert.True((b.Plane | 1) < 0x1000, $"{at}: node {n} marks plane {b.Plane}, past the plane table's 4,096");
                }
            }
        }
        foreach (var p in new[] { sectors, verts, sectorVerts, surfaces, groups, planes, portals, lights, layers, pages, side, materials, nodes, links, nodePlanes })
            TestContext.Current.SendDiagnosticMessage(p.ToString());

        Assert.Equal(112, levels);
        // The ceilings.
        Assert.True(sectors.Value <= 0xffff, $"{sectors}: a sector's number is the low 16 bits of its id");
        Assert.True(verts.Value <= 0x3fff, $"{verts} (1.1 asserts SECTOR_MAX_VERTS)");
        Assert.True(sectorVerts.Value <= 0x3fff, $"{sectorVerts} (1.1 asserts SECTOR_MAX_VERTS)");
        Assert.True(surfaces.Value <= 0xfff, $"{surfaces} (1.1 asserts SECTOR_MAX_SURFACES)");
        Assert.True(groups.Value <= 0x1ff, $"{groups} (1.1 asserts SECTOR_MAX_GROUPS)");
        Assert.True(planes.Value <= 0x1000, $"{planes} (1.1 asserts SECTOR_MAX_PLANES; its plane table's size)");
        Assert.True(layers.Value <= 4, $"{layers} (a group record has room for four)");
        // The values the doc quotes.
        Assert.Equal((437, "N1_WAR2"), (sectors.Value, sectors.Where));
        Assert.Equal((3195, "P2_VYC1 sector 17"), (verts.Value, verts.Where));
        Assert.Equal((12702, "P2_VYC1 sector 17"), (sectorVerts.Value, sectorVerts.Where));
        Assert.Equal((2640, "P2_VYC1 sector 17"), (surfaces.Value, surfaces.Where));
        Assert.Equal((81, "V1_EAST sector 6"), (groups.Value, groups.Where));
        Assert.Equal((2814, "P1_ARC2 sector 63"), (planes.Value, planes.Where));
        Assert.Equal(26, portals.Value);
        Assert.Equal(166, lights.Value);
        Assert.Equal(4, layers.Value);
        Assert.Equal((32, "L1_TOL1"), (pages.Value, pages.Where));
        Assert.Equal(256, side.Value);
        Assert.Equal((138, "N1_UTWN"), (materials.Value, materials.Where));
        Assert.Equal((1880, "N1_SEW2"), (nodes.Value, nodes.Where));
        Assert.True(links.Value < 256 && nodePlanes.Value < 256, $"{links}, {nodePlanes}: both are a byte on disk");
    }

    /// <summary>
    /// **No view in the levels with the most sectors comes near the 255-sector visible list** (1.1
    /// asserts <c>numSectors &lt; SECTOR_MAX_VISIBLE</c>; the list has room for 255). Eyes at
    /// head height over walkable surfaces, looking every way at 1.1's 80° across and 4:3, with the far clip
    /// at 10,000 (the default), in the six levels with the most sectors and the Old Town.
    /// The most found are 194 in V1_INNR (243 sectors), 190 in V1_SOUT and 107 of the Old Town's 183 —
    /// headroom a level twice the Old Town's size would nearly use up.
    /// </summary>
    [Fact]
    public void No_view_in_the_largest_levels_comes_near_the_visible_sector_list()
    {
        using var files = Install.Open();
        float fovY = 2f * MathF.Atan(MathF.Tan(40f * MathF.PI / 180f) * 3f / 4f);
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(fovY, 4f / 3f, 10f, 10000f);
        var largest = Levels(files).Select(n => (Name: n, Nil: NilLevel.Read(files.Read($"levels/{n}.nil"), n)))
            .OrderByDescending(l => l.Nil.Sectors.Length).Take(6).ToList();
        if (!largest.Any(l => l.Name.Equals("P1_OLDT", StringComparison.OrdinalIgnoreCase)))
            largest.Add(("P1_OLDT", NilLevel.Read(files.Read("levels/P1_OLDT.nil"), "P1_OLDT")));
        int most = 0;
        foreach (var (name, nil) in largest)
        {
            var solid = new Collision(nil);
            var rng = new Random(11);
            int levelMost = 0, looks = 0;
            for (int i = 0; i < 4000; i++)
            {
                int si = rng.Next(nil.Sectors.Length);
                var s = nil.Sectors[si];
                var walkable = s.Surfaces.Where(f => f.PathNode >= 0).ToArray();
                if (walkable.Length == 0) continue;
                var f = walkable[rng.Next(walkable.Length)];
                var centre = Vector3.Zero;
                for (int k = 0; k < f.NumVerts; k++) centre += s.Vertices[s.SectorVertices[f.FirstVertex + k].Vertex];
                var eye = centre / f.NumVerts + new Vector3(0, 0, 60f);
                int start = solid.SectorAt(eye, si);
                if (start < 0) continue;
                float yaw = (float)(rng.NextDouble() * Math.PI * 2), pitch = (float)((rng.NextDouble() - 0.5) * 0.4);
                var forward = new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch));
                var view = Matrix4x4.CreateLookAt(eye, eye + forward, Vector3.UnitZ);
                var walk = new PortalView();
                walk.Walk(nil, PortalView.SettledStart(nil, solid, start, eye), eye, view * proj, ScreenRect.Full, PortalCamera.From(view, proj));
                levelMost = Math.Max(levelMost, walk.Visible.Count);
                looks++;
            }
            TestContext.Current.SendDiagnosticMessage($"{name}: {nil.Sectors.Length} sectors, at most {levelMost} visible in {looks} looks");
            Assert.True(looks > 800, $"{name}: {looks} looks");
            most = Math.Max(most, levelMost);
        }
        Assert.True(most < PortalView.MaxSectors, $"a view reached {most} of the list's {PortalView.MaxSectors}");
    }
}
