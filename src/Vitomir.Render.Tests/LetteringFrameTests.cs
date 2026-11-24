// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// **Our own lettering, drawn** (ours): a face's distance-field atlas through the lettering
/// shader. The words land where the face's measures say, filled with the colour asked, with the outline and the
/// shadow round them, and nothing elsewhere; and one atlas draws them sharp small and large.
/// </summary>
[Collection("headless")]
public unsafe class LetteringFrameTests(Headless gpu)
{
    static readonly SDL_FColor Backdrop = new() { r = 0.20f, g = 0.20f, b = 0.25f, a = 1f };

    byte[] Draw(int w, int h, Action<UiCanvas> draw, string? save = null)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var ui = new UiCanvas(new UiAssets(gpu.Files!));
        using var back = new UiRenderer(gpu.Gpu!);
        var target = gpu.Gpu!.CreateTarget(w, h, gpu.Gpu.ColorFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Gpu.Device);
        var clear = new SDL_GPUColorTargetInfo
        {
            texture = target, clear_color = Backdrop,
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        SDL_EndGPURenderPass(SDL_BeginGPURenderPass(cmd, &clear, 1, null));
        ui.Begin(w, h);
        draw(ui);
        back.DrawTo(cmd, target, ui.List);
        SDL_SubmitGPUCommandBuffer(cmd);
        var pixels = gpu.Gpu.Download(target, w, h);
        SDL_ReleaseGPUTexture(gpu.Gpu.Device, target);
        if (save is not null) Png.Write(save, w, h, pixels);
        return pixels;
    }

    /// <summary>A pixel, (x, y) in the picture's pixels with y running **up** from its bottom, as the sheet's does.</summary>
    static (int R, int G, int B) At(byte[] rgba, int w, int h, int x, int y)
    {
        int row = (h - 1 - y) * w * 4 + x * 4;
        return (rgba[row], rgba[row + 1], rgba[row + 2]);
    }

    static readonly (int, int, int) Back = (51, 51, 64);

    static string? Save(string name) =>
        Environment.GetEnvironmentVariable("VTMR_SHOTS") is { Length: > 0 } dir ? Path.Combine(dir, name + ".png") : null;

    static readonly Tint Bone = new(0.93f, 0.89f, 0.80f);

    [Fact]
    public void A_word_is_drawn_where_its_measures_say_in_its_colour_and_nowhere_else()
    {
        var face = Lettering.Load(Lettering.Capitals);
        const float size = 80f, x0 = 100f, y0 = 200f;
        var shot = Draw(640, 480, ui => ui.Letters(face, x0, y0, size, "HOLM", Bone, LetterStyle.Plain), Save("letters-holm"));

        // The H's left stem, a third of the way up: its first painted pixel near the pen's start, and the fill colour
        // three pixels in.
        int midY = (int)(y0 + 0.30f * size);
        int stem = -1;
        for (int x = (int)x0 - 10; x < (int)x0 + 20 && stem < 0; x++)
            if (At(shot, 640, 480, x, midY) != Back) stem = x;
        Assert.True(stem >= 0, "nothing drawn where the H's stem should be");
        var c = At(shot, 640, 480, stem + 3, midY);
        Assert.True(Math.Abs(c.R - 237) < 12 && Math.Abs(c.G - 227) < 12 && Math.Abs(c.B - 204) < 12, $"the H's stem is {c}");

        // The painted columns span what the face measures, give or take the serifs' overhang.
        int first = -1, last = -1;
        for (int x = 0; x < 640; x++)
            for (int y = (int)y0; y < (int)(y0 + size * 0.7f); y++)
                if (At(shot, 640, 480, x, y) != Back) { if (first < 0) first = x; last = x; break; }
        float measured = face.Measure("HOLM", size);
        Assert.InRange(first, (int)x0 - 6, (int)x0 + (int)(0.08f * size));
        Assert.InRange(last - first, measured * 0.80f, measured * 1.05f);

        // Well away from the word: the backdrop untouched.
        Assert.Equal(Back, At(shot, 640, 480, 600, 40));
        Assert.Equal(Back, At(shot, 640, 480, 50, 400));
    }

