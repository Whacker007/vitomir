// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The swivel's hold, and the swivel lock** (ours). The host starts and stops the
/// swivel on its key's down and up (1.1 reads its key table every frame, and cannot lose one),
/// so a release that came while a conversation's cutscene had the camera was lost and the swivel stayed
/// on with nothing held. <see cref="SwivelHold"/> ends the swivel when the game camera loses the mouse and
/// re-checks the real keys when it comes back — a repair of our own code. And what was worth keeping of the
/// bug is a command of its own, <see cref="KeyCommand.SwivelLock"/> — Ctrl and the middle button by
/// default, <c>[qol] camera</c>'s — which these tests hold to the binding layer and the ini as well.
/// </summary>
public class SwivelLockTests
{
    static readonly int Middle = KeyChord.MouseBase + 2;

    [Fact]
    public void A_release_lost_in_a_cutscene_ends_the_swivel_and_it_stays_off_when_the_camera_comes_back()
    {
        var hold = new SwivelHold();
        Assert.Equal(SwivelStep.Nothing, hold.Frame(gameCamera: true, look: false, swivelling: true, held: true));   // the drag, held
        // The conversation's cutscene takes the camera: the swivel is let go on that frame, and only then.
        Assert.Equal(SwivelStep.Stop, hold.Frame(gameCamera: false, look: false, swivelling: true, held: true));
        Assert.Equal(SwivelStep.Nothing, hold.Frame(gameCamera: false, look: false, swivelling: false, held: false));
        // The button came up during it. Back in the game camera, nothing is held, so nothing starts.
        Assert.Equal(SwivelStep.Nothing, hold.Frame(gameCamera: true, look: false, swivelling: false, held: false));
        Assert.Equal(SwivelStep.Nothing, hold.Frame(gameCamera: true, look: false, swivelling: false, held: false));
    }

    [Fact]
    public void Still_held_when_the_camera_comes_back_the_swivel_is_on_again()
    {
        var hold = new SwivelHold();
        hold.Frame(true, false, true, true);
        Assert.Equal(SwivelStep.Stop, hold.Frame(false, false, true, true));
        Assert.Equal(SwivelStep.Start, hold.Frame(true, false, false, true));
        // Only on the way back: a key held with no swivel in the game camera is the events' to start.
        Assert.Equal(SwivelStep.Nothing, hold.Frame(true, false, true, true));
    }

    [Fact]
    public void The_first_person_look_keeps_the_hold_it_took_over_and_the_free_camera_s_own_is_left_alone()
    {
        var hold = new SwivelHold();
        hold.Frame(true, false, true, true);
        Assert.Equal(SwivelStep.Nothing, hold.Frame(gameCamera: false, look: true, swivelling: true, held: true));
        // A hold that is not the game camera's (the free camera's drag look) is never let go by this.
        var free = new SwivelHold();
        free.Frame(false, false, false, false);
        Assert.Equal(SwivelStep.Nothing, free.Frame(false, false, true, false));
    }

    [Fact]
    public void The_lock_turns_the_swivel_on_with_nothing_held_and_off_again()
    {
        var hold = new SwivelHold();
        hold.Frame(true, false, false, false);
        Assert.Equal(SwivelStep.Start, hold.Toggle(gameCamera: true));
        Assert.True(hold.Locked);
        Assert.False(hold.ReleaseEnds);                       // letting go of X or the middle button does not end it
        Assert.Equal(SwivelStep.Nothing, hold.Frame(true, false, true, false));
        // Something let the hold go without leaving the game camera: the lock puts it back.
        Assert.Equal(SwivelStep.Start, hold.Frame(true, false, false, false));
        // Off, it stops whatever is held: the lock's own chord is the middle button, down at that moment.
        Assert.Equal(SwivelStep.Stop, hold.Toggle(gameCamera: true));
        Assert.False(hold.Locked);
        Assert.True(hold.ReleaseEnds);
    }

    [Fact]
    public void The_lock_ends_itself_where_the_swivel_must_end_and_does_not_come_back()
    {
        var hold = new SwivelHold();
        hold.Frame(true, false, false, false);
        hold.Toggle(true);
        Assert.Equal(SwivelStep.Stop, hold.Frame(gameCamera: false, look: false, swivelling: true, held: false));   // a menu, a cutscene
        Assert.False(hold.Locked);
        Assert.Equal(SwivelStep.Nothing, hold.Frame(true, false, false, false));
        // First person ends it too, leaving the hold to the look.
        hold.Toggle(true);
        Assert.Equal(SwivelStep.Nothing, hold.Frame(gameCamera: false, look: true, swivelling: true, held: false));
        Assert.False(hold.Locked);
        // And outside the game camera it cannot be turned on.
        Assert.Equal(SwivelStep.Nothing, hold.Toggle(gameCamera: false));
        Assert.False(hold.Locked);
    }

