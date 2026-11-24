// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The camera's two casts into the world (`CameraSweep`), as in 1.1:
/// The game camera's fat ray (radius 10, surfaces, props flagged
/// <c>THING_FLAG_CAMERABLOCK</c> and regions that collide with cameras) and the cutscene eye's thin
/// one (surfaces only, the eye put on the wall). The shape tests run with no install;
/// the rest use the convent's courtyard, whose cloister has four camera-clip regions over its
/// windows and a camera-blocking tracker.
/// </summary>
public class CameraSweepTests
{
    static readonly Lazy<Location?> Courtyard = new(() =>
    {
        var dir = GameFiles.Locate();
        if (dir is null) return null;
        return Location.Load(new GameFiles(dir), "P1_CTDY", things: true, sky: false, scene: "CTDY_4_1");
    });

    static Location Place()
    {
        Assert.SkipWhen(Courtyard.Value is null, "no game install");
        return Courtyard.Value!;
    }

    static float? Fat(Location place, Vector3 from, Vector3 dir, float length) =>
        CameraSweep.GameCamera(place, CameraSweep.StartSector(place, from, -1), from, Vector3.Normalize(dir), length);

    // ------------------------------------------------------------------------ the shapes

    // The camera's shape tests are the sweep's own (the original's three shape tests are one
    // copy, `WorldSweep`); these read them the way the camera's copy answered, a distance or null.
    static float? RaySphere(Vector3 o, Vector3 d, float length, Vector3 centre, float radius) =>
        WorldSweep.MeetsSphere(o, d, length, 0f, centre, radius, out float t) ? t : null;

    static float? RayCylinder(Vector3 o, Vector3 d, float length, Vector3 foot, float radius, float height) =>
        WorldSweep.MeetsCylinder(o, d, length, foot, radius, height, out float t) ? t : null;

    static float? RayBox(Vector3 o, Vector3 d, float length, Matrix4x4 rotation, Vector3 at, Vector3 min, Vector3 max) =>
        WorldSweep.MeetsBox(o, d, length, rotation, at, min, max, out float t) ? t : null;

    [Fact]
    public void A_ray_enters_a_sphere_where_its_surface_is()
    {
        var t = RaySphere(Vector3.Zero, Vector3.UnitX, 100f, new Vector3(50, 0, 0), 10f);
        Assert.Equal(40f, t!.Value, 3);
        Assert.Null(RaySphere(Vector3.Zero, Vector3.UnitX, 100f, new Vector3(50, 20, 0), 10f));
    }

    /// <summary>
    /// **A sphere met past the end of the ray answers 0**: the shape test writes 0 whenever the entry
    /// point is outside 0..length but the centre's closest approach is within the length plus the
    /// radius. A camera-blocking sphere just out of reach would put the camera on the target.
    /// </summary>
    [Fact]
    public void A_sphere_just_beyond_the_ray_s_end_answers_zero()
    {
        // Closest approach 109 along and 8 across: the surface is met at 103, past the end at 100.
        var t = RaySphere(Vector3.Zero, Vector3.UnitX, 100f, new Vector3(109, 8, 0), 10f);
        Assert.Equal(0f, t);
        Assert.Equal(95f, RaySphere(Vector3.Zero, Vector3.UnitX, 100f, new Vector3(105, 0, 0), 10f));
    }

    [Fact]
    public void A_ray_meets_a_standing_cylinder_at_its_side_and_not_over_its_top()
    {
        var foot = new Vector3(50, 0, 0);
        Assert.Equal(40f, RayCylinder(new Vector3(0, 0, 20), Vector3.UnitX, 100f, foot, 10f, 64f)!.Value, 3);
        Assert.Null(RayCylinder(new Vector3(0, 0, 80), Vector3.UnitX, 100f, foot, 10f, 64f));
    }

    [Fact]
    public void A_ray_is_clipped_by_a_turned_box()
    {
        // A box ±10 on its own axes, turned a quarter about Z: its faces are still ±10 either way.
        var turned = Matrix4x4.CreateRotationZ(MathF.PI / 2f);
        var t = RayBox(Vector3.Zero, Vector3.UnitX, 100f, turned, new Vector3(50, 0, 0),
                                   new Vector3(-10, -10, -10), new Vector3(10, 10, 10));
        Assert.Equal(40f, t!.Value, 3);
        Assert.Null(RayBox(Vector3.Zero, Vector3.UnitX, 30f, turned, new Vector3(50, 0, 0),
                                       new Vector3(-10, -10, -10), new Vector3(10, 10, 10)));
    }

    // ------------------------------------------------------------------ the game camera

    /// <summary>
    /// **The fat ray's centre stops a radius short of a wall it meets head on**: the sphere touches
    /// the plane where its centre is 10 from it, and that is the distance the camera
    /// takes — no further clearance added.
    /// </summary>
    [Fact]
    public void The_fat_ray_stops_a_radius_short_of_a_wall_it_meets_head_on()
    {
        var place = Place();
        // The cloister's east wall is at x = 1152, 52 ahead; the camera-clip region over it starts
        // on the same plane, and the sphere's surface reaches the wall first.
        Assert.Equal(42f, Fat(place, new(1100, -112, 200), Vector3.UnitX, 300f)!.Value, 2);
    }

