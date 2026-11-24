// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The free camera's own keys**: 1.1's keys are the defaults, on a page of
/// their own and rebindable like every command; they take no key from the game while the free camera
/// is not up, and while it is, a chord of theirs is theirs alone (1.1's mode 3 reads nothing else); with
/// <c>[qol] driving</c> off they are 1.1's steps — 100 a second along the camera's own axes, 400 with
/// Shift, the arrows 90° a second with the Up arrow tipping the view down, the keypad's / and * rolling,
/// Home levelling on the frame it goes down — and with it on, driving's handling (ours), reworked
/// to a design of its own: W and S along the view whatever the pitch, A and D along the camera's
/// right and X and C along its up, at driving's speeds; the middle button's drag look, the game camera's
/// swivel's sense and half a degree a mickey; and the keypad's 5 levelling the view to the horizon —
/// none of which reaches 1.1's steps with the switch off. The developer's Ctrl+P goes in and
/// the free camera's own goes back.
/// </summary>
public class FreeCameraKeysTests
{
    const float Tolerance = 1e-3f;

    static void Near(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < Tolerance, $"expected {expected}, got {actual}");

    static void Near(float expected, float actual) =>
        Assert.True(MathF.Abs(expected - actual) < Tolerance, $"expected {expected}, got {actual}");

    static FreeCamera At(float yaw = 0f, float pitch = 0f, float roll = 0f) =>
        new() { Position = new Vector3(10, 20, 30), Yaw = yaw, Pitch = pitch, Roll = roll };

    static Vector3 Row(Matrix4x4 m, int row) => row switch
    {
        1 => new Vector3(m.M11, m.M12, m.M13),
        2 => new Vector3(m.M21, m.M22, m.M23),
        _ => new Vector3(m.M31, m.M32, m.M33),
    };

    [Fact]
    public void The_free_cameras_keys_are_1_1s_on_a_page_of_their_own_and_rebind_with_modifiers()
    {
        var keys = new KeyBindings();
        KeyChord K(int input, KeyMods mods = KeyMods.None) => new(input, mods);
        Assert.Equal([K(KeyNames.Letter('W'))], keys[KeyCommand.FlyForward]);
        Assert.Equal([K(KeyNames.Letter('S'))], keys[KeyCommand.FlyBack]);
        Assert.Equal([K(KeyNames.Letter('A'))], keys[KeyCommand.FlyLeft]);
        Assert.Equal([K(KeyNames.Letter('D'))], keys[KeyCommand.FlyRight]);
        Assert.Equal([K(KeyNames.Letter('X'))], keys[KeyCommand.FlyUp]);
        Assert.Equal([K(KeyNames.Letter('C'))], keys[KeyCommand.FlyDown]);
        Assert.Equal([K(KeyNames.Left)], keys[KeyCommand.FlyTurnLeft]);
        Assert.Equal([K(KeyNames.Right)], keys[KeyCommand.FlyTurnRight]);
        Assert.Equal([K(KeyNames.Up)], keys[KeyCommand.FlyLookDown]);      // 1.1's Up arrow tips the view down
        Assert.Equal([K(KeyNames.Down)], keys[KeyCommand.FlyLookUp]);
        Assert.Equal([K(KeyNames.KeypadDivide)], keys[KeyCommand.FlyRollLeft]);
        Assert.Equal([K(KeyNames.KeypadMultiply)], keys[KeyCommand.FlyRollRight]);
        Assert.Equal([K(KeyNames.Home)], keys[KeyCommand.FlyLevel]);
        Assert.Equal([K(KeyNames.Letter('P'), KeyMods.Ctrl)], keys[KeyCommand.FlyCamera]);
        // The later two (ours): the keypad's 5 levels the view, the middle button looks.
        Assert.Equal([K(KeyNames.Keypad(5))], keys[KeyCommand.FlyLevelView]);
        Assert.Equal([KeyChord.Mouse(2)], keys[KeyCommand.FlyDrag]);
        // Fast is 1.1's either Shift — the driving's run, which now has both.
        Assert.Equal([K(KeyNames.LShift), K(KeyNames.RShift)], keys[KeyCommand.Run]);

        // Their own two groups, the seventh and eighth (the camera's and driving's are two), and nothing of theirs in any other.
        var groups = KeysPage.Groups.Where(g => g.Heading.StartsWith("The Free Camera")).ToArray();
        Assert.Equal(new[] { "The Free Camera: Moving", "The Free Camera: Turning" }, groups.Select(g => g.Heading));
        Assert.Equal(6, Array.IndexOf(KeysPage.Groups, groups[0]));
        Assert.Equal(KeyBindings.Flying.OrderBy(c => c), groups.SelectMany(g => g.Commands).OrderBy(c => c));
        Assert.Equal(16, KeyBindings.Flying.Length);
        Assert.Equal("The Storyteller", KeysPage.Groups[^1].Heading);

        // Moved to Shift+E like any other command: read with the Shift held, not without, and the
        // [keys] line reads back.
        keys.Set(KeyCommand.FlyUp, [K(KeyNames.Letter('E'), KeyMods.Shift)]);
        int e = KeyNames.Letter('E');
        Assert.True(keys.Held(KeyCommand.FlyUp, i => i == e, KeyMods.Shift));
        Assert.False(keys.Held(KeyCommand.FlyUp, i => i == e, KeyMods.None));
        var line = keys.Values().Single(v => v.Key == "flyup");
        Assert.Equal("Shift+E", line.Value);
        Assert.Equal(K(e, KeyMods.Shift), KeyChord.Parse(line.Value));
        // And the later two, in [keys] like the rest.
        Assert.Contains(keys.Values(), v => v.Key == "flylevelview" && v.Value == "Keypad 5");
        Assert.Contains(keys.Values(), v => v.Key == "flydraglook" && v.Value == "Middle Click");
        var moved = new KeyBindings();
        moved.Apply(IniFile.Parse("[keys]\nflylevelview = Ctrl+L\nflydraglook = Mouse 4\n"));
        Assert.Equal([K(KeyNames.Letter('L'), KeyMods.Ctrl)], moved[KeyCommand.FlyLevelView]);
        Assert.Equal([KeyChord.Mouse(4)], moved[KeyCommand.FlyDrag]);
    }

