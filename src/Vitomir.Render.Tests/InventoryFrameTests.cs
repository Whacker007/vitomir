// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// The inventory pane, actually drawn. `InventoryPaneTests` in the engine suite proves what
/// *should* be on screen; this proves it reaches it, and in the right half of the screen.
/// </summary>
[Collection("headless")]
public unsafe class InventoryFrameTests(Headless gpu)
{
    const int W = 640, H = 480;

    static readonly SDL_FColor Backdrop = new() { r = 0.35f, g = 0.33f, b = 0.30f, a = 1f };

    byte[] Draw(Action<InventoryPane> fill, bool enabled = true, string? save = null)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var assets = new UiAssets(gpu.Files!);
        var ui = new UiCanvas(assets);
        using var back = new UiRenderer(gpu.Gpu!);
        var pane = InventoryPass.Load(assets);
        fill(pane);
        var pass = new InventoryPass(ui, pane) { Enabled = enabled };

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

    static bool Painted(byte[] rgba, int x, int y)
    {
        var c = At(rgba, x, y);
        return Math.Abs(c.R - 89) > 10 || Math.Abs(c.G - 84) > 10 || Math.Abs(c.B - 76) > 10;
    }

    static string? Save(string name) =>
        Environment.GetEnvironmentVariable("VTMR_SHOTS") is { Length: > 0 } dir
            ? Path.Combine(dir, name + ".png") : null;

    [Fact]
    public void A_pass_that_is_off_draws_nothing_at_all()
    {
        var off = Draw(_ => { }, enabled: false);
        for (int x = 320; x < W; x += 16)
            for (int y = 96; y < 480; y += 16)
                Assert.False(Painted(off, x, y), $"({x},{y}) was painted with the pass off");
    }

    /// <summary>
    /// **It is a right-hand pane.** `visGroup=2`, and its quads run from x=320 to x=640 — so the
    /// left half of the screen is untouched and the character sheet can be up at the same time,
    /// which is what the base game does.
    /// </summary>
    [Fact]
    public void The_pane_paints_the_right_half_and_leaves_the_left_alone()
    {
        var shot = Draw(p => p.Gold = 214, save: Save("inventory"));

        bool right = false;
        for (int x = 400; x < 620 && !right; x += 8)
            for (int y = 160; y < 440 && !right; y += 8)
                right = Painted(shot, x, y);
        Assert.True(right, "the pane drew nothing in its own half");

        for (int x = 8; x < 300; x += 16)
            for (int y = 8; y < 470; y += 16)
                Assert.False(Painted(shot, x, y), $"({x},{y}) is in the left half and was painted");
    }

    /// <summary>
    /// An item lands in its own grid and nowhere else. The body slot is `invBody` at 454,308,
    /// 54 x 96 — two cells across and three down — so a 2 x 3 icon fills it exactly.
    /// </summary>
    [Fact]
    public void An_equipped_item_is_drawn_inside_the_grid_that_holds_it()
    {
        var empty = Draw(_ => { });
        var worn = Draw(p =>
        {
            var a = new Actor();
            a.Inventory.Add(new Item { Name = "rags", Icon = "ragsIcon", IconSize = 5, Location = Slot.Body });
            p.Subject = a;
        }, save: Save("inventory-worn"));

        // Something changed inside invBody's rectangle and nothing changed inside invHead's.
        Assert.True(Differs(empty, worn, 454, 308, 54, 96), "nothing was drawn in the body slot");
        Assert.False(Differs(empty, worn, 454, 410, 54, 64), "something was drawn in the head slot");
    }

    static bool Differs(byte[] a, byte[] b, int x0, int y0, int w, int h)
    {
        for (int y = y0; y < y0 + h; y++)
            for (int x = x0; x < x0 + w; x++)
                if (At(a, x, y) != At(b, x, y)) return true;
        return false;
    }

