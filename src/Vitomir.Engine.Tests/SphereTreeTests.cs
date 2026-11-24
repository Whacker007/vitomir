// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Explosions;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The sphere's walk down a sector's collision tree**, found by the
/// A comparison with 1.1 in the original's surface query:
/// the light blobs, the explosion decals and the blob shadows count a surface only when the walk
/// marks its plane. Detail surfaces are not in the tree, so in 1.1 none of the three lands on one;
/// Vitomir laid them there until then.
/// </summary>
public class SphereTreeTests
{
    static Vector3 Vertex(NilSector s, NilSurface f, int i) => s.Vertices[s.SectorVertices[f.FirstVertex + i].Vertex];

    static Vector3 Centre(NilSector s, NilSurface f)
    {
        var sum = Vector3.Zero;
        for (int i = 0; i < f.NumVerts; i++) sum += Vertex(s, f, i);
        return sum / f.NumVerts;
    }

    /// <summary>
    /// A drawn Detail surface of the monastery whose plane is no node of its sector's tree: a point
    /// 10 in front of its middle passes the distance and circle tests, and still the decal query
    /// leaves it out — while a surface of the same sector whose plane the walk marks is kept.
    /// </summary>
    [Fact]
    public void A_detail_surface_outside_the_tree_takes_no_mark()
    {
        using var files = Install.Open();
        var level = NilLevel.Read(files.Read("levels/P1_MON2.nil"), "P1_MON2.nil");
        const float reach = 40f;
        for (int si = 0; si < level.Sectors.Length; si++)
        {
            var s = level.Sectors[si];
            if (s.BspCount == 0) continue;
            var inTree = new HashSet<int>();
            for (int i = 0; i < s.BspCount; i++) if (s.Bsp(i) is { IsLeaf: false } n) { inTree.Add(n.Plane); inTree.Add(n.Plane ^ 1); }
            for (int fi = 0; fi < s.Surfaces.Length; fi++)
            {
                var f = s.Surfaces[fi];
                uint flags = s.Groups[f.Group].SurfaceFlags;
                if (f.NumVerts < 3 || (flags & NilGroup.FlagDetail) == 0 || (flags & NilGroup.FlagNodraw) != 0) continue;
                if (inTree.Contains(f.Plane)) continue;
                var plane = s.SurfacePlanes[f.Plane];
                var at = Centre(s, f) + plane.Normal * 10f;
                // The tests Vitomir had earlier pass: in front, within reach, the circle on the polygon.
                if (!DynamicLights.Touches(s, f, plane, at - 10f * plane.Normal, MathF.Sqrt(reach * reach - 100f))) continue;
                var marked = SphereTree.Marked(s, at, reach);
                Assert.False(marked?.Contains(f.Plane) == true);
                var near = Decals.Near(level, [si], at, reach);
                Assert.DoesNotContain(near, n => n.Sector == si && n.Surface == fi);
                // Whatever it keeps, the walk marked.
                Assert.All(near, n => Assert.Contains(level.Sectors[n.Sector].Surfaces[n.Surface].Plane, marked!));
                return;
            }
        }
        Assert.Fail("no detail surface outside its sector's tree was found in P1_MON2");
    }

    /// <summary>
    /// The walk itself, in the monastery's corridor: the point is in a tall room sector with no tree
    /// (passed over), and the floor it stands over is in a sector the flood reaches, whose tree the
    /// sphere straddles at the floor's plane — the plane and its flip are marked, and every surface
    /// the decal query keeps there is one the walk marked. A sphere too small to straddle anything
    /// marks nothing.
    /// </summary>
    [Fact]
    public void A_sphere_on_the_floor_marks_the_floors_plane()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, scene: "MON1_8_2", player: "Christof");
        var level = place.Level;
        var at = new Vector3(-945.458f, -580.13f, -244f);           // 8 above the corridor's floor (z -252)
        int start = place.Solid!.SectorAt(at, -1);
        Assert.Equal(0, level.Sectors[start].BspCount);
        Assert.Null(SphereTree.Marked(level.Sectors[start], at, 20f));
        var sectors = place.Props!.SectorsNear(at, 20f, start);
        var near = Decals.Near(level, sectors, at, 20f);
        Assert.NotEmpty(near);
        foreach (var n in near)
        {
            var s = level.Sectors[n.Sector];
            var marked = SphereTree.Marked(s, at, 20f);
            Assert.NotNull(marked);
            int plane = s.Surfaces[n.Surface].Plane;
            Assert.Contains(plane, marked!);
            Assert.Contains(plane ^ 1, marked!);
            Assert.True(SphereTree.Marked(s, at, 0.001f) is null or { Count: 0 });
        }
    }
}
