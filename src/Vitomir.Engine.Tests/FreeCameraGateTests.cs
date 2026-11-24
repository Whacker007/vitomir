// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **Ctrl+P's way into the free camera** (*"if it's on, then the player can press ctrl p and use that
/// mode. Off, that free camera mode doesn't work."*). 1.1 opens it only with its developer switch (the options
/// word's 0x80, <c>-devmode</c>), never on a player's machine and on a host only in a moderated game. Vitomir's QoL
/// <c>freecamera</c>, off by default, stands beside <c>--devmode</c>; <c>--debug</c> no longer opens it; the rest of
/// 1.1's test stands.
/// </summary>
public class FreeCameraGateTests
{
    static string Nowhere => Path.Combine(Path.GetTempPath(), "vitomir-no-such-settings");

    [Fact]
    public void The_switch_is_off_by_default_and_named_freecamera()
    {
        Assert.False(new Qol().FreeCameraKey);
        Assert.True(Qol.Known("freecamera"));
        Assert.True(Qol.Parse(["--qol", "freecamera"], Nowhere, userPath: Path.Combine(Nowhere, "user.ini")).FreeCameraKey);
        Assert.False(Qol.Parse(["--qol", "all", "--no-qol", "freecamera"], Nowhere, userPath: Path.Combine(Nowhere, "user.ini")).FreeCameraKey);
    }

    [Fact]
    public void Off_ctrl_p_does_nothing_on_it_does_and_devmode_opens_it_as_1_1_s_does()
    {
        Assert.False(FreeCameraGate.Opens(new Qol(), 0, client: false, host: null));
        Assert.True(FreeCameraGate.Opens(new Qol { FreeCameraKey = true }, 0, client: false, host: null));
        Assert.True(FreeCameraGate.Opens(new Qol(), GameOptions.DevMode, client: false, host: null));   // -devmode
        Assert.False(FreeCameraGate.Opens(new Qol(), 0x40, client: false, host: null));                // -console is not it
    }

    /// <summary>The network branches: never on a player's machine; on a host only with Storyteller moderation (0x400).</summary>
    [Fact]
    public void Never_on_a_player_s_machine_and_on_a_host_only_in_a_moderated_game()
    {
        var on = new Qol { FreeCameraKey = true };
        Assert.False(FreeCameraGate.Opens(on, GameOptions.DevMode, client: true, host: null));
        Assert.False(FreeCameraGate.Opens(on, 0, client: false, host: GameFlags.CoOp));
        Assert.True(FreeCameraGate.Opens(on, 0, client: false, host: GameFlags.Moderated | GameFlags.CoOp));
        Assert.Equal(HostSwitches.Governor.Machine, HostSwitches.Of("qol", "freecamera"));
    }
}
