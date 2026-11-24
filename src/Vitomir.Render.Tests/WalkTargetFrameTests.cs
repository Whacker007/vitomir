// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Render.Tests;

/// <summary>
/// The click-to-move ring, drawn. <see cref="WalkTarget"/> carries the rules; these check that
/// what reaches the screen is a ring that shrinks and fades, and that it is additive — which is
/// what mode 13 (<c>Glow</c>) means and the one thing a still of a single frame cannot show.
/// </summary>
[Collection("headless")]
public class WalkTargetFrameTests(Headless h)
{
    const int W = 640, H = 360;

    /// <summary>
    /// Renders P1_HAVN with a ring on the floor in front of the camera, <paramref name="age"/> seconds old; with
    /// <paramref name="cleared"/> the same set-up (the marker made, the ring set) and the ring cleared before the picture.
    /// </summary>
    byte[] Frame(float age, string? save = null, bool cleared = false)
    {
        Location? place = null;
        var pixels = h.Frame("P1_HAVN", W, H,
            prepare: p => place = p,
            settings: f =>
            {
                f.Marker = f.Drawn.NewMarker(place!.Materials.Get(WalkTarget.Material));
                // **On the floor a little way ahead**, which is where a click marker goes: straight
                // down from a point in front of the camera. It comes with that surface's own
                // normal, so this exercises the tilt as well as the draw.
                var cam = place.Camera();
                var hit = Pick.Nearest(place.Level, cam.Position + cam.Forward * 260f, -Vector3.UnitZ);
                Assert.NotNull(hit);
                f.Sim.WalkTarget.Set(hit!.Value.Point, Pick.NormalAt(place.Level, hit.Value));
                Assert.True(f.Sim.WalkTarget.Active, "nothing to draw: the camera's ray found no plane");
                if (age > 0f) f.Sim.WalkTarget.Advance(age);
                if (cleared) f.Sim.WalkTarget.Clear();
            });
        if (save is not null) Png.Write(save, W, H, pixels);
        return pixels;
    }

    static long Light(byte[] a) { long s = 0; foreach (var b in a) s += b; return s; }

    /// <summary>
    /// A fresh ring adds light to the frame, and an old one adds less: the fade is the vertex
    /// colour going to black under an additive blend, so brightness is the thing to measure.
    /// </summary>
    [Fact]
    public void The_ring_lights_the_floor_and_dims_as_it_goes()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        var none = h.Frame("P1_HAVN", W, H);
        // `VTMR_RING_PNG` writes one file per age, so the fade can be looked at and not only
        // measured — the standing rule from early on.
        string? save = Environment.GetEnvironmentVariable("VTMR_RING_PNG");
        var fresh = Frame(0f, save);
        var old = Frame(0.8f, save is null ? null : Path.ChangeExtension(save, null) + ".late.png");

        long dark = Light(none), bright = Light(fresh), fading = Light(old);
        Assert.True(bright > dark, $"the ring added no light at all ({dark} -> {bright})");
        Assert.True(fading < bright, $"the ring did not dim ({bright} -> {fading})");
        Assert.True(fading > dark, $"the ring vanished four fifths of the way through ({dark} -> {fading})");
    }

    /// <summary>
    /// Once its second is up it adds nothing at all.
    ///
    /// Measured against the device's **own** noise rather than byte for byte: this container's
    /// software Vulkan does not draw the first frame of a device quite like the rest (see
    /// `FrameTests.A_level_draws_the_same_frame_twice`), so two renders of the same view can differ
    /// by a handful of levels for reasons that have nothing to do with the ring. The baseline is
    /// two ringless frames, and the spent ring has to be no further apart than they are. The ringless frames are made the
    /// same way as the spent one (the marker's material loaded, the ring set on the floor) with the ring cleared, so the
    /// only difference is the ring: a plain frame without the marker can differ by a few levels where the material's
    /// texture lands, depending on what earlier tests in the run left loaded on the device (and so on their order).
    /// </summary>
    [Fact]
    public void When_its_second_is_up_it_is_gone()
    {
        Assert.SkipWhen(h.Skip is not null, h.Skip ?? "");
        long a = Light(Frame(0f, cleared: true)), b = Light(Frame(0f, cleared: true));
        long spent = Light(Frame(WalkTarget.Lifetime));
        long noise = Math.Abs(a - b);
        Assert.True(Math.Abs(spent - b) <= noise,
                    $"a spent ring still added {spent - b} against a device noise floor of {noise}");
    }
}
