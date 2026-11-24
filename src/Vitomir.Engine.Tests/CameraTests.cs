// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The engine's camera: the game camera's numbers, the
/// cutscene shots, the first-person look, the edge turn and the screen effects. Everything here
/// runs with no level — the camera asks the world its two questions through delegates.
/// </summary>
public class CameraTests
{
    internal static WorldThing Someone(Vector3 at, float yaw = 0f, float height = 0f, ThingType type = ThingType.Player,
                              Location? place = null)
    {
        var actor = new Actor();
        actor.Health = actor.MaxHealth = 100;
        return new WorldThing
        {
            Index = 0, Guid = 1, Scene = NsdThing.Placed("test", at, Vector3.Zero, 0),
            Template = "test", Type = type, Actor = actor, Position = at,
            Orientation = new Vector3(0, 0, yaw), Collide = CollideShape.Cylinder, Height = height,
            Place = place ?? Somewhere.Value,
        };
    }

    /// <summary>
    /// A place for the test characters to be in. The cutscene asks whether its subjects are still
    /// in the world every frame, and "in the world" means in a place — so the tests borrow the
    /// haven, geometry only, once. Null with no install, and then only the framing tests skip.
    /// </summary>
    static readonly Lazy<Location?> Somewhere = new(() =>
    {
        var dir = GameFiles.Locate();
        if (dir is null) return null;
        var files = new GameFiles(dir);
        return Location.Load(files, "P1_HAVN", things: false, sky: false);
    });

    static void NeedsAPlace() => Assert.SkipWhen(Somewhere.Value is null, "no game install");

    /// <summary>
    /// The courtyard outside the convent, geometry only — a real place with real walls, for the two
    /// tests below that need the pull-in to meet something.
    /// </summary>
    static readonly Lazy<Location?> ConventDay = new(() =>
    {
        var dir = GameFiles.Locate();
        if (dir is null) return null;
        return Location.Load(new GameFiles(dir), "P1_CTDY", things: false, sky: false);
    });

    static WorldCamera Following(WorldThing who)
    {
        var camera = new WorldCamera { HeadOf = null };
        camera.Start(who);
        return camera;
    }

    static void Near(Vector3 want, Vector3 got, float within = 0.01f) =>
        Assert.True(Vector3.Distance(want, got) <= within, $"wanted {want}, got {got}");

    // ------------------------------------------------------------------------ the game camera

    /// <summary>
    /// **Where 1.1's camera sits.** Pitched down 30°, facing the way the character faces, 128 units
    /// above the character's origin, and 354.79 back — within a tenth of what the pitch-eased
    /// distance comes to at −30°, so a fresh camera does not visibly drift on its first frames.
    /// </summary>
    [Fact]
    public void The_game_camera_starts_thirty_degrees_down_and_354_back()
    {
        var christof = Someone(new Vector3(100, 200, 0), yaw: 0f);
        var camera = Following(christof);
        camera.Tick(1f / 60f, 16);

        Assert.Equal(CameraMode.Game, camera.Mode);
        Near(new Vector3(100, 200, 128), camera.Target);
        Assert.Equal(-30f, WorldCamera.Signed(camera.Angles.X), 3);
        // Facing +Y (yaw 0), looking down: the eye is behind (−Y) and above.
        var f = camera.Forward;
        Assert.Equal(0f, f.X, 3);
        Assert.Equal(MathF.Cos(MathF.PI / 6), f.Y, 3);
        Assert.Equal(-0.5f, f.Z, 3);
        Assert.Equal(WorldCamera.StartDistance, camera.Distance, 0);
        Assert.Equal(WorldCamera.StartDistance, camera.DistanceTarget, 0);
        Near(camera.Target - f * camera.Distance, camera.Eye, 0.05f);
    }

