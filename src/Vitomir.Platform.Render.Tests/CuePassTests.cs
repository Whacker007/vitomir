// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Render.Overlay;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The flurry's cue drawn, without a GPU (<c>Overlay/CuePass.cs</c>): a star for each live flash, at
/// its point, opening and fading over its life.
/// </summary>
public sealed class CuePassTests
{
    static readonly Vector3 Eye = new(0, 0, 1.7f), Point = new(3, 0, 1.2f);

    static List<ReachPass.Vertex> Built(params BlowCueMark[] marks)
    {
        var v = new List<ReachPass.Vertex>();
        CuePass.Build(marks, Eye, v);
        return v;
    }

    [Fact]
    public void Each_flash_is_a_star_of_eight_strokes_round_its_point_that_opens_and_fades()
    {
        var young = Built(new BlowCueMark(Point, 0.01f, BlowCues.Life, 1));
        var old = Built(new BlowCueMark(Point, 0.14f, BlowCues.Life, 1));
        Assert.Equal(8 * 6, young.Count);                                        // eight ribbons of two triangles
        Assert.Equal(2 * 8 * 6, Built(new BlowCueMark(Point, 0.05f, BlowCues.Life, 1), new BlowCueMark(Point, 0.05f, BlowCues.Life, 2)).Count);
        static float Reach(List<ReachPass.Vertex> v) => v.Max(x => Vector3.Distance(x.Pos / Modern.ModernMath.UnitsPerMetre, Point));
        Assert.True(Reach(old) > Reach(young) * 1.5f, $"young {Reach(young):0.000} m, old {Reach(old):0.000} m");
        Assert.True(old.Max(x => x.Colour.W) < young.Max(x => x.Colour.W));
        Assert.Empty(Built());
    }

    [Fact]
    public void The_kth_flash_comes_k_spacings_after_the_hit_and_lives_its_life()
    {
        var cues = new BlowCues();
        long tick = 600;                                                        // 5 s at 120 ticks a second
        for (int k = 1; k <= 3; k++) cues.Add(Point, tick, k);
        var live = new List<BlowCueMark>();
        double hit = tick / (double)Core.World.TicksPerSecond;
        cues.Fill(hit + 0.01, live);
        Assert.Empty(live);                                                     // the first extra blow's flash waits one spacing
        cues.Fill(hit + BlowCues.Spacing + 0.01, live);
        Assert.Equal([1], live.Select(m => m.Index));
        cues.Fill(hit + 3 * BlowCues.Spacing + 0.01, live);
        Assert.Equal([1, 2, 3], live.Select(m => m.Index));
        cues.Fill(hit + 3 * BlowCues.Spacing + BlowCues.Life + 0.01, live);
        Assert.Empty(live);
    }

    [Fact]
    public void A_blow_a_guard_took_is_a_small_steel_grey_cross_at_its_moment_not_a_gold_star()
    {
        // Four strokes, no warm gold, a little more than half the star's reach; drawn from its own tick.
        var star = Built(new BlowCueMark(Point, 0.05f, BlowCues.Life, 1));
        var flash = Built(new BlowCueMark(Point, 0.05f, BlowCues.Life, 1, Guarded: true));
        Assert.Equal(4 * 6, flash.Count);
        Assert.All(flash, v => Assert.True(v.Colour.Z >= v.Colour.X, $"steel, not gold: {v.Colour}"));
        Assert.All(star, v => Assert.True(v.Colour.X > v.Colour.Z));
        static float Reach(List<ReachPass.Vertex> v) => v.Max(x => Vector3.Distance(x.Pos / Modern.ModernMath.UnitsPerMetre, Point));
        Assert.InRange(Reach(flash), Reach(star) * 0.45f, Reach(star) * 0.75f);
        var cues = new BlowCues();
        cues.AddGuarded(Point, 600, 2);
        var live = new List<BlowCueMark>();
        cues.Fill(600 / (double)Core.World.TicksPerSecond + 0.01, live);
        Assert.Equal((2, true), (Assert.Single(live).Index, live[0].Guarded));
    }
}