    [Fact]
    public void An_outline_and_a_shadow_dress_the_word_and_a_plain_word_has_neither()
    {
        var face = Lettering.Load(Lettering.Capitals);
        const float size = 90f, x0 = 120f, y0 = 200f;
        var dressed = new LetterStyle(Outline: new Tint(0.05f, 0.03f, 0.02f), OutlineWidth: 0.03f,
                                      Shadow: new Tint(0f, 0f, 0f, 0.8f), ShadowX: 0.05f, ShadowY: 0.05f, ShadowSoftness: 0.03f);
        var plain = Draw(640, 480, ui => ui.Letters(face, x0, y0, size, "I", Bone, LetterStyle.Plain));
        var shot = Draw(640, 480, ui => ui.Letters(face, x0, y0, size, "I", Bone, dressed), Save("letters-i-dressed"));

        // Just right of the I's stem at its middle: the outline, dark, where the plain one shows the backdrop.
        int y = (int)(y0 + 0.35f * size);
        int right = -1;
        for (int x = 400; x > 0; x--)
            if (At(plain, 640, 480, x, y) != Back) { right = x; break; }
        Assert.True(right > 0, "the plain I was not drawn");
        var edge = At(shot, 640, 480, right + 1, y);
        Assert.True(edge.R < 40 && edge.G < 40 && edge.B < 45, $"no dark outline beside the stem: {edge}");
        // Further out, to the right and below (the sheet's y runs up): the shadow, darker than the backdrop; on the
        // plain word, the backdrop.
        int sx = right + (int)(0.05f * size), sy = y - (int)(0.05f * size);
        var under = At(shot, 640, 480, sx, sy);
        Assert.True(under.R < 45, $"no shadow: {under}");
        Assert.Equal(Back, At(plain, 640, 480, sx, sy));
    }

    [Fact]
    public void One_atlas_draws_a_word_sharp_large_and_small()
    {
        var face = Lettering.Load(Lettering.Capitals);
        // Drawn at 4K's scale (the 640x480 sheet over 2880 lines), the stem's edge is a pixel or two of blend, not a blur.
        var big = Draw(1920, 1440, ui => ui.Letters(face, 100f, 200f, 60f, "I", Bone, LetterStyle.Plain), Save("letters-i-large"));
        int y = (int)((200f + 0.35f * 60f) * 3f);
        int blended = 0;
        for (int x = 0; x < 1920; x++)
        {
            var c = At(big, 1920, 1440, x, y);
            if (c != Back && Math.Abs(c.R - 237) > 12) blended++;
        }
        Assert.InRange(blended, 1, 6);
        // And small, at 1.1's own size of words (an em of 12 units), it is still drawn.
        var small = Draw(640, 480, ui => ui.Letters(face, 100f, 200f, 12f, "VITOMIR", Bone, LetterStyle.Plain), Save("letters-small"));
        int painted = 0;
        for (int x = 95; x < 200; x++)
            for (int yy = 198; yy < 214; yy++)
                if (At(small, 640, 480, x, yy) != Back) painted++;
        Assert.True(painted > 60, $"the small word painted {painted} pixels");
    }

