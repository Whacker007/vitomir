// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// The character sheet, actually drawn. `CharacterPaneTests` in the engine suite proves what
/// *should* be on screen; this proves it reaches it, which is a different failure — the status
/// bar's first run was laid out correctly and drawn entirely white.
///
/// **Joins the `headless` collection** rather than taking its own fixture: there is one SDL device
/// for the run and a second `IClassFixture&lt;Headless&gt;` quits the video subsystem out from under
/// the first. That cost a round trip when the conversation render tests were written.
/// </summary>
[Collection("headless")]
public unsafe class CharacterFrameTests(Headless gpu)
{
    const int W = 640, H = 480;

    /// <summary>A mid-grey behind everything, so the pane's own artwork and its text both show.</summary>
    static readonly SDL_FColor Backdrop = new() { r = 0.35f, g = 0.33f, b = 0.30f, a = 1f };

    /// <summary>
    /// What `tbtextcolor=255,222,0` actually draws as. The parser stores the three numbers in
    /// written order, lowest byte first, with `0xff` above them, so the first is the low
    /// byte of a little-endian ARGB word — **blue**. It is a bright cyan, not the yellow the
    /// numbers read as, and a base-game screenshot shows exactly that.
    /// </summary>
    static readonly (int R, int G, int B) Effective = (0, 222, 255);

    byte[] Draw(Action<CharacterPane> fill, bool enabled = true, string? save = null)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var assets = new UiAssets(gpu.Files!);
        var ui = new UiCanvas(assets);
        using var back = new UiRenderer(gpu.Gpu!);
        var pane = CharacterPass.Load(assets);
        fill(pane);
        var pass = new CharacterPass(ui, pane) { Enabled = enabled };

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

    /// <summary>The pixel at a point in `.nui` space, which counts up from the bottom.</summary>
    static (int R, int G, int B) At(byte[] rgba, int x, int y)
    {
        int row = (H - 1 - y) * W * 4 + x * 4;
        return (rgba[row], rgba[row + 1], rgba[row + 2]);
    }

