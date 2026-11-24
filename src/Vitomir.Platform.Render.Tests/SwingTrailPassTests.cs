// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render.Overlay;

namespace Vitomir.Platform.Render.Tests;

/// <summary>
/// The swing's trail drawn, without a GPU (<c>Overlay/SwingTrailPass.cs</c>): a ribbon for each piece of the
/// tip's path, the blade and the miss, a star for the reticle's point and the nearest pass, each in its kind's colour,
/// fading with its trail; nothing for a faded line or none.
/// </summary>
public sealed class SwingTrailPassTests
{
    static readonly Vector3 Eye = new(0, -3, 1.7f);

    static List<ReachPass.Vertex> Built(params SwingTrailLine[] lines)
    {
        var v = new List<ReachPass.Vertex>();
        SwingTrailPass.Build(lines, Eye, v);
        return v;
    }

    [Fact]
    public void Lines_are_ribbons_and_points_are_stars_in_their_kinds_colours()
    {
        var a = new Vector3(-0.5f, 1, 1.2f);
        var b = new Vector3(0.5f, 1, 1.3f);
        Assert.Equal(6, Built(new SwingTrailLine(a, b, SwingTrailKind.Tip, 1f)).Count);           // two triangles
        Assert.Equal(6, Built(new SwingTrailLine(a, b, SwingTrailKind.Blade, 1f)).Count);
        Assert.Equal(6, Built(new SwingTrailLine(a, b, SwingTrailKind.Miss, 1f)).Count);
        var star = Built(new SwingTrailLine(a, a, SwingTrailKind.Reticle, 1f));
        Assert.Equal(3 * 6, star.Count);                                                          // three crossed strokes
        Assert.Equal(3 * 6, Built(new SwingTrailLine(b, b, SwingTrailKind.Nearest, 1f)).Count);
        // Each in its colour: the reticle's magenta, the nearest pass's yellow, the tip's cyan, the blade faint.
        Assert.All(star, v => Assert.Equal(SwingTrailPass.Colours[(int)SwingTrailKind.Reticle], v.Colour));
        Assert.True(Built(new SwingTrailLine(a, b, SwingTrailKind.Blade, 1f))[0].Colour.W < Built(new SwingTrailLine(a, b, SwingTrailKind.Tip, 1f))[0].Colour.W);
        // A star stands round its point: every corner within its arm and the stroke's width of it.
        Assert.All(star, v => Assert.InRange(Vector3.Distance(v.Pos / Modern.ModernMath.UnitsPerMetre, a), 0f, 0.1f));
    }

    [Fact]
    public void A_fading_trail_is_fainter_and_a_faded_or_empty_one_draws_nothing()
    {
        var a = new Vector3(-0.5f, 1, 1.2f);
        var b = new Vector3(0.5f, 1, 1.3f);
        float full = Built(new SwingTrailLine(a, b, SwingTrailKind.Tip, 1f))[0].Colour.W;
        float half = Built(new SwingTrailLine(a, b, SwingTrailKind.Tip, 0.5f))[0].Colour.W;
        Assert.Equal(full * 0.5f, half, 4);
        Assert.Empty(Built(new SwingTrailLine(a, b, SwingTrailKind.Tip, 0f)));
        Assert.Empty(Built(new SwingTrailLine(a, a, SwingTrailKind.Tip, 1f)));                   // a piece of no length
        Assert.Empty(Built());
    }
}
