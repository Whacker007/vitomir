// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using SDL;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using static SDL.SDL3;

namespace Vitomir.Render.Tests;

/// <summary>
/// The conversation panes, actually drawn. `ConversationPaneTests` in the engine suite proves what
/// *should* be on screen; this proves it reaches the screen, which is a different failure — the
/// status bar's first run was laid out correctly and drawn entirely white.
///
/// These need only the UI half of an install (`ui/*.nui`, a font, four TGAs), not a level.
///
/// **They join the `headless` collection rather than taking their own fixture.** There is one SDL
/// device for the whole run and it is torn down with `SDL_Quit`; a second `IClassFixture&lt;Headless&gt;`
/// makes a second one, and whichever finishes first quits the video subsystem out from under the
/// other. That is exactly what happened on the first run on the laptop — these four skipped with
/// "Video subsystem not initialized" while the level tests passed — and it did not show up in the
/// cloud, where the level tests skip for want of an install and nothing raced.
/// </summary>
[Collection("headless")]
public unsafe class ConversationFrameTests(Headless gpu)
{
    /// <summary>The 640x480 the panes are authored in, drawn 1:1 so a still is comparable by eye.</summary>
    const int W = 640, H = 480;

    /// <summary>A mid-grey behind everything, so white text and an opaque black box both show up.</summary>
    static readonly SDL_FColor Backdrop = new() { r = 0.35f, g = 0.33f, b = 0.30f, a = 1f };

