// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.Anim;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// The party heads, drawn. The rule this file exists for is an early one, paid twice since: **render
/// and look before delivering anything visual.** A test that measures "did the picture change" is
/// not a test that anybody looked at the picture, so these measure the head's own rectangle and
/// assert that nothing lands outside it.
/// </summary>
[Collection("headless")]
public unsafe class HeadsFrameTests(Headless gpu)
{
    /// <summary>
    /// The window the heads are drawn into. 1280x720 by default; <c>VTMR_HEAD_SIZE=640x480</c>
    /// renders at the interface's own 4:3 size instead, which is the one to compare against the
    /// original running. Paired with <c>VTMR_HEAD_PNG</c>, this is how the standing rule —
    /// render and look before delivering anything visual — gets paid for this pass.
    /// </summary>
    static readonly (int W, int H) Window = Size();

    static (int, int) Size()
    {
        var text = Environment.GetEnvironmentVariable("VTMR_HEAD_SIZE");
        if (text?.Split('x') is [var w, var h] && int.TryParse(w, out int width) && int.TryParse(h, out int height))
            return (width, height);
        return (1280, 720);
    }

    static int W => Window.W;
    static int H => Window.H;

    static (int X, int Y, int W, int H) Box()
    {
        float sx = W / 640f, sy = H / 480f;
        return ((int)(PartyHeads.ViewX * sx), (int)(PartyHeads.ViewY * sy),
                (int)(PartyHeads.ViewW * sx), (int)(PartyHeads.ViewH * sy));
    }

    static WorldThing Member(string model, string motion, int guid = 1) => new()
    {
        Index = 0, Guid = guid, Scene = new NsdThing(), Template = "Christof", Type = ThingType.Player,
        Actor = new Actor { HeadModel = model, HeadMotionSet = motion },
    };

    byte[] Draw(bool heads, string? save = null) => Draw(heads, [0f], save)[0];

