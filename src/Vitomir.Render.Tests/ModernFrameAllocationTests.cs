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
/// **What a frame of a cell level allocates**
/// (ours). A frame log of a walk through the new Old Town showed a full garbage collection in
/// most of its seconds. Two things made them: the texture streamer read every mip into a new array each
/// time a map changed its top mip (megabytes on the large object heap, several times a second on a walk),
/// and the streamer and the renderer made their lists of draws anew each frame with LINQ (a third of a
/// megabyte a frame in the town). Both now keep what they use from frame to frame.
///
/// Pinned here on a small level cooked in the test — a 48 m floor in 15 m cells with two hundred boxes of
/// twenty-four materials on it and a skyline round it, a few hundred draws — walked through with the camera
/// turning, so that what is seen and what casts change every frame: once the first frames have grown the
/// lists, a frame's streaming and drawing allocate under <see cref="BytesAFrame"/> on the frame's thread.
/// An object on the large object heap is at least 85,000 bytes, so a frame under the bound has put nothing
/// there. The same walk with the lists made anew each frame, as before, allocated over 150,000 bytes a
/// frame on this level. And the orders the passes draw in are exactly the ones LINQ gave
/// (<see cref="ModernDrawOrder"/>): pinned against LINQ itself on made-up draws.
/// </summary>
[Collection("headless")]
public unsafe class ModernFrameAllocationTests(Headless h)
{
    const int W = 160, H = 90;
    const float M = L2Manifest.EngineUnitsPerMetre;
    /// <summary>What a frame's streaming and drawing may allocate at the most once the lists have grown (bytes).</summary>
    const long BytesAFrame = 4096;
    const int Materials = 24;

    static L2Vertex Vertex(Vector3 p, Vector3 n) => new() { Position = p, Normal = n, Tangent = new(1, 0, 0, 1), Uv0 = new(p.X, p.Y), Color = 0xffffffff };

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

    /// <summary>The level: the floor (−24..24 m), two hundred boxes over it in a grid, their materials taking turns, and a skyline of land and houses.</summary>
    static string Cook(string root)
    {
        var package = L2Package.PathFor(root, "alloctest");
        var floor = Floor("floor", 0, -24, 24, 3);
        var meshes = new List<CookMesh> { floor };
        int n = 0;
        for (float y = -22; y < 22 && n < 200; y += 3)
            for (float x = -22; x < 22 && n < 200; x += 3, n++)
                meshes.Add(Box($"box{n}", 1 + n % Materials, new(x, y, 0), new(x + 1, y + 1, 1 + n % 5)));
        CellCook.Cook(new CellCookInput
        {
            Name = "alloctest", Vertices = [.. floor.Vertices.Select(v => v.Position * M)], Indices = [.. floor.Indices.Select(i => (int)i)],
            TriangleMaterials = new int[floor.Indices.Length / 3],
            Materials = ["ground", .. Enumerable.Range(0, Materials).Select(i => $"wall{i}"), "city_land", "city_wall"],
            Ground = m => m == "ground", Passable = _ => false, Meshes = meshes, CellSize = 15,
        }, package);
        var sky = new List<CookMesh>
        {
            Box("land", Materials + 1, new(24, -90, -0.4f), new(90, 90, 0)), Box("land", Materials + 1, new(-90, -90, -0.4f), new(-24, 90, 0)),
            Box("land", Materials + 1, new(-24, 24, -0.4f), new(24, 90, 0)), Box("land", Materials + 1, new(-24, -90, -0.4f), new(24, -24, 0)),
        };
        for (int k = 0; k < 12; k++) sky.Add(Box($"house{k}", Materials + 2, new(30 + 4 * k, -4, 0), new(33 + 4 * k, 4, 9)));
        var source = Path.Combine(root, "city.glb");
        File.WriteAllBytes(source, [0]);
        var opened = L2Package.Open(package);
        var entry = CellSkyline.Cook([.. sky], Vector2.Zero, source, 2, 2, [], package);
        opened.Manifest.Skyline = entry;
        File.WriteAllText(Path.Combine(package, L2Package.ManifestFile), opened.Manifest.Write());
        File.Delete(Path.Combine(package, entry.Asset));
        return package;
    }

