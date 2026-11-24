// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The small description box on screen. `RolloverTests` proves what it would say; this proves it is
// drawn — **backdrop and text**, over the whole rectangle the pane declares.
//
// The first version of this test asserted only that *something* changed, and passed while the box
// was drawing its backdrop squashed into two thirds of its slot and no text at all. Both faults
// were invisible to it and obvious the moment somebody rendered a frame and looked at it. So it
// now asserts the two things that were wrong: that the backdrop fills its own rectangle, and that
// there are bright pixels inside it, which only glyphs can be.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

[Collection("headless")]
public class RolloverFrameTests(Headless h)
{
    void Ready() => Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");

    [Fact]
    public void The_small_description_box_is_drawn_with_its_backdrop_and_its_name()
    {
        Ready();
        const int W = 640, H = 480;                 // the UI's own space, so one frame pixel is one UI unit
        var place = Location.Load(h.Files!, "P1_OTDY", scene: "OTDY_4_1", player: "Christof");
        using var frame = new FrameRenderer(h.Gpu!, place);
        var ui = new UiCanvas(new UiAssets(h.Files!));
        using var back = new UiRenderer(h.Gpu!);
        var words = new Strings(h.Files!);
        words.Add("prague");
        var pass = new RolloverPass(ui) { Text = words };
        frame.After = back.Of(pass);
        var camera = place.Camera();

        _ = Headless.Shoot(frame, W, H, camera);
        var empty = Headless.Shoot(frame, W, H, camera);
        Assert.Null(pass.Line);

        pass.Hovered = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is not null);
        Assert.NotNull(pass.Line);
        var shown = Headless.Shoot(frame, W, H, camera);

        // The pane's own rectangle, in frame pixels. `.nui` y counts **up from the bottom**, so
        // `descSmall`'s pos 160,93 is just above the status bar, not at the top of the screen.
        int left = pass.Pane.Pos[0], width = pass.Pane.Quads.Sum(q => q.Size[0]);
        int height = pass.Pane.Quads.Max(q => q.Size[1]);
        int bottom = H - pass.Pane.Pos[1], top = bottom - height;