    [Fact]
    public void They_take_no_key_from_the_game_until_the_free_camera_is_up_and_then_theirs_are_theirs_alone()
    {
        var keys = new KeyBindings();
        int c = KeyNames.Letter('C'), x = KeyNames.Letter('X'), r = KeyNames.Letter('R');
        // Not up: C is the character sheet and X the swivel, as they were.
        Assert.Equal([KeyCommand.CharacterSheet], keys.Pressed(c, KeyMods.None, false).Select(p => p.Command));
        Assert.Equal([KeyCommand.Swivel], keys.Pressed(x, KeyMods.None, false).Select(p => p.Command));
        // The arrows, Home, the keypad's / and * and P are nobody's default for the host's swallowing or
        // polling — so the reader's Up still scrolls and the arrows still turn the game camera.
        foreach (int k in new[] { KeyNames.Up, KeyNames.Down, KeyNames.Left, KeyNames.Right, KeyNames.Home,
                                   KeyNames.KeypadDivide, KeyNames.KeypadMultiply, KeyNames.Letter('P') })
        {
            Assert.False(KeyBindings.AnyDefault(k));
            Assert.False(KeyBindings.PolledDefault(k));
        }
        Assert.True(KeyBindings.AnyDefault(c));       // the character sheet's still
        // Up: a chord of theirs is theirs, and R, which is not, is still the regroup.
        Assert.Empty(keys.Pressed(c, KeyMods.None, false, flying: true));
        Assert.Empty(keys.Pressed(x, KeyMods.None, false, flying: true));
        Assert.Empty(keys.Pressed(KeyNames.Letter('P'), KeyMods.Ctrl, false, flying: true));
        Assert.Equal([KeyCommand.Regroup], keys.Pressed(r, KeyMods.None, false, flying: true).Select(p => p.Command));
        // Fly down moved to V: C is the character sheet's again, up or not.
        keys.Set(KeyCommand.FlyDown, [new KeyChord(KeyNames.Letter('V'))]);
        Assert.Equal([KeyCommand.CharacterSheet], keys.Pressed(c, KeyMods.None, false, flying: true).Select(p => p.Command));
        // No free-camera command is ever one a key press acts as.
        foreach (var k in Enumerable.Range(4, 100))
            Assert.DoesNotContain(keys.Pressed(k, KeyMods.None, false), p => KeyBindings.Flying.Contains(p.Command));
    }

