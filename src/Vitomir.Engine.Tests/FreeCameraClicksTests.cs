// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The mouse's clicks in the free camera**: 1.1's input frame runs the click
/// dispatcher there — twice — and hands the orders no thing,
/// since only the game camera's pick fills the rollover's; the BLOCKSELECT swallow it
/// tests first is the last game-camera pick's. Which button is a click is the bindings': with
/// <c>[qol] driving</c> on the middle button is the free camera's drag look, with it off 1.1's flag
/// click, which the dispatcher sends to the ground order with its Shift bit (bit 1 set).
/// </summary>
public class FreeCameraClicksTests
{
    [Fact]
    public void A_click_goes_to_the_world_once_in_the_game_camera_twice_in_the_free_camera_and_never_otherwise()
    {
        Assert.Equal(1, FreeCameraClicks.Dispatches(CameraMode.Game));
        Assert.Equal(2, FreeCameraClicks.Dispatches(CameraMode.Free));
        Assert.Equal(0, FreeCameraClicks.Dispatches(CameraMode.Look));
        Assert.Equal(0, FreeCameraClicks.Dispatches(CameraMode.Path));
        Assert.Equal(0, FreeCameraClicks.Dispatches(CameraMode.Cutscene));
    }

    [Fact]
    public void In_the_free_camera_a_click_acts_on_the_ground_never_on_a_thing()
    {
        Assert.True(FreeCameraClicks.Picks(CameraMode.Game));
        Assert.False(FreeCameraClicks.Picks(CameraMode.Free));
    }

    [Fact]
    public void The_free_camera_keeps_the_last_game_camera_swallow()
    {
        // The game camera: what the pick finds now.
        Assert.True(FreeCameraClicks.Swallowed(CameraMode.Game, pickedNow: true, lastGameCameraPick: false));
        Assert.False(FreeCameraClicks.Swallowed(CameraMode.Game, pickedNow: false, lastGameCameraPick: true));
        // The free camera: whatever the game camera left — a raising door under the pointer at the
        // Ctrl+P refuses every click until the game camera picks again.
        Assert.True(FreeCameraClicks.Swallowed(CameraMode.Free, pickedNow: false, lastGameCameraPick: true));
        Assert.False(FreeCameraClicks.Swallowed(CameraMode.Free, pickedNow: true, lastGameCameraPick: false));
    }

    /// <summary>
    /// The split the free camera's keys have: with driving on, the middle button up in the free
    /// camera is the drag look's and no click; with driving off (and <c>[qol] camera</c>'s drag swivel
    /// off) it is 1.1's third binding, handed on as the left button with Shift, for the world alone.
    /// The left and right buttons are the dispatcher's either way.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_middle_button_is_a_click_in_the_free_camera_only_with_driving_off(bool driving)
    {
        var keys = new KeyBindings();
        int left = KeyChord.MouseBase + 1, middle = KeyChord.MouseBase + 2, right = KeyChord.MouseBase + 3;
        var got = keys.Pressed(middle, KeyMods.None, driving, camera: false, flying: true).Select(p => p.Command).ToList();
        if (driving) Assert.Empty(got);
        else
        {
            Assert.Equal([KeyCommand.SecondaryClick], got);
            Assert.Equal((1, InputModifier.Shift, true), KeyBindings.ClickAs(KeyCommand.SecondaryClick));
        }
        Assert.Equal([KeyCommand.Act], keys.Pressed(left, KeyMods.None, driving, flying: true).Select(p => p.Command));
        Assert.Equal([KeyCommand.Cast], keys.Pressed(right, KeyMods.None, driving, flying: true).Select(p => p.Command));
    }
}
