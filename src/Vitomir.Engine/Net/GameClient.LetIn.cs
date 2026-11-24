// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The player's half of "Start into a game under way": the session's stage as the host last said it, the lobby's
// Start that asks to be let in, and the host's answer; and the other players' characters named by their players'
// names on this machine. The host's half is GameServer.LetIn.cs.
namespace Vitomir.Engine.Net;

public sealed partial class GameClient
{
    /// <summary>
    /// The session's stage as the host last said it (<see cref="SessionStage"/>; 1.1's copy of message
    /// 0x33's first byte): <see cref="SessionStage.Forming"/> until the host's place is loaded in a started
    /// game, then <see cref="SessionStage.UnderWay"/>. 0 until the host answers the join.
    /// </summary>
    public byte Stage { get; private set; }

    /// <summary>
    /// **Ours**: this machine asks to be let in by itself as soon as the game is under way, as if its Start were
    /// pressed — the command line's `--join` and the tests, which have no lobby. The lobby turns it off, and the
    /// player presses Start (<see cref="AskIn"/>), as in 1.1.
    /// </summary>
    public bool StartsItself { get; set; } = true;

    /// <summary>
    /// Whether the lobby's Start is lit for this player (a player's machine, network mode 1, lights it only
    /// while the stage is 2): in the game's room, the game under way — and (ours) with a character the host accepted and
    /// the game's chronicle in this install (GameClient.Room.cs).
    /// </summary>
    public bool CanStart => State == ClientState.Room && Stage == SessionStage.UnderWay && Accepted && ChronicleHere;

    /// <summary>
    /// **The player's Start** (not once the character is placed, the session's state at 4; then
    /// the start's network mode 1 arm — the party cleared, and message 0x40 sent, empty and reliable). The host answers only a player still in its room, with the place to load
    /// (<see cref="Msg.Start"/>). Nothing is sent from any other state.
    /// </summary>
    public void AskIn()
    {
        if (State != ClientState.Room) return;
        peer.Send(link, Channel.Reliable, new NetWriter(Msg.LetIn).ToArray());
        Log?.Invoke("net: Start — asking the host to be let in");
    }

    /// <summary>The stage heard (the `Welcome`'s, or <see cref="Msg.Stage"/>): kept, and asked in by itself when <see cref="StartsItself"/>.</summary>
    void Staged(byte stage)
    {
        Stage = stage;
        if (StartsItself && CanStart) AskIn();
    }

    /// <summary>
    /// The host's answer to this machine's Start (message 0x3f, read for this machine's own
    /// slot): the place to load — the chronicle's revive location — and the host's switches; the load begins.
    /// </summary>
    void LetInAnswered(NetReader r)
    {
        Settings = GameSettings.Read(r);
        Switches(HostSwitches.Read(r));
        Begin();
    }

    /// <summary>
    /// **Another player's character named by its player's name** (the handler of message 0x32 writes the
    /// player's network name into the character's name, which the rollover gives before the description in a network
    /// game; <see cref="Ui.Rollover.NameOf"/>). Vitomir's player record and the character itself arrive apart (a
    /// `Joined` and a `Made`), so each names the character when the other is already here.
    /// </summary>
    void NamePlayersCharacter(int guid)
    {
        if (guid == 0 || mirror?.Find(guid) is not { Actor: { } actor }) return;
        foreach (var p in players.Values)
            if (p.Avatar == guid && p.Name.Length > 0) { actor.Name = p.Name; return; }
    }
}