    /// <summary>
    /// The steeper it looks down the further back it sits: 512 at the −80° limit, 300 at −20°,
    /// on a quarter cosine between them — and the zoom scales that.
    /// </summary>
    [Fact]
    public void The_distance_follows_the_pitch_and_the_zoom()
    {
        var camera = Following(Someone(Vector3.Zero));
        // Hold the tilt key for a second: 180°/s, clamped at the −80° limit.
        camera.TurnPitch(-180f);
        camera.Tick(1f, 1000);
        Assert.Equal(-80f, WorldCamera.Signed(camera.Angles.X), 3);
        Assert.Equal(512f, camera.DistanceTarget, 2);

        camera.StopTurning();
        camera.TurnPitch(180f);
        camera.Tick(1f, 2000);
        Assert.Equal(-20f, WorldCamera.Signed(camera.Angles.X), 3);
        Assert.Equal(300f, camera.DistanceTarget, 2);

        // The zoom keys bring it in, never past a tenth, never out past 1.
        camera.Zoom(-5f);
        Assert.Equal(WorldCamera.ZoomMin, camera.ZoomScale);
        camera.Zoom(5f);
        Assert.Equal(WorldCamera.ZoomMax, camera.ZoomScale);
        camera.Zoom(-0.5f);
        camera.StopTurning();
        camera.Tick(1f / 60f, 2016);
        Assert.Equal(150f, camera.DistanceTarget, 2);
    }

    /// <summary>
    /// **A wall pulls the camera in at once and lets it out slowly.** The pull-in sets both the
    /// distance and where it is heading; once clear, the distance eases back out at 1000 a second.
    /// </summary>
    [Fact]
    public void A_wall_pulls_the_camera_in_at_once_and_lets_it_out_slowly()
    {
        bool wall = true;
        var camera = new WorldCamera { HeadOf = null, Obstruction = (_, _, want) => wall ? 100f : null };
        camera.Start(Someone(Vector3.Zero));
        camera.Tick(1f / 60f, 16);
        Assert.Equal(100f, camera.Distance, 2);

        // Clear again: each frame eases first and then works out where it is heading, so the
        // distance moves out 100 a tenth of a second towards the 354.79 the pitch asks for.
        wall = false;
        camera.Tick(0.1f, 116);
        Assert.Equal(200f, camera.Distance, 1);
        camera.Tick(0.1f, 216);
        Assert.Equal(300f, camera.Distance, 1);
        camera.Tick(0.1f, 316);
        Assert.Equal(354.79f, camera.Distance, 2);          // the pitch's own answer with the table cosine: the 354.79 a new camera is built at
    }

    /// <summary>
    /// **The steady distance is 1.1's 354.79**. The easing's cosine is the engine's table one
    /// (<see cref="TableTrig"/>): six-decimal sines, read back one entry short for a cosine —
    /// cos 75° comes to 0.258449, and 300 + 212 × that to 354.7911, which is the distance a camera is
    /// built at and the 354.8 that 1.1's camera stands at. The library's cosine made it 354.87.
    /// </summary>
    [Fact]
    public void The_table_cosine_holds_the_game_camera_at_1_1s_distance()
    {
        Assert.Equal(0.258449f, TableTrig.Cos(75f), 5);
        Assert.Equal(354.7911f, 300f + 212f * TableTrig.Cos(75f), 3);
        Assert.Equal(WorldCamera.StartDistance, 300f + 212f * TableTrig.Cos(75f), 2);
        // The four quarters and their ends, near enough the true ones.
        foreach (float a in new[] { 0f, 10f, 45f, 89.99f, 90f, 135f, 180f, 200f, 270f, 300f, 359.9f, -30f, 720f })
        {
            var (sin, cos) = TableTrig.SinCos(a);
            // Within the one entry (90/4096°) the table reads short by, and never a quadrant's sign wrong.
            Assert.InRange(sin - MathF.Sin(a * MathF.PI / 180f), -5e-4f, 5e-4f);
            Assert.InRange(cos - MathF.Cos(a * MathF.PI / 180f), -5e-4f, 5e-4f);
        }
        // Entry 1 is the six-decimal 0.000383, and the cosine of 0 is the last entry, 1.
        Assert.Equal(0.000383f, TableTrig.SinCos(90f / 4096f).Sin, 6);
        Assert.Equal(1f, TableTrig.Cos(0f));
    }