    /// <summary>
    /// **A wall's edge within the radius counts** — measured flat in the wall's own plane from the
    /// edge's line, so a thin frame beside the ray's path stops the camera even where the
    /// sphere itself would pass it. Here a two-unit-wide jamb eight units to the side of the ray
    /// stops it at 10.9, though the wall straight ahead is 56 away.
    /// </summary>
    [Fact]
    public void A_frame_eight_units_to_the_side_stops_the_camera()
    {
        var place = Place();
        float fat = Fat(place, new(488, 200, 200), Vector3.UnitY, 300f)!.Value;
        Assert.Equal(10.94f, fat, 1);
        var eye = CameraSweep.CutsceneEye(place, CameraSweep.StartSector(place, new(488, 200, 200), -1),
                                          new(488, 200, 200), new(488, 500, 200));
        Assert.Equal(new Vector3(488, 256, 200), eye);
    }

    /// <summary>
    /// **A region that collides with cameras stops the game camera** (region flag 2): the
    /// cloister's windows have one over them, 32 deep, so the camera cannot back out through the
    /// window the thin cutscene ray sees straight through to the far wall.
    /// </summary>
    [Fact]
    public void A_camera_clip_region_keeps_the_camera_out_of_a_window()
    {
        var place = Place();
        var from = new Vector3(600, 200, 210);
        Assert.Equal(88f, Fat(place, from, Vector3.UnitY, 400f)!.Value, 2);     // the region at y = 288
        var eye = CameraSweep.CutsceneEye(place, CameraSweep.StartSector(place, from, -1), from, from + Vector3.UnitY * 400f);
        Assert.Equal(184f, Vector3.Distance(from, eye!.Value), 1);            // the cutscene sees through
    }

    /// <summary>
    /// **A prop flagged CAMERABLOCK stops it and the same prop unflagged does not** (a
    /// thing hit counts only with its flag 0x4000). The courtyard's tracker is a thin box
    /// with the flag.
    /// </summary>
    [Fact]
    public void A_camera_blocking_prop_stops_the_camera_and_only_while_flagged()
    {
        var place = Place();
        var tracker = place.Things.First(t => t.Template == "tracker_thing" && t.Has(WorldThing.Flag.CameraBlock));
        var from = tracker.Position + new Vector3(0, 132, 0);
        Assert.Equal(128f, Fat(place, from, -Vector3.UnitY, 300f)!.Value, 2);
        tracker.Flags &= ~WorldThing.Flag.CameraBlock;
        try { Assert.Null(Fat(place, from, -Vector3.UnitY, 300f)); }
        finally { tracker.Flags |= WorldThing.Flag.CameraBlock; }
    }

    /// <summary>
    /// **A camera held against a wall does not drift at any frame rate** with the engine's own
    /// sweep either: it eases out before it casts, the cast finds the same
    /// wall at the same distance, and the eye is drawn there every frame.
    /// </summary>
    [Fact]
    public void The_game_camera_rests_where_the_sweep_puts_it()
    {
        var place = Place();
        var who = new WorldThing
        {
            Index = 0, Guid = 9001, Scene = NsdThing.Placed("test", new(653, 22, 0), Vector3.Zero, 0),
            Template = "test", Type = ThingType.Prop, Position = new(653, 22, 0), Place = place,
            Sector = CameraSweep.StartSector(place, new(653, 22, 64), -1),
        };
        var camera = new WorldCamera { HeadOf = null };
        camera.Obstruction = (from, towards, want) =>
            CameraSweep.GameCamera(place, CameraSweep.StartSector(place, from, who.Sector), from, towards, want);
        camera.Start(who);
        camera.Angles = new Vector3(-26.75f + 360f, 0f, 209.05f);
        int ms = 0;
        for (int i = 0; i < 60; i++) camera.Tick(1f / 300f, ms += 3);
        float min = camera.Distance, max = camera.Distance;
        for (int i = 0; i < 600; i++)
        {
            camera.Tick(1f / 300f, ms += 3);
            min = MathF.Min(min, camera.Distance);
            max = MathF.Max(max, camera.Distance);
        }
        Assert.NotNull(camera.PulledTo);
        Assert.True(max - min < 0.01f, $"the camera swung {max - min:0.00} units standing still");
    }

    // --------------------------------------------------------------------- the cutscene

    /// <summary>
    /// **The cutscene eye goes onto the wall** (the hit point over both copies of the eye),
    /// every frame, and the frame looks from there.
    /// </summary>
    [Fact]
    public void A_cutscene_eye_behind_a_wall_is_put_on_the_wall()
    {
        var wall = new Vector3(3, 4, 5);
        var camera = new WorldCamera { HeadOf = null, SightLine = (_, _, _) => wall };
        var subject = new WorldThing
        {
            Index = 0, Guid = 9002, Scene = NsdThing.Placed("test", new(100, 0, 0), Vector3.Zero, 0),
            Template = "test", Type = ThingType.Prop, Position = new(100, 0, 0), Collide = CollideShape.Sphere, Radius = 32,
        };
        Assert.SkipWhen(Courtyard.Value is null, "no game install");   // "in the world" means in a place
        subject.Place = Courtyard.Value;
        camera.Start(subject);
        camera.SetupCutscene(subject, null);
        camera.SetShot((int)CameraShot.ProfileA, 2, 2, 0, 0);
        Assert.Equal(wall, camera.Eye);
        Assert.Equal(wall, camera.Frame.Translation);
    }

    /// <summary>
    /// **A cutscene sets the render view's bit 0 and the release clears it**:
    /// every model at full detail however far away. Vitomir draws every model whole anyway.
    /// </summary>
    [Fact]
    public void A_cutscene_draws_every_model_at_full_detail_until_the_release()
    {
        var camera = new WorldCamera { HeadOf = null };
        Assert.False(camera.FullDetail);
        camera.EnterCutscene();
        Assert.True(camera.FullDetail);
        camera.Release(null);
        Assert.False(camera.FullDetail);
    }
}
