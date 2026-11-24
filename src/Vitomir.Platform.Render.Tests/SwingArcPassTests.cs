// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Overlay;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The swing's arc drawn, without a GPU (<c>Overlay/SwingArcPass.cs</c>): two triangles a quad in each layer, the
/// tip edge at the quad's brightness times the opacity and the inner edge clear; the layer drawn through things at its share,
/// the depth-tested one making the full opacity over it; the meeting's mark whole across the blade in its own colour; nothing
/// at no opacity.
/// </summary>
public sealed class SwingArcPassTests
{
    static readonly SwingArcQuad Quad = new(new Vector3(0, 1, 1.2f), new Vector3(0, 1.6f, 1.2f), new Vector3(0.3f, 1, 1.2f), new Vector3(0.3f, 1.6f, 1.2f), 1f, 0.5f);

    static (List<ReachPass.Vertex> Through, List<ReachPass.Vertex> Front) Built(float opacity, params SwingArcQuad[] quads)
    {
        var (t, f) = (new List<ReachPass.Vertex>(), new List<ReachPass.Vertex>());
        SwingArcPass.Build(quads, opacity, SwingArcPass.HiddenShare, t, f);
        return (t, f);
    }

    static float Over(float under, float top) => under + top * (1f - under);

    [Fact]
    public void A_quad_is_two_triangles_a_layer_its_tip_bright_and_its_inner_edge_clear()
    {
        var (through, front) = Built(0.25f, Quad);
        Assert.Equal(6, through.Count);
        Assert.Equal(6, front.Count);
        // The vertices: inner A, tip A, tip B, inner A, tip B, inner B.
        Assert.Equal(0f, front[0].Colour.W);
        Assert.Equal(0f, front[5].Colour.W);
        Assert.Equal(0.25f * SwingArcPass.HiddenShare, through[1].Colour.W, 5);
        Assert.Equal(0.25f, Over(through[1].Colour.W, front[1].Colour.W), 4);                      // seen: the full opacity
        Assert.Equal(0.125f, Over(through[2].Colour.W, front[2].Colour.W), 4);                     // the corner at half brightness
        Assert.All(front, v => Assert.Equal(SwingArcPass.Colour, new Vector3(v.Colour.X, v.Colour.Y, v.Colour.Z)));
    }

    [Fact]
    public void The_mark_is_whole_across_the_blade_in_its_colour_and_no_opacity_draws_nothing()
    {
        var mark = Quad with { A = 1f, B = 1f, Mark = true };
        var (_, front) = Built(0.25f, mark);
        Assert.All(front, v => Assert.Equal(SwingArcPass.MarkColour, new Vector3(v.Colour.X, v.Colour.Y, v.Colour.Z)));
        Assert.True(front[0].Colour.W > 0f);                                                       // the inner edge too
        var (t0, f0) = Built(0f, Quad, mark);
        Assert.All(t0, v => Assert.Equal(0f, v.Colour.W));
        Assert.All(f0, v => Assert.Equal(0f, v.Colour.W));
        var (te, fe) = Built(0.25f);
        Assert.Empty(te);
        Assert.Empty(fe);
    }
}
