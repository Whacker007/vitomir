// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The first-person field of view across (the slider in vertical degrees or in horizontal
/// ones): the vertical field drawn worked out from the field across and the picture's shape, at 16:9 and at 32:9 (the
/// same field across on both, the wider one shorter up the picture), kept within the pick field; and the freed
/// pointer's place put into the pick field under it is the ray under the cursor, off the centre, on a wide picture.
/// </summary>
public sealed class FieldOfViewAxisTests
{
    const float Wide = 16f / 9f, Wider = 32f / 9f;

    /// <summary>The horizontal angle (degrees) of the ray through the picture's right edge, half way down.</summary>
    static float HalfAcross(float vertical, float aspect)
    {
        var view = new ViewPoint(Vector3.Zero, 0f, 0f, vertical);
        var d = PointerPick.Direction(view, new Vector2(1f, 0.5f), aspect);
        return MathF.Abs(DetMath.Atan2(d.Y, d.X)) * DetMath.RadToDeg;
    }

    [Fact]
    public void A_field_across_gives_the_vertical_field_of_the_pictures_shape_at_16_9_and_32_9()
    {
        // 2·atan(tan(h/2) / aspect).
        Assert.Equal(80f, FirstPersonView.VerticalOf(112.33f, Wide), 0.05f);           // the vertical default's field across at 16:9
        Assert.Equal(73.74f, FirstPersonView.VerticalOf(106.26f, Wide), 0.05f);        // 90° across at 4:3, carried to 16:9
        Assert.Equal(79.65f, FirstPersonView.VerticalOf(FirstPersonView.DefaultHorizontalField, Wide), 0.05f);
        Assert.Equal(45.27f, FirstPersonView.VerticalOf(112f, Wider), 0.05f);
        Assert.Equal(22.28f, FirstPersonView.VerticalOf(70f, Wider), 0.05f);
        // The same field across on both shapes: the right edge's ray is h/2 aside on each.
        foreach (float h in new[] { 70f, 90f, 112f, 125f })
            foreach (float aspect in new[] { Wide, Wider, 21f / 9f })
                Assert.Equal(h / 2f, HalfAcross(FirstPersonView.VerticalOf(h, aspect), aspect), 0.05f);
        // Kept within a sane range: never past the pick field (110), the slider's ends, the default for nothing sensible.
        Assert.Equal(FirstPersonView.MostDrawnField, FirstPersonView.VerticalOf(140f, 4f / 3f));
        Assert.Equal(110f, FirstPersonView.MostDrawnField);
        Assert.Equal(FirstPersonView.VerticalOf(140f, Wide), FirstPersonView.VerticalOf(400f, Wide));
        Assert.Equal(FirstPersonView.VerticalOf(70f, Wider), FirstPersonView.VerticalOf(10f, Wider));
        Assert.Equal(FirstPersonView.VerticalOf(112f, Wide), FirstPersonView.VerticalOf(float.NaN, Wide));
        Assert.Equal(FirstPersonView.VerticalOf(112f, Wide), FirstPersonView.VerticalOf(112f, 0f));     // an unknown shape is 16:9
        // Drawn: the vertical axis is the field itself, as before.
        Assert.Equal(80f, FirstPersonView.Drawn(FieldOfViewAxis.Vertical, 80f, Wider));
        Assert.Equal(FirstPersonView.VerticalOf(112f, Wider), FirstPersonView.Drawn(FieldOfViewAxis.Horizontal, 112f, Wider));
    }

    [Theory]
    [InlineData(112f, 16f / 9f)]
    [InlineData(112f, 32f / 9f)]
    [InlineData(140f, 21f / 9f)]
    [InlineData(70f, 16f / 9f)]
    public void Under_a_field_across_the_pointers_place_off_the_centre_put_into_the_pick_field_is_the_ray_under_the_cursor(float across, float aspect)
    {
        var settings = new CameraSettings();
        float drawnField = FirstPersonView.Drawn(FieldOfViewAxis.Horizontal, across, aspect);
        float scale = FirstPersonView.PickScale(drawnField, settings.FirstPersonPickField);
        Assert.InRange(scale, 0f, 1.0001f);
        var drawn = new ViewPoint(new Vector3(1, 2, 1.6f), 0.7f, -0.2f, drawnField);
        var picked = drawn with { FieldOfViewDegrees = settings.FirstPersonPickField };
        foreach (var place in new[] { new Vector2(0.9f, 0.2f), new Vector2(0.05f, 0.7f), new Vector2(1f, 1f), new Vector2(0.62f, 0.48f) })
        {
            var pick = FirstPersonView.ToPick(place, scale);
            Assert.True(PointerPick.OnPicture(pick));
            var a = PointerPick.Direction(drawn, place, aspect);
            var b = PointerPick.Direction(picked, pick, aspect);
            Assert.True(Vector3.Distance(a, b) < 1e-4f, $"{across}° across at {aspect:0.##}, place {place}: {a} against {b}");
            Assert.True(Vector2.Distance(FirstPersonView.FromPick(pick, scale), place) < 1e-5f);
        }
    }
}
