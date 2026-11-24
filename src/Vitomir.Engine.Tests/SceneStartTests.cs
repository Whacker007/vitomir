// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A scene's start, the steps a comparison with 1.1 found unbuilt**: the far clip set from the
/// scene in force or to 10,000 on every start, not only when a camera is made; the frame
/// clock started again, so the load is no frame's step; the game's pause cleared;
/// and every floating text taken down, a fading chapter title with them,
/// or with no scene the end text alone.
/// </summary>
public class SceneStartTests
{
    [Fact]
    public void Every_scene_start_sets_the_far_clip_to_the_scene_s_own_or_ten_thousand()
    {
        Assert.Equal(6000f, SceneStart.FarClip(new NsdScene { ZClip = 6000f }, 3000f));
        Assert.Equal(10_000f, SceneStart.FarClip(new NsdScene { ZClip = 0f }, 3000f));     // the last place's 3000 does not follow the party
        Assert.Equal(3000f, SceneStart.FarClip(null, 3000f));                              // no scene: left as it was
    }

    static Caption Title(string text) => new(text, Captions.TitleFont, 0, 400, 0, 8000, 2000, 2000);

    [Fact]
    public void A_scene_start_restarts_the_frame_clock_unpauses_the_game_and_takes_every_floating_text_down()
    {
        using var files = Install.Open();
        var assets = new UiAssets(files);
        var text = new Strings(files);
        var menus = new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find) { State = FrontEnd.StateGame, Paused = true };
        var captions = new Captions();
        var endText = new EndTextHold(captions);
        captions.Add(Title("Monastery 1"));
        endText.Put([Title("All coterie members are dead!")]);
        captions.Add(Title("a title put up since"));

        ulong lastFrame = 1_000_000_000UL;
        SceneStart.Begin(menus, endText, new NsdScene(), ref lastFrame, 9_000_000_000UL);
        Assert.Equal(9_000_000_000UL, lastFrame);                                          // the load is no frame's step
        Assert.Equal(0.001f, FrameStep.Seconds(9_000_000_000UL - lastFrame), 6);
        Assert.False(menus.Paused);
        Assert.Empty(captions.Up);
        Assert.False(endText.Up);
    }

    [Fact]
    public void A_place_with_no_scene_takes_only_the_end_text_down()
    {
        var captions = new Captions();
        var endText = new EndTextHold(captions);
        endText.Put([Title("All coterie members are dead!")]);
        var title = Title("a title put up since");
        captions.Add(title);
        endText.SceneStarted(withScene: false);
        Assert.False(endText.Up);
        Assert.Same(title, Assert.Single(captions.Up));
    }
}
