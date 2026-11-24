// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// Whether a frame runs behind an inactive window (`WindowActive`, a comparison with 1.1): 1.1's
/// WM_ACTIVATE keeps one flag — set by the focus with the window not minimised — and
/// WinMain's loop runs no frame at all while it is clear, unless a network
/// session is up. Pinned because Vitomir ran on behind the window until then: Alt+Tab out of a
/// fight and it went on without the player.
/// </summary>
public class WindowActiveTests
{
    [Fact]
    public void A_single_player_game_stands_still_while_its_window_has_lost_the_focus()
    {
        var active = new WindowActive();
        Assert.True(active.Runs(network: false));       // ours: active until told otherwise
        active.Focus(false);
        Assert.False(active.Active);
        Assert.False(active.Runs(network: false));
        active.Focus(true);
        Assert.True(active.Runs(network: false));
    }

    [Fact]
    public void A_minimised_window_is_not_active_even_with_the_focus()
    {
        var active = new WindowActive();
        active.Minimise(true);
        Assert.False(active.Runs(network: false));
        active.Minimise(false);
        Assert.True(active.Runs(network: false));
    }

    [Fact]
    public void A_network_game_runs_on_behind_an_inactive_window()
    {
        var active = new WindowActive();
        active.Focus(false);
        active.Minimise(true);
        Assert.True(active.Runs(network: true));
    }
}
