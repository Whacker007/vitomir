// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.World;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// The camera's fade and flash, actually drawn: the fade multiplies what is on screen by
/// <c>level / 255</c> (the <c>subblend</c> of `fade.nam`), and the flash adds its colour scaled by
/// <c>intensity / 255</c> (the <c>addblend</c> of `whiteFlash.nam`). The pass reads the frame's snapshot
/// (<see cref="ClassicScreen"/>), filled from the engine's effects as the producer fills it.
/// </summary>
[Collection("headless")]
public unsafe class ScreenEffectsFrameTests(Headless gpu)
{
    const int W = 64, H = 48;
    static readonly SDL_FColor Backdrop = new() { r = 0.8f, g = 0.6f, b = 0.4f, a = 1f };

    byte[] Draw(ScreenEffects fx)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        using var pass = new ScreenEffectsPass(gpu.Gpu!);
        var target = gpu.Gpu!.CreateTarget(W, H, gpu.Gpu.ColorFormat,
                                           SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Gpu.Device);
        var clear = new SDL_GPUColorTargetInfo
        {
            texture = target, clear_color = Backdrop,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        SDL_EndGPURenderPass(SDL_BeginGPURenderPass(cmd, &clear, 1, null));
        pass.Draw(cmd, target, W, H, new ClassicScreen(fx.FadeLevel, fx.FlashIntensity, fx.FlashColor));   // WorldSnapshot.Fill's
        SDL_SubmitGPUCommandBuffer(cmd);
        var pixels = gpu.Gpu.Download(target, W, H);
        SDL_ReleaseGPUTexture(gpu.Gpu.Device, target);
        return pixels;
    }

    static (int R, int G, int B) Middle(byte[] rgba)
    {
        int i = (H / 2 * W + W / 2) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2]);
    }

    [Fact]
    public void With_nothing_running_the_screen_is_untouched()
    {
        var (r, g, b) = Middle(Draw(new ScreenEffects()));
        Assert.InRange(r, 202, 206);
        Assert.InRange(g, 151, 155);
        Assert.InRange(b, 100, 104);
    }

    [Fact]
    public void A_fade_halfway_to_black_halves_the_picture_and_a_finished_one_is_black()
    {
        var fx = new ScreenEffects();
        fx.AddFade(128f, 128f, 1000, false, 0);
        fx.Step(10, 10);
        var (r, g, b) = Middle(Draw(fx));
        Assert.InRange(r, 100, 106);        // 204 × 128/255
        Assert.InRange(g, 74, 80);
        Assert.InRange(b, 49, 54);

        fx.ClearAll();
        fx.AddFade(0f, 0f, 1000, false, 0);
        fx.Step(10, 10);
        Assert.Equal((0, 0, 0), Middle(Draw(fx)));
    }

    [Fact]
    public void A_flash_adds_its_colour_scaled_by_its_intensity()
    {
        var fx = new ScreenEffects();
        fx.AddFlash(255f, 255f, 1000, 0x0000ff, false, 0);       // blue at full strength
        fx.Step(10, 10);
        var (r, g, b) = Middle(Draw(fx));
        Assert.InRange(r, 202, 206);
        Assert.InRange(g, 151, 155);
        Assert.Equal(255, b);
    }
}
