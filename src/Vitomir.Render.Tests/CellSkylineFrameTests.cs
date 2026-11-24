// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Levels.Cells;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;
using Vitomir.Render.Modern;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// The sun on a streamed cell level with a skyline round it (ours):
/// a small level cooked here — a 48 m floor in 15 m cells and a 6 m wall — with land and a house of the
/// city beside it, drawn through <see cref="CellStreamScene"/> and <see cref="ModernRenderer"/> with the
/// skyline and without. The skyline must change nothing on the level's own open ground: the same sunlit
/// cobbles, the same wall's shadow. The wall stands in a cell behind the camera, wholly out of its view,
/// and still shades the ground ahead. Asserts what a working sun must show, not the exact picture, so it
/// holds on any device, the cloud's software Vulkan included.
/// </summary>
[Collection("headless")]
public unsafe class CellSkylineFrameTests(Headless h)
{
    const int W = 320, H = 180;
    const float M = L2Manifest.EngineUnitsPerMetre;

    static L2Vertex Vertex(Vector3 p, Vector3 n) => new() { Position = p, Normal = n, Tangent = new(1, 0, 0, 1), Uv0 = new(p.X, p.Y), Color = 0xffffffff };

    /// <summary>A floor of <paramref name="tile"/>-metre squares from <paramref name="from"/> to <paramref name="to"/> on both axes, at height 0, facing up.</summary>
    static CookMesh Floor(string name, int material, float from, float to, float tile)
    {
        var v = new List<L2Vertex>(); var idx = new List<uint>();
        for (float y = from; y < to; y += tile)
            for (float x = from; x < to; x += tile)
            {
                uint o = (uint)v.Count;
                foreach (var p in new[] { new Vector3(x, y, 0), new(x + tile, y, 0), new(x + tile, y + tile, 0), new(x, y + tile, 0) }) v.Add(Vertex(p, Vector3.UnitZ));
                idx.AddRange([o, o + 1, o + 2, o, o + 2, o + 3]);
            }
        return new CookMesh(name, material, [.. v], [.. idx]);
    }

