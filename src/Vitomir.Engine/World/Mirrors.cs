// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The mirrors a camera sees, and the view it sees them with: the first half of the world renderer's
// world draw, read. The drawing is `Render.ReflectionPass`'s; this is the
// arithmetic, which needs no GPU.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>What a camera sees of a level's mirrors this frame (<see cref="Mirrors.Find"/>).</summary>
/// <param name="Rect">The mirrors' rectangle on the screen, all of them united.</param>
/// <param name="Plane">The plane the view is reflected in — the **last** mirror surface found.</param>
/// <param name="Sectors">The sectors the mirrors seen are in, in the walk's order.</param>
/// <param name="View">The mirrored view (the reflection, then the camera's view).</param>
/// <param name="ViewProjection">The mirrored view with the projection its near plane moved onto the mirror (<see cref="Mirrors.Oblique"/>).</param>
public sealed record MirrorView(ScreenRect Rect, NilPlane Plane, IReadOnlyList<int> Sectors, Matrix4x4 View, Matrix4x4 ViewProjection);

/// <summary>
/// **Reflect** groups (0x8000; a sector with one is given flag 2 when the level loads) are mirrors.
/// This class is the reflection pass's arithmetic.
/// </summary>
public static class Mirrors
{
    /// <summary>A mirror's corner at or behind this <c>w</c> is behind the eye (0.1).</summary>
    public const float SurfaceNearW = 0.1f;
    /// <summary>What a point's height over the mirror is multiplied by to reflect it (−2).</summary>
    public const float Reflect = -2f;

    /// <summary>Whether a group is a mirror.</summary>
    public static bool IsMirror(NilGroup g) => (g.SurfaceFlags & NilGroup.FlagReflect) != 0;

    /// <summary>Whether a sector has a mirror in it (the load's sector flag 2, `SECTOR_HASREFLECTIONS`).</summary>
    public static bool HasMirror(NilSector s) => s.Groups.Any(IsMirror);

    /// <summary>
    /// The world draw's mirror search: in every sector the walk reached that has a mirror, every
    /// surface of a Reflect group whose plane the eye is not behind (<c>dist ≤ n · eye</c>) and which
    /// comes out on the screen within that sector's rectangle (clipped at
    /// <c>w &gt; 0.1</c>) — the rectangles united, the plane kept being the last one found, so every
    /// mirror in view is drawn with that one reflection — then the eye reflected in it and the
    /// projection given a near plane on it. Null when no mirror is in view.
    /// </summary>
    public static MirrorView? Find(NilLevel level, PortalView walk, Vector3 eye, Matrix4x4 view, Matrix4x4 projection)
    {
        var viewProj = view * projection;
        var rect = ScreenRect.None;
        NilPlane plane = default;
        var sectors = new List<int>();
        foreach (var (si, sectorRect) in walk.Visible)
        {
            var s = level.Sectors[si];
            bool here = false;
            foreach (var g in s.Groups)
            {
                if (!IsMirror(g)) continue;
                for (int k = 0; k < g.NumSurfaces; k++)
                {
                    var sf = s.Surfaces[g.FirstSurface + k];
                    var p = s.SurfacePlanes[sf.Plane];
                    if (!(p.Dist <= Vector3.Dot(p.Normal, eye))) continue;
                    var poly = new Vector3[sf.NumVerts];
                    for (int i = 0; i < poly.Length; i++) poly[i] = s.Vertices[s.SectorVertices[sf.FirstVertex + i].Vertex];
                    if (!PortalView.Project(poly, viewProj, sectorRect, out var r, SurfaceNearW)) continue;
                    rect = rect.Union(r);
                    plane = p;
                    here = true;
                }
            }
            if (here) sectors.Add(si);
        }
        if (sectors.Count == 0) return null;
        var mirrored = Mirror(plane) * view;
        return new MirrorView(rect, plane, sectors, mirrored, mirrored * Oblique(projection, mirrored, plane));
    }

    /// <summary>
    /// The reflection in a plane (the world draw's arithmetic: a point moved by <c>n × ((n · p − d) × −2)</c>,
    /// a direction by <c>n × ((n · a) × −2)</c>) as a matrix on row vectors.
    /// </summary>
    public static Matrix4x4 Mirror(NilPlane plane)
    {
        var n = plane.Normal;
        float d = plane.Dist;
        var m = Matrix4x4.Identity;
        m.M11 = 1f + Reflect * n.X * n.X; m.M12 = Reflect * n.X * n.Y; m.M13 = Reflect * n.X * n.Z;
        m.M21 = Reflect * n.Y * n.X; m.M22 = 1f + Reflect * n.Y * n.Y; m.M23 = Reflect * n.Y * n.Z;
        m.M31 = Reflect * n.Z * n.X; m.M32 = Reflect * n.Z * n.Y; m.M33 = 1f + Reflect * n.Z * n.Z;
        m.M41 = -Reflect * d * n.X; m.M42 = -Reflect * d * n.Y; m.M43 = -Reflect * d * n.Z;
        return m;
    }

    /// <summary>
    /// **Ours**: the projection with its near plane moved onto the mirror (Lengyel's oblique near
    /// plane, for a depth range of 0 to 1 and row vectors), so that what lies behind the glass —
    /// which the reflection would put between the eye and the glass — is not drawn. 1.1 gets there
    /// with a portal walk from the mirror's own sector (in the mirror's rectangle); this
    /// needs no list of sectors.
    /// </summary>
    public static Matrix4x4 Oblique(Matrix4x4 projection, Matrix4x4 mirroredView, NilPlane plane)
    {
        // The mirrored view maps a world point p to R(p) and then views it. Kept are the points in
        // front of the mirror before the reflection, n · p − d ≥ 0 — the room the eye is in, which
        // the reflection puts behind the glass; the plane taken into view space by the mirrored
        // view's inverse, transposed. The reflected eye is behind the glass, so it is cut away.
        if (!Matrix4x4.Invert(mirroredView, out var inv)) return projection;
        var world = new Vector4(plane.Normal, -plane.Dist);
        var c = Vector4.Transform(world, Matrix4x4.Transpose(inv));
        if (!Matrix4x4.Invert(projection, out var pinv)) return projection;
        var corner = Vector4.Transform(new Vector4(MathF.Sign(c.X), MathF.Sign(c.Y), 1f, 1f), pinv);
        var col4 = new Vector4(projection.M14, projection.M24, projection.M34, projection.M44);
        float denom = Vector4.Dot(c, corner);
        if (MathF.Abs(denom) < 1e-12f) return projection;
        var col3 = c * (Vector4.Dot(corner, col4) / denom);
        var p = projection;
        p.M13 = col3.X; p.M23 = col3.Y; p.M33 = col3.Z; p.M43 = col3.W;
        return p;
    }
}
