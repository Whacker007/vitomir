// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The portraits' own lights**: the two lights the heads' draw writes — key (255,
/// 226, 173) at 500, fill (191, 226, 255) at 200, point lights with no fall-off — lighting a head's
/// vertex the renderer's way (colour × cosine × I / 392, no base, scaled to fit). Pins
/// that a face square to the key comes out at the key's hue scaled to 255, that the fill alone
/// gives its colour × 200/392, that distance changes nothing, and that a face turned from both is black.
/// </summary>
public class HeadLightingTests
{
    static (uint R, uint G, uint B) Bytes(uint c) => (c & 0xff, (c >> 8) & 0xff, (c >> 16) & 0xff);

    [Fact]
    public void A_face_square_to_the_key_is_the_keys_hue_at_full()
    {
        var key = HeadLighting.Lights[0];
        Assert.Equal(HeadLighting.KeyIntensity, key.Now);
        Assert.Equal(0f, key.Falloff);
        // A vertex at the origin facing the key, and turned away from the fill (whose light is behind it).
        var n = Vector3.Normalize(key.Position);
        Assert.True(Vector3.Dot(HeadLighting.Lights[1].Position, n) < 0f);
        var (r, g, b) = Bytes(HeadLighting.Colour(Vector3.Zero, n));
        // 255 × 500/392 is past white: scaled so red is 255, the hue kept (226/255, 173/255).
        Assert.Equal(255u, r);
        Assert.InRange(g, 225u, 227u);
        Assert.InRange(b, 172u, 174u);
    }

    [Fact]
    public void The_fill_alone_is_its_colour_times_200_over_392_at_any_distance()
    {
        var fill = HeadLighting.Lights[1];
        var n = Vector3.Normalize(fill.Position);
        Assert.True(Vector3.Dot(HeadLighting.Lights[0].Position, n) < 0f);
        foreach (float away in new[] { 0f, 30f })
        {
            var p = -n * away;                               // further from the light along its own line
            var (r, g, b) = Bytes(HeadLighting.Colour(p, n));
            Assert.InRange(r, 96u, 98u);                    // 191 × 200/392 = 97.4
            Assert.InRange(g, 114u, 116u);                  // 226 × 200/392 = 115.3
            Assert.InRange(b, 129u, 131u);                  // 255 × 200/392 = 130.1
        }
    }

    [Fact]
    public void A_face_turned_from_both_lights_is_black()
    {
        var n = -Vector3.Normalize(HeadLighting.Lights[0].Position + HeadLighting.Lights[1].Position);
        Assert.True(Vector3.Dot(HeadLighting.Lights[0].Position, n) < 0f && Vector3.Dot(HeadLighting.Lights[1].Position, n) < 0f);
        Assert.Equal(0xff000000u, HeadLighting.Colour(Vector3.Zero, n));
    }
}
