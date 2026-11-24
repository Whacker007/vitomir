// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The camera's mouse button**. `[qol] camera` swivelled on a right-button drag, so a
/// right click that moved more than four pixels was a swivel and not a cast. So the
/// middle button moves the camera, and the right button stays the ordinary right click: the
/// swivel's drag is a command of its own, <see cref="KeyCommand.DragSwivel"/> — the middle button,
/// handed on as 1.1's swivel key X, held and dragged — live only while the switch is on,
/// when it takes the middle button from 1.1's secondary click (its third binding) as a driving
/// key takes its key; and the right button is always 1.1's cast. It is on the key page and in the ini.
/// </summary>
public class CameraButtonTests
{
    static readonly int Middle = KeyChord.MouseBase + 2, Right = KeyChord.MouseBase + 3;

    static IEnumerable<KeyCommand> Pressed(KeyBindings keys, int input, bool camera) =>
        keys.Pressed(input, KeyMods.None, driving: true, camera).Select(f => f.Command);

    [Fact]
    public void With_the_camera_switch_on_the_middle_button_is_the_swivel_handed_on_as_X()
    {
        var keys = new KeyBindings();
        Assert.Equal([KeyCommand.DragSwivel], Pressed(keys, Middle, camera: true));
        Assert.Equal(new KeyChord(KeyNames.Letter('X')), KeyBindings.HandedOn(KeyCommand.DragSwivel));
        Assert.Equal(KeyBindings.Of(KeyCommand.Swivel).Defaults[0], KeyBindings.HandedOn(KeyCommand.DragSwivel));
        // Shift held with it is still the swivel: the chord asks for no modifier.
        Assert.Equal([KeyCommand.DragSwivel], keys.Pressed(Middle, KeyMods.Shift, driving: false, camera: true).Select(f => f.Command));
    }

    [Fact]
    public void With_it_off_the_middle_button_is_1_1s_secondary_click_again()
    {
        var keys = new KeyBindings();
        Assert.Equal([KeyCommand.SecondaryClick], Pressed(keys, Middle, camera: false));
        Assert.Equal([KeyCommand.SecondaryClick], keys.Pressed(Middle, KeyMods.None, driving: false).Select(f => f.Command));
    }

    [Fact]
    public void The_right_button_is_always_the_cast()
    {
        var keys = new KeyBindings();
        Assert.Equal([KeyCommand.Cast], Pressed(keys, Right, camera: true));
        Assert.Equal([KeyCommand.Cast], Pressed(keys, Right, camera: false));
        Assert.DoesNotContain(new KeyChord(Right), keys[KeyCommand.DragSwivel]);
        Assert.DoesNotContain(new KeyChord(Right), keys[KeyCommand.Swivel]);
    }

    [Fact]
    public void The_drag_swivel_is_on_the_key_page_and_moves_through_the_ini()
    {
        Assert.Contains(KeysPage.Groups, g => g.Commands.Contains(KeyCommand.DragSwivel));
        Assert.True(KeysPage.RowOf(KeyCommand.DragSwivel) > 0);
        Assert.Equal("Drag Swivel", KeyBindings.Of(KeyCommand.DragSwivel).Name);
        Assert.Contains(new KeyBindings().Values(), v => v.Key == "dragswivel" && v.Value == "Middle Click");

        // Bound to nothing: the middle button is the secondary click with the switch on as well.
        var none = new KeyBindings();
        none.Apply(IniFile.Parse("[keys]\ndragswivel = none\n"));
        Assert.Empty(none[KeyCommand.DragSwivel]);
        Assert.Equal([KeyCommand.SecondaryClick], Pressed(none, Middle, camera: true));

        // Moved to a key: V swivels while held, and the middle button is 1.1's click again.
        var key = new KeyBindings();
        key.Apply(IniFile.Parse("[keys]\ndragswivel = V\n"));
        Assert.Equal([KeyCommand.DragSwivel], Pressed(key, KeyNames.Letter('V'), camera: true));
        Assert.Equal([KeyCommand.SecondaryClick], Pressed(key, Middle, camera: true));
        Assert.Empty(Pressed(key, KeyNames.Letter('V'), camera: false));
    }
}
