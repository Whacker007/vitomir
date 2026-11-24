// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The pointer kept in the window and held at its edge (`PointerHold`), which is
/// what lets a push against the side of the screen keep turning the camera as 1.1's own pointer
/// does (the interface holds it inside the screen while the mouse keeps reporting): confined while the
/// game camera has the mouse, held in relative mode while it is in an edge strip, let go — and put
/// back where the game's pointer is — when it comes away, and out of the swivel's way.
/// </summary>
public class PointerHoldTests
{
    [Fact]
    public void The_pointer_is_confined_while_the_game_camera_has_the_mouse()
    {
        var hold = new PointerHold();
        Assert.Equal(new PointerHold.Change(true, null), hold.Frame(worldHasMouse: true, atEdge: false, swivelling: false));
        Assert.Equal(new PointerHold.Change(null, null), hold.Frame(true, false, false));
        // A menu or a shop takes the mouse: let go, so the player can leave the window.
        Assert.Equal(new PointerHold.Change(false, null), hold.Frame(false, false, false));
    }

    [Fact]
    public void A_push_against_the_edge_is_held_until_the_pointer_comes_away()
    {
        var hold = new PointerHold();
        hold.Frame(true, false, false);
        Assert.Equal(new PointerHold.Change(null, true), hold.Frame(true, atEdge: true, false));
        Assert.True(hold.Holding);
        Assert.Equal(new PointerHold.Change(null, null), hold.Frame(true, true, false));
        Assert.Equal(new PointerHold.Change(null, false), hold.Frame(true, atEdge: false, false));
        Assert.False(hold.Holding);
    }

    [Fact]
    public void The_swivel_keeps_relative_mode_for_itself()
    {
        var hold = new PointerHold();
        hold.Frame(true, false, false);
        hold.Frame(true, true, false);
        // X pressed at the edge: the swivel owns relative mode now and lets go of it itself.
        Assert.Equal(new PointerHold.Change(null, null), hold.Frame(true, true, swivelling: true));
        Assert.False(hold.Holding);
    }

    [Fact]
    public void A_menu_opened_at_the_edge_lets_go_of_both()
    {
        var hold = new PointerHold();
        hold.Frame(true, false, false);
        hold.Frame(true, true, false);
        Assert.Equal(new PointerHold.Change(false, false), hold.Frame(false, true, false));
    }
}