    [Fact]
    public void A_frame_of_a_cell_level_allocates_under_four_kilobytes_and_so_nothing_on_the_large_object_heap()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var root = Path.Combine(Path.GetTempPath(), "vitomir-alloc-" + Guid.NewGuid().ToString("N"));
        try
        {
            var level = CellLevel.Load(Cook(root));
            var gpu = h.Gpu!;
            var settings = new ModernSettings { SunAzimuth = 180, SunElevation = 30, HazeVisibilityMetres = 0 };
            using var stream = new CellStreamScene(gpu, level, 1f, new ModernStreamSettings { Skyline = true, SkylineShadowMetres = 300 });
            using var renderer = new ModernRenderer(gpu, settings, gpu.ColorFormat);
            var target = gpu.CreateTarget(W, H, gpu.ColorFormat,
                SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER);
            const int Warm = 12, Counted = 24;
            long worst = 0, total = 0;
            int draws = 0, casters = 0;
            for (int f = 0; f < Warm + Counted; f++)
            {
                // A walk along the level's middle with the head turning: what is seen and what casts change each frame.
                var camera = new FreeCamera { Position = new Vector3(-20f + f, 0.5f * f - 6f, 1.7f) * M, Yaw = -40 + 7 * f, Pitch = -8 };
                var viewProj = ModernRenderer.ViewProjection(camera, (float)W / H);
                if (!stream.Started) stream.LoadNow(camera.Position, viewProj);
                long before = GC.GetAllocatedBytesForCurrentThread();
                var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
                stream.Update(cmd, camera.Position, viewProj);
                renderer.Render(cmd, target, W, H, stream.Scene, camera);
                var fence = SDL_SubmitGPUCommandBufferAndAcquireFence(cmd);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                SDL_WaitForGPUFences(gpu.Device, true, &fence, 1);
                SDL_ReleaseGPUFence(gpu.Device, fence);
                draws = Math.Max(draws, stream.Scene.Draws.Count);
                casters = Math.Max(casters, stream.Scene.Casters?.Count ?? 0);
                if (f < Warm) continue;
                worst = Math.Max(worst, allocated);
                total += allocated;
            }
            SDL_ReleaseGPUTexture(gpu.Device, target);
            // The level is big enough to mean something: hundreds of draws seen and cast.
            Assert.True(draws >= 150, $"only {draws} draws were seen at the most");
            Assert.True(casters >= 200, $"only {casters} casters at the most");
            Assert.True(worst < BytesAFrame, $"a frame allocated {worst:N0} bytes (the mean {total / Counted:N0}); the bound is {BytesAFrame:N0}");
            Assert.True(worst < 85_000);       // and so nothing went to the large object heap
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Made-up draws: materials taking turns, boxes at growing distances, some at the same distance.</summary>
    static (ModernScene.Draw[] Draws, GltfScene Materials) MadeUp()
    {
        var materials = new GltfScene();
        // 0 opaque single, 1 opaque double, 2 blended single, 3 masked double, 4 blended double.
        materials.Materials.Add(new GltfMaterial { Name = "a" });
        materials.Materials.Add(new GltfMaterial { Name = "b", DoubleSided = true });
        materials.Materials.Add(new GltfMaterial { Name = "c", AlphaMode = GltfAlphaMode.Blend });
        materials.Materials.Add(new GltfMaterial { Name = "d", AlphaMode = GltfAlphaMode.Mask, DoubleSided = true });
        materials.Materials.Add(new GltfMaterial { Name = "e", AlphaMode = GltfAlphaMode.Blend, DoubleSided = true });
        var draws = new ModernScene.Draw[60];
        for (int i = 0; i < draws.Length; i++)
        {
            int material = i % 7 == 6 ? -1 : (i * 3) % 5;                 // the default material among them
            float x = (i * 37) % 11;                                       // distances that repeat: ties
            draws[i] = new ModernScene.Draw((uint)i, 3, material, new Vector3(x, 0, 0), new Vector3(x + 1, 1, 1));
        }
        return (draws, materials);
    }

    [Fact]
    public void The_passes_orders_are_exactly_the_ones_LINQ_gave()
    {
        var (draws, materials) = MadeUp();                              // no GPU in this one: the lists alone
        var eye = new Vector3(3.5f, 0, 0);
        // Everything in view: six planes that hold all of space on their inner side.
        var planes = new Vector4[6];
        for (int i = 0; i < 6; i++) planes[i] = new Vector4(0, 0, 0, 1);
        var order = new ModernDrawOrder();
        order.See(draws, materials, planes, eye);

        GltfMaterial Of(ModernScene.Draw d) => d.Material >= 0 && d.Material < materials.Materials.Count ? materials.Materials[d.Material] : GltfMaterial.Default();
        bool Opaque(ModernScene.Draw d) => Of(d).AlphaMode != GltfAlphaMode.Blend;
        bool TwoSided(ModernScene.Draw d) => Of(d).DoubleSided;
        var seen = draws.ToList();
        Assert.Equal(seen, order.Seen);
        var groups = seen.Where(Opaque).GroupBy(TwoSided).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.OrderBy(d => d.Material).ToList());
        Assert.Equal(groups[false], order.Opaque[0]);
        Assert.Equal(groups[true], order.Opaque[1]);
        Assert.Equal(seen.Where(d => !Opaque(d)).OrderByDescending(d => Vector3.DistanceSquared(d.Centre, eye)).ToList(), order.Blended);
        Assert.Contains(order.Opaque[0], d => d.Material == -1);          // the default material sorts first, as an int does
        Assert.Equal(-1, order.Opaque[0][0].Material);

        // The sun's casters: not blended, in the scene's order; a cascade's by sidedness, the first one met first.
        Assert.Equal(seen.Where(Opaque).ToList(), order.Cast(draws, materials));
        int first = order.Within(draws, materials, planes);
        var cascade = seen.Where(Opaque).GroupBy(TwoSided).ToList();
        Assert.Equal(cascade[0].Key ? 1 : 0, first);
        Assert.Equal(cascade[0].ToList(), order.Cascade[first]);
        Assert.Equal(cascade[1].ToList(), order.Cascade[1 - first]);

        // A pass with a list of its own: the not-blended draws in view by sidedness, the casters left alone.
        int own = order.OpaqueWithin(draws, materials, planes);
        Assert.Equal(first, own);
        Assert.Equal(cascade[0].ToList(), order.Cascade[own]);
        Assert.Equal(cascade[1].ToList(), order.Cascade[1 - own]);

        // A second frame that sees less leaves nothing of the first behind.
        planes[0] = new Vector4(-1, 0, 0, 5f);                             // only what reaches x < 5
        order.See(draws, materials, planes, eye);
        var fewer = draws.Where(d => ModernMath.BoxVisible(planes, d.Min, d.Max)).ToList();
        Assert.True(fewer.Count < seen.Count && fewer.Count > 0);
        Assert.Equal(fewer, order.Seen);
        Assert.Equal(fewer.Where(Opaque).Count(), order.Opaque[0].Count + order.Opaque[1].Count);
    }
}
