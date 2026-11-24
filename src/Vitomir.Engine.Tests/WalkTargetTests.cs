// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Scene;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>The click-to-move ring's arithmetic, against 1.1's constants.</summary>
public class WalkTargetTests
{
    static WalkTarget Placed()
    {
        var t = new WalkTarget();
        t.Set(new Vector3(100f, 200f, 50f), Vector3.UnitZ);
        return t;
    }

    /// <summary>64 world units across at the click, 0.4 at the end, and 255 down to 0.</summary>
    [Fact]
    public void It_shrinks_from_sixty_four_units_to_almost_nothing_over_one_second()
    {
        var t = Placed();
        Assert.Equal(32f, t.HalfSize, 3);                 // 1.0 * 31.8 + 0.2
        Assert.Equal(255f, t.Colour.X, 3);
        Assert.Equal(255f, t.Colour.W, 3);

        Assert.True(t.Advance(0.5f));
        Assert.Equal(16.1f, t.HalfSize, 3);               // 0.5 * 31.8 + 0.2
        Assert.Equal(127.5f, t.Colour.X, 3);
        // **The alpha never moves.** The fade is the colour going to black under an additive blend,
        // which is the one thing that would look wrong if it were transcribed as an alpha fade.
        Assert.Equal(255f, t.Colour.W, 3);

        Assert.False(t.Advance(0.5f));
        Assert.False(t.Active);
        Assert.Equal(0f, t.Remaining);
    }

    /// <summary>A click that picked no surface places nothing, and disturbs nothing.</summary>
    [Fact]
    public void A_click_that_found_no_plane_leaves_the_last_one_alone()
    {
        var t = Placed();
        t.Advance(0.25f);
        t.Set(new Vector3(9f, 9f, 9f), null);
        Assert.Equal(new Vector3(100f, 200f, 50f), t.Point);
        Assert.Equal(0.75f, t.Remaining, 3);

        t.Set(new Vector3(9f, 9f, 9f), Vector3.Zero);     // a degenerate plane is no plane
        Assert.Equal(new Vector3(100f, 200f, 50f), t.Point);
    }

    /// <summary>Placing one again restarts its clock, and the walker stopping takes it away.</summary>
    [Fact]
    public void A_new_click_restarts_it_and_stopping_ends_it()
    {
        var t = Placed();
        t.Advance(0.9f);
        t.Set(new Vector3(1f, 2f, 3f), Vector3.UnitZ);
        Assert.Equal(1f, t.Remaining, 3);
        Assert.Equal(new Vector3(1f, 2f, 3f), t.Point);

        t.Clear();
        Assert.False(t.Active);
        Assert.False(t.Advance(0.1f));
    }

    /// <summary>
    /// The quad lies in the clicked surface's plane, is square, and carries the whole texture. On a
    /// slope it tilts with the ground, which is what the engine's basis-from-the-normal does.
    /// </summary>
    [Theory]
    [InlineData(0f, 0f, 1f)]                    // flat floor
    [InlineData(0f, 0.7071f, 0.7071f)]          // a 45-degree ramp
    [InlineData(1f, 0f, 0f)]                    // a wall, which a click can land on
    public void The_quad_lies_in_the_clicked_surface(float nx, float ny, float nz)
    {
        var normal = Vector3.Normalize(new Vector3(nx, ny, nz));
        var t = new WalkTarget();
        t.Set(new Vector3(10f, 20f, 30f), normal);
        var corners = t.Corners();

        Assert.Equal(4, corners.Length);
        foreach (var (position, _) in corners)
        {
            // In the plane: the offset from the click point has no component along the normal.
            Assert.Equal(0f, Vector3.Dot(position - t.Point, normal), 3);
            // And every corner is the same distance out, so it is a square about the point.
            Assert.Equal(t.HalfSize * MathF.Sqrt(2f), (position - t.Point).Length(), 2);
        }
        // Opposite corners are opposite, so the point is the centre and not a corner.
        Assert.Equal(t.Point, (corners[0].Position + corners[2].Position) / 2f);

        var uvs = corners.Select(c => c.Uv).ToArray();
        Assert.Equal([new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1)], uvs);
    }

    /// <summary>
    /// The material really is in the install, really is a ring, and really is premultiplied by its
    /// own alpha — which is what makes an additive blend the right one and is worth pinning,
    /// because a reimplementation that "fixes" the texture to straight alpha would draw a grey disc.
    /// </summary>
    [Fact]
    public void The_material_ships_and_is_a_premultiplied_ring()
    {
        using var files = Install.Open();
        var materials = new MaterialCache(files);
        var mat = materials.Items[materials.Get(WalkTarget.Material)];
        var image = mat.Image;
        Assert.NotNull(image);
        Assert.Equal(64, image!.Width);
        Assert.Equal(64, image.Height);

        float Alpha(int x, int y) => image.Rgba[(y * image.Width + x) * 4 + 3];
        // Hollow in the middle, brightest about two thirds of the way out, dark at the rim.
        Assert.Equal(0f, Alpha(32, 32));
        Assert.True(Alpha(32, 12) > 150f, $"the ring is faint at its peak ({Alpha(32, 12)})");
        Assert.True(Alpha(32, 0) < 40f, $"the ring runs into its own edge ({Alpha(32, 0)})");

        // Premultiplied: every texel's colour is its alpha times one constant tint.
        var ratios = new List<float>();
        for (int i = 0; i < image.Width * image.Height; i++)
        {
            float a = image.Rgba[i * 4 + 3];
            if (a > 40f) ratios.Add(image.Rgba[i * 4] / a);
        }
        Assert.True(ratios.Count > 500);
        float mean = ratios.Average();
        Assert.True(ratios.All(r => MathF.Abs(r - mean) < 0.05f),
                    "the red channel is not a constant multiple of the alpha, so it is not premultiplied");
    }
}