    [Fact]
    public void One_press_of_1_1s_steps_moves_100_a_second_along_the_cameras_own_axes_and_400_with_shift()
    {
        var keys = new FreeCameraKeys();
        var cam = At();
        keys.Step(cam, FreeKeys.Forward, 0.5f, driving: false);
        Near(new Vector3(60, 20, 30), cam.Position);                    // yaw 0 looks along +x
        keys.Step(cam, FreeKeys.Forward | FreeKeys.Fast, 0.5f, driving: false);
        Near(new Vector3(260, 20, 30), cam.Position);
        // D is the camera's right (−y at yaw 0), and two keys are the sum, not one length.
        cam = At();
        keys.Step(cam, FreeKeys.Forward | FreeKeys.Right, 1f, driving: false);
        Near(new Vector3(110, -80, 30), cam.Position);
        // Pitched 30° up, W flies up the view and X along the camera's own up, not the world's.
        cam = At(pitch: 30f);
        keys.Step(cam, FreeKeys.Forward, 1f, driving: false);
        Near(new Vector3(10, 20, 30) + new Vector3(MathF.Cos(MathF.PI / 6), 0, MathF.Sin(MathF.PI / 6)) * 100f, cam.Position);
        cam = At(pitch: 30f);
        keys.Step(cam, FreeKeys.Up, 1f, driving: false);
        Near(new Vector3(10, 20, 30) + new Vector3(-MathF.Sin(MathF.PI / 6), 0, MathF.Cos(MathF.PI / 6)) * 100f, cam.Position);
        // Paused (the game's clock stopped): nothing moves or turns.
        cam = At();
        keys.Step(cam, FreeKeys.Forward | FreeKeys.TurnLeft, 0f, driving: false);
        Near(new Vector3(10, 20, 30), cam.Position);
        Assert.Equal(0f, cam.Yaw);
    }

    [Fact]
    public void The_arrows_turn_90_a_second_the_up_arrow_tips_the_view_down_and_nothing_holds_the_pitch()
    {
        var keys = new FreeCameraKeys();
        var cam = At();
        keys.Step(cam, FreeKeys.TurnLeft, 1f, driving: false);
        Near(90f, cam.Yaw);                                             // left: +x round to +y
        keys.Step(cam, FreeKeys.TurnRight | FreeKeys.Fast, 0.25f, driving: false);
        Near(45f, cam.Yaw);                                             // 180 a second with Shift
        cam = At();
        keys.Step(cam, FreeKeys.LookDown, 0.5f, driving: false);
        Near(-45f, cam.Pitch);
        Assert.True(cam.Forward.Z < 0f);
        // On over the top: 1.1 wraps the pitch and never holds it.
        keys.Step(cam, FreeKeys.LookDown, 1.5f, driving: false);
        Near(180f, MathF.Abs(cam.Pitch));
        Near(new Vector3(-1, 0, 0), cam.Forward);
        // The picture is still a picture: the view matrix is finite looking straight down.
        cam = At(pitch: -90f);
        Assert.False(float.IsNaN(cam.View.M11));
    }

    [Fact]
    public void The_keypad_rolls_the_view_and_home_levels_it_on_the_frame_it_goes_down()
    {
        var keys = new FreeCameraKeys();
        var cam = At(yaw: 30f, pitch: 20f);
        // The axes are 1.1's and their second row is the view.
        Near(cam.Forward, new Vector3(cam.Axes.M21, cam.Axes.M22, cam.Axes.M23));
        var right = new Vector3(cam.Axes.M11, cam.Axes.M12, cam.Axes.M13);
        keys.Step(cam, FreeKeys.RollRight, 0.5f, driving: false);
        Near(45f, cam.Roll);
        Assert.True(Vector3.Dot(cam.Up, right) > 0.5f);                 // the up leans to the right
        Near(cam.Forward, new Vector3(cam.Axes.M21, cam.Axes.M22, cam.Axes.M23));
        // Home goes down: level, this frame.
        keys.Step(cam, FreeKeys.Level, 0.1f, driving: false);
        Assert.Equal(0f, cam.Roll);
        Assert.Equal(Vector3.UnitZ, cam.Up);
        // Still held, it is not a new press: the roll key rolls.
        keys.Step(cam, FreeKeys.Level | FreeKeys.RollLeft, 0.5f, driving: false);
        Near(-45f, cam.Roll);
        keys.Step(cam, FreeKeys.None, 0.1f, driving: false);
        keys.Step(cam, FreeKeys.Level | FreeKeys.RollLeft, 0.5f, driving: false);
        Near(-45f, cam.Roll);                                           // levelled, then the frame's roll
        // Held when the camera comes up, it is an old press.
        keys.Rest();
        keys.Step(cam, FreeKeys.Level, 0.1f, driving: false);
        Near(-45f, cam.Roll);
    }

