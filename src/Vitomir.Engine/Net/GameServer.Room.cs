// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's half of the game's room (ours, protocol 23): each player's character chosen or changed there and checked in
// 1.1's words, the host's settings changed there and sent to everybody, whether each player's install has the chronicle,
// and whether every player is ready for the host's Start. 1.1 takes the character with the join itself and checks its name
// and template there; its lobby has no way to change the game's settings once it is made. The player's half is
// GameClient.Room.cs.
namespace Vitomir.Engine.Net;

public sealed partial class NetPlayer
{
    /// <summary>
    /// Whether the player's install has the game's chronicle, as its machine last said (<see cref="Msg.HasChronicle"/>). Taken to
    /// be so until it says otherwise: a machine with no lobby (the command line's `--join`, the tests) never says.
    /// </summary>
    public bool HasChronicle { get; set; } = true;

    /// <summary>Whether the player's machine says whether it has the chronicle (it has said so once), so a change of chronicle waits for its word.</summary>
    public bool SaysChronicle { get; set; }
}

public sealed partial class GameServer
{
    /// <summary>The room heard a character accepted or changed, or a player's install lacking the chronicle (the host's own lobby shows it).</summary>
    public event Action? RoomChanged;

    void Received(NetPlayer who, Msg kind, NetReader r)
    {
        switch (kind)
        {
            case Msg.Choose: Choose(who, NetCharacter.Read(r)); break;
            case Msg.HasChronicle:
                who.HasChronicle = r.Bool();
                who.SaysChronicle = true;
                if (!who.HasChronicle) Log?.Invoke($"net: {who.Name}'s install has no {Settings.Chronicle}");
                RoomChanged?.Invoke();
                break;
        }
    }

    /// <summary>
    /// **A player's character, chosen in the room** (or changed there, before the player is let in): its name must be
    /// a name (`UI_MPPC_NONAME`), no other player's (`UI_NET_JOINDUPLICATE`, compared as 1.1's join compares, case and all —
    /// the host's own box saying "&lt;name&gt; rejected - duplicate name", `UI_NET_DUPLICATENAME`), and once the host's place is
    /// up its template one the place has (`UI_NET_BADTEMPLATE`) — 1.1's own checks of its join, made here. Accepted, the player is
    /// told (<see cref="Msg.Chose"/>, empty), everybody learns the slot's name (<see cref="Msg.Joined"/>) and the room hears
    /// `UI_NET_HASJOINED` the first time, or that the player changed character (ours) after. Refused, the player is told
    /// 1.1's reason and keeps what it had.
    /// </summary>
    void Choose(NetPlayer p, NetCharacter character)
    {
        if (p.LetIn) { Log?.Invoke($"net: {p.Name} chose a character in the game; kept the one it has"); return; }
        if (Check(p, character) is { } refusal)
        {
            Send(p, Channel.Reliable, new NetWriter(Msg.Chose).Str(refusal).ToArray());
            Log?.Invoke($"net: {p.Name}'s character {NetCharacter.CleanName(character.Name)} refused — {refusal}");
            return;
        }
        Send(p, Channel.Reliable, new NetWriter(Msg.Chose).Str("").ToArray());
        Accept(p, character);
    }

    /// <summary>
    /// **The host's own character, chosen in its room** (the lobby's Player Setup): checked as a player's is
    /// (<see cref="Choose"/>), and 1.1's refusal's key answered, or null when it is taken.
    /// </summary>
    public string? ChooseHost(NetCharacter character)
    {
        if (slots[0] is not { } me || Started) return null;
        if (Check(me, character) is { } refusal) { Log?.Invoke($"net: the host's character {character.Name} refused — {refusal}"); return refusal; }
        Accept(me, character);
        return null;
    }

    string? Check(NetPlayer p, NetCharacter character)
    {
        string name = NetCharacter.CleanName(character.Name);
        if (name.Length == 0) return "UI_MPPC_NONAME";
        if (Players.Any(o => o != p && o.HasCharacter && NetJoin.SameName(o.Name, name)))
        {
            Posted?.Invoke($"{name} {Find(NetJoin.DuplicateName) ?? NetJoin.DuplicateName}", 0xffffffffu);
            return "UI_NET_JOINDUPLICATE";
        }
        if (Place?.Templates is { } set && !set.Has(character.Template)) return "UI_NET_BADTEMPLATE";
        return null;
    }

    void Accept(NetPlayer p, NetCharacter character)
    {
        bool first = !p.HasCharacter;
        string was = p.Name;
        p.Character = character with { Name = NetCharacter.CleanName(character.Name) };
        foreach (var o in Players.Where(o => o.Link is not null)) Send(o, Channel.Reliable, Joined(p));
        if (first) Room("UI_NET_HASJOINED", p.Name);
        else if (was != p.Name) Words($"{was} is now {p.Name}.");                    // ours: 1.1's join has no change of character
        Log?.Invoke($"net: slot {p.Slot} is {p.Name} ({p.Character.Template}, {p.Character.Type})");
        RoomChanged?.Invoke();
    }

    /// <summary>A line of ours (no 1.1 key) to the whole room.</summary>
    void Words(string words)
    {
        Posted?.Invoke(words, 0xffffffffu);
        foreach (var p in Players.Where(p => p.Link is not null))
            Send(p, Channel.Reliable, new NetWriter(Msg.Post).Str("").Str(words).U32(0xffffffffu).Str("").ToArray());
    }

    /// <summary>
    /// **The host's settings changed in the room** (the lobby's Game Setup; ours — 1.1's game is made once and kept): the
    /// chronicle, the name, the password, the options and the most players, before the game starts. The place the game is
    /// played in follows the chronicle (<see cref="GameSettings.For"/>). Everybody is sent the new settings
    /// (<see cref="Msg.Setup"/>), and a player whose machine says whether it has the chronicle is not ready until it says so again.
    /// </summary>
    public void Change(GameSettings settings)
    {
        if (Started) return;
        bool other = !string.Equals(settings.Chronicle, Settings.Chronicle, StringComparison.OrdinalIgnoreCase);
        Settings = settings with { SceneGuid = Settings.SceneGuid };
        if (other) foreach (var p in Players.Where(p => p.SaysChronicle)) p.HasChronicle = false;
        var w = new NetWriter(Msg.Setup);
        Settings.Write(w);
        var message = w.ToArray();
        foreach (var p in Players.Where(p => p.Link is not null)) Send(p, Channel.Reliable, message);
        Words(other ? $"The host has chosen the chronicle {Find(Settings.Chronicle) ?? Settings.Chronicle}." : "The host has changed the game's settings.");
        Log?.Invoke($"net: the game is now \"{Settings.Name}\" — {Settings.Chronicle}, at most {Settings.MaxPlayers}");
        RoomChanged?.Invoke();
    }

    /// <summary>
    /// **Whether the host's Start is lit** (ours: 1.1's is lit for the host whatever): every player has a character the host
    /// accepted — the host's own too, when it plays — and every player's install has the chronicle.
    /// </summary>
    public bool ReadyToStart => Players.All(p => (p.Host && !HostPlays) || (p.HasCharacter && p.HasChronicle));
}
