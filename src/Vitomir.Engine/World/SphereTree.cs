// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The sphere's walk down a sector's collision tree that the sphere's surface query makes before it
// looks at a single surface (found by a comparison with 1.1).
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// **Which of a sector's planes a sphere counts against** — the tree
/// walk, which the sphere's surface query (the light blobs, the explosion decals
/// and the blob shadows) runs on each sector of its flood before
/// it tests one surface. Its whole loop, which a first reading missed:
/// <list type="number">
/// <item>a sector with **no collision tree**  is passed over — no
/// shipped sector without a tree has a drawn surface (145 of 7,712, measured), so this never
/// shows;</item>
/// <item>the tree is walked from the root with the sphere: a leaf answers its solid bit;
/// at a node the sphere wholly in front goes to the front child, wholly behind to the back child (the
/// centre's distance from the node's plane against the radius); a sphere that
/// straddles the plane walks **both**, front first, and each child that answers solid **marks the
/// node's plane and its flip** (<c>plane ^ 1</c>) with the query's byte stamp (a table
/// a plane, the stamp bumped for each sector). A walk that answers nothing solid passes
/// the sector over;</item>
/// <item>then only a surface whose plane was marked is looked at — before the
/// distance and the circle tests Vitomir already had.</item>
/// </list>
/// So a surface counts only when its plane splits the tree somewhere the sphere reaches, with solid
/// on a side of it within the sphere. **Detail** surfaces are not in the collision tree: 69,554 of
/// the game's 83,519 drawn detail surfaces have a plane that is no node of their sector's tree
/// (with 1,734 others, 71,288 of 835,193 drawn surfaces; measured), and in 1.1 a torch's
/// light blob, an explosion's scorch and a character's blob shadow never land on them. Vitomir laid
/// all three on every surface the distance and circle tests passed until then.
/// </summary>
public static class SphereTree
{
    /// <summary>
    /// The planes of <paramref name="sector"/> the sphere marks (each with its flip), or null when
    /// the sector has no tree or the walk found nothing solid — the sector is then passed over.
    /// </summary>
    public static HashSet<int>? Marked(NilSector sector, Vector3 centre, float radius)
    {
        if (sector.BspCount == 0) return null;
        var marked = new HashSet<int>();
        return Walk(sector, 0, centre, radius, marked) ? marked : null;
    }

    /// <summary>The tree walk from node <paramref name="index"/>: whether a leaf the sphere reaches is solid.</summary>
    static bool Walk(NilSector sector, int index, Vector3 c, float r, HashSet<int> marked)
    {
        while (true)
        {
            if (index < 0 || index >= sector.BspCount) return false;     // ours: a bad index answers open
            var node = sector.Bsp(index);
            if (node.IsLeaf) return (node.Kind & NilBspNode.SolidBit) != 0;
            if (node.Plane >= sector.SurfacePlanes.Length) return false; // ours: 1.1 asserts planeNum < 0x1000
            var plane = sector.SurfacePlanes[node.Plane];
            float d = Vector3.Dot(plane.Normal, c) - plane.Dist;
            if (d > r) { index = node.Front; continue; }
            if (d < -r) { index = node.Back; continue; }
            bool any = false;
            if (Walk(sector, node.Front, c, r, marked)) { any = true; Mark(node.Plane, marked); }
            if (Walk(sector, node.Back, c, r, marked)) { any = true; Mark(node.Plane, marked); }
            return any;
        }
    }

    static void Mark(int plane, HashSet<int> marked)
    {
        marked.Add(plane);
        marked.Add(plane ^ 1);
    }
}