    /// <summary>
    /// Changing who is followed slides the target over a tenth of a second when the two are close,
    /// rather than cutting.
    /// </summary>
    [Fact]
    public void A_new_subject_nearby_is_slid_to_not_cut_to()
    {
        var one = Someone(Vector3.Zero);
        var two = Someone(new Vector3(200, 0, 0));
        var camera = Following(one);
        camera.Tick(1f / 60f, 16);
        camera.SetSubject(two);
        camera.Tick(0.05f, 66);
        // The first frame after the change is still at the start of the slide.
        Assert.Equal(0f, camera.Target.X, 1);
        camera.Tick(0.05f, 116);
        Assert.Equal(100f, camera.Target.X, 1);
        camera.Tick(0.05f, 166);
        Assert.Equal(200f, camera.Target.X, 1);
    }

    // ---------------------------------------------------------------------------- cutscenes

    /// <summary>
    /// A cutscene takes the bar and the pointer away and closes the panes; a shot with no lerp
    /// time is on screen at once; the release puts the game camera back at a fixed place — facing
    /// the way the character faces, −30°, **300** back (the near end, not the 354 of a fresh
    /// camera), 80° lens.
    /// </summary>
    [Fact]
    public void A_cutscene_takes_the_screen_and_the_release_gives_it_back()
    {
        NeedsAPlace();
        var christof = Someone(new Vector3(0, 0, 0), yaw: 90f, height: 100f);
        var wilhem = Someone(new Vector3(200, 0, 0), height: 100f);
        var camera = Following(christof);
        camera.Tick(1f / 60f, 16);
        int began = 0;
        camera.CutsceneBegan += () => began++;

        camera.SetupCutscene(christof, wilhem);
        Assert.Equal(CameraMode.Cutscene, camera.Mode);
        Assert.Equal(1, began);
        Assert.True(camera.StatusBarHidden);
        Assert.False(camera.CursorShown);
        Assert.Equal((6, 1, 2, 0), camera.LastShot);          // the default: POV A, medium-low, medium

        camera.SetShot((int)CameraShot.AngularB, 2, 2, 0, 0);
        camera.Tick(1f / 60f, 32);
        // Looking from the eye at the look-at point, with no roll.
        var view = Vector3.Normalize(camera.Target - camera.Eye);
        Near(view, camera.Forward, 0.001f);
        Assert.Equal(0f, Vector3.Dot(new Vector3(camera.Frame.M11, camera.Frame.M12, camera.Frame.M13), Vector3.UnitZ), 3);

        camera.Release(christof);
        Assert.Equal(CameraMode.Game, camera.Mode);
        Assert.False(camera.StatusBarHidden);
        Assert.True(camera.CursorShown);
        Assert.Equal(90f, camera.Angles.Z, 3);
        Assert.Equal(-30f, WorldCamera.Signed(camera.Angles.X), 3);
        Assert.Equal(300f, camera.Distance);
        Assert.Equal(WorldCamera.DefaultFov, camera.Fov);
    }