    [Fact]
    public void With_driving_W_S_D_A_X_and_C_fly_along_the_cameras_own_axes_pitched_and_rolled_at_drivings_speeds()
    {
        var keys = new FreeCameraKeys();
        var start = new Vector3(10, 20, 30);
        // Looking down 40° and rolled 25°: each key goes along the camera's own axis, not the ground's.
        foreach (var (key, row, sign) in new[] { (FreeKeys.Forward, 2, 1f), (FreeKeys.Back, 2, -1f), (FreeKeys.Right, 1, 1f),
                                                 (FreeKeys.Left, 1, -1f), (FreeKeys.Up, 3, 1f), (FreeKeys.Down, 3, -1f) })
        {
            var one = At(yaw: 30f, pitch: -40f, roll: 25f);
            var axis = Row(one.Axes, row) * sign;
            keys.Step(one, key, 0.5f, driving: true);
            Near(start + axis * Player.DefaultWalkSpeed * 0.5f, one.Position);
            one = At(yaw: 30f, pitch: -40f, roll: 25f);
            keys.Step(one, key | FreeKeys.Fast, 0.5f, driving: true);
            Near(start + axis * Player.DefaultRunSpeed * 0.5f, one.Position);
        }
        // W dives straight at where the view points, S backs straight out, pitched up or down.
        var down = At(yaw: 90f, pitch: -30f);
        keys.Step(down, FreeKeys.Forward, 1f, driving: true);
        Near(start + down.Forward * Player.DefaultWalkSpeed, down.Position);
        Assert.True(down.Position.Z < start.Z);
        var up = At(yaw: 90f, pitch: 60f);
        keys.Step(up, FreeKeys.Back, 1f, driving: true);
        Near(start - up.Forward * Player.DefaultWalkSpeed, up.Position);
        Assert.True(up.Position.Z < start.Z);
        // X at a pitched camera is the camera's up, leaning back, not the world's straight up.
        var tipped = At(pitch: 40f);
        keys.Step(tipped, FreeKeys.Up, 1f, driving: true);
        Near(start + new Vector3(-MathF.Sin(MathF.PI * 40 / 180), 0, MathF.Cos(MathF.PI * 40 / 180)) * Player.DefaultWalkSpeed, tipped.Position);
        // Two keys, or three, are one length, as driving's push is.
        var cam2 = At(yaw: 30f, pitch: -40f, roll: 25f);
        keys.Step(cam2, FreeKeys.Forward | FreeKeys.Left | FreeKeys.Up, 1f, driving: true);
        Near(Player.DefaultWalkSpeed, Vector3.Distance(start, cam2.Position));
        // Paused: nothing moves.
        cam2 = At(pitch: -40f);
        keys.Step(cam2, FreeKeys.Forward, 0f, driving: true);
        Near(start, cam2.Position);
        // The arrows at the game camera's 180 a second, Shift or not, the same way round as 1.1's; the pitch held short of straight down.
        var cam = At();
        keys.Step(cam, FreeKeys.TurnLeft | FreeKeys.Fast, 0.25f, driving: true);
        Near(45f, cam.Yaw);
        keys.Step(cam, FreeKeys.LookDown, 1f, driving: true);
        Near(-FreeCameraKeys.DrivingPitchLimit, cam.Pitch);
    }

