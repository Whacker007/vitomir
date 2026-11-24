// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Formats;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours.** The <see cref="NilLevel"/> a cell level hands the game (<see cref="CellLevel"/>): one
/// sector the size of the level, sunlit, with no collision tree (so nothing in it is solid to 1.1's
/// tests, as in a sector without one, <c>Collision.Solid</c>) and nothing to draw, whose path nodes
/// are the slots of the navigation mesh's polygons, filled in the walk map as cells load.
/// </summary>
public static class CellShell
{
    /// <summary>
    /// The sector's flags: those of the daytime Old Town's outdoor sectors (<c>P1_OTDY</c> sector 0,
    /// 0x10005), whose 0x10000 is the sunlit bit the model lighting tests (<c>ModelLight.SunlitSector</c>).
    /// </summary>
    public const uint SectorFlags = 0x10005;

    /// <summary>How far the sector's box reaches past the level's triangles (engine units).</summary>
    public const float Margin = 1024f;

    /// <summary>The <c>.nil</c> version the shell claims: the shipped levels'.</summary>
    public const uint NilVersion = 27;

    /// <summary>
    /// The shell for a level whose ground is <paramref name="slots"/> walk-node slots (every polygon of
    /// every cell's navigation-mesh tile, <see cref="CellStream"/>): one sector the size of the level,
    /// sunlit, holding the level's lights, with a path node per slot and no surfaces of its own. The
    /// pieces of ground are put into the walk map as their cells load (<c>WalkMap.Restream</c>), so the
    /// shell does not change as the level streams — and a level of many cells is not held to a sector's
    /// 65,536 vertices.
    /// </summary>
    public static NilLevel Build(L2Manifest manifest, IReadOnlyList<L2Light> lights, int slots, float scale)
    {
        var b = manifest.Bounds;
        var min = new Vector3(b.Min[0], b.Min[1], b.Min[2]) * scale - new Vector3(Margin);
        var max = new Vector3(b.Max[0], b.Max[1], b.Max[2]) * scale + new Vector3(Margin);
        var sector = new NilSector
        {
            Flags = SectorFlags,
            SectorPlanes =
            [
                new(Vector3.UnitX, min.X), new(-Vector3.UnitX, -max.X),
                new(Vector3.UnitY, min.Y), new(-Vector3.UnitY, -max.Y),
                new(Vector3.UnitZ, min.Z), new(-Vector3.UnitZ, -max.Z),
            ],
            SurfacePlanes = [], Vertices = [], SectorVertices = [], Groups = [], Surfaces = [],
            Lights = [.. lights.Select(l => PointLight(l, scale))],
        };
        var pathNodes = new NilPathNode[slots];
        for (int i = 0; i < slots; i++) pathNodes[i] = new NilPathNode([], []);
        return new NilLevel
        {
            Version = NilVersion, GlobalLight = Sun(manifest.GlobalLight), Ambient = Ambient(manifest.GlobalLight),
            Materials = [], Sectors = [sector], PathNodes = pathNodes, Lightmaps = [],
        };
    }

    /// <summary>
    /// The daytime Old Town's sun (<c>P1_OTDY</c>'s global light: type 2, colour 255, 230, 170,
    /// intensity 250) when the manifest names none; the manifest's direction when it does.
    /// </summary>
    static NilLight Sun(L2GlobalLight? g)
    {
        var colour = g is { Color.Length: 3 } ? new Vector3(g.Color[0], g.Color[1], g.Color[2]) : new Vector3(255, 230, 170);
        var direction = g is { Direction.Length: 3 } ? new Vector3(g.Direction[0], g.Direction[1], g.Direction[2]) : new Vector3(0.2182179f, 0.4364358f, -0.8728716f);
        float intensity = g?.Intensity ?? 250f;
        var head = new byte[12];
        head[4] = 2;
        return new NilLight(0, false, 2, 0, colour, intensity, intensity, Vector3.Normalize(direction), Vector3.Zero, 0, 0, 0, 0, head);
    }

    /// <summary>The level's ambient, in the <c>.nil</c>'s own scale (the daytime Old Town's 51000, 46000, 34000 by default).</summary>
    static Vector3 Ambient(L2GlobalLight? g) =>
        g is { Ambient.Length: 3 } && g.Ambient.Any(a => a > 0) ? new Vector3(g.Ambient[0], g.Ambient[1], g.Ambient[2]) : new Vector3(51000, 46000, 34000);

    /// <summary>
    /// A <c>LITE</c> light as one of the sector's point lights, for 1.1's model lighting: colour on
    /// 0..255, and a strength that falls to nothing at its range (strength − distance × falloff,
    /// <c>ThingLight.Strength</c>).
    /// </summary>
    static NilLight PointLight(L2Light l, float scale)
    {
        var head = new byte[12];
        head[3] = 1;
        float intensity = 100f * l.Intensity;
        float range = MathF.Max(l.Range * scale, 1f);
        return new NilLight(0, true, NilLight.Point, 0, l.Colour * 255f, intensity, intensity, Vector3.Zero, l.Position * scale,
                            0, 0, range, intensity / range, head);
    }

    /// <summary>A polygon's plane by Newell's method, facing up: <c>normal · p = dist</c>.</summary>
    public static NilPlane PlaneOf(Vector3[] o)
    {
        var n = Vector3.Zero; var centre = Vector3.Zero;
        for (int i = 0; i < o.Length; i++)
        {
            var p = o[i]; var q = o[(i + 1) % o.Length];
            n += new Vector3((p.Y - q.Y) * (p.Z + q.Z), (p.Z - q.Z) * (p.X + q.X), (p.X - q.X) * (p.Y + q.Y));
            centre += p;
        }
        centre /= o.Length;
        n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ;
        if (n.Z < 0) n = -n;
        return new NilPlane(n, Vector3.Dot(n, centre));
    }

    /// <summary>
    /// The scene a cell level stands in for a <c>.nsd</c>: its <c>start</c> anchors as player starts
    /// (each on the ground under it, facing its yaw), and no far clip to speak of.
    /// </summary>
    public static NsdScene Scene(string name, IEnumerable<L2Anchor> anchors, float scale, CellCollision collision)
    {
        var starts = new List<NsdStart>();
        foreach (var a in anchors.Where(a => a.Kind == "start"))
        {
            var p = new Vector3(a.Position[0], a.Position[1], a.Position[2]) * scale;
            p.Z = collision.Floor(p.X, p.Y, p.Z, 4 * CellLevel.FloorWindow) ?? p.Z;
            float yaw = a.Rotation.Length > 2 ? a.Rotation[2] * MathF.PI / 180f : 0f;
            float c = MathF.Cos(yaw), s = MathF.Sin(yaw);
            starts.Add(new NsdStart(0, [c, s, 0, -s, c, 0, 0, 0, 1], p, a.Group ?? 0, (uint)starts.Count));
        }
        return new NsdScene { Starts = [.. starts], ZClip = FarClip };
    }

    /// <summary>The far plane a cell level's scene asks for (engine units): 3 km, where the modern path's haze has the rest.</summary>
    public const float FarClip = 3000f * 72f;
}
