// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Codex natives that ask about the network, answered from a game the host is running. In single
// player `CodexScenes` answers them as 1.1 does with no network session: `NetIsServer` true, no
// players; this puts the host's own answers over those for a network game.
using Vitomir.Engine.Net;

namespace Vitomir.Scripting;

public static class NetNatives
{
    /// <summary>
    /// Answers the network natives from <paramref name="server"/> (registered after
    /// <see cref="CodexScenes"/>'s own, so these win):
    /// <list type="bullet">
    /// <item>`NetIsConnected`, `NetIsServer`, `NetIsClient` — the
    /// session's word; scripts run only on the host, so a client never asks;</item>
    /// <item>`NetIsNoAutoConversations` and `NetIsNoAutoAdvance` — both
    /// `~flags >> 11 & 1`: true unless the game runs with Auto Storyteller (0x800);</item>
    /// <item>`GetMaxMPPlayers` — the session's count; `GetNumMPPlayers` (the same
    /// taken-slot count the WON listing uses) — how many are in; `GetMPPlayer(n)`
    /// — slot n's character's GUID, 0 for an empty slot, a slot whose character is not in
    /// the world (or, ours, in another of the host's places), or n outside 0 to 7.</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// <see cref="Register(CodexHost, GameServer)"/>, and the natives that show things to one player
    /// sent to that player's machine, and the arrivals and closed read panes told to the scripts
    /// (<see cref="CodexScenes.Host"/>).
    /// </summary>
    public static void Register(CodexScenes scenes, GameServer server)
    {
        Register(scenes.Engine, server);
        scenes.Host(server);
    }

    public static void Register(CodexHost engine, GameServer server)
    {
        engine.On("Codex.NetIsConnected", (_, _) => CodexHost.Box(true));
        engine.On("Codex.NetIsServer", (_, _) => CodexHost.Box(true));
        engine.On("Codex.NetIsClient", (_, _) => CodexHost.Box(false));
        engine.On("Codex.NetIsNoAutoConversations", (_, _) => CodexHost.Box(!server.Flags.HasFlag(GameFlags.AutoStoryteller)));
        engine.On("Codex.NetIsNoAutoAdvance", (_, _) => CodexHost.Box(!server.Flags.HasFlag(GameFlags.AutoStoryteller)));
        engine.On("CodexPlayer.GetMaxMPPlayers", (_, _) => CodexHost.Box(server.Settings.MaxPlayers & 0xff));
        engine.On("CodexPlayer.GetNumMPPlayers", (_, _) => CodexHost.Box(server.Players.Count() & 0xff));
        engine.On("CodexPlayer.GetMPPlayer", (_, a) =>
        {
            int n = CodexHost.Int(a, 0);
            if (n < 0 || n >= GameSettings.Slots) return CodexHost.Box(0);
            // **Ours**: 0 too for a character in another of the host's places — a GUID names a thing of one
            // place in Vitomir (1.1's are the whole game's), and the script asking works in its own.
            return CodexHost.Box(server.Slots[n]?.Avatar is { InWorld: true } body
                                 && (body.Place is null || server.Places.Working is not { } here || ReferenceEquals(body.Place, here)) ? body.Guid : 0);
        });
    }
}