    [Fact]
    public void With_driving_the_middle_buttons_drag_turns_and_tilts_the_view_as_the_game_cameras_swivel_does()
    {
        var keys = new FreeCameraKeys();
        var cam = At(yaw: 30f, pitch: -10f, roll: 5f);
        // The mouse right and down: the view turns right and tips down, half a degree a mickey.
        keys.Step(cam, FreeKeys.Drag, 0.02f, driving: true, mouse: new Vector2(20, 8));
        Near(20f, cam.Yaw);
        Near(-14f, cam.Pitch);
        Near(5f, cam.Roll);
        Near(new Vector3(10, 20, 30), cam.Position);
        // The same sense and size as the game camera's swivel, which the drag swivel runs.
        var game = new WorldCamera();
        game.Angles = new Vector3(WorldCamera.Wrap(-45f), 0f, 100f);        // the angles are kept 0..360
        var before = game.Angles;
        game.Swivel(20, 8);
        Near(20f - 30f, WorldCamera.Signed(game.Angles.Z - before.Z));
        Near(-14f - -10f, WorldCamera.Signed(game.Angles.X - before.X));
        // It goes by the mouse, not the clock: paused, it still turns; it tips no further than ±89°.
        keys.Step(cam, FreeKeys.Drag, 0f, driving: true, mouse: new Vector2(0, 400));
        Near(-FreeCameraKeys.DrivingPitchLimit, cam.Pitch);
        keys.Step(cam, FreeKeys.Drag, 0f, driving: true, mouse: new Vector2(-40, -600));
        Near(40f, cam.Yaw);
        Near(FreeCameraKeys.DrivingPitchLimit, cam.Pitch);
        // Not held, the mouse turns nothing.
        cam = At();
        keys.Step(cam, FreeKeys.None, 0.02f, driving: true, mouse: new Vector2(50, 50));
        Assert.Equal(0f, cam.Yaw);
        Assert.Equal(0f, cam.Pitch);
    }

    [Fact]
    public void With_driving_level_view_puts_the_view_level_with_the_horizon_keeping_the_heading_on_the_frame_it_goes_down()
    {
        var keys = new FreeCameraKeys();
        var cam = At(yaw: 123f, pitch: -50f, roll: 30f);
        keys.Step(cam, FreeKeys.None, 0.1f, driving: true);            // up a frame with it not held: then a press is new
        keys.Step(cam, FreeKeys.LevelView, 0.1f, driving: true);
        Assert.Equal(0f, cam.Pitch);
        Assert.Equal(0f, cam.Roll);
        Near(123f, cam.Yaw);
        Assert.Equal(Vector3.UnitZ, cam.Up);
        Near(0f, cam.Forward.Z);
        // Still held, it is not a new press: the look keys tip the view.
        keys.Step(cam, FreeKeys.LevelView | FreeKeys.LookDown, 0.1f, driving: true);
        Near(-18f, cam.Pitch);
        // Let go and pressed again: level again, then the frame's own tip.
        keys.Step(cam, FreeKeys.None, 0.1f, driving: true);
        keys.Step(cam, FreeKeys.LevelView | FreeKeys.LookDown, 0.1f, driving: true);
        Near(-18f, cam.Pitch);
        // Held when the camera comes up, it is an old press.
        keys.Rest();
        keys.Step(cam, FreeKeys.LevelView, 0.1f, driving: true);
        Near(-18f, cam.Pitch);
        // Home is still 1.1's, the roll alone.
        cam = At(yaw: 10f, pitch: -30f, roll: 20f);
        keys.Step(cam, FreeKeys.None, 0.1f, driving: true);
        keys.Step(cam, FreeKeys.Level, 0.1f, driving: true);
        Assert.Equal(0f, cam.Roll);
        Near(-30f, cam.Pitch);
    }