        int covered = 0, outside = 0, bright = 0;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = (y * W + x) * 4;
                bool moved = empty[i] != shown[i] || empty[i + 1] != shown[i + 1] || empty[i + 2] != shown[i + 2];
                bool inBox = x >= left && x < left + width && y >= top && y < bottom;
                if (!moved) continue;
                if (!inBox) { outside++; continue; }
                covered++;
                if (shown[i] > 180 && shown[i + 1] > 180 && shown[i + 2] > 180) bright++;
            }

        // **The backdrop fills its slot.** A stretched power-of-two canvas would leave the top
        // third of the box untouched, which is the fault this number catches.
        int area = width * height;
        Assert.True(covered > area * 9 / 10,
                    $"the backdrop covered {covered} of the box's {area} pixels");
        Assert.True(outside * 20 < covered, $"{outside} pixels changed outside the box");

        // **And the name is in it.** Only glyphs are this bright against that backdrop.
        Assert.True(bright > 40, $"only {bright} bright pixels inside the box — the name did not draw");

        pass.Hovered = null;
        Assert.Equal(empty, Headless.Shoot(frame, W, H, camera));
    }

    /// <summary>
    /// **The large box waits a second and a half, and only an item ever arms it.** That is the
    /// rollover's mode 3 (the status bar's timer is armed 1,500 ms ahead, and mode 3 is picked only
    /// for thing types 1, 5 and 6), and it is the difference between a HUD that answers
    /// every twitch of the mouse and one that waits to see whether you meant it.
    /// </summary>
    [Fact]
    public void The_large_box_waits_and_only_an_item_ever_brings_it_up()
    {
        Ready();
        const int W = 640, H = 480;
        var place = Location.Load(h.Files!, "P1_OTDY", scene: "OTDY_4_1", player: "Christof");
        using var frame = new FrameRenderer(h.Gpu!, place);
        var ui = new UiCanvas(new UiAssets(h.Files!));
        using var back = new UiRenderer(h.Gpu!);
        var words = new Strings(h.Files!);
        var effects = new Vitomir.Engine.Rpg.EffectDefinitions(h.Files!);
        Assert.SkipWhen(effects.File is null, "misc/effects.ned not in the install");
        var pass = new RolloverPass(ui) { Text = words, Effects = effects.File };
        frame.After = back.Of(pass);
        var camera = place.Camera();
        var rat = place.Make("VitaeRat");
        Assert.SkipWhen(rat?.Item is null, "VitaeRat not in the template set");
        rat!.Item!.Flags |= Vitomir.Engine.Rpg.ItemFlags.Identified;

        _ = Headless.Shoot(frame, W, H, camera);
        pass.NowMs = 0;
        pass.Hovered = rat;
        Assert.False(pass.LargeUp, "the large box came up at once");
        var waiting = Headless.Shoot(frame, W, H, camera);

        pass.NowMs = Rollover.LargeAfterMs - 1;
        Assert.False(pass.LargeUp, "the large box came up a millisecond early");
        pass.NowMs = Rollover.LargeAfterMs;
        Assert.True(pass.LargeUp, "the large box never came up");
        var shown = Headless.Shoot(frame, W, H, camera);
        Assert.True(Headless.Differing(waiting, shown) > 0.02f,
                    $"the detail block changed only {Headless.Differing(waiting, shown):P2} of the picture");

        // A character never arms it however long you look.
        pass.Hovered = place.Things.First(t => t.Type == ThingType.Actor && t.Actor is not null);
        pass.NowMs = 0;
        Assert.False(pass.LargeUp);
        pass.NowMs = 60_000;
        Assert.False(pass.LargeUp, "a character brought the detail block up");
        Assert.Empty(pass.Detail);
    }

    /// <summary>
    /// A control's own `hintstring` goes in the small box and **nowhere near** the large one —
    /// a different route into the same pane, and the reason `Toggle Inventory Pane`
    /// looks like a description without being one.
    /// </summary>
    [Fact]
    public void An_interface_controls_hint_takes_the_small_box_and_not_the_large()
    {
        Ready();
        var place = Location.Load(h.Files!, "P1_OTDY", scene: "OTDY_4_1", player: "Christof");
        using var frame = new FrameRenderer(h.Gpu!, place);
        var ui = new UiCanvas(new UiAssets(h.Files!));
        using var back = new UiRenderer(h.Gpu!);
        var words = new Strings(h.Files!);
        var effects = new Vitomir.Engine.Rpg.EffectDefinitions(h.Files!);
        var bar = ui.Assets.Pane("statusBarSP");
        var button = bar.Elements.First(e => e.Get("hintstring") is { Length: > 0 });
        var pass = new RolloverPass(ui) { Text = words, Effects = effects.File, NowMs = 60_000 };

        var rat = place.Make("VitaeRat");
        Assert.SkipWhen(rat?.Item is null, "VitaeRat not in the template set");
        rat!.Item!.Flags |= Vitomir.Engine.Rpg.ItemFlags.Identified;
        pass.Hovered = rat;
        Assert.NotEmpty(pass.Detail);

        // The hint wins over whatever is behind the interface, and takes the large box away.
        pass.Control = button;
        Assert.Equal(words[button.Get("hintstring")!], pass.Line!.Value.Text);
        Assert.Empty(pass.Detail);
        Assert.False(pass.LargeUp);
    }

    /// <summary>
    /// **A torch in the pack puts the large box up, empty, over the portraits' rectangle.** Its two
    /// lines are blank rows (`RolloverTests.A_torch_or_a_letter_is_two_blank_rows_in_the_pack_and_on_the_shelf`),
    /// the pack's icon is mode 2 (both panes at once), and nothing asks what the box
    /// holds — so `descLargeSP`'s black backdrop is drawn over its whole rectangle, the one the party
    /// heads use, with no glyph in it, exactly as 1.1 does.
    /// </summary>
    [Fact]
    public void A_torch_in_the_pack_covers_the_heads_rectangle_with_an_empty_large_box()
    {
        Ready();
        const int W = 640, H = 480;
        var place = Location.Load(h.Files!, "P1_OTDY", scene: "OTDY_4_1", player: "Christof");
        using var frame = new FrameRenderer(h.Gpu!, place);
        var ui = new UiCanvas(new UiAssets(h.Files!));
        using var back = new UiRenderer(h.Gpu!);
        var words = new Strings(h.Files!);
        var effects = new Vitomir.Engine.Rpg.EffectDefinitions(h.Files!);
        var pass = new RolloverPass(ui) { Text = words, Effects = effects.File };
        frame.After = back.Of(pass);
        var camera = place.Camera();
        var torch = place.Make("torchU");
        Assert.SkipWhen(torch?.Item is null, "torchU not in the template set");
        torch!.Item!.Flags |= Vitomir.Engine.Rpg.ItemFlags.Identified;

        _ = Headless.Shoot(frame, W, H, camera);
        var empty = Headless.Shoot(frame, W, H, camera);
        pass.Lines = IconRollover.Item(torch.Item, onStatusBar: false, user: null, words, effects.File);
        Assert.True(pass.LargeUp, "the pack's icon brings the large box up at once");
        Assert.Equal(2, pass.Detail.Count);
        var shown = Headless.Shoot(frame, W, H, camera);

        int left = pass.Large.Pos[0], width = pass.Large.Quads.Sum(q => q.Size[0]);
        int height = pass.Large.Quads.Max(q => q.Size[1]);
        int bottom = H - pass.Large.Pos[1], top = bottom - height;
        int dark = 0, bright = 0;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < left + width; x++)
            {
                int i = (y * W + x) * 4;
                if (shown[i] < 24 && shown[i + 1] < 24 && shown[i + 2] < 24) dark++;
                if (shown[i] > 180 && shown[i + 1] > 180 && shown[i + 2] > 180) bright++;
            }
        int area = width * height;
        Assert.True(dark > area * 9 / 10, $"only {dark} of the large box's {area} pixels are its black backdrop");
        Assert.Equal(0, bright);
        Assert.NotEqual(empty, shown);
    }
}
