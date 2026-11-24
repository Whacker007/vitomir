// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The messages of Vitomir's multiplayer and how each is written. **Ours**: 1.1's own message kinds
//  are DirectPlay payloads of at most 255 bytes whose
// bodies are mostly unread; these carry the same things in a form of our own.
using System.Numerics;
using System.Text;

namespace Vitomir.Engine.Net;

/// <summary>What a message is: its first byte.</summary>
public enum Msg : byte
{
    // ---- the session ----
    /// <summary>Client → host: the knock — the protocol, the password's proof and this machine's build and data (23: no character; it is chosen in the room, <see cref="Choose"/>). Answered by <see cref="Welcome"/> or <see cref="Refused"/>.</summary>
    Hello = 1,
    /// <summary>Host → client: you are in (your slot, the chronicle, the place, the game's flags).</summary>
    Welcome = 2,
    /// <summary>Host → client: you are not (a `UI_NET_*` key — 1.1's own reasons, message 0x3c).</summary>
    Refused = 3,
    /// <summary>Host → all: somebody joined, with their slot, name and character's GUID.</summary>
    Joined = 4,
    /// <summary>Host → all: somebody left (`UI_NET_HASLEFT`, `UI_NET_HASDISCONNECTED`).</summary>
    Left = 5,
    /// <summary>Either way: goodbye, with the reason (a kick is `UI_STCPKICKBUTTON`).</summary>
    Bye = 6,
    /// <summary>Host → one player: your Start lets you in — the place to load and the host's switches (16, until then sent bare to everybody in the room at the host's Start).</summary>
    Start = 7,
    /// <summary>Client → host: the place is loaded; make my character and send me the world.</summary>
    Ready = 8,
    /// <summary>Host → all: the game has moved to another place (1.1's 0x3e/0x3f): load it.</summary>
    Travel = 9,

    // ---- the talk ----
    /// <summary>Client → host: a chat line as typed (1.1's message 7).</summary>
    Say = 10,
    /// <summary>Host → client: a chat line to show (1.1's message 8).</summary>
    Chat = 11,
    /// <summary>Host → client: a line for the message box (1.1's 0x51).</summary>
    Post = 12,

    // ---- the world ----
    /// <summary>Client → host: an order (1.1's message 0x1e).</summary>
    Order = 20,
    /// <summary>Host → client: a thing has come into the world (1.1's 3 and 0x11).</summary>
    Made = 21,
    /// <summary>Host → client: a thing has left it (1.1's 0x10).</summary>
    Removed = 22,
    /// <summary>Host → client, unreliable: where things are and what they are doing.</summary>
    Snapshot = 23,
    /// <summary>Host → client: a pane up or down on the players' machines (1.1's 0x48): on or off, then (14) which — 0 the load screen while the host loads a place, 1 the pause menu of the Storyteller's Pause (<see cref="NetPane"/>).</summary>
    Pause = 24,
    /// <summary>Host → client: your character, whole (health, blood, experience, what it carries).</summary>
    Sheet = 25,
    /// <summary>Client → host: bring me back (1.1's 0x49).</summary>
    Revive = 26,

    // ---- the Storyteller ----
    /// <summary>Unused: the Storyteller is always the host, so nothing of the seat crosses the wire (reserved).</summary>
    Story = 30,

    // ---- conversations and cutscenes (`Net.HostScenes`, `Net.ClientScenes`) ----
    /// <summary>Host → player: run this conversation on your machine (1.1's 0x2e).</summary>
    Converse = 50,
    /// <summary>Player → host: a line spoken, a reply picked, or the end (1.1's 0x30).</summary>
    Said = 51,
    /// <summary>Host → player: a line of somebody else's conversation, overheard (1.1's 0x31).</summary>
    Overheard = 52,
    /// <summary>Host → player: a cutscene shot for your camera (1.1's 0x2b).</summary>
    CameraShot = 53,
    /// <summary>Host → player: your camera back (1.1's 0x2c).</summary>
    CameraRelease = 54,
    /// <summary>Host → player: a camera path for your camera (1.1's 0x2d).</summary>
    CameraPath = 55,
    /// <summary>Player → host: that path has ended (1.1's 0x2d the other way).</summary>
    PathEnded = 56,

