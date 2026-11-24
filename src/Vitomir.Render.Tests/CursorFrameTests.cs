// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>The cursor, actually drawn — the eight glyphs and an item on the point.</summary>
[Collection("headless")]
public unsafe class CursorFrameTests(Headless gpu)
{
    const int W = 640, H = 480;
    static readonly SDL_FColor Backdrop = new() { r = 0.35f, g = 0.33f, b = 0.30f, a = 1f };

    byte[] Draw(CursorState state, float x, float y, string? save = null)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var assets = new UiAssets(gpu.Files!);
        var ui = new UiCanvas(assets);
        using var back = new UiRenderer(gpu.Gpu!);
        var pass = new CursorPass(ui, state) { X = x, Y = y };

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

    static bool Painted(byte[] rgba, int x, int y)
    {
        int row = (H - 1 - y) * W * 4 + x * 4;
        return Math.Abs(rgba[row] - 89) > 10 || Math.Abs(rgba[row + 1] - 84) > 10
            || Math.Abs(rgba[row + 2] - 76) > 10;
    }

    /// <summary>Whether anything was drawn inside a rectangle of the `.nui` space.</summary>
    static bool AnyIn(byte[] rgba, int x0, int y0, int x1, int y1)
    {
        for (int x = Math.Max(0, x0); x < Math.Min(W, x1); x++)
            for (int y = Math.Max(0, y0); y < Math.Min(H, y1); y++)
                if (Painted(rgba, x, y)) return true;
        return false;
    }

    /// <summary>The painted bounding box, as (x0, y0, x1, y1) inclusive in `.nui` space.</summary>
    static (int, int, int, int) Box(byte[] rgba)
    {
        int x0 = W, y0 = H, x1 = -1, y1 = -1;
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (Painted(rgba, x, y))
                {
                    if (x < x0) x0 = x;
                    if (y < y0) y0 = y;
                    if (x > x1) x1 = x;
                    if (y > y1) y1 = y;
                }
        return (x0, y0, x1, y1);
    }

    static string? Save(string name) =>
        Environment.GetEnvironmentVariable("VTMR_SHOTS") is { Length: > 0 } dir
            ? Path.Combine(dir, name + ".png") : null;

    /// <summary>
    /// All eight shipped textures resolve and draw. A missing one would leave the quad blank, so
    /// this is the test that the names in `Cursors` are the names the install has.
    /// </summary>
    [Fact]
    public void Every_registered_cursor_draws_something()
    {
        for (int i = 0; i < Cursors.Count; i++)
        {
            var state = new CursorState();
            state.Set((CursorId)i);
            var shot = Draw(state, 300, 300);
            Assert.True(AnyIn(shot, 260, 260, 340, 340),
                        $"{Cursors.Name((CursorId)i)} drew nothing at the pointer");
        }
    }

    /// <summary>
    /// **Where the point is.** A top-left-anchored glyph hangs below and to the right of the
    /// pointer in `.nui` terms; a centred one straddles it. Sampling either side of the pointer is
    /// what tells the two apart, and it is the thing a wrong anchor would get backwards.
    /// </summary>
    [Fact]
    public void The_pointer_sits_at_the_corner_or_the_middle_of_the_glyph()
    {
        var pointer = new CursorState();
        pointer.Set(CursorId.Default);                        // top-left anchored
        var one = Draw(pointer, 300, 300, Save("cursor-default"));
        // At 640 x 480 the UI scale is 1:1, so the glyph's painted box is exactly the 32 x 32 it
        // occupies — the pointer at its top-left corner, which in `.nui` terms is its highest y.
        Assert.Equal((300, 268, 331, 299), Box(one));
        Assert.False(AnyIn(one, 260, 302, 298, 340), "something above and left of the point");

        var marker = new CursorState();
        marker.Set(CursorId.Negative);                        // centred
        var two = Draw(marker, 300, 300, Save("cursor-negative"));
        Assert.True(AnyIn(two, 286, 286, 300, 300), "the centred glyph missed its own point");
        Assert.True(AnyIn(two, 300, 300, 314, 314), "the centred glyph is only on one side");
    }

    /// <summary>An item in hand is its own icon, centred, at the size its cells measure.</summary>
    [Fact]
    public void An_item_in_hand_is_drawn_as_the_cursor()
    {
        var state = new CursorState();
        var sword = new Item { Name = "Fine Rapier", Icon = "swordBroadIcon", IconSize = 5 };
        state.Update(0, sword, CursorId.Default);
        Assert.Same(sword, state.Showing);

        var shot = Draw(state, 320, 240, Save("cursor-carrying"));
        var (w, h) = state.Extent;
        Assert.Equal((2 * InventoryPane.CellWidth, 3 * InventoryPane.CellHeight), (w, h));
        Assert.True(AnyIn(shot, (int)(320 - w / 2), (int)(240 - h / 2), (int)(320 + w / 2), (int)(240 + h / 2)),
                    "the carried icon drew nothing around the pointer");
        // And nothing far away from it: the icon is the cursor, not a pane.
        Assert.False(AnyIn(shot, 8, 8, 60, 60), "the carried icon painted the corner");
    }
}