    byte[] Draw(Action<ConversationPane> fill, string? save = null)
    {
        Assert.SkipWhen(gpu.Skip is not null, gpu.Skip ?? "");
        var assets = new UiAssets(gpu.Files!);
        var ui = new UiCanvas(assets);
        using var back = new UiRenderer(gpu.Gpu!);
        var pane = ConversationPass.Load(assets);
        fill(pane);
        var pass = new ConversationPass(ui, pane);

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
    /// Whether any pixel in a band is a colour, within a tolerance. The tolerance is there because
    /// these run on whichever backend the machine has — Direct3D 12 on the laptop, a software
    /// Vulkan in the cloud — and a colour that is right to within a few levels is the claim.
    /// </summary>
    static bool Ink(byte[] rgba, int lowY, int highY, (int R, int G, int B) want, int slack = 6) =>
        Rows(rgba, lowY, highY, want, slack).Count > 0;

    /// <summary>Which rows in a band hold a given colour, so a test can say one is above another.</summary>
    static List<int> Rows(byte[] rgba, int lowY, int highY, (int R, int G, int B) want, int slack = 6)
    {
        var rows = new List<int>();
        for (int y = lowY; y <= highY; y++)
            for (int x = 64; x < 576; x++)
            {
                var c = At(rgba, x, y);
                if (Math.Abs(c.R - want.R) <= slack && Math.Abs(c.G - want.G) <= slack
                    && Math.Abs(c.B - want.B) <= slack) { rows.Add(y); break; }
            }
        return rows;
    }

    /// <summary>The grey the test clears to, sampled rather than computed from the float.</summary>
    static (int R, int G, int B) Empty(byte[] rgba) => At(rgba, 2, 2);

    /// <summary>Nothing is drawn at all until something has been said.</summary>
    [Fact]
    public void An_idle_conversation_draws_nothing()
    {
        var blank = Draw(_ => { });
        var empty = Empty(blank);
        for (int y = 0; y < H; y += 7)
            for (int x = 0; x < W; x += 7)
                Assert.Equal(empty, At(blank, x, y));
    }

    /// <summary>
    /// **The transcript's two colours reach the screen.** The conversation pane alternates `0xffffff` and
    /// `0xafafaf` line by line, and both are in the frame, pixel for pixel — which is the whole
    /// claim, and the sort of thing that is laid out correctly and drawn wrong.
    ///
    /// The transcript has **no background of its own**, so the frame above the panes is untouched:
    /// the lines sit straight on the scene, exactly as they do in the original.
    /// </summary>
    [Fact]
    public void The_transcript_alternates_white_and_grey_on_screen()
    {
        var shot = Draw(pane =>
        {
            pane.Say(Conversation.Subtitle("Anezka", "The mighty walls of Prague once held all demons at bay. But the szlachta have overtaken a silver mine to the east."));
            pane.Say(Conversation.Subtitle("Christof", "I will never let them take thee!"));
            pane.Say(Conversation.Subtitle("Anezka", "Oh, sir..."));
        }, Save("conversation-transcript"));

        Assert.True(Ink(shot, 32, 160, (255, 255, 255)), "no white line in the transcript");
        Assert.True(Ink(shot, 32, 160, (175, 175, 175)), "no 0xafafaf line in the transcript");

        // Above the panes, nothing: the transcript draws no background.
        var empty = Empty(shot);
        for (int y = 200; y < H; y += 7)
            for (int x = 0; x < W; x += 7)
                Assert.Equal(empty, At(shot, x, y));
    }

    /// <summary>
    /// **The reply box is opaque black and the hovered row is yellow.** The list declares
    /// `color=255,0,0,0`, which is what makes it a filled box over the transcript rather than more
    /// text on the scene; the highlight is the one colour here taken from a screenshot rather than
    /// from 1.1's own rules.
    ///
    /// Row 0 is the **top** of the list, so the hovered row's yellow has to sit above the other
    /// row's white — which is the part a wrong sign would get backwards.
    /// </summary>
    [Fact]
    public void The_reply_box_is_opaque_and_the_hovered_row_is_yellow()
    {
        var shot = Draw(pane =>
        {
            pane.Say(Conversation.Subtitle("Anezka", "Oh, sir..."));
            pane.Offer(["Tomorrow I shall venture into the silver mine, and flush the vermin from their holes.",
                        "A pity I am in poor health, or I would kill them all."]);
            pane.Hovered = 0;
        }, Save("conversation-replies"));

        var fill = At(shot, 300, 40);                          // the list's own opaque background
        Assert.True(fill is { R: < 12, G: < 12, B: < 12 }, $"the reply box is not opaque black: {fill}");

        var yellow = Rows(shot, 32, 96, (255, 255, 0));
        var white = Rows(shot, 32, 96, (255, 255, 255));
        Assert.NotEmpty(yellow);
        Assert.NotEmpty(white);
        // `.nui` y counts up, so "above" is a larger y.
        Assert.True(yellow.Min() > white.Max(),
                    $"the hovered row is not above the others: yellow {yellow.Min()}..{yellow.Max()}, white {white.Min()}..{white.Max()}");
    }

    /// <summary>
    /// **Five replies stay inside the box** (in play at Unorna's shop the fifth hung
    /// under it): 1.1 reads the list's <c>itemspacing=0x04</c> as 0, so five 10-pixel rows fit its 60,
    /// the fifth 10 above the box's bottom; and more would scroll, the draw cutting at the box's edges.
    /// No reply's white reaches below the box (y 34) or above it (y 94).
    /// </summary>
    [Fact]
    public void Five_replies_are_drawn_inside_the_box()
    {
        var shot = Draw(pane => pane.Offer(["Know thou of the first thing?", "Know thou of the second?",
                                            "What means the third?", "What of the fourth?", "I thank thee."]),
                        Save("conversation-five-replies"));
        var white = Rows(shot, 0, 140, (255, 255, 255));
        Assert.NotEmpty(white);
        Assert.True(white.Min() >= 34 && white.Max() < 94, $"reply ink outside the box: rows {white.Min()}..{white.Max()}");
        Assert.True(Ink(shot, 44, 56, (255, 255, 255)), "the fifth reply is not at the bottom of the box");
    }

    /// <summary>With nothing on offer the black box is gone and the transcript is not covered.</summary>
    [Fact]
    public void The_reply_box_is_only_up_while_there_is_something_to_say()
    {
        var shot = Draw(pane => pane.Say(Conversation.Subtitle("Anezka", "Oh, sir...")));
        var gap = At(shot, 300, 40);
        Assert.False(gap is { R: < 12, G: < 12, B: < 12 }, "the reply box is up with nothing on offer");
    }

    /// <summary>Where a still goes when `VTMR_SHOTS` names a folder; nowhere otherwise.</summary>
    static string? Save(string name) =>
        Environment.GetEnvironmentVariable("VTMR_SHOTS") is { Length: > 0 } dir
            ? Path.Combine(dir, name + ".png") : null;
}