    /// <summary>
    /// Whether a colour appears anywhere in a rectangle of `.nui` space, within a tolerance —
    /// these run on Direct3D 12 on the laptop and a software Vulkan in the cloud, and "right to
    /// within a few levels" is the claim being made.
    /// </summary>
    static bool Ink(byte[] rgba, int x0, int y0, int x1, int y1, (int R, int G, int B) want, int slack = 8)
    {
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var c = At(rgba, x, y);
                if (Math.Abs(c.R - want.R) <= slack && Math.Abs(c.G - want.G) <= slack
                    && Math.Abs(c.B - want.B) <= slack) return true;
            }
        return false;
    }

    static (int R, int G, int B) Empty(byte[] rgba) => At(rgba, W - 3, H - 3);

    static Actor Christof(ActorType type = ActorType.Vampire)
    {
        var a = new Actor { Type = type, Name = "Christof", IsPlayerThing = true };
        for (int i = 0; i < Actor.Stats; i++) { a.BaseStat[i] = 20; a.Stat[i] = 30; }
        a.Stat[(int)ActorStat.Blood] = 25;
        a.Stat[(int)ActorStat.BloodPool] = 50;
        a.Stat[(int)ActorStat.Mana] = 25;
        a.Stat[(int)ActorStat.ManaPool] = 50;
        a.Stat[(int)ActorStat.Humanity] = 70;
        a.Stat[(int)ActorStat.Frenzy] = 10;
        a.Stat[(int)ActorStat.Faith] = 10;
        a.Health = 80;
        a.MaxHealth = 100;
        a.Experience = 12_000;
        a.SpendableExperience = 450;
        return a;
    }

    static string? Save(string name) =>
        Environment.GetEnvironmentVariable("VTMR_SHOTS") is { Length: > 0 } dir
            ? Path.Combine(dir, name + ".png") : null;

    /// <summary>A pass that is off draws nothing at all, which is what a closed pane is.</summary>
    [Fact]
    public void A_closed_pane_draws_nothing()
    {
        var blank = Draw(p => p.Subject = Christof(), enabled: false);
        var empty = Empty(blank);
        for (int y = 0; y < H; y += 7)
            for (int x = 0; x < W; x += 7)
                Assert.Equal(empty, At(blank, x, y));
    }

    /// <summary>
    /// **The pane reaches the screen, and it occupies the bottom left.** `pos=0,0` in a space that
    /// counts up from the bottom puts 320 x 352 of artwork there, and the top right of the frame is
    /// untouched — which is the half of "it drew" that a `.nui` reading alone cannot claim.
    /// </summary>
    [Fact]
    public void The_pane_fills_the_bottom_left_and_leaves_the_rest_alone()
    {
        var shot = Draw(p => p.Subject = Christof(), save: Save("character-vampire"));
        var empty = Empty(shot);

        // Something was drawn inside the pane's rectangle.
        bool painted = false;
        for (int y = 130; y < 470 && !painted; y += 5)
            for (int x = 5; x < 310; x += 5)
                if (At(shot, x, y) != empty) { painted = true; break; }
        Assert.True(painted, "the character pane drew nothing");

        // And nothing outside it: the pane is 320 wide and its art starts at y=120.
        for (int y = 0; y < H; y += 9)
            for (int x = 330; x < W; x += 9)
                Assert.Equal(empty, At(shot, x, y));
    }

    /// <summary>
    /// **The effective column takes the pane's own colour and the base column does not.**
    /// `tbtextcolor` is declared on the nine effective boxes and on nothing else in the left
    /// column, so it has to appear at x=134 and not at x=108.
    /// </summary>
    [Fact]
    public void The_effective_stat_column_is_the_panes_own_colour()
    {
        var shot = Draw(p => p.Subject = Christof(), save: Save("character-stats"));

        // Strength sits at y=307 with its base at x=108 and its effective value at x=134.
        Assert.True(Ink(shot, 134, 305, 160, 322, Effective), "tbtextcolor missing from the effective column");
        Assert.False(Ink(shot, 106, 305, 132, 322, Effective), "the base column should not take tbtextcolor");
    }

    /// <summary>
    /// **Only one of each overlapping pair of bars is on screen**, and which one depends on the
    /// actor's type and nothing else. The two fills are different bitmaps, so a vampire's frame and
    /// a human's frame differ in the same 81 x 8 rectangle — if both were drawn, or the wrong one,
    /// the two frames would match there.
    /// </summary>
    [Fact]
    public void A_vampire_and_a_human_draw_different_bars_in_the_same_place()
    {
        var vampire = Draw(p => p.Subject = Christof(), save: Save("character-vampire-bars"));
        var human = Draw(p => p.Subject = Christof(ActorType.Human), save: Save("character-human-bars"));

        // The pool bar is declared at pos=144,374 size=81,8 — the same rectangle for both.
        bool differs = false;
        for (int y = 374; y < 382 && !differs; y++)
            for (int x = 144; x < 225; x++)
                if (At(vampire, x, y) != At(human, x, y)) { differs = true; break; }
        Assert.True(differs, "blood and mana drew the same thing in the same place");

        // Health is unconditional, so its rectangle is identical in both.
        for (int y = 390; y < 398; y++)
            for (int x = 144; x < 225; x += 3)
                Assert.Equal(At(vampire, x, y), At(human, x, y));
    }

    /// <summary>
    /// A bar's fill is **proportional**: the health bar at 80 of 100 paints its left four fifths
    /// and leaves the rest of its rectangle as the pane's artwork. Drawn against a second frame at
    /// full health so the difference is the fill and not the backdrop.
    /// </summary>
    [Fact]
    public void A_bar_is_filled_to_its_own_fraction()
    {
        var hurt = Draw(p => p.Subject = Christof());
        var whole = Draw(p => { var a = Christof(); a.Health = a.MaxHealth; p.Subject = a; });

        // The health bar is pos=144,390 size=81,8. At 80% the fill ends around x=209.
        bool sameEarly = true;
        for (int y = 391; y < 397; y++)
            for (int x = 146; x < 200; x += 4)
                if (At(hurt, x, y) != At(whole, x, y)) { sameEarly = false; break; }
        Assert.True(sameEarly, "the filled part of the bar should be identical below 80%");

        bool differsLate = false;
        for (int y = 391; y < 397 && !differsLate; y++)
            for (int x = 212; x < 224; x++)
                if (At(hurt, x, y) != At(whole, x, y)) { differsLate = true; break; }
        Assert.True(differsLate, "the last fifth of the health bar should differ at 80% health");
    }

    /// <summary>
    /// **The columns line up on their middles, which is what `textboxflags=0x01` asks for.** The
    /// base game's sheet: every value centred in its box. (This test asserted flush-right
    /// edges, when the flags had been read with the box's width and height swapped.)
    /// Drawn twice — once with a one-digit value in a box and once with three — the two texts share a
    /// middle and neither shares an edge.
    /// </summary>
    [Fact]
    public void Numbers_of_different_widths_share_a_middle()
    {
        // charHumanity is a 25-wide box near the top of the status block; anything in the pane's
        // white would do, and this one is plain white text with nothing else on its row.
        var one = Draw(p => { p.Subject = Christof(); p.Subject!.Stat[(int)ActorStat.Humanity] = 7; });
        var three = Draw(p => { p.Subject = Christof(); p.Subject!.Stat[(int)ActorStat.Humanity] = 100; });

        var box = CharacterPass.Load(new UiAssets(gpu.Files!)).Pane.Elements
                               .First(e => e.Name.Equals("charHumanity", StringComparison.OrdinalIgnoreCase));
        int bx = box.Pos[0], by = box.Pos[1], bw = box.Size[0], bh = box.Size[1];

        int rightOne = RightmostInk(one, bx, by, bx + bw - 1, by + bh - 1);
        int rightThree = RightmostInk(three, bx, by, bx + bw - 1, by + bh - 1);
        int leftOne = LeftmostInk(one, bx, by, bx + bw - 1, by + bh - 1);
        int leftThree = LeftmostInk(three, bx, by, bx + bw - 1, by + bh - 1);

        Assert.True(rightOne > 0 && rightThree > 0, "nothing was drawn in the humanity box");
        float middleOne = (leftOne + rightOne) / 2f, middleThree = (leftThree + rightThree) / 2f;
        Assert.True(Math.Abs(middleOne - middleThree) <= 1.5f,
                    $"middles differ: '7' at {middleOne}, '100' at {middleThree}");
        Assert.True(leftOne > leftThree + 2 && rightOne < rightThree - 2,
                    $"'7' should sit inside '100' at both ends: {leftOne}..{rightOne} vs {leftThree}..{rightThree}");
        Assert.True(Math.Abs(middleOne - (bx + bw / 2f)) <= 2f, $"'7' is not in the middle of its box: {middleOne}");
    }


    static int RightmostInk(byte[] rgba, int x0, int y0, int x1, int y1)
    {
        for (int x = x1; x >= x0; x--)
            for (int y = y0; y <= y1; y++)
                if (Bright(At(rgba, x, y))) return x;
        return -1;
    }

    static int LeftmostInk(byte[] rgba, int x0, int y0, int x1, int y1)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (Bright(At(rgba, x, y))) return x;
        return int.MaxValue;
    }

    /// <summary>A lit glyph pixel against the pane's dark artwork, on either GPU.</summary>
    static bool Bright((int R, int G, int B) c) => c.R > 170 && c.G > 170 && c.B > 170;

    /// <summary>
    /// **The soak pie as drawn, under both rules.** The fire wedge (`charSoakFire`, pos 209,200,
    /// 30 x 32) with every other soak at 0. 1.1's sevenths draw leather armour's 10 exactly as they
    /// draw nothing, and 86 exactly as 100; the <c>soakpie</c> switch draws 10 as the first step and
    /// keeps full for 100 — the two things asked of it, seen on the screen rather than in the
    /// frame number.
    /// </summary>
    [Fact]
    public void The_soakpie_switch_shows_a_small_soak_and_keeps_full_for_a_hundred()
    {
        byte[] Fire(float soak, bool even) => Draw(p =>
        {
            var a = Christof();
            for (int i = 0; i < a.Soak.Length; i++) a.Soak[i] = 0f;
            a.Soak[(int)DamageType.Fire] = soak;
            p.Subject = a;
            p.Rules = new Qol { SoakPie = even };
        }, save: Save($"soakpie-{(even ? "on" : "off")}-{soak}"));

        bool Same(byte[] one, byte[] two)
        {
            for (int y = 200; y < 232; y++)
                for (int x = 209; x < 239; x++)
                    if (At(one, x, y) != At(two, x, y)) return false;
            return true;
        }

        Assert.True(Same(Fire(0, even: false), Fire(10, even: false)), "1.1 draws a soak of 10 as empty");
        Assert.True(Same(Fire(86, even: false), Fire(100, even: false)), "1.1 draws 86 as full");

        var none = Fire(0, even: true);
        Assert.False(Same(none, Fire(10, even: true)), "the switch should show a soak of 10");
        Assert.False(Same(Fire(99, even: true), Fire(100, even: true)), "the switch keeps full for 100");
        Assert.True(Same(Fire(100, even: true), Fire(100, even: false)), "full is the same picture either way");
        Assert.True(Same(none, Fire(0, even: false)), "and so is empty");
    }
}