    /// <summary>A closed box, its faces outward and counter-clockwise from outside.</summary>
    static CookMesh Box(string name, int material, Vector3 min, Vector3 max)
    {
        var v = new List<L2Vertex>(); var idx = new List<uint>();
        void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            uint o = (uint)v.Count;
            foreach (var p in new[] { a, b, c, d }) v.Add(Vertex(p, n));
            idx.AddRange([o, o + 1, o + 2, o, o + 2, o + 3]);
        }
        Vector3 P(int x, int y, int z) => new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
        Face(Vector3.UnitZ, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1));
        Face(-Vector3.UnitZ, P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0));
        Face(Vector3.UnitX, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1));
        Face(-Vector3.UnitX, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0));
        Face(Vector3.UnitY, P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0));
        Face(-Vector3.UnitY, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1));
        return new CookMesh(name, material, [.. v], [.. idx]);
    }

    /// <summary>
    /// Cooks the level into <paramref name="root"/>: the floor (−24..24 m, 3 m squares) and a wall 6 m high at
    /// x −2..−0.5, y 1..3, in 15 m cells (their edges on multiples of 15 m, so the wall's cell ends at x 0);
    /// then its skyline: land round the floor out to 90 m and a city house 16 m beyond its +X edge.
    /// </summary>
    static string Cook(string root)
    {
        var package = L2Package.PathFor(root, "suntest");
        var floor = Floor("floor", 0, -24, 24, 3);
        var wall = Box("wall", 1, new(-2, 1, 0), new(-0.5f, 3, 6));
        CellCook.Cook(new CellCookInput
        {
            Name = "suntest", Vertices = [.. floor.Vertices.Select(v => v.Position * M)], Indices = [.. floor.Indices.Select(i => (int)i)],
            TriangleMaterials = new int[floor.Indices.Length / 3], Materials = ["ground", "wall", "city_land", "city_wall"],
            Ground = m => m == "ground", Passable = _ => false, Meshes = [floor, wall], CellSize = 15,
        }, package);
        var sky = new[]
        {
            Box("land", 2, new(24, -90, -0.4f), new(90, 90, 0)), Box("land", 2, new(-90, -90, -0.4f), new(-24, 90, 0)),
            Box("land", 2, new(-24, 24, -0.4f), new(24, 90, 0)), Box("land", 2, new(-24, -90, -0.4f), new(24, -24, 0)),
            Box("house", 3, new(40, -4, 0), new(48, 4, 9)),
        };
        var source = Path.Combine(root, "city.glb");
        File.WriteAllBytes(source, [0]);
        var opened = L2Package.Open(package);
        var entry = CellSkyline.Cook(sky, Vector2.Zero, source, 2, 2, [], package);
        opened.Manifest.Skyline = entry;
        File.WriteAllText(Path.Combine(package, L2Package.ManifestFile), opened.Manifest.Write());
        // The renderer takes its materials from the skyline's glTF when there is one; this test's are the defaults.
        File.Delete(Path.Combine(package, entry.Asset));
        return package;
    }

    /// <summary>Standing 1 m past the wall's cell, 1.7 m up, looking along +X a little down: the wall is behind.</summary>
    static FreeCamera Camera() => new() { Position = new Vector3(1f, 0, 1.7f) * M, Yaw = 0, Pitch = -15 };

    Func<Vector3, int> Render(CellLevel level, bool skyline, out int drawnSkyline)
    {
        var settings = new ModernSettings { SunAzimuth = 180, SunElevation = 30, HazeVisibilityMetres = 0 };
        using var stream = new CellStreamScene(h.Gpu!, level, 1f, new ModernStreamSettings { Skyline = skyline });
        using var renderer = new ModernRenderer(h.Gpu!, settings, h.Gpu!.ColorFormat);
        var camera = Camera();
        var viewProj = ModernRenderer.ViewProjection(camera, (float)W / H);
        stream.LoadNow(camera.Position, viewProj);
        var cmd = SDL_AcquireGPUCommandBuffer(h.Gpu!.Device);
        stream.Update(cmd, camera.Position, viewProj);
        var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
        SDL_WaitForGPUFences(h.Gpu!.Device, true, &fence, 1);
        SDL_ReleaseGPUFence(h.Gpu!.Device, fence);
        drawnSkyline = stream.DrawnSkylineDraws;
        // The precondition: the wall's cell is wholly out of the camera's view.
        var planes = ModernMath.FrustumPlanes(viewProj);
        var wallCell = level.Manifest.Cells.Single(c => c.Bounds.Min[0] < -1 && c.Bounds.Max[0] > -1 && c.Bounds.Min[1] < 2 && c.Bounds.Max[1] > 2);
        Assert.False(ModernMath.BoxVisible(planes, new Vector3(wallCell.Bounds.Min[0], wallCell.Bounds.Min[1], wallCell.Bounds.Min[2]) * M,
                                           new Vector3(wallCell.Bounds.Max[0], wallCell.Bounds.Max[1], wallCell.Bounds.Max[2]) * M), "the wall's cell is in view");
        var pixels = renderer.RenderToImage(stream.Scene, camera, W, H);
        return p =>
        {
            var c = Vector4.Transform(new Vector4(p * M, 1), viewProj);
            int x = (int)((c.X / c.W * 0.5f + 0.5f) * W), y = (int)((0.5f - c.Y / c.W * 0.5f) * H);
            int o = (Math.Clamp(y, 0, H - 1) * W + Math.Clamp(x, 0, W - 1)) * 4;
            return pixels[o] + pixels[o + 1] + pixels[o + 2];
        };
    }

    [Fact]
    public void Open_ground_is_lit_and_shaded_by_the_sun_the_same_with_the_skyline_as_without_even_by_a_wall_out_of_view()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var root = Path.Combine(Path.GetTempPath(), "vitomir-sun-" + Guid.NewGuid().ToString("N"));
        try
        {
            var level = CellLevel.Load(Cook(root));
            Assert.NotNull(level.Manifest.Skyline);
            // The sun from −X at 30°: the 6 m wall's shadow runs 10.4 m along +X from its foot, 2 m wide.
            var shade = new Vector3(6, 2, 0); var sun = new Vector3(6, -3, 0);
            var with = Render(level, skyline: true, out int skylineDraws);
            var without = Render(level, skyline: false, out int noSkylineDraws);
            Assert.True(skylineDraws > 0, "the skyline is drawn");
            Assert.Equal(0, noSkylineDraws);
            foreach (var (name, at) in new[] { ("with the skyline", with), ("without it", without) })
            {
                Assert.True(at(shade) < at(sun) * 0.6f, $"{name}: the wall's shadow {at(shade)} against sunlit ground {at(sun)}");
                Assert.True(at(shade) > 10, $"{name}: the shadow is lit by the sky, not black");
            }
            Assert.InRange(with(sun), without(sun) * 0.97f, without(sun) * 1.03f + 3);
            Assert.InRange(with(shade), without(shade) * 0.95f, without(shade) * 1.05f + 3);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