    /// <summary>
    /// **Cells count down and `.nui` space counts up**, so a row index is flipped against the
    /// grid's own height. An item in row 0 of the 10 x 4 pack draws at the *top* of the grid, which
    /// in this space is the highest y — and the arithmetic is worth pinning without a GPU too.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 346, 241)]     // row 0 of 4, a 1x1: three rows below it, 145 + 3*32
    [InlineData(0, 3, 346, 145)]     // row 3, the bottom row: no rows below
    [InlineData(4, 1, 454, 209)]     // column 4, row 1: two rows below
    public void A_cell_row_counts_down_from_the_top_and_the_pane_counts_up(int cx, int cy, int x, int y)
    {
        var grid = new InventoryGrid("invInventory", Slot.Inventory, 10, 4);
        var element = Vitomir.Formats.NuiPane.Parse(
            "element icongrid name=invInventory pos=346,145 size=270,128 gridsize=10,4", "t.nui").Elements[0];
        var item = new Item { Name = "vial", Location = Slot.Inventory, Cell = (cx, cy) };
        Assert.Equal((x, (float)y), InventoryPass.CellCorner(element, new PlacedItem(item, grid, cx, cy)));
    }

    /// <summary>
    /// **Christof's own starting kit, end to end**: `misc/Christof.NPC` names two items, the
    /// templates supply them, `StartingKit` places them, and the pane draws them. Five pieces
    /// across three batches, and one assertion that fails if any of them is out.
    /// </summary>
    [Fact]
    public void The_kit_a_character_file_names_reaches_the_screen()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        Assert.SkipUnless(gpu.Files!.TryRead("misc/Christof.npc", out var data), "no misc/Christof.npc");
        var npc = Vitomir.Formats.NpcFile.Read(data, "Christof.npc");
        var templates = new Vitomir.Engine.World.TemplateSet(gpu.Files);
        Assert.SkipUnless(templates.Has("broadsword"), "no templates in this install");

        var thing = new Vitomir.Engine.World.WorldThing
        {
            Index = 0, Guid = 1, Scene = new Vitomir.Formats.NsdThing(), Template = "christof",
            Type = Vitomir.Engine.World.ThingType.Player,
            Actor = new Actor { Type = ActorType.Human, Name = "Christof" },
        };
        var given = Vitomir.Engine.Rpg.StartingKit.Give(
            thing, npc, templates, new Vitomir.Engine.Rpg.EffectDefinitions(gpu.Files));
        Assert.All(given, g => Assert.True(g.Placed, $"{g.Template} went nowhere: {g.Note}"));

        var empty = Draw(_ => { });
        var armed = Draw(p => { p.Subject = thing.Actor; p.Gold = npc.Cash; },
                         save: Save("inventory-christof"));

        // The broadsword is 2 x 3 and equips to the right weapon slot, invRightWeapon at 353,300.
        Assert.True(Differs(empty, armed, 353, 300, 54, 96), "the broadsword is not in the weapon slot");
        // And the left weapon slot, which nothing went into, is untouched.
        Assert.False(Differs(empty, armed, 555, 300, 54, 96), "something appeared in the off hand");
    }

    /// <summary>
    /// **A pair of gloves shows on both arms, and outlines on both**: Wilhem's
    /// `leathergloves`, worn on the right arm, draw in `invRightArm` (421,320) and `invLeftArm`
    /// (514,320); with the pointer on the left one both draw the red-outlined `_r` picture and nothing
    /// else on the pane changes.
    /// </summary>
    [Fact]
    public void Gloves_draw_on_both_arms_and_both_outline_while_one_is_hovered()
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        Assert.SkipUnless(new UiAssets(gpu.Files!).Texture("leatherGlovesIcon" + InventoryPane.HoverSuffix) is not null,
                          "no leatherGlovesIcon_r in this install");
        var a = new Actor();
        a.Inventory.Add(new Item
        {
            Name = "leathergloves", Type = ItemType.Handwear, Icon = "leatherGlovesIcon", IconSize = 1,
            Flags = ItemFlags.TwoHanded, Location = Slot.RightArm,
        });
        var empty = Draw(_ => { });
        var worn = Draw(p => p.Subject = a, save: Save("inventory-gloves"));
        var hovered = Draw(p => { p.Subject = a; p.Pointer = (514 + 10, 320 + 30); }, save: Save("inventory-gloves-hover"));

        Assert.True(Differs(empty, worn, 421, 320, 27, 64), "no glove on the right arm");
        Assert.True(Differs(empty, worn, 514, 320, 27, 64), "no glove on the left arm");
        Assert.True(Differs(worn, hovered, 421, 320, 27, 64), "the right glove did not outline");
        Assert.True(Differs(worn, hovered, 514, 320, 27, 64), "the left glove did not outline");
        Assert.False(Differs(worn, hovered, 320, 0, 100, 480), "something left of the right arm changed");
        Assert.False(Differs(worn, hovered, 541, 0, 99, 480), "something right of the left arm changed");
    }
}
