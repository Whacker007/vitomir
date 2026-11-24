// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Modern;
using Vitomir.Platform.Render.Overlay;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The reach highlight's drawing that needs no GPU (<c>Overlay/ReachPass.cs</c>): each move in the proving
/// ground's colour, a lip of two moves dashed in both, each dash a rim and a line turned to the eye, in the
/// renderer's units, and as wide at a distance as near in pixels.
/// </summary>
public sealed class ReachPassTests
{
    static ReachMark Mark(ReachMoves moves) =>
        new(moves, new Vector3(2, -0.125f, 0.49f), new Vector3(2, 0.125f, 0.49f), new Vector3(-1, 0, 0), 0.45f);

    [Fact]
    public void Each_move_has_the_proving_ground_s_colour_and_a_lip_of_two_moves_is_dashed_in_both()
    {
        Span<Vector4> c = stackalloc Vector4[6];
        Assert.Equal(1, ReachPass.Colours(ReachMoves.Hop, c));
        Assert.Equal(new Vector3(0.98f, 0.82f, 0.22f), new Vector3(c[0].X, c[0].Y, c[0].Z));   // make_course.py: "stop", yellow
        Assert.Equal(2, ReachPass.Colours(ReachMoves.Hurdle | ReachMoves.Vault, c));
        Assert.Equal(ReachPass.Hurdle, c[0]);
        Assert.Equal(ReachPass.Vault, c[1]);
        Assert.Equal(1, ReachPass.Colours(ReachMoves.ClimbOnto, c));
        Assert.Equal(ReachPass.Vault, c[0]);                                                     // vault and climb share blue
        Assert.Equal(1, ReachPass.Colours(ReachMoves.Out, c));
        Assert.True(c[0].W < 0.6f, "out of reach is faint");
        Assert.Equal(1, ReachPass.Colours(ReachMoves.Ladder, c));                                 // a ladder's foot and top
        Assert.Equal(new Vector3(0.62f, 0.88f, 0.18f), new Vector3(c[0].X, c[0].Y, c[0].Z));   // make_course.py: "ladder", lime

        var vertices = new List<ReachPass.Vertex>();
        ReachPass.Build([Mark(ReachMoves.Hurdle | ReachMoves.Vault)], new Vector3(0, 0, 1.7f), vertices);
        Assert.Equal(2 * 2 * 6, vertices.Count);                                                 // two dashes, each a rim and a line
        Assert.Equal(ReachPass.Hurdle, vertices[6].Colour);
        Assert.Equal(ReachPass.Vault, vertices[18].Colour);
        Assert.All(vertices, v => Assert.InRange(v.Pos.Z, 0.4f * ModernMath.UnitsPerMetre, 0.6f * ModernMath.UnitsPerMetre));
    }

    [Fact]
    public void A_line_is_as_wide_in_pixels_far_as_near_and_nothing_is_drawn_seen_end_on()
    {
        Assert.Equal(2f * ReachPass.HalfWidth(4f), ReachPass.HalfWidth(8f), 4);                 // proportional to the distance
        Assert.Equal(0.01f, ReachPass.HalfWidth(0.5f));                                          // never under a centimetre
        var vertices = new List<ReachPass.Vertex>();
        // The eye straight along the lip's line: a ribbon has no side to turn.
        ReachPass.Build([new ReachMark(ReachMoves.Hop, new Vector3(2, 0, 0.5f), new Vector3(3, 0, 0.5f), new Vector3(0, -1, 0), 0.45f)],
                        new Vector3(0, 0, 0.5f), vertices);
        Assert.Empty(vertices);
    }
}
