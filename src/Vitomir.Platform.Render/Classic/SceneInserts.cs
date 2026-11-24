// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The 1.1 look, in the platform's renderer.
// The draw list's model inserts drawn: the party heads and the character screens' portrait,
// each from its snapshot scene. Reads no engine object (UiBoundaryTests).
using SDL;
using Vitomir.Platform.Interface;
using static SDL.SDL3;

using Vitomir.Platform.Presentation.Classic;

namespace Vitomir.Platform.Render.Classic;

/// <summary>
/// **The model inserts drawn** (<see cref="DrawKind.Model"/>; the party heads and the character screens' portrait):
/// each snapshot scene by id with a world renderer of its own, which is what gets the materials and the draw modes
/// for nothing, and a depth target the size of the picture. A genuine break in the UI's 2D stream: 1.1 flushes the pending
/// 2D batch on the way in and on the way out. Reads only the scene and the insert.
/// </summary>
public sealed unsafe class SceneInserts(Gpu gpu) : IModelInserts, IDisposable
{
    sealed class Scene(WorldRenderer renderer)
    {
        public readonly WorldRenderer Renderer = renderer;
        public SDL_GPUTexture* Depth;
        public int DepthWidth, DepthHeight;
    }

    readonly Dictionary<int, Scene> scenes = [];

    /// <summary>Makes the scene's renderer now (the party heads' was made with the program's passes, before any frame).</summary>
    public void Make(int id)
    {
        if (!scenes.ContainsKey(id)) scenes[id] = new Scene(new WorldRenderer(gpu, []));
    }

    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int width, int height, int id, ClassicScene scene,
                     in ModelInsert insert, ReadOnlySpan<ModelDraw> draws)
    {
        Make(id);
        var s = scenes[id];
        var renderer = s.Renderer;
        renderer.Sync(scene);
        if (draws.Length == 0) return;
        EnsureDepth(s, width, height);
        // The scene's poses are uploaded here rather than by the frame's own renderer. Outside the render pass, as a copy
        // pass has to be.
        renderer.UploadAnimated(cmd);

        var colour = new SDL_GPUColorTargetInfo
        {
            texture = target, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var ds = new SDL_GPUDepthStencilTargetInfo
        {
            texture = s.Depth, clear_depth = 1, load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
            store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
            stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_DONT_CARE,
            stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_DONT_CARE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &colour, 1, &ds);
        var r = insert.Viewport;
        var view = new SDL_GPUViewport { x = r.X, y = r.Y, w = r.Width, h = r.Height, min_depth = 0, max_depth = 1 };
        SDL_SetGPUViewport(pass, &view);
        var scissor = new SDL_Rect { x = r.X, y = r.Y, w = r.Width, h = r.Height };
        SDL_SetGPUScissor(pass, &scissor);
        foreach (var d in draws)
        {
            renderer.Only = d.Identity;
            SDL_SetGPUViewport(pass, &view);
            renderer.DrawOpaque(cmd, pass, d.Transform, BatchLayer.Thing);
        }
        renderer.Only = null;
        SDL_EndGPURenderPass(pass);
    }

    void EnsureDepth(Scene s, int width, int height)
    {
        if (s.Depth != null && s.DepthWidth == width && s.DepthHeight == height) return;
        if (s.Depth != null) SDL_ReleaseGPUTexture(gpu.Device, s.Depth);
        s.Depth = gpu.CreateTarget(width, height, gpu.DepthFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET);
        s.DepthWidth = width;
        s.DepthHeight = height;
    }

    public void Dispose()
    {
        foreach (var s in scenes.Values)
        {
            s.Renderer.Dispose();
            if (s.Depth != null) SDL_ReleaseGPUTexture(gpu.Device, s.Depth);
            s.Depth = null;
        }
        scenes.Clear();
    }
}
