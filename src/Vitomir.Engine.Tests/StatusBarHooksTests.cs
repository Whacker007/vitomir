// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;
using Xunit;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The status bar's show and hide hooks: hiding the bar — F11 or a cutscene — takes every pane
/// and both description boxes down, and each hook runs only on a real change (the hide and
/// the show test the pane's shown bit first). Earlier, Vitomir's bar went and left the pack, the sheet and the rest
/// open behind it.
/// </summary>
public class StatusBarHooksTests
{
    [Fact]
    public void Hiding_the_bar_runs_the_hide_once_and_showing_it_the_show_once()
    {
        var hooks = new StatusBarHooks(shown: true);
        Assert.Equal(StatusBarHooks.Hook.None, hooks.Step(true));      // the show on a shown pane: nothing
        Assert.Equal(StatusBarHooks.Hook.Hide, hooks.Step(false));     // F11, or a cutscene's way in
        Assert.Equal(StatusBarHooks.Hook.None, hooks.Step(false));     // the hide on a hidden pane: nothing
        Assert.False(hooks.Shown);
        Assert.Equal(StatusBarHooks.Hook.Show, hooks.Step(true));      // F11 again, or the cutscene's release
        Assert.True(hooks.Shown);
    }

    [Fact]
    public void A_bar_that_starts_down_runs_nothing_until_it_comes_up()
    {
        var hooks = new StatusBarHooks(shown: false);
        Assert.Equal(StatusBarHooks.Hook.None, hooks.Step(false));
        Assert.Equal(StatusBarHooks.Hook.Show, hooks.Step(true));
    }

    /// <summary>The panes' list: the player's four panes, and in the Storyteller's seat the seat's five after them.</summary>
    [Fact]
    public void The_hide_takes_the_four_panes_down_and_in_the_seat_the_seat_s_five_too()
    {
        Assert.Equal([StatusBarHooks.Pane.Quests, StatusBarHooks.Pane.Inventory, StatusBarHooks.Pane.Disciplines,
                      StatusBarHooks.Pane.Character], StatusBarHooks.Down(seat: false));
        Assert.Equal(9, StatusBarHooks.Down(seat: true).Count);
        Assert.Equal(StatusBarHooks.Pane.StoryScene, StatusBarHooks.Down(seat: true)[^1]);
        Assert.True(StatusBarHooks.BoxesAtOnce);                        // No 0.3 s fade
    }
}