    /// <summary>
    /// One pass, drawn once per entry in <paramref name="clock"/>, each at that many seconds. One
    /// pass across all of them on purpose: the heads keep their animation between frames, so this
    /// is the only way to see them move.
    /// </summary>
    byte[][] Draw(bool heads, float[] clock, string? save = null)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var materials = new MaterialCache(gpu.Files!);
        var motions = new MotionSets(gpu.Files!);
        using var back = new UiRenderer(gpu.Gpu!);
        using var inserts = new SceneInserts(gpu.Gpu!);
        back.Models = inserts;
        var pass = new HeadsPass(new UiCanvas(new UiAssets(gpu.Files!)), gpu.Files!, materials, motions, new LightStyles())
        {
            Enabled = heads,
            Party = [Member("christofH.nod", "ChristofDaHead.nms")],
        };
        var frames = new byte[clock.Length][];
        for (int i = 0; i < clock.Length; i++)
        {
            pass.Seconds = clock[i];
            // Several frames go to several files, so a motion can be looked at and not just measured.
            frames[i] = Once(back, pass, save is null ? null
                                   : clock.Length == 1 ? save
                                   : Path.ChangeExtension(save, null) + $".{i}" + Path.GetExtension(save));
        }
        return frames;
    }

    byte[] Once(UiRenderer back, HeadsPass pass, string? save)
    {
        var target = gpu.Gpu!.CreateTarget(W, H, gpu.Gpu.ColorFormat,
                                           SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET);
        var cmd = SDL_AcquireGPUCommandBuffer(gpu.Gpu.Device);
        var clear = new SDL_GPUColorTargetInfo
        {
            texture = target, clear_color = new SDL_FColor { r = 0.05f, g = 0.05f, b = 0.08f, a = 1 },
            load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR, store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
        };
        SDL_EndGPURenderPass(SDL_BeginGPURenderPass(cmd, &clear, 1, null));
        pass.Models.Frame(pass.Enabled, null);          // the heads' own pass before the draw
        back.Of(pass).Draw(cmd, target, W, H, null!, null!);
        SDL_SubmitGPUCommandBuffer(cmd);
        var pixels = gpu.Gpu.Download(target, W, H);
        SDL_ReleaseGPUTexture(gpu.Gpu.Device, target);
        if (save is not null) Png.Write(save, W, H, pixels);
        return pixels;
    }

    static int Lit(byte[] pixels, (int X, int Y, int W, int H) box, int floor)
    {
        int count = 0;
        for (int y = box.Y; y < box.Y + box.H; y++)
            for (int x = box.X; x < box.X + box.W; x++)
            {
                int i = (y * W + x) * 4;
                if (i + 2 < pixels.Length && (pixels[i] > floor || pixels[i + 1] > floor || pixels[i + 2] > floor))
                    count++;
            }
        return count;
    }

    /// <summary>
    /// A journey rebuilds every companion from their template, keeping their GUID, so the party that
    /// arrives is three new things. They keep the heads they had: before, each journey made
    /// three more, and every one of them was posed every frame for the rest of the run.
    /// </summary>
    [Fact]
    public void A_party_carried_across_journeys_keeps_its_heads()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        using var back = new UiRenderer(gpu.Gpu!);
        using var inserts = new SceneInserts(gpu.Gpu!);
        back.Models = inserts;
        var pass = new HeadsPass(new UiCanvas(new UiAssets(gpu.Files!)), gpu.Files!, new MaterialCache(gpu.Files!), new MotionSets(gpu.Files!), new LightStyles())
        {
            Enabled = true,
        };
        for (int journey = 0; journey < 4; journey++)
        {
            pass.Party = [.. Enumerable.Range(1, 3).Select(g => Member("christofH.nod", "ChristofDaHead.nms", g))];
            Once(back, pass, null);
            Assert.Equal(3, pass.HeadsMade);
            Assert.NotNull(pass.Playing(pass.Party[2]));
        }
    }

    /// <summary>
    /// Every fragment shader is made with two sampler slots (<see cref="Gpu.FragmentSamplers"/>):
    /// one made with one is refused, because a one-slot draw in the frame is what let SDL's Direct3D 12
    /// backend write a two-slot draw's descriptor past the end of its heap.
    /// </summary>
    [Fact]
    public void A_fragment_shader_with_one_sampler_slot_is_refused()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        Assert.Throws<ArgumentException>(() =>
            gpu.Gpu!.LoadShader("Ui", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: 1, uniformBuffers: 0));
    }

    /// <summary>A head actually appears, inside its own rectangle and nowhere else.</summary>
    [Fact]
    public void A_party_head_draws_inside_the_viewport_and_only_there()
    {
        var box = Box();
        var bare = Draw(heads: false);
        var drawn = Draw(heads: true, save: Environment.GetEnvironmentVariable("VTMR_HEAD_PNG"));

        int before = Lit(bare, box, 60), after = Lit(drawn, box, 60);
        Assert.True(after > before + 200,
                    $"only {after - before} more lit pixels in the head box ({before} -> {after}); "
                    + "the head is not drawing");

        int outside = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (x >= box.X && x < box.X + box.W && y >= box.Y && y < box.Y + box.H) continue;
                int i = (y * W + x) * 4;
                if (bare[i] != drawn[i] || bare[i + 1] != drawn[i + 1] || bare[i + 2] != drawn[i + 2]) outside++;
            }
        Assert.True(outside == 0, $"{outside} pixels changed outside the head viewport");
    }

    /// <summary>Switched off, it draws nothing at all.</summary>
    [Fact]
    public void Switched_off_it_draws_nothing()
    {
        var a = Draw(heads: false);
        var b = Draw(heads: false);
        Assert.Equal(a, b);
    }

    /// <summary>
    /// The head is alive: `head_idle` loops at 30 frames a second, so a second apart is a different
    /// picture. The heads' advance is a pass of its own, separate from the draw, for exactly this
    /// reason: the heads move on whether or not anything else is happening.
    /// </summary>
    [Fact]
    public void A_head_keeps_moving()
    {
        var box = Box();
        var frames = Draw(heads: true, [0f, 0.5f, 1f],
                          Environment.GetEnvironmentVariable("VTMR_HEAD_STRIP"));
        int moved = 0;
        for (int y = box.Y; y < box.Y + box.H; y++)
            for (int x = box.X; x < box.X + box.W; x++)
            {
                int i = (y * W + x) * 4;
                if (frames[0][i] != frames[2][i] || frames[0][i + 1] != frames[2][i + 1]) moved++;
            }
        Assert.True(moved > 100, $"only {moved} pixels changed over a second; the head is frozen");

        // And it is the clock that moves it, not the act of drawing: the same second twice running
        // is the same picture.
        var still = Draw(heads: true, [2f, 2f]);
        Assert.Equal(still[0], still[1]);
    }
}