    /// <summary>
    /// A shot with a lerp time moves on a half cosine — slow, fast, slow — so halfway through the
    /// time it is exactly halfway there. **And it never changes the lens**: the easing writes its
    /// first tiny step over the target as well as the fov, so the camera keeps the lens it had.
    /// Only a shot with no lerp time changes lens, at once.
    /// </summary>
    [Fact]
    public void A_shot_eases_on_a_half_cosine_and_only_an_instant_one_changes_the_lens()
    {
        NeedsAPlace();
        var christof = Someone(new Vector3(0, 0, 0), height: 100f);
        var camera = Following(christof);
        camera.Tick(1f / 60f, 16);
        camera.SetupCutscene(christof, null);         // POV A over a tenth of a second
        camera.Tick(0.2f, 216);
        Assert.Equal(80f, camera.Fov, 1);             // not POV A's 60: the game camera's lens stayed
        var from = camera.Eye;

        camera.Tick(0.05f, 266);                      // the frame the shot will borrow the timing of
        from = camera.Eye;
        camera.SetShot((int)CameraShot.ProfileA, 2, 3, 0, 1000);
        // The shot ran one frame of its own on the spot, so ten more of 50 ms are halfway.
        for (int i = 1; i <= 10; i++) camera.Tick(0.05f, 266 + 50 * i);
        var half = camera.Eye;
        for (int i = 11; i <= 22; i++) camera.Tick(0.05f, 266 + 50 * i);
        var end = camera.Eye;
        Near((end - from) * 0.5f + from, half, 0.05f);
        Assert.Equal(80f, camera.Fov, 1);
        Assert.Equal((8, 2, 3, 0), camera.LastShot);

        camera.SetShot((int)CameraShot.ProfileA, 2, 3, 0, 0);
        Assert.Equal(45f, camera.Fov);                // no lerp: the new lens at once
        Near(end, camera.Eye, 0.01f);                 // and the same framing, cached, the same eye
    }

    /// <summary>
    /// Going into a cutscene brings back a character a script made invisible — the steered
    /// character's DON'T RENDER bit goes with the first-person bit (the <c>0xffffff7d</c> mask).
    /// </summary>
    [Fact]
    public void A_cutscene_clears_the_steered_characters_hiding_bits()
    {
        var christof = Someone(Vector3.Zero);
        christof.RenderFlags = WorldCamera.RenderDontRender | WorldCamera.RenderFirstPerson | 0x4;
        var camera = Following(christof);
        camera.EnterCutscene();
        Assert.Equal(0x4u, christof.RenderFlags);
        Assert.False(christof.ModelHidden);
    }

    // ------------------------------------------------------------------ the player's own hands

    /// <summary>
    /// **Z**: the eye goes to the head, the body is left out of the view (REFLECTION ONLY), the
    /// pointer goes, and the mouse turns the view half a degree a mickey, the pitch held to ±80°.
    /// Letting go puts the game camera back where a release does — and clears DON'T RENDER too.
    /// </summary>
    [Fact]
    public void The_first_person_look_hides_the_body_and_turns_by_the_mouse()
    {
        var christof = Someone(new Vector3(0, 0, 0));
        var camera = Following(christof);
        camera.Tick(1f / 60f, 16);
        Assert.True(camera.EnterLook());
        Assert.Equal(CameraMode.Look, camera.Mode);
        Assert.True(christof.ModelHidden);
        Assert.False(camera.CursorShown);

        camera.MouseDx = 20f;                  // right: yaw goes down by ten degrees
        camera.MouseDy = -40f;                 // up: pitch goes up by twenty, from −30 to −10
        camera.Tick(1f / 60f, 32);
        Assert.Equal(350f, camera.Angles.Z, 3);
        Assert.Equal(-10f, WorldCamera.Signed(camera.Angles.X), 3);
        Near(new Vector3(0, 0, WorldCamera.StartDistance - WorldCamera.StartDistance + 128f), camera.Eye);

        camera.MouseDx = 0; camera.MouseDy = -400f;
        camera.Tick(1f / 60f, 48);
        Assert.Equal(80f, WorldCamera.Signed(camera.Angles.X), 3);

        christof.RenderFlags |= WorldCamera.RenderDontRender;
        Assert.True(camera.BackToGame(christof));
        Assert.Equal(CameraMode.Game, camera.Mode);
        Assert.Equal(0u, christof.RenderFlags);
        Assert.Equal(-30f, WorldCamera.Signed(camera.Angles.X), 3);
        Assert.Equal(300f, camera.Distance);
        Assert.True(camera.CursorShown);
    }

