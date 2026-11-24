// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using SDL;
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// A renderer over a mesh with nothing in it. This is not a hypothetical: the status bar's head
/// viewport builds its <see cref="WorldRenderer"/> in its constructor, before a single head model
/// has been loaded, so the first <see cref="WorldRenderer.Sync"/> runs against an empty mesh.
///
/// The bug this file exists for is worth recording in full, because it is the mirror image of the
/// usual one. A zero-byte GPU buffer is illegal — D3D12 answers <c>0x80070057</c> and the Vulkan
/// spec forbids it — but SwiftShader, which is the device these tests run on in the cloud, hands
/// one back without complaint. So the cloud run was green and the same two tests threw on the first
/// machine with a real driver. The platform difference did not invent a false failure; it *hid* a
/// real one. Hence these, which assert the contract rather than the picture, and so hold on any
/// device: an empty span yields no buffer, and a renderer with no buffer draws nothing quietly.
/// </summary>
[Collection("headless")]
public unsafe class EmptyMeshTests(Headless gpu)
{
    [Fact]
    public void A_buffer_of_nothing_is_no_buffer()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        Assert.True(gpu.Gpu!.CreateBuffer<SceneVertex>(
            SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, []) == null);
        Assert.True(gpu.Gpu!.CreateBuffer<uint>(
            SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, []) == null);
    }

    [Fact]
    public void A_renderer_over_an_empty_mesh_builds_and_draws_nothing()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var materials = new MaterialCache(gpu.Files!);
        var mesh = new SceneMesh();
        // The renderer reads the snapshot's scene, filled from the empty mesh.
        var snapshot = new SceneSnapshot(mesh, materials, new ScenePoses(mesh), new LightStyles());
        var scene = new ClassicScene();
        snapshot.Fill(scene, 0f);
        using var renderer = new WorldRenderer(gpu.Gpu!, []);
        renderer.Sync(scene);
        Assert.Equal(0, renderer.Triangles);

        const int w = 64, h = 64;
        var target = gpu.Gpu!.CreateTarget(w, h, gpu.Gpu.ColorFormat,
                                           SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Gpu.Device);
        renderer.Sync(scene);
        renderer.UploadAnimated(cmd);
        var clear = new SDL_GPUColorTargetInfo
        {
            texture = target, clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &clear, 1, null);
        renderer.DrawOpaque(cmd, pass, Matrix4x4.Identity, BatchLayer.Level);
        renderer.DrawTranslucent(cmd, pass, Matrix4x4.Identity, Vector3.Zero);
        SDL_EndGPURenderPass(pass);
        SDL_SubmitGPUCommandBuffer(cmd);
        SDL_ReleaseGPUTexture(gpu.Gpu.Device, target);
    }
}