    // ---- a player's own character ----
    /// <summary>Player → host: cast a discipline (1.1's 0x18).</summary>
    Cast = 60,
    /// <summary>Player → host: a move in the pack — take off, put on, move, drop, use (1.1's 0x13 … 0x17).</summary>
    Pack = 61,
    /// <summary>Host → players: a projectile has been fired, to fly on their machines as a picture (ours; 1.1's 0x50).</summary>
    Fired = 62,
    /// <summary>
    /// Player → host, unreliable: the keyboard's push on their own character — `[qol] driving` (ours;
    /// 1.1 has no driving and so no message for it). The host walks the character (`GameServer`).
    /// </summary>
    Drive = 63,

    // ---- what scripts show to one player (`Net.HostTexts`, `Net.ClientTexts`) ----
    /// <summary>Host → player: open the read pane on this document (1.1's 0x26).</summary>
    Text = 64,
    /// <summary>Player → host: the read pane a script opened has closed (1.1's 0x27), for `textended`.</summary>
    TextEnded = 65,
    /// <summary>Host → players: a quest has been loaded (1.1's 0x44).</summary>
    QuestLoaded = 66,
    /// <summary>Host → players: a quest's flags (1.1's 0x45).</summary>
    QuestFlags = 67,
    /// <summary>Host → a newcomer: the quests so far, at most eight (1.1's 0x46).</summary>
    QuestList = 68,
    /// <summary>Host → player: open your advancement screen with this much to spend (1.1's 0x37).</summary>
    Advance = 70,
    /// <summary>Player → host: the advancement screen has closed — the stats and disciplines chosen (1.1's 0x36 and 0x38).</summary>
    Advanced = 71,

    // ---- what each machine sees of the others (`Net.HostSeen`, `Net.ClientSeen`) ----
    /// <summary>Host → players: what a character wears — every worn item's GUID, template and slot (1.1's 0x13 … 0x15 and 0x35).</summary>
    Kit = 80,
    /// <summary>Host → players: an effect put on a character (1.1's 0x19).</summary>
    EffectOn = 81,
    /// <summary>Host → players: an effect's end (1.1's 0x1a).</summary>
    EffectOff = 82,
    /// <summary>Player → host: cancel this effect of mine (1.1's 0x1a the other way).</summary>
    EffectCancel = 83,
    /// <summary>Host → players: a blow, to be shown and not dealt (1.1's 0xd).</summary>
    Blow = 84,
    /// <summary>Host → players: a thing's foley (1.1's 0x4b).</summary>
    Foley = 85,
    /// <summary>Host → players: a named sound on a thing (1.1's 0x4c).</summary>
    Sound = 86,
    /// <summary>Host → players: things' flags — render, thing, actor, team (1.1's message 4).</summary>
    Flags = 87,
    /// <summary>Host → players: a hitscan shot, for each machine to fire itself (1.1's 0x50).</summary>
    Shot = 88,

    // ---- the shops and the network arms of the single-player pieces (120–139; `Net.HostShops`, `Net.ClientShops`) ----
    /// <summary>Host → shopper: open this shop on your machine — the store's file, name, keeper, seed, rates and each line's count (1.1's 0x42).</summary>
    Shop = 120,
    /// <summary>Either way (1.1's 0x43): the shopper's purchase, sale or close, and the host's word that one went through.</summary>
    ShopDeal = 121,
    /// <summary>Host → player: what your character has to spend, when it has changed (ours — the sheet carries no purse).</summary>
    Purse = 122,
    /// <summary>Host → players in the place: a strike became a throw, to be replayed with the roll skipped (1.1's 0xc with its last byte 1, replayed as a throw).</summary>
    Thrown = 123,
    /// <summary>Player → host: drop this much money at my character's feet (1.1's 0x1d).</summary>
    DropCash = 124,

    // ---- the multiplayer tail ----
    /// <summary>Host → every other player: a player's character has gone to another place — its slot, the location's index, entrance and name (1.1's 0x3f, which the mover sends to every client; a machine not that player's reads it and takes the character out of its world).</summary>
    Went = 140,
    /// <summary>Host → players in the place: a strike that is not a throw, to be struck again on each machine — the attacker, the target (0 for a point), the weapon's item GUID, the point, the attack, and (ours) the swapped-in weapon's template (1.1's 0xc with its last byte 0, the weapon swapped in by the weapon swap).</summary>
    Struck = 141,
    /// <summary>Host → players in the place: a thing attached to another — child, parent, bone, flags and (ours) the relative matrix (1.1's 0x25: from `AttachThing` on a host, and from the newcomer's catch-up).</summary>
    Attached = 142,
    /// <summary>Host → players: a character's own name (1.1's 0x53: with every character announced, and in the catch-up, read into the thing's name).</summary>
    Named = 143,
    /// <summary>Host → one player: open the haven vault's pane for your character (1.1's 0x47: when the script's `Vault` names a player's character that is not the host's control; read, it opens the vault pane). No payload. `Msg` is a byte, so these numbers are 180-189.</summary>
    Vault = 180,
    /// <summary>Both ways: an item into and out of a character's haven vault — the mover, the item put in, the item taken out (1.1's 0x16: a player's machine → the host → every player, <see cref="VaultMoves"/>).</summary>
    VaultMove = 181,
    /// <summary>Host → one player: what her character's haven vault holds, all of it (15, one message, the transport's pieces carrying it), replacing what her machine had, sent as the host opens it there (ours: 1.1's player's machine knows every item its character has, and Vitomir's sheet no longer carries the vault's; <see cref="VaultMoves.Contents"/>).</summary>
    VaultItems = 182,

