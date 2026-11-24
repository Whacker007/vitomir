// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a game is made with (`mpCreateGame`) and the flags it carries.
namespace Vitomir.Engine.Net;

/// <summary>
/// The game flags: four are the create screen's boxes, three the
/// Storyteller's controls, and one says a password was given.
/// </summary>
[Flags]
public enum GameFlags : uint
{
    None = 0,
    /// <summary>0x4: the game has a password (set with the password).</summary>
    Password = 0x4,
    /// <summary>0x8: closed to newcomers — the Storyteller's Game Lock; a join fails with `UI_NET_JOINCLOSED`.</summary>
    Locked = 0x8,
    /// <summary>0x80: the Storyteller's Comm Lock, `UI_STCPCOMMSLOCKED`.</summary>
    CommLocked = 0x80,
    /// <summary>
    /// 0x100: **co-op** — the create screen's first option, `mpcgCoOpCombative`, *checked*. Set, every
    /// joiner is on team 0 (<see cref="NetJoin.TeamOf"/>); clear, the game is combative, each player
    /// their own team, and the lobby's game pane says `UI_NET_COMBATIVE`. Named `Combative` until
    /// The opposite of what the set bit means (a comparison with 1.1).
    /// </summary>
    CoOp = 0x100,
    /// <summary>0x200: Track Humanity.</summary>
    TrackHumanity = 0x200,
    /// <summary>0x400: Storyteller moderation — only then can the host take the Storyteller's seat.</summary>
    Moderated = 0x400,
    /// <summary>0x800: Auto Storyteller — the chronicle's scripts converse and advance on their own (`NetIsNoAutoAdvance`, is its absence).</summary>
    AutoStoryteller = 0x800,
}

/// <summary>A game as the host made it.</summary>
public sealed record GameSettings
{
    /// <summary>The game's name (`mpcgNameEdit`, 32 characters; `UI_NET_DEFAULTGAME` "%P's game" by default).</summary>
    public string Name { get; init; } = "";
    /// <summary>The chronicle's file name without `.nsc` (`MP_CurseDarkness`), or empty for a bare level.</summary>
    public string Chronicle { get; init; } = "";
    /// <summary>The level and the scene the game is played in (the chronicle's first location unless told).</summary>
    public string Level { get; init; } = "";
    public string Scene { get; init; } = "";
    /// <summary>The entrance the players arrive at (the chronicle's portal, or 0).</summary>
    public int Entrance { get; init; }
    public GameFlags Flags { get; init; } = GameFlags.AutoStoryteller;
    /// <summary>The password, empty for none (32 characters).</summary>
    public string Password { get; init; } = "";
    /// <summary>
    /// How many may play, the host among them: 1.1's table has eight slots, and its session's count (read by
    /// `GetMaxMPPlayers`) is set to 8 and never changed, so a game of 1.1's allows eight (four
    /// until then). The create screen's most players and `--max-players` choose fewer.
    /// </summary>
    public int MaxPlayers { get; init; } = Slots;
    /// <summary>
    /// The first GUID the host's scene of this place was numbered from, so a
    /// joining machine can put its copy of the place on the host's numbers (<see cref="World.Location.RenumberScene"/>) —
    /// the host's GUIDs come from the one game-wide counter. 1.1 sends the scene's GUIDs
    /// themselves instead, 32 a message (message 0x41). 0 for "as numbered".
    /// </summary>
    public int SceneGuid { get; init; }

    /// <summary>
    /// A game of a chronicle: played where its players start — the `reviveloc` line, which the MP
    /// chronicles all give ("where a multiplayer character revives, and where joining clients
    /// start"), else its first location — at that line's entrance.
    /// </summary>
    public static GameSettings For(World.Chronicle chronicle, string name, GameFlags flags, string password = "", int maxPlayers = Slots)
    {
        var data = chronicle.Data;
        int at = data.ReviveLocation >= 0 ? data.ReviveLocation : 0;
        var place = chronicle.At(at) ?? chronicle.At(0);
        return new GameSettings
        {
            Name = name, Chronicle = chronicle.Name, Level = place?.LevelName ?? "", Scene = place?.SceneName ?? "",
            Entrance = data.ReviveLocation >= 0 ? data.ReviveEntrance : 0,
            Flags = flags | (password.Length > 0 ? GameFlags.Password : 0), Password = password, MaxPlayers = maxPlayers,
        };
    }

    /// <summary>1.1's ceiling: eight slots of 0x50 bytes, and `GetMPPlayer` refuses 8 and above.</summary>
    public const int Slots = 8;

    public void Write(NetWriter w) =>
        w.Str(Name).Str(Chronicle).Str(Level).Str(Scene).I32(Entrance).U32((uint)Flags).U8((byte)MaxPlayers).I32(SceneGuid);

    public static GameSettings Read(NetReader r) => new()
    {
        Name = r.Str(), Chronicle = r.Str(), Level = r.Str(), Scene = r.Str(), Entrance = r.I32(),
        Flags = (GameFlags)r.U32(), MaxPlayers = r.U8(), SceneGuid = r.I32(),
    };
}

/// <summary>A player as everybody sees them: the slot, the name, the character's GUID once it is in the world.</summary>
public sealed record PlayerInfo(int Slot, string Name, string Template, int Avatar, bool Host, bool Storyteller)
{
    public void Write(NetWriter w) => w.U8((byte)Slot).Str(Name).Str(Template).I32(Avatar).Bool(Host).Bool(Storyteller);
    public static PlayerInfo Read(NetReader r) => new(r.U8(), r.Str(), r.Str(), r.I32(), r.Bool(), r.Bool());
}

/// <summary>
/// What a LAN query is answered with: the lobby's row `"%s (%d)"` and what the lobby's game
/// pane shows of it. 1.1's own announcement carries the name
/// and the chronicle (32 characters each), <b>the game's flags</b> (which the pane
/// reads for its options), the protocol, the room's size, how many are in it and DirectPlay's session
/// GUID; Vitomir's carries the port in the GUID's place and whether the game has started (ours).
/// A game of another protocol is listed all the same (<see cref="ProtocolVersion"/>): 1.1 keeps it, and
/// only Join Game refuses it (`UI_NET_BADPROTOCOL`, the record's protocol tested against 0x10;
/// `LobbyJoin`). The answer's layout after the protocol is taken to be this version's (ours).
/// </summary>
public sealed record GameInfo(string Name, string Chronicle, int Players, int MaxPlayers, bool Started, int Port,
                              GameFlags Flags = GameFlags.None, int ProtocolVersion = Protocol.Version)
{
    public byte[] ToArray() =>
        new NetWriter(Msg.GameInfo).I32(ProtocolVersion).Str(Name).Str(Chronicle).U32((uint)Flags).U8((byte)Players)
            .U8((byte)MaxPlayers).Bool(Started).I32(Port).ToArray();

    public static GameInfo? Read(byte[] data)
    {
        try
        {
            var r = new NetReader(data);
            if (r.Kind != Msg.GameInfo) return null;
            int protocol = r.I32();
            string name = r.Str(), chronicle = r.Str();
            var flags = (GameFlags)r.U32();
            return new GameInfo(name, chronicle, r.U8(), r.U8(), r.Bool(), r.I32(), flags, protocol);
        }
        catch (EndOfStreamException) { return null; }
    }

    /// <summary>The lobby's row: the name and how many are in it (`"%s (%d)"`).</summary>
    public string Row => $"{Name} ({Players})";
}
