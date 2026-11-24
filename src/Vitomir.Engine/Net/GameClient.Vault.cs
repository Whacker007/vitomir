// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player's machine asks the host for a vault move: 1.1's message 0x16.
namespace Vitomir.Engine.Net;

public sealed partial class GameClient
{
    /// <summary>
    /// A vault move asked of the host (<see cref="VaultMoves.Ask"/>; 1.1's message 0x16, reliably), its
    /// GUIDs this machine's turned into the host's. Nothing is sent before the game is being played, or for a mover
    /// the host does not know.
    /// </summary>
    public void Send(VaultMove move)
    {
        if (mirror is null || State != ClientState.Playing) return;
        int H(int g) => g != 0 && mirror.Place.Thing(g) is { } t ? mirror.HostGuid(t) : 0;
        int mover = H(move.Mover);
        if (mover == 0 || (move.In == 0 && move.Out == 0)) return;                 // 1.1 asserts: an actor, and something
        var w = new NetWriter(Msg.VaultMove);
        new VaultMove(mover, H(move.In), H(move.Out)).Write(w);
        peer.Send(link, Channel.Reliable, w.ToArray());
    }
}