    /// <summary>
    /// A level's camera clears the look's bit on the steered character, so
    /// one who crossed an exit with the look key down is drawn in the next place (a comparison with 1.1's finding).
    /// </summary>
    [Fact]
    public void A_new_level_draws_a_character_who_left_the_last_one_looking_round()
    {
        var christof = Someone(Vector3.Zero);
        var camera = Following(christof);
        Assert.True(camera.EnterLook());
        Assert.True(christof.ModelHidden);
        var next = new WorldCamera { HeadOf = null };
        next.Start(christof);
        Assert.False(christof.ModelHidden);
        Assert.Equal(CameraMode.Game, next.Mode);
    }

    [Fact]
    public void A_dead_character_cannot_look_out_of_its_own_eyes()
    {
        var christof = Someone(Vector3.Zero);
        christof.Actor!.Flags |= Actor.Flag.Dead;
        var camera = Following(christof);
        Assert.False(camera.EnterLook());
        Assert.Equal(CameraMode.Game, camera.Mode);
    }

    /// <summary>X or numpad 5: half a degree a mickey, the pitch held to the game camera's −80..−20.</summary>
    [Fact]
    public void The_swivel_turns_half_a_degree_a_mickey_within_the_game_cameras_limits()
    {
        var camera = Following(Someone(Vector3.Zero));
        camera.Swivel(40f, 0f);
        Assert.Equal(340f, camera.Angles.Z, 3);
        camera.Swivel(0f, 200f);
        Assert.Equal(-80f, WorldCamera.Signed(camera.Angles.X), 3);
        camera.Swivel(0f, -400f);
        Assert.Equal(-20f, WorldCamera.Signed(camera.Angles.X), 3);
    }

    /// <summary>
    /// The pointer against the edge: the top and bottom strips turn the pitch (only with Mouse
    /// Pitch on), the sides the yaw, a degree a second per mickey — and being at an edge is what
    /// keeps the rate from being zeroed next frame.
    /// </summary>
    [Fact]
    public void The_edge_of_the_screen_turns_the_camera()
    {
        var camera = Following(Someone(Vector3.Zero));
        Assert.False(camera.EdgeTurn(320, 240, 10f, 10f, mousePitch: true));
        Assert.Equal(0f, camera.PitchRate);

        Assert.True(camera.EdgeTurn(320, 479, 0f, 12f, mousePitch: true));
        Assert.Equal(-12f, camera.PitchRate);
        camera.StopTurning();
        Assert.True(camera.EdgeTurn(320, 0, 0f, 12f, mousePitch: false));
        Assert.Equal(0f, camera.PitchRate);

        Assert.True(camera.EdgeTurn(0, 240, -30f, 0f, mousePitch: false));
        Assert.Equal(30f, camera.YawRate);
        Assert.True(camera.EdgeTurn(640, 240, 30f, 0f, mousePitch: false));
        Assert.Equal(0f, camera.YawRate);
        // Rates are held to ±180 however hard the push.
        camera.EdgeTurn(0, 240, -1000f, 0f, false);
        Assert.Equal(180f, camera.YawRate);
    }

    // ------------------------------------------------------------------------------- paths

