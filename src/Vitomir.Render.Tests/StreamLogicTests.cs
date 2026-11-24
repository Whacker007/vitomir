// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Render.Modern;

namespace Vitomir.Render.Tests;

/// <summary>
/// The streamed renderer's pure decisions (ours, Phase 3): the
/// geometry arena's ranges handed out and joined again, the mip a surface asks for at a distance, and
/// the texture budget's choice of which maps give up a mip.
/// </summary>
public class StreamLogicTests
{
    [Fact]
    public void The_geometry_arena_hands_out_ranges_and_joins_them_when_given_back()
    {
        var a = new ArenaAllocator(100);
        long x = a.Allocate(30), y = a.Allocate(30), z = a.Allocate(30);
        Assert.Equal((0L, 30L, 60L), (x, y, z));
        Assert.Equal(-1, a.Allocate(20));                          // 10 left
        a.Free(y, 30);
        Assert.Equal(30, a.LargestFree);
        Assert.Equal(30, a.Allocate(25));                          // first fit: the hole
        a.Free(30, 25); a.Free(x, 30); a.Free(z, 30);
        Assert.Equal(1, a.FreeRanges);                             // all one range again
        Assert.Equal(100, a.LargestFree);
        Assert.Equal(0, a.Used);
        Assert.Throws<InvalidOperationException>(() => { a.Allocate(10); a.Free(0, 10); a.Free(0, 10); });
    }

    [Fact]
    public void A_surface_asks_for_the_mip_whose_texels_a_metre_are_nearest_the_screen_s_pixels()
    {
        // A 1024 map repeated once a metre: 1024 texels a metre at full size.
        Assert.Equal(0, TextureStreamer.MipFor(1024, 1024, 2000, 1, 11));   // closer than a texel a pixel: full size
        Assert.Equal(0, TextureStreamer.MipFor(1024, 1024, 1024, 1, 11));
        Assert.Equal(1, TextureStreamer.MipFor(1024, 1024, 512, 1, 11));
        Assert.Equal(3, TextureStreamer.MipFor(1024, 1024, 100, 1, 11));    // 1024 / 100 ≈ 10: 2³ ≤ 10 < 2⁴
        Assert.Equal(4, TextureStreamer.MipFor(1024, 1024, 100, 2, 11));    // repeated twice a metre: twice the texels
        Assert.Equal(10, TextureStreamer.MipFor(1024, 1024, 0.001f, 1, 11)); // never past the last
        Assert.Equal(10, TextureStreamer.MipFor(1024, 1024, 100, 0, 11));   // no density known: the smallest
    }

    [Fact]
    public void Over_its_budget_the_map_longest_unused_gives_up_a_mip_first_and_none_below_its_small_mips()
    {
        long[] Chain() => [1024, 256, 64, 16, 16];                          // a map's levels' bytes
        var maps = new List<(long[], int, int, int, int)>
        {
            (Chain(), 2, 2, 0, 0),       // asked for full size this frame
            (Chain(), 0, 2, int.MaxValue, 50),   // at full size, not asked for in 50 frames
        };
        Assert.Equal([0, 0], TextureStreamer.Plan(maps, 10_000));           // room for both
        Assert.Equal([0, 1], TextureStreamer.Plan(maps, 1400 + 400));       // the unused one gives up its top first
        Assert.Equal([0, 2], TextureStreamer.Plan(maps, 1400 + 100));
        Assert.Equal([1, 2], TextureStreamer.Plan(maps, 400 + 100));        // then the one in use
        Assert.Equal([2, 2], TextureStreamer.Plan(maps, 1));                // never below the small mips
        // Asked for coarser than it has, a map keeps its mips while the budget allows.
        var coarser = new List<(long[], int, int, int, int)> { (Chain(), 0, 2, 2, 0) };
        Assert.Equal([0], TextureStreamer.Plan(coarser, 10_000));
        Assert.Equal([2], TextureStreamer.Plan(coarser, 100));
    }
}
