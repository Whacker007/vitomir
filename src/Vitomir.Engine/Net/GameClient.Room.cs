// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The player's half of the game's room (ours, protocol 23): the character chosen there and the host's answer, the host's
// settings changed there, and whether this install has the game's chronicle. 1.1's join carries the character itself and
// has no change of settings once a game is made. The host's half is GameServer.Room.cs.
namespace Vitomir.Engine.Net;

public sealed partial class GameClient
{
    /// <summary>Whether the host accepted <see cref="Me"/> (<see cref="Msg.Chose"/>); a player comes into the game only with an accepted character.</summary>
    public bool Accepted { get; private set; }

    /// <summary>The character the host last accepted, which a refused change leaves in force.</summary>
    NetCharacter? accepted;

    /// <summary>
    /// Whether this install has a chronicle, by its name (the lobby's: its chronicle set); null says nothing to the host,
    /// which then takes it that this machine has it (the command line's `--join`, the tests).
    /// </summary>
    public Func<string, bool>? HasChronicle { get; init; }

    /// <summary>The host's answer to a character asked for: null when accepted, else 1.1's refusal's key (a line in the room).</summary>
    public event Action<string?>? Chosen;

    /// <summary>The host's settings changed in the room (<see cref="Msg.Setup"/>); <see cref="Settings"/> is the new one.</summary>
    public event Action? SettingsChanged;

    /// <summary>Whether this install has the game's chronicle, as last told the host (true until known otherwise).</summary>
    public bool ChronicleHere { get; private set; } = true;

    /// <summary>
    /// **A character chosen in the room** (the lobby's Player Setup, or the one given at the start, sent straight after the
    /// welcome): sent to the host, which answers with acceptance or 1.1's refusal (<see cref="Chosen"/>). Nothing is sent
    /// once this machine is let into the game. A character past the transport's bound is not sent (<see cref="NetJoin.Refused"/>):
    /// the join ends, as it ended when the knock carried it.
    /// </summary>
    public void Choose(NetCharacter character)
    {
        Me = character;
        Accepted = false;
        if (State != ClientState.Room) return;                       // sent with the welcome when it comes
        var w = new NetWriter(Msg.Choose);
        character.Write(w);
        var message = w.ToArray();
        if (NetJoin.Refused(message, character, m => Log?.Invoke(m)) is { } refused) { peer.Disconnect(link); Gone(refused); return; }
        peer.Send(link, Channel.Reliable, message);
        Log?.Invoke($"net: bringing {character.Name} ({character.Template})");
    }

    /// <summary>The welcome: the character given at the start sent (the command line's), and whether the chronicle is here.</summary>
    void Welcomed()
    {
        if (Me is { } me) Choose(me);
        SayChronicle();
    }

    void SayChronicle()
    {
        if (HasChronicle is not { } has || Settings is null) return;
        ChronicleHere = Settings.Chronicle.Length == 0 || has(Settings.Chronicle);
        peer.Send(link, Channel.Reliable, new NetWriter(Msg.HasChronicle).Bool(ChronicleHere).ToArray());
    }

    void InRoom(NetReader r)
    {
        switch (r.Kind)
        {
            case Msg.Chose:
            {
                string key = r.Str();
                if (key.Length == 0)
                {
                    accepted = Me;
                    Accepted = true;
                    Chosen?.Invoke(null);
                    if (StartsItself && CanStart) AskIn();            // a game under way: in at once, as before (GameClient.LetIn.cs)
                    break;
                }
                Log?.Invoke($"net: {Me?.Name} refused — {key}");
                Me = accepted;
                Accepted = accepted is not null;
                Chosen?.Invoke(key);
                // A machine with no lobby has no way to choose again: the join ends with the host's reason, as it did when the
                // knock carried the character.
                if (StartsItself && !Accepted) { Gone(key); peer.Disconnect(link, key); }
                break;
            }
            case Msg.Setup:
                Settings = GameSettings.Read(r);
                Log?.Invoke($"net: the host's game is now \"{Settings.Name}\" — {Settings.Chronicle}");
                SayChronicle();
                SettingsChanged?.Invoke();
                break;
        }
    }
}
