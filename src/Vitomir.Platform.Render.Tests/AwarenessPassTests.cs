// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Overlay;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The awareness overlay's drawing that needs no GPU (<c>Overlay/AwarenessPass.cs</c>; the light and conceal areas' outlines): every observer's cone on
/// the ground, a keeper of the peace's in blue; no mark unaware, a ? aware, an orange ! alerted, a red ! alarmed.
/// </summary>
public sealed class AwarenessPassTests
{
    static AwarenessMark Mark(Awareness level, bool keeper = false) =>
        new(new Vector3(5, 0, 2.2f), new Vector3(5, 0, 0), MathF.PI, 1f, 10f, level, keeper);

    static List<ReachPass.Vertex> Built(params AwarenessMark[] marks)
    {
        var v = new List<ReachPass.Vertex>();
        AwarenessPass.Build(marks, new Vector3(0, 0, 1.7f), v);
        return v;
    }

    [Fact]
    public void Each_rung_has_its_mark_over_the_head_and_every_observer_its_cone()
    {
        int cone = Built(Mark(Awareness.Unaware)).Count;
        Assert.True(cone > 0);
        Assert.All(Built(Mark(Awareness.Unaware)), x => Assert.Equal(AwarenessPass.Cone, x.Colour));
        Assert.All(Built(Mark(Awareness.Unaware, keeper: true)), x => Assert.Equal(AwarenessPass.KeeperCone, x.Colour));
        foreach (var (level, colour) in new[] { (Awareness.Aware, AwarenessPass.Aware), (Awareness.Alerted, AwarenessPass.Alerted), (Awareness.Alarmed, AwarenessPass.Alarmed) })
        {
            var v = Built(Mark(level));
            Assert.True(v.Count > cone, $"{level}: no mark");
            Assert.All(v.Skip(cone), x => Assert.Equal(colour, x.Colour));
        }
        Assert.True(Built(Mark(Awareness.Aware)).Count > Built(Mark(Awareness.Alerted)).Count);   // a ? has more strokes than a !
    }

    [Fact]
    public void A_light_area_s_outline_and_a_conceal_area_s_are_drawn_each_in_its_own_colour()
    {
        var v = new List<ReachPass.Vertex>();
        AwarenessPass.Build([], [new AwarenessEdge(new(1, 0, 0.04f), new(2, 0, 0.04f), Conceal: false), new AwarenessEdge(new(1, 1, 0.04f), new(2, 1, 0.04f), Conceal: true)],
            new Vector3(0, 0, 1.7f), v);
        Assert.NotEmpty(v);
        Assert.Equal(v.Count / 2, v.Count(x => x.Colour == AwarenessPass.LightArea));
        Assert.Equal(v.Count / 2, v.Count(x => x.Colour == AwarenessPass.ConcealArea));
        Assert.NotEqual(AwarenessPass.LightArea, AwarenessPass.ConcealArea);
    }
}
