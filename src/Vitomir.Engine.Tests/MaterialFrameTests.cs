// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Scene;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Page-flipped materials and the light-style flicker that switches their frames. The storm that
/// drives them is the game's own script, so it is tested where that runs: Vitomir.Scripting.Tests.
/// </summary>
public class MaterialFrameTests
{
    [Fact]
    public void A_texture_is_found_on_the_engines_search_path_not_beside_its_material()
    {
        using var files = Install.Open();
        // black.tga is written by materials under 3d/materials/, but the only one the game ships
        // is materials/prague/black.tga — the search path is what finds it.
        Assert.True(files.TryReadMaterialFile("black.tga", out var found));
        Assert.True(files.TryRead("materials/prague/black.tga", out var prague));
        Assert.Equal(prague, found);
        Assert.False(files.TryRead("3d/materials/black.tga", out _));
    }

    [Fact]
    public void The_storms_bolt_is_two_frames_the_first_of_which_is_black()
    {
        using var files = Install.Open();
        var m = Materials.Load(files, "3d/materials/lightning1");
        Assert.Equal(2, m.Frames.Length);
        Assert.Equal(100000, m.PageFlipMs);
        Assert.Equal(0, m.CurrentFrame);
        var black = m.Images[m.Frames[0]]!;
        Assert.All(black.Rgba.Chunk(4), px => Assert.Equal(0, px[0] + px[1] + px[2]));
        // Frame 1 is the bolt: bright somewhere, and drawn on top of the sky by adding to it.
        Assert.Contains(m.Images[m.Frames[1]]!.Rgba, b => b > 200);
        Assert.True(m.Traits.AddBlend);
    }

    [Fact]
    public void A_page_flipped_material_steps_a_frame_every_pageflip_milliseconds()
    {
        using var files = Install.Open();
        var cache = new MaterialCache(files);
        var m = cache.Items[cache.Get("3d/materials/lightning1")];
        var styles = new LightStyles();
        cache.Animate(99_000f, styles);
        Assert.Equal(0, m.CurrentFrame);
        cache.Animate(2_000f, styles);       // past 100 s
        Assert.Equal(1, m.CurrentFrame);
        cache.Animate(100_000f, styles);     // and back round
        Assert.Equal(0, m.CurrentFrame);
    }

    [Fact]
    public void A_flicker_material_follows_its_light_style()
    {
        using var files = Install.Open();
        var cache = new MaterialCache(files);
        var m = cache.Items[cache.Get("3d/materials/lightning1")];
        // No shipped material uses the keyword, so drive the machinery directly: style 7 is one of
        // the fluorescent styles, which is either fully lit or fully dark.
        var flicker = new LoadedMaterial
        {
            Name = m.Name, Nam = m.Nam, Images = m.Images, Frames = m.Frames, FlickerStyle = 7,
        };
        var styles = new LightStyles();
        styles.Enable(7, true);
        bool sawLit = false, sawDark = false;
        for (int i = 0; i < 400; i++)
        {
            styles.Step(1000f / 60f);
            flicker.ApplyFlicker(styles);
            Assert.Equal(styles.Brightness(7) == 0 ? 0 : 1, flicker.CurrentFrame);
            sawLit |= flicker.CurrentFrame == 1;
            sawDark |= flicker.CurrentFrame == 0;
        }
        Assert.True(sawLit && sawDark, "a fluorescent style should have blinked inside seven seconds");
    }
}
