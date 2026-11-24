// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Whether a thing that arrives after the level has loaded actually reaches the screen, and whether
// one that leaves the world actually leaves it. `Vitomir.Engine.Tests/DrawableTests` proves the
// geometry is in the mesh; these two prove it comes out of the GPU.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Render.Tests;

[Collection("headless")]
public class ArrivalFrameTests(Headless h)
{
    void Ready() => Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");

    /// <summary>
    /// A thing spawned in front of the camera between two frames shows up in the second one, and
    /// taking it out of the world again puts the picture back exactly as it was.
    ///
    /// The second half is the part that was quietly wrong before: the mesh keeps a thing's geometry
    /// after it leaves the world, because taking it out would shift every index after it, so
    /// something had to stop it being drawn. Nothing did. A sword picked up off the floor went on
    /// lying there.
    ///
    /// The first frame is thrown away: on this container's software Vulkan it differs from every
    /// frame after it (see <see cref="FrameTests.A_level_draws_the_same_frame_twice"/>), and this
    /// test is about frames two, three and four.
    /// </summary>
    [Fact]
    public void A_thing_that_arrives_is_drawn_and_one_that_leaves_is_not()
    {
        Ready();
        var place = Location.Load(h.Files!, "P1_HAVN");
        using var frame = new FrameRenderer(h.Gpu!, place);
        var camera = place.Camera();

        _ = Headless.Shoot(frame, 320, 180, camera);
        var before = Headless.Shoot(frame, 320, 180, camera);

        // A copy of something this level already has, so its model is known good, put where the
        // camera cannot miss it.
        string template = place.Drawn.First(t => t.Type == ThingType.Prop && t.Model.Length > 0).Template;
        var at = camera.Position + camera.Forward * 50f;
        var made = place.Place(template, at, Vector3.Zero, place.Player?.Thing.Sector ?? 0);
        Assert.NotNull(made);
        Assert.True(made.InWorld);

        var after = Headless.Shoot(frame, 320, 180, camera);
        Assert.True(Headless.Differing(before, after) > 0.02f,
                    $"{template} arrived in front of the camera and changed "
                    + $"{Headless.Differing(before, after):P2} of the picture");

        // Out of the world: the geometry stays in the mesh, and nothing draws it.
        Assert.True(place.Withdraw(made));
        var gone = Headless.Shoot(frame, 320, 180, camera);
        Assert.Equal(before, gone);
    }
}