    /// <summary>
    /// **A menu option's tip** (ours, <c>MenuTips</c>), drawn by the menu pass: on the Controls screen, the pointer rested on
    /// *auto repeat attack*'s box, a dark panel with a gold rim by the pointer, its words in it — and none before the rest.
    /// </summary>
    [Fact]
    public void A_rested_option_s_tip_is_drawn_by_the_pointer()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var assets = new UiAssets(gpu.Files!);
        var text = new Strings(gpu.Files!);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find);
        var options = new OptionsMenu(menus, text.Find, new GameOptions());
        menus.Options = options;
        options.Open();
        var pane = menus.Pane(OptionsMenu.ControlsFile);
        menus.PushLayer();
        menus.Show(pane);
        var box = pane.Element("opconRepeatAttack")!;
        var (bx, by) = pane.At(box);
        menus.PointerAt(bx + 6, by + 7);
        byte[] Shot(string name)
        {
            var ui = new UiCanvas(assets);
            using var back = new UiRenderer(gpu.Gpu!);
            var pass = new MenuPass(ui, menus);
            const int w = 640, h = 480;
            var target = gpu.Gpu!.CreateTarget(w, h, gpu.Gpu.ColorFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
            var cmd = SDL_AcquireGPUCommandBuffer(gpu.Gpu.Device);
            back.Of(pass).Draw(cmd, target, w, h, null!, null!);
            SDL_SubmitGPUCommandBuffer(cmd);
            var shot = gpu.Gpu.Download(target, w, h);
            SDL_ReleaseGPUTexture(gpu.Gpu.Device, target);
            if (Save(name) is { } path) Png.Write(path, w, h, shot);
            return shot;
        }
        menus.Frame(1000);
        var before = Shot("tip-before");
        menus.Frame(1600);
        Assert.NotNull(menus.Tip);
        var after = Shot("tip-controls");
        // The tip's panel: below and right of the pointer, or left of it near the right edge; somewhere the pictures differ,
        // and they differ only there (the rest of the screen is the same).
        int changed = 0;
        for (int i = 0; i < before.Length; i += 4) if (before[i] != after[i] || before[i + 1] != after[i + 1] || before[i + 2] != after[i + 2]) changed++;
        Assert.InRange(changed, 2000, 40000);
    }

    /// <summary>
    /// **1.1's Options screen in our lettering** (<c>MenuLettering</c>), drawn by the menu pass: the seven words are
    /// on the screen in 1.1's bone colour, and the row under the pointer — keybinds — in 1.1's red.
    /// </summary>
    [Fact]
    public void The_options_screen_s_words_are_drawn_and_the_one_under_the_pointer_is_red()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var assets = new UiAssets(gpu.Files!);
        var text = new Strings(gpu.Files!);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { Dress = (n, f) => MenuLettering.Dress(assets, n, f) };
        var options = new OptionsMenu(menus, text.Find, new GameOptions());
        menus.Options = options;
        options.Open();
        var pane = menus.Pane(OptionsMenu.OptionsFile);
        pane.Buttons[OptionsMenu.KeybindsButton] = ButtonState.Rollover;
        var ui = new UiCanvas(assets);
        using var back = new UiRenderer(gpu.Gpu!);
        var pass = new MenuPass(ui, menus);
        const int w = 640, h = 480;
        var target = gpu.Gpu!.CreateTarget(w, h, gpu.Gpu.ColorFormat, SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Gpu.Device);
        back.Of(pass).Draw(cmd, target, w, h, null!, null!);
        SDL_SubmitGPUCommandBuffer(cmd);
        var shot = gpu.Gpu.Download(target, w, h);
        SDL_ReleaseGPUTexture(gpu.Gpu.Device, target);
        if (Save("letters-options") is { } path) Png.Write(path, w, h, shot);

        // A row's band: from its baseline to its x-height, across the column.
        int Count(int row, Func<(int R, int G, int B), bool> test)
        {
            int baseline = (int)(MenuLettering.OptionsFirst - MenuLettering.OptionsPitch * row), n = 0;
            for (int y = baseline; y < baseline + 11; y++)
                for (int x = 250; x < 392; x++)
                    if (test(At(shot, w, h, x, y))) n++;
            return n;
        }
        static bool Bone((int R, int G, int B) c) => c.R > 170 && c.G > 150 && c.B > 130;
        static bool Red((int R, int G, int B) c) => c.R > 110 && c.G < 40 && c.B < 40;
        for (int row = 0; row < MenuLettering.OptionsRows.Length; row++)
        {
            if (row == 2) continue;
            Assert.True(Count(row, Bone) > 40, $"{MenuLettering.OptionsRows[row].Word}: {Count(row, Bone)} bone pixels");
        }
        Assert.True(Count(2, Red) > 40, $"keybinds under the pointer: {Count(2, Red)} red pixels");
        Assert.True(Count(2, Bone) < 5, "keybinds is red, not bone");
    }
}