    /// <summary>
    /// **Space skips a line in the game camera, a camera path and a cutscene**, and
    /// not in the first-person look or the free camera. The path case is
    /// the one Vitomir had wrong until then: the Knight of St John's conversation outside the convent
    /// plays under a camera path, and Space hurries it in 1.1.
    /// </summary>
    [Fact]
    public void Space_skips_a_line_in_the_game_camera_a_path_and_a_cutscene()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        using var files = new GameFiles(dir!);
        var who = Someone(Vector3.Zero);
        var camera = Following(who);
        Assert.True(camera.SpaceSkipsLine);                          // the game camera
        Assert.True(camera.EnterLook());
        Assert.False(camera.SpaceSkipsLine);                         // the look
        camera.BackToGame(who);
        camera.EnterCutscene();
        Assert.True(camera.SpaceSkipsLine);                          // a cutscene
        Assert.True(files.TryRead("paths/MON1Fight.ncp", out var data));
        Assert.True(camera.PlayPath(NcpCameraPath.Read(data, "MON1Fight.ncp"), 30f, null));
        Assert.Equal(CameraMode.Path, camera.Mode);
        Assert.True(camera.SpaceSkipsLine);                          // a camera path
        camera.Start(null);
        Assert.Equal(CameraMode.Free, camera.Mode);
        Assert.False(camera.SpaceSkipsLine);                         // the free camera
    }

    /// <summary>
    /// A camera path out of the install plays to its end, tells the script once, and then holds —
    /// the camera stays in path mode on the last keyframe until something releases it.
    /// </summary>
    [Fact]
    public void A_camera_path_plays_to_its_end_tells_once_and_holds()
    {
        var dir = GameFiles.Locate();
        Assert.SkipWhen(dir is null, "no game install");
        using var files = new GameFiles(dir!);
        Assert.True(files.TryRead("paths/MON1Fight.ncp", out var data));
        var ncp = NcpCameraPath.Read(data, "MON1Fight.ncp");
        var camera = Following(Someone(Vector3.Zero));
        int ended = 0;
        Assert.True(camera.PlayPath(ncp, 30f, () => ended++));
        Assert.Equal(CameraMode.Path, camera.Mode);
        Assert.True(camera.StatusBarHidden);

        float seconds = ncp.TotalLength / 30f + 1f;
        for (float t = 0; t < seconds; t += 0.1f) camera.Tick(0.1f, (int)(t * 1000));
        Assert.Equal(1, ended);
        Near(ncp.Keyframes[^1].Position, camera.Eye);
        camera.Tick(0.1f, 99_000);
        Assert.Equal(1, ended);
        Assert.Equal(CameraMode.Path, camera.Mode);
    }

    // ------------------------------------------------------------------------ screen effects

    /// <summary>
    /// A fade runs its level linearly and **stays where it ends** — the screen is left black until
    /// another fade or a clear brings it back. A bounce runs out in half the time and comes back.
    /// </summary>
    [Fact]
    public void A_fade_holds_where_it_ends_and_a_bounce_comes_back()
    {
        var fx = new ScreenEffects();
        Assert.NotEqual(0, fx.AddFade(255f, 0f, 1000, false, 0));
        for (int t = 100; t <= 500; t += 100) fx.Step(t, 100);
        Assert.Equal(127.5f, fx.FadeLevel, 1);
        for (int t = 600; t <= 1200; t += 100) fx.Step(t, 100);
        Assert.Equal(0f, fx.FadeLevel);
        Assert.Equal(0, fx.Running);
        fx.Step(5000, 100);
        Assert.Equal(0f, fx.FadeLevel);                        // held
        fx.ClearAll();
        Assert.Equal(255f, fx.FadeLevel);

        fx.AddFade(255f, 0f, 1000, bounce: true, 0);
        for (int t = 100; t <= 500; t += 100) fx.Step(t, 100);
        Assert.Equal(0f, fx.FadeLevel);                        // half the time to get there
        for (int t = 600; t <= 1100; t += 100) fx.Step(t, 100);
        Assert.Equal(255f, fx.FadeLevel);                      // and back in the other half
    }

    [Fact]
    public void The_lowest_fade_and_the_strongest_flash_win_and_eight_is_the_limit()
    {
        var fx = new ScreenEffects();
        fx.AddFade(200f, 200f, 1000, false, 0);
        fx.AddFade(100f, 100f, 1000, false, 0);
        fx.AddFlash(50f, 50f, 1000, 0xff0000, false, 0);
        fx.AddFlash(150f, 150f, 1000, 0x00ff00, false, 0);
        fx.Step(100, 100);
        Assert.Equal(100f, fx.FadeLevel);
        Assert.Equal(150f, fx.FlashIntensity);
        Assert.Equal(0x00ff00u, fx.FlashColor);
        for (int i = 0; i < 4; i++) Assert.NotEqual(0, fx.AddFilter(1000, 0x123456, 0));
        Assert.Equal(0, fx.AddFade(0f, 0f, 1000, false, 0));
    }

    // ------------------------------------------------------------- the pull-in's reach

    /// <summary>
    /// **The pull-in can see a wall that is nearer than it wants to go plus the clearance.** The
    /// engine sweeps a sphere of radius 10 whose centre travels the distance asked for, so its
    /// surface reaches 10 further; a thin ray stopped at the distance asked for is blind to exactly
    /// that last 10 units (`ChaseRay.Distance`).
    ///
    /// The check is written without naming a number, so it holds wherever the wall is: ask with no
    /// limit to find what the level really answers, then ask again for a distance inside the blind
    /// band. The answer has to be the same. Before then the second call came back with the
    /// distance it was asked for, having seen nothing at all.
    /// </summary>
    [Fact]
    public void The_pull_in_sees_a_wall_just_beyond_what_the_camera_wants()
    {
        Assert.SkipWhen(ConventDay.Value is null, "no game install");
        var place = ConventDay.Value!;
        var (target, back) = TheJitterSpot(place);

        float reach = ChaseRay.Distance(place.Level, target, back, 10_000f);
        Assert.True(reach < 1_000f, $"the spot was meant to have a wall behind it, got {reach}");

        float want = reach + ChaseRay.Clearance / 2f;
        Assert.Equal(reach, ChaseRay.Distance(place.Level, target, back, want), 1);
    }

    /// <summary>
    /// **A camera held still against a wall does not move, however fast the loop runs.** This is
    /// the jitter seen in play: pulled in, the camera could not see the wall that
    /// had pulled it, eased back out, was snatched in again, over and over. One frame of easing at
    /// 60 fps crossed the whole blind band in a single step, so it only showed on a fast machine —
    /// hence the 300 fps here, where the swing measured the full 10 units.
    /// </summary>
    [Fact]
    public void A_still_camera_against_a_wall_does_not_drift_at_any_frame_rate()
    {
        Assert.SkipWhen(ConventDay.Value is null, "no game install");
        var place = ConventDay.Value!;
        var camera = new WorldCamera
        {
            HeadOf = null,
            Obstruction = (from, towards, want) => ChaseRay.Distance(place.Level, from, towards, want),
        };
        camera.Start(Someone(JitterSpot, place: place));
        camera.Angles = new Vector3(JitterPitch + 360f, 0f, JitterYaw);

        int ms = 0;
        const float step = 1f / 300f;
        for (int i = 0; i < 60; i++) camera.Tick(step, ms += 3);   // let it settle against the wall
        float min = camera.Distance, max = camera.Distance;
        for (int i = 0; i < 600; i++)
        {
            camera.Tick(step, ms += 3);
            min = MathF.Min(min, camera.Distance);
            max = MathF.Max(max, camera.Distance);
        }
        Assert.True(max - min < 0.01f, $"the camera swung {max - min:0.00} units standing still ({min:0.00}..{max:0.00})");
    }

    /// <summary>Where the journal caught it: the courtyard, facing the wall that pulled it in.</summary>
    static readonly Vector3 JitterSpot = new(653f, 22f, 0f);
    const float JitterYaw = 209.05f, JitterPitch = -26.75f;

    static (Vector3 Target, Vector3 Back) TheJitterSpot(Location place)
    {
        var camera = new WorldCamera { HeadOf = null };
        camera.Start(Someone(JitterSpot, place: place));
        camera.Angles = new Vector3(JitterPitch + 360f, 0f, JitterYaw);
        camera.Tick(1f / 60f, 16);
        return (camera.Target, -camera.Forward);
    }

}
