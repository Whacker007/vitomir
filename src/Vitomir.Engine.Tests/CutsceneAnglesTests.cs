// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **A cutscene's frame writes where it looks into the camera's own pitch and yaw** (the cutscene's look-at writes
/// them). In 1.1's new game
/// through `3_1_Collapse` the camera swings from `pitch -26.7; yaw -134.9`
/// to `-5.7; -46.4` and `3_3_C&A` opens at `14; 150`, where Vitomir's once stayed on the game camera's 330
/// and 270. Vitomir's run of the same new game gives `-26.6; -136.4` → `-5.8; -46.7` and
/// `14; 150`. The angles are signed, not wrapped, and 0 faces +Y as the game camera's yaw does.
/// </summary>
public class CutsceneAnglesTests
{
    static (float Pitch, float Yaw) Looking(Vector3 along) =>
        WorldCamera.PitchYawOf(WorldCamera.LookingAt(Vector3.Zero, along, Matrix4x4.Identity));

    [Fact]
    public void A_look_comes_to_signed_angles_with_zero_along_plus_y()
    {
        Assert.Equal((0f, 0f), Looking(Vector3.UnitY));
        Near((0f, 90f), Looking(-Vector3.UnitX));              // the game camera's own sense of turn
        Near((0f, -90f), Looking(Vector3.UnitX));
        Near((-45f, 180f), Looking(new Vector3(0, -1, -1)));   // down is negative, not 315
        Near((30f, -135f), Looking(new Vector3(1, -1, MathF.Sqrt(2f) * MathF.Tan(MathF.PI / 6f))));
        Assert.Equal((90f, 0f), Looking(Vector3.UnitZ));       // straight up: no yaw to speak of
        Assert.Equal((-90f, 0f), Looking(-Vector3.UnitZ));
    }

    /// <summary>And they are the game camera's own angles read back, for any look without roll.</summary>
    [Theory]
    [InlineData(-30f, 270f)]
    [InlineData(14f, 150f)]
    [InlineData(-26.7f, -134.9f)]
    public void They_are_the_angles_the_matrix_was_made_from(float pitch, float yaw)
    {
        var (p, y) = WorldCamera.PitchYawOf(WorldCamera.FromAngles(new Vector3(pitch, 0f, yaw), Vector3.Zero));
        Assert.Equal(pitch, p, 0.01f);
        Assert.Equal(WorldCamera.Wrap(yaw), WorldCamera.Wrap(y), 0.01f);
    }

    [Fact]
    public void A_cutscene_shot_puts_its_look_into_the_cameras_angles()
    {
        var place = Somewhere.Value;
        Assert.SkipWhen(place is null, "no game install");
        var christof = Someone(new Vector3(0, 0, 0), place!, guid: 1);
        var anezka = Someone(new Vector3(200, 100, 0), place!, guid: 2);
        var camera = new WorldCamera { HeadOf = null };
        camera.Start(christof);
        camera.Tick(1f / 60f, 16);
        Assert.Equal(330f, camera.Angles.X, 0.5f);             // the game camera: −30°, wrapped

        camera.SetupCutscene(christof, anezka);
        camera.SetShot((int)CameraShot.AngularB, 2, 2, 0, 0);
        camera.Tick(0.05f, 66);
        var (pitch, yaw) = WorldCamera.PitchYawOf(camera.Frame);
        Assert.Equal(pitch, camera.Angles.X);
        Assert.Equal(yaw, camera.Angles.Z);
        Assert.InRange(camera.Angles.X, -90f, 90f);            // signed, as 1.1's are
    }

    static void Near((float, float) want, (float, float) got)
    {
        Assert.Equal(want.Item1, got.Item1, 0.01f);
        Assert.Equal(want.Item2, got.Item2, 0.01f);
    }

    static WorldThing Someone(Vector3 at, Location place, int guid)
    {
        var actor = new Actor();
        actor.Health = actor.MaxHealth = 100;
        return new WorldThing
        {
            Index = 0, Guid = guid, Scene = NsdThing.Placed("test", at, Vector3.Zero, 0),
            Template = "test", Type = ThingType.Player, Actor = actor, Position = at,
            Orientation = Vector3.Zero, Collide = CollideShape.Cylinder, Height = 100f, Place = place,
        };
    }

    /// <summary>A place for the subjects to be in, as <c>CameraTests</c> borrows one: the haven, geometry only.</summary>
    static readonly Lazy<Location?> Somewhere = new(() =>
    {
        var dir = GameFiles.Locate();
        return dir is null ? null : Location.Load(new GameFiles(dir), "P1_HAVN", things: false, sky: false);
    });
}