    [Fact]
    public void Level_view_and_the_drag_look_are_the_free_cameras_own_under_driving_and_nothing_without_it()
    {
        var keys = new KeyBindings();
        int kp5 = KeyNames.Keypad(5), middle = KeyChord.MouseBase + 2;
        // Not up: the keypad's 5 is the swivel and the middle button the drag swivel (or 1.1's secondary click), as they were.
        Assert.Equal([KeyCommand.Swivel], keys.Pressed(kp5, KeyMods.None, driving: true).Select(p => p.Command));
        Assert.Equal([KeyCommand.DragSwivel], keys.Pressed(middle, KeyMods.None, driving: true, camera: true).Select(p => p.Command));
        Assert.Equal([KeyCommand.SecondaryClick], keys.Pressed(middle, KeyMods.None, driving: true).Select(p => p.Command));
        // Up with driving: theirs alone.
        Assert.Empty(keys.Pressed(kp5, KeyMods.None, driving: true, flying: true));
        Assert.Empty(keys.Pressed(middle, KeyMods.None, driving: true, camera: true, flying: true));
        // Up without driving: they take nothing, and the chords go where they went.
        Assert.Equal([KeyCommand.Swivel], keys.Pressed(kp5, KeyMods.None, driving: false, flying: true).Select(p => p.Command));
        Assert.Equal([KeyCommand.DragSwivel], keys.Pressed(middle, KeyMods.None, driving: false, camera: true, flying: true).Select(p => p.Command));
        Assert.True(keys.Flies(kp5, KeyMods.None, driving: true));
        Assert.False(keys.Flies(kp5, KeyMods.None, driving: false));
        // Moved to L: the keypad's 5 is the swivel's again, up or not; L is the level view's under driving.
        keys.Set(KeyCommand.FlyLevelView, [new KeyChord(KeyNames.Letter('L'))]);
        Assert.Equal([KeyCommand.Swivel], keys.Pressed(kp5, KeyMods.None, driving: true, flying: true).Select(p => p.Command));
        Assert.True(keys.Flies(KeyNames.Letter('L'), KeyMods.None, driving: true));

        // And 1.1's steps are untouched by them: the same keys with and without the level view and the drag, frame by frame.
        var plain = new FreeCameraKeys();
        var extra = new FreeCameraKeys();
        var a = At(yaw: 20f, pitch: -35f, roll: 15f);
        var b = At(yaw: 20f, pitch: -35f, roll: 15f);
        FreeKeys[] frames = [FreeKeys.Forward, FreeKeys.Forward | FreeKeys.LookDown, FreeKeys.Up | FreeKeys.RollRight,
                             FreeKeys.None, FreeKeys.Back | FreeKeys.Fast, FreeKeys.Level, FreeKeys.Right | FreeKeys.TurnLeft];
        foreach (var held in frames)
        {
            plain.Step(a, held, 0.1f, driving: false);
            extra.Step(b, held | FreeKeys.LevelView | FreeKeys.Drag, 0.1f, driving: false, mouse: new Vector2(30, -12));
            Assert.Equal(a.Position, b.Position);
            Assert.Equal((a.Yaw, a.Pitch, a.Roll), (b.Yaw, b.Pitch, b.Roll));
        }
        // And 1.1's W still goes 100 a second along the view.
        var c = At(pitch: 30f);
        plain.Step(c, FreeKeys.Forward | FreeKeys.LevelView, 1f, driving: false);
        Near(new Vector3(10, 20, 30) + new Vector3(MathF.Cos(MathF.PI / 6), 0, MathF.Sin(MathF.PI / 6)) * 100f, c.Position);
        Near(30f, c.Pitch);
    }

    static WorldThing Someone() => new()
    {
        Index = 0, Guid = 1, Scene = NsdThing.Placed("test", Vector3.Zero, Vector3.Zero, 0), Template = "test",
        Type = ThingType.Player, Actor = new Actor { Health = 100, MaxHealth = 100 }, Orientation = Vector3.Zero,
    };

    [Fact]
    public void Ctrl_P_goes_in_from_the_game_camera_the_flight_is_the_cameras_and_the_way_back_keeps_the_yaw()
    {
        var who = Someone();
        var camera = new WorldCamera { HeadOf = null };
        camera.Start(who);
        camera.Tick(0.05f, 50);
        who.RenderFlags |= WorldCamera.RenderFirstPerson;
        camera.CursorShown = false;
        Assert.True(camera.EnterFree(who));
        Assert.Equal(CameraMode.Free, camera.Mode);
        Assert.Equal(0u, who.RenderFlags & WorldCamera.RenderFirstPerson);
        Assert.True(camera.CursorShown);
        // Flown: the camera's eye and angles are the flown camera's, and its frame looks the same way.
        var view = new FreeCamera { Position = new Vector3(100, 200, 300), Yaw = 75f, Pitch = -20f, Roll = 10f };
        camera.FlownTo(view);
        camera.Tick(0.05f, 100);
        Near(view.Position, camera.Frame.Translation);
        Near(view.Forward, camera.Forward);
        Near(view.Up, camera.Up);
        // Back: the game camera, keeping the yaw the free camera left.
        float yaw = camera.Angles.Z;
        Assert.True(camera.BackToGame(who));
        Assert.Equal(CameraMode.Game, camera.Mode);
        Assert.Equal(yaw, camera.Angles.Z);
        // Only from the game camera or a cutscene: not from the look.
        Assert.True(camera.EnterLook());
        Assert.False(camera.EnterFree(who));
    }
}