    // ---- the LAN ----
    /// <summary>A broadcast: is anybody hosting? (the LAN lobby's refresh).</summary>
    Query = 40,
    /// <summary>The answer: a game's name, chronicle, players and state.</summary>
    GameInfo = 41,
    /// <summary>A broadcast: a line said in the LAN room (`LanRoom`).</summary>
    RoomSay = 42,
    /// <summary>A broadcast every 2 s: this player is in the LAN room — its name (1.1's 0x4e, from the lobby's pump; read into the room's player list) and (ours) the room's token, so two lobbies on one machine are two players. These numbers are 186-190.</summary>
    RoomHere = 186,

    // ---- what a player's machine makes for itself (206-209; `Net.MadeHere`, `Net.ClientSeen`) ----
    /// <summary>Host → players in the place: a body gibbed, for each machine to throw its own chunks under its own no-gore setting and take the body out (1.1's 0x1f with the action 0x1b). The body's GUID. Sent before the body's removal.</summary>
    Gibbed = 206,
    /// <summary>Host → players in the place: a head struck off, for each machine to strike its own off (1.1's 0x1f with the action 0x19). The victim's GUID and the attacker's.</summary>
    Beheaded = 207,
    // ---- the game under way (210-213) ----
    /// <summary>Host → every player: the session's stage (<see cref="SessionStage"/>; 1.1's 0x33's first byte, read into the session's stage) — sent when the host's place is loaded; the `Welcome` carries it too.</summary>
    Stage = 210,
    /// <summary>Player → host: Start — let me into the game under way (1.1's 0x40). No payload; answered by <see cref="Start"/>.</summary>
    LetIn = 211,
    // ---- what a player's machine does for itself (214-217; `Net.SeenRevive`) ----
    /// <summary>Host → players in the place: a character revived, for each machine to revive its own copy — the weapon a death hid shown again (read, each machine revives its copy). The GUID, the health fraction and the blood fraction.</summary>
    Revived = 214,
    // ---- her own walk predicted on her machine (`OwnWalk`; 220-) ----
    /// <summary>Host → one player: a numbered order of hers answered (ours; <see cref="NetOrder.Seq"/>, <see cref="OwnWalk"/>) — its number, what came of it (<see cref="Rpg.OrderResult"/>) and the host's time it was carried out, the snapshots' clock. Sent for every order with a number, refused ones too. A numbered cast's (protocol 22; <see cref="NetCast.Seq"/>, <see cref="OwnCast"/>) carries three more: why (<see cref="Rpg.CastWhy"/>), the discipline's recast clock left and how long the cast holds her there.</summary>
    Taken = 220,
    // ---- the game's room (ours, 230-233): the character chosen there, the host's settings changed there ----
    /// <summary>Player → host: the character this player brings (23; <see cref="NetCharacter"/>), chosen in the room or changed there before the player is let in. Answered by <see cref="Chose"/>.</summary>
    Choose = 230,
    /// <summary>Host → that player: the answer to its <see cref="Choose"/> — empty for accepted (everybody is told by <see cref="Joined"/>), else 1.1's refusal (`UI_MPPC_NONAME`, `UI_NET_JOINDUPLICATE`, `UI_NET_BADTEMPLATE`).</summary>
    Chose = 231,
    /// <summary>Host → every player: the game's settings changed in the room (the host's Game Setup: the chronicle, the name, the options, the most players).</summary>
    Setup = 232,
    /// <summary>Player → host: whether this install has the game's chronicle (sent after the welcome and after each <see cref="Setup"/>); a player without it is not ready.</summary>
    HasChronicle = 233,
}

