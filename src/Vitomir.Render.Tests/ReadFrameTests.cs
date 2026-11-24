// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.Ui;
using Vitomir.Formats;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>The read dialog, actually drawn — with the letter Christof carries in it.</summary>
[Collection("headless")]
public unsafe class ReadFrameTests(Headless gpu)
{
    const int W = 640, H = 480;
    static readonly SDL_FColor Backdrop = new() { r = 0.35f, g = 0.33f, b = 0.30f, a = 1f };

    byte[] Draw(NtxEntry? document, string? save = null)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var assets = new UiAssets(gpu.Files!);
        var ui = new UiCanvas(assets);
        using var back = new UiRenderer(gpu.Gpu!);
        var pane = ReadPass.Load(assets);
        pane.Letter(assets);                             // laid out in the body's own font (the pane's, not the pass's)
        pane.Open(document);
        var pass = new ReadPass(ui, pane);

        var target = gpu.Gpu!.CreateTarget(W, H, gpu.Gpu.ColorFormat,
                                           SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Gpu.Device);
        var clear = new SDL_GPUColorTargetInfo
        {
            texture = target, clear_color = Backdrop,
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

    static (int R, int G, int B) At(byte[] rgba, int x, int y)
    {
        int row = (H - 1 - y) * W * 4 + x * 4;
        return (rgba[row], rgba[row + 1], rgba[row + 2]);
    }

    static bool Painted(byte[] rgba, int x, int y)
    {
        var c = At(rgba, x, y);
        return Math.Abs(c.R - 89) > 10 || Math.Abs(c.G - 84) > 10 || Math.Abs(c.B - 76) > 10;
    }

    static string? Save(string name) =>
        Environment.GetEnvironmentVariable("VTMR_SHOTS") is { Length: > 0 } dir
            ? Path.Combine(dir, name + ".png") : null;

    [Fact]
    public void With_nothing_open_the_dialog_draws_nothing()
    {
        var shot = Draw(null);
        for (int x = 150; x < 486; x += 16)
            for (int y = 160; y < 470; y += 16)
                Assert.False(Painted(shot, x, y), $"({x},{y}) was painted with no document open");
    }

    /// <summary>
    /// **The letter, end to end**: `prague.ntx` → the pane → wrapped lines → pixels. And it is a
    /// dialog, so it sits in the middle rather than in a half: the far corners stay clear.
    /// </summary>
    [Fact]
    public void The_letter_christof_carries_reaches_the_screen()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var documents = new Documents(gpu.Files!);
        Assert.SkipUnless(documents.Add("prague"), "no strings/prague.ntx in this install");
        var letter = documents.Find("CuthbertLetter");
        Assert.NotNull(letter);

        var shot = Draw(letter, Save("read-cuthbert"));

        bool inside = false;
        for (int x = 170; x < 460 && !inside; x += 4)
            for (int y = 200; y < 440 && !inside; y += 4)
                inside = Painted(shot, x, y);
        Assert.True(inside, "the dialog drew nothing in its own box");

        // It is a dialog in the middle of the screen: the corners outside it are untouched.
        Assert.False(Painted(shot, 8, 8), "the bottom-left corner was painted");
        Assert.False(Painted(shot, W - 8, 8), "the bottom-right corner was painted");
        Assert.False(Painted(shot, 8, H - 8), "the top-left corner was painted");
    }
}
