// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The Pause key** (in a play-test, pressing the Pause key did not pause the
/// game). 1.1 reads it as `DIK_PAUSE` pressed this frame and, in single player only,
/// flips the game pause with "Game paused." or "Game resumed." in the message box. It
/// is the one flag the escape menu and the save and load screens set too, so each carries on what the
/// other paused. The key is 1.1's only binding for it outside the developer keys, and it stays on the
/// key page, remappable, like every other command.
/// </summary>
public class PauseKeyTests
{
    static FrontEnd Menus(GameFiles files, bool singlePlayer = true)
    {
        var assets = new UiAssets(files);
        var text = new Strings(files);
        return new FrontEnd(n => assets.Pane(n), n => assets.Font(n), text.Find)
        {
            State = FrontEnd.StateGame,
            SinglePlayer = singlePlayer,
        };
    }

    [Fact]
    public void The_Pause_key_pauses_and_carries_on_with_1_1s_own_two_lines()
    {
        using var files = Install.Open();
        var menus = Menus(files);
        var text = new Strings(files);
        Assert.False(menus.Paused);

        Assert.Equal(FrontEnd.PausedLine, menus.PauseKey());
        Assert.True(menus.Paused);
        Assert.Equal("Game paused.", text[FrontEnd.PausedLine]);

        Assert.Equal(FrontEnd.ResumedLine, menus.PauseKey());
        Assert.False(menus.Paused);
        Assert.Equal("Game resumed.", text[FrontEnd.ResumedLine]);
    }

    [Fact]
    public void In_a_network_game_the_Pause_key_does_nothing()
    {
        using var files = Install.Open();
        var menus = Menus(files, singlePlayer: false);
        Assert.Null(menus.PauseKey());
        Assert.False(menus.Paused);
    }

    /// <summary>One flag: the escape menu's resume carries on a game the key paused, and the key carries on one a menu left paused.</summary>
    [Fact]
    public void The_escape_menu_and_the_Pause_key_share_one_pause()
    {
        using var files = Install.Open();
        var menus = Menus(files);
        menus.PauseKey();
        menus.OpenEscape();
        Assert.True(menus.Paused);
        menus.PutEscapeAway();
        Assert.False(menus.Paused);             // the escape menu's close clears it, whoever set it

        menus.Paused = true;                    // as the escape menu leaves it (paused)
        Assert.Equal(FrontEnd.ResumedLine, menus.PauseKey());
        Assert.False(menus.Paused);
    }

    [Fact]
    public void Pause_is_on_the_key_page_and_can_be_moved()
    {
        Assert.Contains(KeysPage.Groups, g => g.Commands.Contains(KeyCommand.Pause));
        var info = KeyBindings.Of(KeyCommand.Pause);
        Assert.Equal(CommandKind.Press, info.Kind);
        Assert.Equal([new KeyChord(KeyNames.Pause)], info.Defaults);

        var keys = new KeyBindings();
        Assert.Equal([KeyCommand.Pause], keys.Pressed(KeyNames.Pause, KeyMods.None, driving: true).Select(f => f.Command));
        // Ctrl+Pause (a Break, and a laptop's usual way to it) is still the key.
        Assert.Equal([KeyCommand.Pause], keys.Pressed(KeyNames.Pause, KeyMods.Ctrl, driving: false).Select(f => f.Command));

        keys.Apply(IniFile.Parse("[keys]\npause = P\n"));
        Assert.Equal([KeyCommand.Pause], keys.Pressed(KeyNames.Letter('P'), KeyMods.None, driving: false).Select(f => f.Command));
        Assert.Empty(keys.Pressed(KeyNames.Pause, KeyMods.None, driving: false));
        Assert.True(KeyBindings.AnyDefault(KeyNames.Pause));   // so the Pause key, moved away, is swallowed
        Assert.Contains(keys.Values(), v => v.Key == "pause" && v.Value == "P");
    }

    /// <summary>
    /// The Pause key stops the game's clock, not the camera: the camera turns by the interface's,
    /// so the keypad still turns a paused game's view — the game's own frame, with
    /// its clock stopped, moves nothing, and the interface's quarter second turns it 45° at 180° a
    /// second. A camera that is not the game camera does not take it.
    /// </summary>
    [Fact]
    public void Paused_the_game_camera_still_turns_by_the_interface_clock()
    {
        var christof = CameraTests.Someone(new Vector3(100, 200, 0), yaw: 0f);
        var camera = new WorldCamera { HeadOf = null };
        camera.Start(christof);
        camera.Tick(1f / 60f, 16);
        float yaw = camera.Angles.Z;

        camera.StopTurning();
        camera.TurnYaw(180f);                   // the keypad's 4, held
        camera.Tick(0f, 16);                    // the game's frame: its clock stopped
        Assert.Equal(yaw, camera.Angles.Z, 3);
        camera.InterfaceTick(0.25f);
        Assert.Equal((yaw + 45f) % 360f, camera.Angles.Z, 3);

        var free = new WorldCamera { HeadOf = null };
        free.Start(null);                       // no one to follow: the free camera
        float freeYaw = free.Angles.Z;
        free.TurnYaw(180f);
        free.InterfaceTick(0.25f);
        Assert.Equal(freeYaw, free.Angles.Z, 3);
    }
}
