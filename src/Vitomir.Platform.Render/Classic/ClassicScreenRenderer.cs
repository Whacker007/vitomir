// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A frame with no world in it: black, and the interface on top. The front end is drawn this way —
// state 2 runs the interface and not the world (flags 0x19) — and so
// are the title screen and the loading plaque between a menu and a level.
// The screen renderer's drawing half (the module never draws); what goes on the black
// is the module's (Vitomir.Classic's ScreenFrame emits it).
using SDL;
using static SDL.SDL3;

namespace Vitomir.Platform.Render.Classic;

/// <summary>Clears to black and draws what goes over it (<see cref="ClassicOver"/>: the menus, the floating texts, the pointer).</summary>
public sealed unsafe class ClassicScreenRenderer(Gpu gpu) : IClassicScreen
{
    /// <summary>The same into a host's picture (<see cref="GpuPicture"/>).</summary>
    public void Draw(in GpuPicture picture, ClassicOver? over) => Draw(picture.Commands, picture.Target, picture.Width, picture.Height, over);

    public void Draw(SDL_GPUCommandBuffer* cmd, SDL_GPUTexture* target, int width, int height, ClassicOver? over)
    {
        var color = new SDL_GPUColorTargetInfo
        {
            texture = target, clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        var pass = SDL_BeginGPURenderPass(cmd, &color, 1, null);
        SDL_EndGPURenderPass(pass);
        // A screen with no world: no frame under what goes over it (the screen's fade and flash draw nothing).
        over?.Draw(cmd, target, width, height, null);
    }

    /// <summary>One frame with no window, as RGBA with the top row first.</summary>
    public byte[] Still(int width, int height, ClassicOver? over)
    {
        var target = gpu.CreateTarget(width, height, gpu.ColorFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Device);
        Draw(cmd, target, width, height, over);
        SDL_SubmitGPUCommandBuffer(cmd);
        var pixels = gpu.Download(target, width, height);
        SDL_ReleaseGPUTexture(gpu.Device, target);
        return pixels;
    }
}
