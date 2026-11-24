// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The camera to watch from (<see cref="OrbitCamera"/>): held, the look swings it round and tilts it; let go, it stays as a
/// turn from the driving camera's (so it stays on his left as he turns); the wheel zooms within its range; a double tap eases it back to the default;
/// the tilt within its limits; no roll; its aim on the player; kept out of walls; first person (not allowed) does nothing.
/// </summary>
public sealed class OrbitCameraTests
{
    const float Frame = 1f / 120f;
    static readonly CameraSettings Camera = new();

    static CollisionWorld Open() => new TestLevel().Floor(-20, -20, 20, 20).Build().Collision;

    [Fact]
    public void Held_the_look_swings_and_tilts_it_let_go_it_stays_and_a_double_tap_puts_it_back()
    {
        var orbit = new OrbitCamera();
        Assert.True(orbit.IsDefault);
        double now = 10;
        // Pressed and held, the mouse moving: the look swung to the left (positive; the camera to his right) and tilted down (negative).
        orbit.Step(true, true, true, new Vector2(0.4f, -0.2f), 0f, 0f, now, Frame);
        for (int i = 0; i < 60; i++) orbit.Step(true, true, false, new Vector2(0.02f, -0.005f), 0f, 0f, now += Frame, Frame);
        Assert.InRange(orbit.Yaw, 1.59f, 1.61f);
        Assert.InRange(orbit.Pitch, -0.51f, -0.49f);
        Assert.True(orbit.Shoulder < 0.01f, "its aim come onto the player");
        Assert.False(orbit.IsDefault);
        // Let go: it stays (the look goes to the player's turn then, not here).
        for (int i = 0; i < 120; i++) orbit.Step(true, false, false, Vector2.Zero, 0f, 0f, now += Frame, Frame);
        Assert.InRange(orbit.Yaw, 1.59f, 1.61f);
        Assert.InRange(orbit.Pitch, -0.51f, -0.49f);
        // A press long after the last: no reset; a second within the double tap: eased back to the default.
        now += 1.0;
        orbit.Step(true, true, true, Vector2.Zero, 0f, 0f, now, Frame);
        orbit.Step(true, false, false, Vector2.Zero, 0f, 0f, now += Frame, Frame);
        Assert.False(orbit.Returning);
        orbit.Step(true, true, true, Vector2.Zero, 0f, 0f, now += 0.15, Frame);
        Assert.True(orbit.Returning);
        float yawWas = orbit.Yaw;
        for (int i = 0; i < 120 && !orbit.IsDefault; i++)
        {
            orbit.Step(true, false, false, new Vector2(0.05f, 0f), 0f, 0f, now += Frame, Frame);
            Assert.True(MathF.Abs(orbit.Yaw) <= MathF.Abs(yawWas) + 1e-6f, "easing back, not swung further");
            yawWas = orbit.Yaw;
        }
        Assert.True(orbit.IsDefault);
    }

    [Fact]
    public void The_tilt_and_the_zoom_keep_within_their_limits_and_first_person_does_nothing()
    {
        var s = new OrbitCameraSettings();
        var orbit = new OrbitCamera(s);
        double now = 0;
        orbit.Step(true, true, true, new Vector2(0f, -5f), 0f, 0f, now, Frame);
        Assert.InRange(orbit.Pitch, s.LowestPitchDegrees * DetMath.DegToRad - 1e-4f, s.LowestPitchDegrees * DetMath.DegToRad + 1e-4f);
        orbit.Step(true, true, false, new Vector2(0f, 10f), 0f, 0f, now += Frame, Frame);
        Assert.InRange(orbit.Pitch, s.HighestPitchDegrees * DetMath.DegToRad - 1e-4f, s.HighestPitchDegrees * DetMath.DegToRad + 1e-4f);
        // Measured from the driving pitch: with the driving camera looking down 0.3, the highest tilt is the same total.
        orbit.Step(true, true, false, new Vector2(0f, 10f), 0f, -0.3f, now += Frame, Frame);
        Assert.InRange(-0.3f + orbit.Pitch, s.HighestPitchDegrees * DetMath.DegToRad - 1e-4f, s.HighestPitchDegrees * DetMath.DegToRad + 1e-4f);
        // The wheel, held or not: nearer up to the nearest, farther to the farthest.
        orbit.Step(true, false, false, Vector2.Zero, 2f, 0f, now += Frame, Frame);
        Assert.InRange(orbit.Boom, Camera.Boom - 2f * s.ZoomStepMetres - 1e-4f, Camera.Boom - 2f * s.ZoomStepMetres + 1e-4f);
        orbit.Step(true, false, false, Vector2.Zero, 100f, 0f, now += Frame, Frame);
        Assert.Equal(s.NearestMetres, orbit.Boom);
        orbit.Step(true, false, false, Vector2.Zero, -100f, 0f, now += Frame, Frame);
        Assert.Equal(s.FarthestMetres, orbit.Boom);
        // Not allowed (first person, the pointer freed): the key, the look and the wheel do nothing.
        var still = new OrbitCamera(s);
        still.Step(false, true, true, new Vector2(1f, 1f), 3f, 0f, 0, Frame);
        still.Step(false, true, true, new Vector2(1f, 1f), 3f, 0f, 0.1, Frame);
        Assert.True(still.IsDefault);
        Assert.False(still.Held);
    }

    [Fact]
    public void Its_view_is_level_aimed_at_the_player_stays_on_his_left_as_he_turns_and_out_of_walls()
    {
        var level = Open();
        var orbit = new OrbitCamera();
        orbit.Put(-DetMath.HalfPi, -0.4f);   // the look turned a quarter to the right: the camera on his left
        var head = new Vector3(0f, 0f, 1.6f);
        foreach (float heading in new[] { 0f, 1f, 2.5f, -2f })
        {
            var (s, c) = DetMath.SinCos(heading);
            var offset = new Vector3(s, -c, 0f) * Camera.ShoulderSide + new Vector3(0f, 0f, Camera.ShoulderUp);
            var v = orbit.View(level, Camera, head, heading, 0f, offset, Camera.Boom, 75f);
            // Level: a view is a yaw and a pitch, no roll; aimed at the player's head (the shoulder's offset gone).
            var toHead = head - v.Eye;
            var f = v.Forward;
            float miss = (toHead - f * Vector3.Dot(toHead, f)).Length();
            Assert.InRange(miss, 0f, 0.01f);
            Assert.InRange(toHead.Length(), Camera.Boom - 0.01f, Camera.Boom + 0.01f);
            // On his left: the camera to the left of where he heads, as he turns.
            var left = new Vector3(-s, c, 0f);
            Assert.True(Vector3.Dot(v.Eye - head, left) > 1.5f, $"on his left heading {heading}");
            Assert.True(v.Eye.Z > head.Z, "tilted down onto him from above");
        }
        // A wall between: kept off it, never through it.
        var walled = new TestLevel().Floor(-20, -20, 20, 20).Box(new Vector3(-5f, 1.2f, 0f), new Vector3(5f, 1.5f, 4f)).Build().Collision;
        var w = new OrbitCamera();
        w.Put(-DetMath.HalfPi, 0f);
        var into = w.View(walled, Camera, head, 0f, 0f, Vector3.Zero, Camera.Boom, 75f);
        Assert.InRange(into.Eye.Y, 0.5f, 1.2f - Camera.ProbeRadius + 0.06f);
    }
}