/// <summary>Writes a message: little-endian numbers, UTF-8 strings with a 16-bit length.</summary>
public sealed class NetWriter
{
    readonly MemoryStream stream = new();
    readonly BinaryWriter w;

    public NetWriter(Msg kind)
    {
        w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write((byte)kind);
    }

    public int Length => (int)stream.Length;
    public NetWriter U8(byte v) { w.Write(v); return this; }
    public NetWriter Bool(bool v) { w.Write(v); return this; }
    public NetWriter I16(short v) { w.Write(v); return this; }
    public NetWriter U16(ushort v) { w.Write(v); return this; }
    public NetWriter I32(int v) { w.Write(v); return this; }
    public NetWriter U32(uint v) { w.Write(v); return this; }
    public NetWriter F32(float v) { w.Write(v); return this; }
    public NetWriter Vec(Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); return this; }
    /// <summary>Raw bytes with an 8-bit length (at most 255).</summary>
    public NetWriter Bytes(ReadOnlySpan<byte> bytes)
    {
        w.Write((byte)bytes.Length);
        w.Write(bytes[..Math.Min(bytes.Length, 255)]);
        return this;
    }
    public NetWriter Str(string? s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        if (bytes.Length > ushort.MaxValue) bytes = bytes[..ushort.MaxValue];
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
        return this;
    }
    public byte[] ToArray() { w.Flush(); return stream.ToArray(); }
}

/// <summary>Reads what <see cref="NetWriter"/> wrote. A short message throws, and the caller drops it.</summary>
public sealed class NetReader
{
    readonly BinaryReader r;

    public NetReader(byte[] data)
    {
        r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        Kind = (Msg)r.ReadByte();
    }

    public Msg Kind { get; }
    public bool More => r.BaseStream.Position < r.BaseStream.Length;
    public byte U8() => r.ReadByte();
    public bool Bool() => r.ReadBoolean();
    public short I16() => r.ReadInt16();
    public ushort U16() => r.ReadUInt16();
    public int I32() => r.ReadInt32();
    public uint U32() => r.ReadUInt32();
    public float F32() => r.ReadSingle();
    public Vector3 Vec() => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
    /// <summary>The bytes left to read.</summary>
    public int Left => (int)(r.BaseStream.Length - r.BaseStream.Position);
    /// <summary>A string: its 16-bit length, then that many bytes of UTF-8 — a length past the message's end is a short message.</summary>
    public string Str()
    {
        int n = r.ReadUInt16();
        if (n > Left) throw new EndOfStreamException($"a string of {n} bytes with {Left} left");
        return Encoding.UTF8.GetString(r.ReadBytes(n));
    }
    /// <summary>Raw bytes, at most <paramref name="most"/>: an 8-bit length, then the bytes.</summary>
    public byte[] Bytes(int most)
    {
        int n = r.ReadByte();
        if (n > most || n > Left) throw new EndOfStreamException($"{n} bytes where at most {Math.Min(most, Left)} may be");
        return r.ReadBytes(n);
    }
    /// <summary>
    /// A count read by <paramref name="read"/>, held to what the rest of the message can carry at <paramref name="each"/>
    /// bytes an item at least (nothing a remote sends makes this end hold more than its message's worth).
    /// </summary>
    public int Count(int count, int each)
    {
        if (count < 0 || (long)count * Math.Max(1, each) > Left) throw new EndOfStreamException($"{count} items of {each} bytes with {Left} left");
        return count;
    }
}

