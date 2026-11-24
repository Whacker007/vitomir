// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's half of "Start into a game under way": the session's stage (1.1's session
// keeps it), sent to the players when the host's place is loaded, and a player's Start
// answered with the place to load. The player's half is GameClient.LetIn.cs.
namespace Vitomir.Engine.Net;

/// <summary>
/// **The session's stage** (a byte of the session's, which the lobby reads too): 0 at the
/// session's reset, <see cref="Forming"/> when a game is hosted
/// or joined, and <see cref="UnderWay"/> on the host once its place is loaded in a started game
/// — which sends it to everybody in the slot table's message 0x33 (the stage's byte
/// first). A player's machine copies that byte into its own, and its lobby's **Start** is lit
/// only at <see cref="UnderWay"/> (greyed while it is not a host and the stage is not 2).
/// </summary>
public static class SessionStage
{
    /// <summary>The game is forming: hosted or joined, the host's place not yet loaded (1).</summary>
    public const byte Forming = 1;
    /// <summary>The host's place is loaded in a started game (2): a player's Start lets it in.</summary>
    public const byte UnderWay = 2;
}

public sealed partial class NetPlayer
{
    /// <summary>
    /// The host has let this player into the game (1.1's slot state 5, set as the host answers the player's
    /// Start): false from the join, while the player waits in the game's room (slot state 2 — its chat heard as the
    /// room's, and left out of the whole game's journey), until its Start is answered.
    /// Meaningless for the host's own seat.
    /// </summary>
    public bool LetIn { get; set; }
}

public sealed partial class GameServer
{
    /// <summary>
    /// The session's stage as the host has it (<see cref="SessionStage"/>): forming until the host's place is loaded
    /// (<see cref="Enter"/>), under way after. 1.1's host writes 2 there and nothing writes it
    /// back until the session's reset, which Vitomir's host has as the server's end.
    /// </summary>
    public byte Stage => Place is null ? SessionStage.Forming : SessionStage.UnderWay;

    /// <summary>
    /// The host's place is loaded (behind the host's network mode): the stage is <see cref="SessionStage.UnderWay"/>
    /// and every player is told (message 0x33's first byte; here <see cref="Msg.Stage"/>). **Nobody
    /// is taken in by it**: 1.1's host start sends no player a place — each player's machine lights its Start, and the
    /// player's Start asks to be let in (<see cref="AnswerStart"/>). Players in the room when the host started and players
    /// who join later come in the same way. (The slot table the rest of 0x33 carries is Vitomir's `Welcome`, `Joined`
    /// and `Left`.)
    /// </summary>
    void UnderWay()
    {
        var message = new NetWriter(Msg.Stage).U8(Stage).ToArray();
        foreach (var p in Players.Where(p => p.Link is not null)) Send(p, Channel.Reliable, message);
    }

    /// <summary>
    /// **A player's Start** (message 0x40): the sender's
    /// slot found, and only a player still in the room is answered (the host returns at once for any slot state but 2).
    /// The chronicle's revive location is loaded (with its entrance, both kept in
    /// the slot), the slot marked let in (state 5), and that player alone is sent the place to load
    /// (message 0x3f; here <see cref="Msg.Start"/> with the settings and the host's switches) — <see cref="JoinSettings"/>,
    /// which gives the host's own place when the revive location is it. When the player's machine says it is ready the
    /// arrival is the one every newcomer has (the host's `Ready`). **Ours**: a Start before the host's place is loaded
    /// is ignored (1.1's button is greyed then, so none comes), and so is one from a player with no character or without
    /// the chronicle (its button is greyed then too).
    /// </summary>
    void AnswerStart(NetPlayer p)
    {
        if (p.Host || p.LetIn || Place is null) return;
        // Ours: a player comes in with a character the host accepted, and with the chronicle (GameServer.Room.cs).
        if (!p.HasCharacter || !p.HasChronicle) { Log?.Invoke($"net: {p.Name} pressed Start with {(p.HasCharacter ? "no chronicle" : "no character")}; not let in"); return; }
        p.LetIn = true;
        var w = new NetWriter(Msg.Start);
        JoinSettings(p).Write(w);
        HostSwitches.Write(w, FixesInForce, QolInForce);
        Send(p, Channel.Reliable, w.ToArray());
        Log?.Invoke($"net: {p.Name} pressed Start and is let in");
    }
}
