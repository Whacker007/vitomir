// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The geometry severance's angle band reads (the rules package: <i>"90° as the direct impact angle
/// perpendicular to a struck surface, 0° as a tangential skim"</i>): a blade swept against a hit volume meets it with the
/// <b>surface's own normal</b> — on the capsule's side its radial direction (square to its axis), on an end cap the cap's —
/// so a striking point's speed along that normal over its whole speed (<c>ContactMade.ImpactSpeed / PointSpeed</c>) is the sine of
/// the angle the point's path makes with the surface.
/// </summary>
public sealed class ContactAngleTests
{
    static readonly OrientedCapsule Upright = new(new Vector3(0, 0, 0.8f), new Vector3(0, 0, 1.4f), 0.15f);

    /// <summary>The sine of the angle a point moving along <paramref name="v"/> meets a surface whose outward normal is <paramref name="n"/> (as contact measures it).</summary>
    static float Sine(Vector3 v, Vector3 n) => MathF.Max(0f, -Vector3.Dot(v, n)) / v.Length();

    [Fact]
    public void A_blade_swept_into_the_side_of_a_volume_meets_its_radial_normal_and_the_sine_is_the_paths_angle_to_the_surface()
    {
        // A horizontal blade (along Y) swept toward the upright capsule from +X at a path tilted down by various angles to the
        // horizontal: whatever the tilt, the normal at the side is radial (+X: square to the axis, Z 0).
        foreach (float tilt in new[] { 0f, 20f, 45f, 70f })
        {
            float r = tilt * DetMath.DegToRad;
            var v = new Vector3(-DetMath.Cos(r), 0f, -DetMath.Sin(r)) * 0.6f;   // the step's move
            // It starts 0.09 m off the side, as high above the middle of the side as its path drops on the way there.
            var p0 = new Vector3(0.25f, -0.4f, 1.1f + 0.09f * DetMath.Sin(r) / DetMath.Cos(r));
            var q0 = p0 + new Vector3(0, 0.8f, 0);
            Assert.True(ContactMath.Sweep(p0, q0, p0 + v, q0 + v, 0.01f, Upright, out var hit), $"tilt {tilt}");
            Assert.Equal(1f, hit.Normal.X, 3);                                   // radial: out along +X
            Assert.Equal(0f, hit.Normal.Z, 3);                                   // square to the axis
            // The path meets the side at (90 − tilt)°: square on with no tilt, a skim as the path turns along the axis.
            Assert.Equal(DetMath.Cos(r), Sine(v, hit.Normal), 3);
        }
    }

    [Fact]
    public void On_an_end_cap_the_normal_is_the_caps_and_a_path_along_the_surface_is_a_skim()
    {
        // From above, straight down onto the top cap: the normal is up, the blow square on.
        var p0 = new Vector3(-0.4f, 0f, 1.9f);
        var q0 = new Vector3(0.4f, 0f, 1.9f);
        var down = new Vector3(0, 0, -0.5f);
        Assert.True(ContactMath.Sweep(p0, q0, p0 + down, q0 + down, 0.01f, Upright, out var top));
        Assert.Equal(1f, top.Normal.Z, 3);
        Assert.Equal(1f, Sine(down, top.Normal), 3);
        // A blade already touching the side and moving along the axis (down the arm): no speed into the surface, sine 0 — glancing.
        var p1 = new Vector3(0.16f, -0.4f, 1.1f);
        var q1 = p1 + new Vector3(0, 0.8f, 0);
        Assert.True(ContactMath.Sweep(p1, q1, p1 + down, q1 + down, 0.01f, Upright, out var side));
        Assert.Equal(1f, side.Normal.X, 3);
        Assert.Equal(0f, Sine(down, side.Normal), 3);
    }
}
