// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Video;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// A film actually on the screen: one of the game's own `.bik` files, decoded by the machine's
/// FFmpeg, uploaded and drawn by <see cref="VideoPass"/>. Skipped wherever there is no install, no
/// GPU or no FFmpeg — which is most machines, and is exactly the case the engine has to survive.
/// </summary>
[Collection("headless")]
public unsafe class VideoFrameTests(Headless gpu)
{
    const int W = 640, H = 480;

    byte[]? Draw(string film, int ms, string? save = null)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var tool = FFmpegTool.Find();
        Assert.SkipWhen(tool is null, "no ffmpeg on this machine");
        var path = Films.Find(gpu.Files!, film);
        Assert.SkipWhen(path is null, "this install has no " + film);

        using var clip = VideoClip.Open(tool!, path!);
        Assert.NotNull(clip);
        Assert.True(clip!.Advance(ms), "the film ran out before the frame under test");

        var assets = new UiAssets(gpu.Files!);
        var ui = new UiCanvas(assets);
        using var back = new UiRenderer(gpu.Gpu!);
        using var pass = new VideoPass(ui) { Enabled = true };
        pass.Show(clip);

        var target = gpu.Gpu!.CreateTarget(W, H, gpu.Gpu.ColorFormat,
                                           SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Gpu.Device);
        var clear = new SDL_GPUColorTargetInfo
        {
            texture = target, clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 1 },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        SDL_EndGPURenderPass(SDL_BeginGPURenderPass(cmd, &clear, 1, null));
        back.Of(pass).Draw(cmd, target, W, H, null!, null!);
        SDL_SubmitGPUCommandBuffer(cmd);
        var pixels = gpu.Gpu.Download(target, W, H);
        SDL_ReleaseGPUTexture(gpu.Gpu.Device, target);
        if (save is not null) Png.Write(save, W, H, pixels);
        return pixels;
    }

    /// <summary>The pixel at (x, y) counted from the **top** left, which is how the download comes back.</summary>
    static (int R, int G, int B) At(byte[] rgba, int x, int y)
    {
        int i = (y * W + x) * 4;
        return (rgba[i], rgba[i + 1], rgba[i + 2]);
    }

    static bool Black((int R, int G, int B) p) => p.R < 8 && p.G < 8 && p.B < 8;

    /// <summary>
    /// **The film keeps its shape and is centred.** `Sunset.bik` is 640x346, so fitted to a 640x480
    /// screen it fills the width and leaves a black band of 67 rows above and below. Anything that
    /// stretched it to the screen, or put it in a corner, fails here.
    /// </summary>
    [Fact]
    public void A_film_is_fitted_and_centred_with_black_above_and_below()
    {
        var frame = Draw("Sunset.bik", 4000,
                         Environment.GetEnvironmentVariable("VITOMIR_FRAME_PEEK") is { Length: > 0 } dir
                             ? Path.Combine(dir, "video-sunset.png") : null);
        if (frame is null) return;

        // 640 x 346 into 640 x 480: scale 1, so 67 rows of black top and bottom.
        for (int x = 0; x < W; x += 64)
        {
            Assert.True(Black(At(frame, x, 10)), $"the band above the film was painted at {x},10");
            Assert.True(Black(At(frame, x, H - 11)), $"the band below the film was painted at {x},{H - 11}");
        }

        // And the middle is the film, which is a sunset and so is not black.
        int lit = 0;
        for (int x = 0; x < W; x += 16)
            for (int y = 80; y < H - 80; y += 16)
                if (!Black(At(frame, x, y))) lit++;
        Assert.True(lit > 100, $"only {lit} of the sampled points inside the film were painted at all");
    }

    /// <summary>
    /// **And it is the right way up.** Every texture in this engine is stored bottom row first and
    /// FFmpeg hands its rows over top first, so this is the one thing about drawing a film that is
    /// easy to get upside down. `Sunset.bik` is a sky over a dark horizon: the top half of the
    /// picture is brighter than the bottom, and that is what is checked.
    /// </summary>
    [Fact]
    public void A_films_sky_is_at_the_top()
    {
        var frame = Draw("Sunset.bik", 4000);
        if (frame is null) return;

        long top = 0, bottom = 0;
        for (int x = 0; x < W; x += 4)
        {
            for (int y = 80; y < 200; y += 4) { var p = At(frame, x, y); top += p.R + p.G + p.B; }
            for (int y = 280; y < 400; y += 4) { var p = At(frame, x, y); bottom += p.R + p.G + p.B; }
        }
        Assert.True(top > bottom * 1.2,
                    $"the sky is meant to be the bright half: top {top}, bottom {bottom} — the film may be upside down");
    }
}