    /// <summary>
    /// **The lock comes back** (the QoL <c>relock</c>): with it on, a pane or a menu opening sets the lock aside — the swivel
    /// stops, the pointer is free — and once everything opened is closed it comes back on by itself; a cutscene still ends it
    /// for good, and with the switch off a pane ends it as before.
    /// </summary>
    [Fact]
    public void With_relock_on_a_pane_or_a_menu_sets_the_lock_aside_and_it_comes_back_once_they_are_closed()
    {
        var hold = new SwivelHold { Relock = true };
        hold.Frame(true, false, false, false);
        hold.Toggle(true);
        // The inventory opens (CentreLook.Opened): set aside, the swivel stopped, waiting.
        Assert.Equal(SwivelStep.Stop, hold.SetAside());
        Assert.False(hold.Locked);
        Assert.True(hold.Waiting);
        Assert.Equal(SwivelStep.Nothing, hold.Resume(clear: false));          // still open
        Assert.Equal(SwivelStep.Start, hold.Resume(clear: true));             // closed: back on
        Assert.True(hold.Locked);
        Assert.False(hold.Waiting);

        // The escape menu takes the game camera's mouse (aside): set aside there too, and back when it is closed.
        Assert.Equal(SwivelStep.Stop, hold.Frame(gameCamera: false, look: false, swivelling: true, held: false, aside: true));
        Assert.True(hold.Waiting);
        Assert.Equal(SwivelStep.Nothing, hold.Frame(gameCamera: false, look: false, swivelling: false, held: false, aside: true));
        Assert.True(hold.Waiting);
        Assert.Equal(SwivelStep.Start, hold.Resume(clear: true));
        Assert.True(hold.Locked);
        Assert.Equal(SwivelStep.Nothing, hold.Frame(true, false, swivelling: true, held: false));   // the next frame, swivelling

        // A cutscene (not aside) ends it, and nothing waits.
        Assert.Equal(SwivelStep.Stop, hold.Frame(gameCamera: false, look: false, swivelling: true, held: false));
        Assert.False(hold.Waiting);
        Assert.Equal(SwivelStep.Nothing, hold.Resume(clear: true));
        Assert.False(hold.Locked);

        // Its own keys turn it off for good, set aside or not.
        hold.Frame(true, false, false, false);
        hold.Toggle(true);
        hold.SetAside();
        Assert.Equal(SwivelStep.Start, hold.Toggle(true));                    // a press while waiting turns it on, as ever
        Assert.False(hold.Waiting);
    }

    [Fact]
    public void With_relock_off_a_pane_ends_the_lock_as_it_always_did()
    {
        var hold = new SwivelHold { Relock = false };
        hold.Frame(true, false, false, false);
        hold.Toggle(true);
        Assert.Equal(SwivelStep.Stop, hold.SetAside());
        Assert.False(hold.Waiting);
        Assert.Equal(SwivelStep.Nothing, hold.Resume(clear: true));
        Assert.False(hold.Locked);
        // Turned off while a lock waits, it does not come back.
        var waiting = new SwivelHold { Relock = true };
        waiting.Frame(true, false, false, false);
        waiting.Toggle(true);
        waiting.SetAside();
        waiting.Relock = false;
        Assert.Equal(SwivelStep.Nothing, waiting.Resume(clear: true));
        Assert.False(waiting.Locked);
    }

    [Fact]
    public void Ctrl_and_the_middle_button_is_the_lock_and_the_middle_button_alone_is_still_the_drag()
    {
        var keys = new KeyBindings();
        Assert.Equal([KeyChord.Mouse(2, KeyMods.Ctrl)], keys[KeyCommand.SwivelLock]);
        Assert.Contains(KeyCommand.SwivelLock, KeyBindings.Camera);
        Assert.True(KeyBindings.IsActed(KeyCommand.SwivelLock));
        Assert.Equal([KeyCommand.SwivelLock], keys.Pressed(Middle, KeyMods.Ctrl, driving: true, camera: true).Select(f => f.Command));
        Assert.Equal([KeyCommand.DragSwivel], keys.Pressed(Middle, KeyMods.None, driving: true, camera: true).Select(f => f.Command));
        // With [qol] camera off it is nothing, and Ctrl with the middle button is 1.1's secondary click again.
        Assert.Equal([KeyCommand.SecondaryClick], keys.Pressed(Middle, KeyMods.Ctrl, driving: true, camera: false).Select(f => f.Command));
        // The free camera's drag look has the middle button while it is up, Ctrl or not.
        Assert.Empty(keys.Pressed(Middle, KeyMods.Ctrl, driving: true, camera: true, flying: true));
    }

    [Fact]
    public void The_lock_is_on_the_key_page_and_in_the_ini_like_every_other_command()
    {
        Assert.Contains(KeysPage.Groups, g => g.Commands.Contains(KeyCommand.SwivelLock));
        Assert.Equal("Ctrl+Middle Click", KeyChord.Mouse(2, KeyMods.Ctrl).ToString());
        var keys = new KeyBindings();
        Assert.Contains(keys.Values(), p => p.Key == "swivellock" && p.Value == "Ctrl+Middle Click");
        keys.Apply(IniFile.Parse("[keys]\nswivellock = Alt+Mouse 5, L\n"));
        Assert.Equal([KeyChord.Mouse(5, KeyMods.Alt), new KeyChord(KeyNames.Letter('L'))], keys[KeyCommand.SwivelLock]);
        Assert.Equal([KeyCommand.SwivelLock], keys.Pressed(KeyNames.Letter('L'), KeyMods.None, driving: false, camera: true).Select(f => f.Command));
    }
}