/// <summary>The protocol's own version, checked by the host on every Hello (ours). 1.1's mismatch is `UI_NET_BADPROTOCOL`.</summary>
public static class Protocol
{
    /// <summary>
    /// 3: the welcome and the journey carry the host's switches (<see cref="HostSwitches"/>).
    /// 4: the read pane, the quests and the advancement go to a player's machine
    /// (<see cref="Msg.Text"/> … <see cref="Msg.Advanced"/>), the sheet carries the disciplines, and
    /// the Hello carries the character's saved sheet (<see cref="NetCharacter.Saved"/>).
    /// 5: what each machine sees of the others — worn kit, effects, blows, foley and sounds,
    /// flags and hitscan shots (<see cref="Msg.Kit"/> … <see cref="Msg.Shot"/>); the shops on the
    /// shopper's machine (<see cref="Msg.Shop"/> …), the throw's replay and money dropped from a
    /// player's machine (<see cref="Msg.Thrown"/>, <see cref="Msg.DropCash"/>).
    /// 6: every other player is told where a player has gone (<see cref="Msg.Went"/>), and
    /// every strike is replayed on the players' machines (<see cref="Msg.Struck"/>), attachments and
    /// names cross (<see cref="Msg.Attached"/>, <see cref="Msg.Named"/>).
    /// 10: the game's settings carry where the host's scene is numbered from
    /// (<see cref="GameSettings.SceneGuid"/>), GUIDs being game-wide.
    /// 11: every GUID a host hands out carries 0x40000000 (<see cref="World.ThingGuids.HostBit"/>,
    /// as 1.1's do) — the same messages, but a joining machine of 10 would take the host's numbers for its own count.
    /// 13: the vault handed to a player's machine (<see cref="Msg.Vault"/>).
    /// 14: a LAN game's answer carries its flags (<see cref="GameInfo.Flags"/>, as 1.1's
    /// announcement does), and the LAN room's players say they are there (<see cref="Msg.RoomHere"/>);
    /// the vault's moves and contents (<see cref="Msg.VaultMove"/>, <see cref="Msg.VaultItems"/>; the sheet
    /// carries the vault no longer), and the pause message names its pane.
    /// 15: the sheet, the Hello's and advancement's sheet and the vault's contents carry every item, counted in
    /// a word (the sheet's cap of 24 and the vault's parts gone), the transport sending a long reliable message in
    /// pieces (<see cref="NetPeer.MaxReliableMessage"/>).
    /// 16: a player's machine makes a blow's, a death's, a gib kill's and a beheading's things
    /// itself (<see cref="Msg.Gibbed"/>, <see cref="Msg.Beheaded"/>; the blow says how the death went,
    /// <see cref="SeenBlow.Whole"/>), and the host no longer sends them (<see cref="MadeHere"/>); and
    /// the `Welcome` carries the session's stage and not a started flag, the host's place loaded is told
    /// everybody (<see cref="Msg.Stage"/>), and a player comes into the game by its own Start (<see cref="Msg.LetIn"/>,
    /// answered by <see cref="Msg.Start"/> with the place to load).
    /// 17: a player's machine runs the effects' scripts, so the host no longer sends what they make flagged
    /// `NO NET SYNCH` (<see cref="MadeHere.ByEffect"/>), and a revive is told as 1.1 tells it (<see cref="Msg.Revived"/>).
    /// 18: the sheet carries the actor flags (<see cref="NetSheet.Flags"/>), a snapshot's
    /// character says it is under a move order (<see cref="ThingState.Moving"/>), the LAN room's
    /// line names its speaker by token and not by name (<see cref="LanRoom.Saying"/>), and the host keeps
    /// back what a script makes flagged `NO NET SYNCH` unless the Fix `netprops` is on (<see cref="MadeHere.Scripted"/>).
    /// 19: the sheet carries the second actor flags (<see cref="NetSheet.Flags2"/>); the
    /// flags message carries message 4's whole body (<see cref="ThingFlags"/>): the collide type,
    /// a character's aura, three stats and money, an item's count and flags, a weapon's two loads, a container's flags.
    /// And a thing made carries a prop's owner (<see cref="ThingMade.Owner"/>),
    /// which the network rollover names under a teleporter's end.
    /// 20: the Hello carries the password's proof and not the password (<see cref="PasswordProof"/>), and this
    /// machine's build and data (<see cref="NetIdentity"/>), and a refusal may say what differs; the wire under it is
    /// sealed (<see cref="NetPeer.Version"/> 2, <see cref="NetSeal"/>).
    /// 21: a player's machine numbers its orders (<see cref="NetOrder.Seq"/>) and the host answers each numbered one
    /// (<see cref="Msg.Taken"/>), for her own walk predicted there (<see cref="OwnWalk"/>).
    /// 22: a player's machine numbers its casts too (<see cref="NetCast.Seq"/>, from the orders' count), the host answers
    /// each numbered one with why, the recast clock left and how long it holds her (<see cref="Msg.Taken"/>), and the sheet carries
    /// the host's time and each discipline's recast clock left — for her own cast's start predicted there (<see cref="OwnCast"/>).
    /// 23: the knock carries no character (the identity, the protocol, the password and the room's size are checked there); a
    /// player chooses or changes its character in the room (<see cref="Msg.Choose"/>, answered by <see cref="Msg.Chose"/>), the
    /// host's settings changed in the room go to everybody (<see cref="Msg.Setup"/>), a player says whether it has the
    /// chronicle (<see cref="Msg.HasChronicle"/>), and a slot's name is empty while its player is choosing.
    /// </summary>
    public const int Version = 23;
}
