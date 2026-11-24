// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ui;

namespace Vitomir.Engine.Tests;

/// <summary>
/// When a run starts with the status bar up (ours, `HudStart`): a play-test found
/// `--load` and `--level` runs coming up without it. Every windowed run with a player starts with
/// it now, `--no-hud` starts one without it, and a headless still keeps it off unless asked — the
/// stills compared across batches were taken without it.
/// </summary>
public class HudStartTests
{
    [Fact]
    public void A_windowed_run_with_a_player_starts_with_the_bar()
    {
        Assert.True(HudStart.On(hud: false, noHud: false, playing: false, headless: false, hasPlayer: true));   // --load, --level --player
        Assert.False(HudStart.On(hud: false, noHud: false, playing: false, headless: false, hasPlayer: false)); // a free camera
    }

    [Fact]
    public void No_hud_starts_it_off_whatever_else_asks()
    {
        Assert.False(HudStart.On(hud: false, noHud: true, playing: false, headless: false, hasPlayer: true));
        Assert.False(HudStart.On(hud: true, noHud: true, playing: true, headless: false, hasPlayer: true));
    }

    [Fact]
    public void A_headless_still_has_it_only_when_asked()
    {
        Assert.False(HudStart.On(hud: false, noHud: false, playing: false, headless: true, hasPlayer: true));
        Assert.True(HudStart.On(hud: true, noHud: false, playing: false, headless: true, hasPlayer: true));
        Assert.True(HudStart.On(hud: false, noHud: false, playing: true, headless: true, hasPlayer: true));    // as earlier
    }
}
