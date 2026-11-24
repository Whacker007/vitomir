// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;

namespace Vitomir.Engine.World;

/// <summary>
/// Where the sky may show this frame. The original never draws a level's Sky surfaces; while
/// drawing sectors it grows a screen rectangle around each Sky surface in view (clipped to the
/// near side of w = 0.1, then to the screen), and afterwards draws the sky model restricted to that
/// rectangle. No Sky surface on screen means no sky that frame (and its animation does not advance).
/// Sky groups that are also Nodraw or Reflect never reach that code, so they don't count.
/// Until portal visibility exists, every Sky surface in the level is considered, not just those in
/// visible sectors, so the rectangle can be larger than the original's.
/// </summary>
public sealed class SkyWindow
{
    const float NearW = 0.1f;
    readonly List<Vector3[]> polygons = [];

    public int SurfaceCount => polygons.Count;

    public SkyWindow(NilLevel level)
    {
        foreach (var s in level.Sectors)
            foreach (var g in s.Groups)
            {
                if ((g.SurfaceFlags & NilGroup.FlagSky) == 0 || (g.SurfaceFlags & (NilGroup.FlagNodraw | NilGroup.FlagReflect)) != 0) continue;
                for (int k = 0; k < g.NumSurfaces; k++)
                {
                    var sf = s.Surfaces[g.FirstSurface + k];
                    var poly = new Vector3[sf.NumVerts];
                    for (int i = 0; i < sf.NumVerts; i++) poly[i] = s.Vertices[s.SectorVertices[sf.FirstVertex + i].Vertex];
                    polygons.Add(poly);
                }
            }
    }

    /// <summary>
    /// The pixel rectangle (x, y, width, height; y down) bounding every Sky surface on screen, or
    /// false when none is.
    /// </summary>
    public bool ScreenRect(Matrix4x4 viewProj, int width, int height, out (int X, int Y, int W, int H) rect)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        Span<Vector4> clip = stackalloc Vector4[64];
        Span<Vector2> a = stackalloc Vector2[80], b = stackalloc Vector2[80];
        foreach (var poly in polygons)
        {
            if (poly.Length < 3 || poly.Length > 60) continue;
            for (int i = 0; i < poly.Length; i++) clip[i] = Vector4.Transform(new Vector4(poly[i], 1f), viewProj);
            int n = ClipNear(clip[..poly.Length], a);
            if (n < 3) continue;
            // Screen-space clip to the [-1, 1] square, as the original does before taking bounds.
            n = ClipAxis(a, n, b, 0, +1); n = ClipAxis(b, n, a, 0, -1);
            n = ClipAxis(a, n, b, 1, +1); n = ClipAxis(b, n, a, 1, -1);
            for (int i = 0; i < n; i++)
            {
                minX = MathF.Min(minX, a[i].X); maxX = MathF.Max(maxX, a[i].X);
                minY = MathF.Min(minY, a[i].Y); maxY = MathF.Max(maxY, a[i].Y);
            }
        }
        if (minX >= maxX || minY >= maxY) { rect = default; return false; }
        // NDC (y up) to pixels (y down).
        int x0 = (int)MathF.Floor((minX * 0.5f + 0.5f) * width), x1 = (int)MathF.Ceiling((maxX * 0.5f + 0.5f) * width);
        int y0 = (int)MathF.Floor((0.5f - maxY * 0.5f) * height), y1 = (int)MathF.Ceiling((0.5f - minY * 0.5f) * height);
        x0 = Math.Clamp(x0, 0, width); x1 = Math.Clamp(x1, 0, width);
        y0 = Math.Clamp(y0, 0, height); y1 = Math.Clamp(y1, 0, height);
        rect = (x0, y0, x1 - x0, y1 - y0);
        return rect.W > 0 && rect.H > 0;
    }

    /// <summary>Clips a clip-space polygon to w &gt; 0.1 and projects it to NDC x, y.</summary>
    static int ClipNear(ReadOnlySpan<Vector4> p, Span<Vector2> output)
    {
        int n = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var c = p[i]; var d = p[(i + 1) % p.Length];
            float dc = c.W - NearW, dd = d.W - NearW;
            if (dc > 0) output[n++] = new Vector2(c.X / c.W, c.Y / c.W);
            if ((dc > 0) != (dd > 0))
            {
                var q = Vector4.Lerp(c, d, dc / (dc - dd));
                output[n++] = new Vector2(q.X / q.W, q.Y / q.W);
            }
        }
        return n;
    }

    /// <summary>One Sutherland–Hodgman step against x (axis 0) or y (axis 1) ≤ 1 (side +1) or ≥ −1 (side −1).</summary>
    static int ClipAxis(ReadOnlySpan<Vector2> p, int count, Span<Vector2> output, int axis, int side)
    {
        int n = 0;
        for (int i = 0; i < count; i++)
        {
            var c = p[i]; var d = p[(i + 1) % count];
            float dc = 1f - side * (axis == 0 ? c.X : c.Y), dd = 1f - side * (axis == 0 ? d.X : d.Y);
            if (dc >= 0) output[n++] = c;
            if ((dc >= 0) != (dd >= 0)) output[n++] = Vector2.Lerp(c, d, dc / (dc - dd));
        }
        return n;
    }
}
