// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's half of what scripts show to one player: the read pane (the text native's
// server arm and message 0x26, the close back as 0x27), the message-box lines of the three print
// natives, the quest log (a quest loaded, its
// flags changed, the join's list) and the advancement screen
// (`Advance(player)`, message 0x37; the answer 0x36 / 0x38). Who gets each and what the host does itself are 1.1's; the messages' bytes are ours.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>What the host's read-pane arm did with a script's `ExecuteText`.</summary>
public enum TextRoute
{
    /// <summary>For the character this machine steers: open the pane here, as single player does.</summary>
    Here,
    /// <summary>
    /// Anybody else's: sent to their machine when they are a player, and the native answers **true**
    /// either way — the native returns 1 without opening anything for a reader that is not the host's own.
    /// </summary>
    Elsewhere,
}

public sealed class HostTexts(GameServer server)
{
    /// <summary>The longest line a print sends: 200 bytes with the terminator (the `200` cap).</summary>
    public const int LineLimit = 199;
    /// <summary>A string key's room in the messages (`_strncpy(…, 0x20)`, the last byte the terminator).</summary>
    public const int KeyLimit = 31;
    /// <summary>The radius of a print to those nearby by default — `CONSOLEPRINT_FLAG_RADIUSMED` and any other flag (500).</summary>
    public const float RadiusMedium = 500f;
    /// <summary>`CONSOLEPRINT_FLAG_RADIUSSMALL` (0x4)'s radius (100).</summary>
    public const float RadiusSmall = 100f;
    /// <summary>`CONSOLEPRINT_FLAG_RADIUSLARGE` (0x10)'s radius (1000).</summary>
    public const float RadiusLarge = 1000f;

    /// <summary>`CONSOLEPRINT_FLAG_SERVERCONSOLE` (`Codex.java`): the host's own box only, whatever the GUID.</summary>
    public const int ServerConsole = 0x1;
    /// <summary>`CONSOLEPRINT_FLAG_BROADCAST`: every player, and the host.</summary>
    public const int Broadcast = 0x2;
    /// <summary>`CONSOLEPRINT_FLAG_RADIUSSMALL`.</summary>
    public const int RadiusSmallFlag = 0x4;
    /// <summary>`CONSOLEPRINT_FLAG_RADIUSLARGE`.</summary>
    public const int RadiusLargeFlag = 0x10;

    /// <summary>The most quests the join's list carries (eight).</summary>
    public const int QuestListLimit = 8;

    /// <summary>
    /// A player's read pane has closed (message 0x27 read): the script GUID and the reader,
    /// for the scripts to hear `textended(reader)`. The host does not check that the reader
    /// is the sender's own character, and neither does this.
    /// </summary>
    public event Action<int, int>? TextEnded;

    /// <summary>
    /// A player's advancement screen has closed and its choices are applied to their character on the
    /// host: the player and their character.
    /// </summary>
    public event Action<NetPlayer, WorldThing>? Advanced;

    /// <summary>The player (slot 1 or above) whose character has this GUID — the slot walk each arm makes (stride 0x50).</summary>
    NetPlayer? Remote(int guid) =>
        guid == 0 ? null : server.Players.FirstOrDefault(p => !p.Host && p.Avatar?.Guid == guid);

    /// <summary>
    /// The character the host's machine steers: the body the Storyteller has, else the
    /// host's own. A headless host has none.
    /// </summary>
    int HostSteered => server.Possessed?.Guid ?? server.Slots[0]?.Avatar?.Guid ?? 0;

    // ---- the read pane ---------------------------------------------------------------------------

    /// <summary>
    /// `ExecuteText(script, reader, thing, name)` on the host (net mode 2): the
    /// slot table is walked for the reader, and for a player the pane goes to their machine (message
    /// 0x26: the script, the reader, the thing and the document's name). Then, unless the
    /// reader is the character this machine steers, the native answers true having opened nothing —
    /// which is why the MP endings' `if (!ExecuteText(…)) Advance(…)` never takes its fallback for a
    /// player: their advance comes with the close (`textended`).
    /// </summary>
    public TextRoute Text(int script, int reader, int thing, string name)
    {
        if (Remote(reader) is { } p)
        {
            server.Reliable(p, new NetWriter(Msg.Text).I32(script).I32(reader).I32(thing).Str(Cap(name, KeyLimit)).ToArray());
            server.Note($"net: {p.Name} reads {name}");
        }
        return reader != 0 && reader == HostSteered ? TextRoute.Here : TextRoute.Elsewhere;
    }

    internal void Closed(NetReader r)
    {
        int reader = r.I32(), script = r.I32();
        TextEnded?.Invoke(script, reader);
    }

    // ---- the message box ---------------------------------------------------------------------------

    /// <summary>
    /// `Print(client, flags, text, colour)`'s network arm, asked for any flags but
    /// <see cref="ServerConsole"/>; answers whether the host's own box shows it too. <see cref="Broadcast"/>
    /// goes to everybody and the host. Flags 0 go to the one player whose character the GUID is — and
    /// only to the host when it is the host's own; nowhere when nobody has it. Any other flags go to
    /// every player in the thing's place within the radius (100, 500 or 1,000), and to the host when its
    /// own character stands that near. A GUID that is no thing prints nowhere.
    /// </summary>
    public bool Print(int client, int flags, string text, uint colour)
    {
        text = Cap(text, LineLimit);
        var line = Post("", text, colour);
        if (flags == Broadcast) { Everyone(line); return true; }
        if (Thing(client) is not { } at) return false;
        if (flags == 0)
        {
            if (Slot(client) is not { } p) return false;
            if (p.Host) return true;
            server.Reliable(p, line);
            return false;
        }
        return Nearby(at, Radius(flags), line);
    }

    /// <summary>
    /// `PrintNLS(client, flags, key, colour)`'s network arm (message 0x29): the
    /// key goes to the player whose character the GUID is, whatever the flags, and their machine says it
    /// in its own words; answers false then. For anybody else — the host's own, or nobody — the host
    /// prints it itself (true).
    /// </summary>
    public bool PrintKey(int client, string key, uint colour)
    {
        if (Remote(client) is not { } p) return true;
        server.Reliable(p, Post(Cap(key, KeyLimit), "", colour));
        return false;
    }

    /// <summary>
    /// `PrintFormatNLS(client, flags, key, pairs, colour)`'s network arm (message 0x51): the
    /// key and its pairs, formatted on each machine. <see cref="Broadcast"/> goes to everybody and the
    /// host; a GUID that is no thing prints on the host. Flags 0 go to that player — **and then on
    /// through the radius sweep** (the code falls from the send into it), so
    /// everybody within 500 of them in the same place gets it too, the player a second time, and the
    /// host when its character stands that near; the host's own character's line prints on the host
    /// alone, and a GUID no player has prints nowhere. Any other flags: the radius sweep. Answers
    /// whether the host prints it.
    /// </summary>
    public bool PrintFormat(int client, int flags, string key, string pairs, uint colour)
    {
        var line = Post(Cap(key, KeyLimit), Cap(pairs, LineLimit), colour);
        if (flags == Broadcast) { Everyone(line); return true; }
        if (Thing(client) is not { } at) return true;
        if (flags == 0)
        {
            if (Slot(client) is not { } p) return false;
            if (p.Host) return true;
            server.Reliable(p, line);
            return Nearby(at, RadiusMedium, line);
        }
        return Nearby(at, Radius(flags), line);
    }

    /// <summary>The radius a print's flags ask for: small, large, or the medium for anything else.</summary>
    public static float Radius(int flags) => flags == RadiusSmallFlag ? RadiusSmall : flags == RadiusLargeFlag ? RadiusLarge : RadiusMedium;

    /// <summary>
    /// The radius sweep: every player in the game (slot state 4) whose character
    /// is in <paramref name="at"/>'s place within the radius is sent the line; answers whether the host's
    /// own character is as near, when the host prints it too. A thing in no place prints nowhere.
    /// </summary>
    bool Nearby(WorldThing at, float radius, byte[] line)
    {
        if (at.Place is not { } place) return false;
        foreach (var p in server.Players)
            if (!p.Host && p.Ready && p.Avatar is { } near && ReferenceEquals(near.Place, place)
                && Vector3.Distance(near.Position, at.Position) <= radius)
                server.Reliable(p, line);
        return server.Slots[0]?.Avatar is { } mine && ReferenceEquals(mine.Place, place)
               && Vector3.Distance(mine.Position, at.Position) <= radius;
    }

    // ---- the quest log -------------------------------------------------------------------------------

    /// <summary>A quest the host has just loaded (message 0x44): every player's machine loads it too.</summary>
    public void QuestLoaded(string name) => Everyone(new NetWriter(Msg.QuestLoaded).Str(Cap(name, KeyLimit)).ToArray());

    /// <summary>A quest's flags changed on the host (message 0x45): every player's machine takes them.</summary>
    public void QuestFlags(string name, uint flags) =>
        Everyone(new NetWriter(Msg.QuestFlags).Str(Cap(name, KeyLimit)).U32(flags).ToArray());

    /// <summary>
    /// The quests so far, for a newcomer (message 0x46 — sent at the join): the first
    /// <see cref="QuestListLimit"/> by slot, each name with its flags. **Ours**: sent at every arrival,
    /// because a player's machine has a quest log to put them in only once its place is loaded.
    /// </summary>
    internal void QuestList(NetPlayer p)
    {
        if (server.Place?.Session?.Quests is not { } log) return;
        var some = log.All.OrderBy(q => q.Slot).Take(QuestListLimit).ToList();
        var w = new NetWriter(Msg.QuestList).U8((byte)some.Count);
        foreach (var q in some) w.Str(Cap(q.Name, KeyLimit)).U32(q.Flags);
        server.Reliable(p, w.ToArray());
    }

    // ---- the advancement screen ------------------------------------------------------------------------

    /// <summary>
    /// `Advance(player)` on the host: for a player's character, their machine opens its
    /// advancement screen with what it has to spend (message 0x37: the GUID and the points to spend)
    /// and the native is done — answers true. False for anybody else (the host's own, or no player),
    /// which single player's advance handles; `Advance(0)` is the host's walk,
    /// the caller's too.
    /// </summary>
    public bool Advance(int guid)
    {
        if (Remote(guid) is not { Avatar.Actor: { } actor } p) return false;
        // The numbers first (as the walk does; ours for a script's too), so the screen opens on
        // what the host has — the player's machine holds its sheet from here until the screen closes.
        server.Reliable(p, GameServer.Sheet(p.Avatar!, server.NowMs));
        server.Reliable(p, new NetWriter(Msg.Advance).I32(guid).I32((int)Math.Min(actor.SpendableExperience, int.MaxValue)).I32(0).ToArray());
        server.Note($"net: {p.Name} advances, {actor.SpendableExperience} to spend");
        return true;
    }

    /// <summary>
    /// The host's advancement walk is asked for — a script's `Advance(0)` or the
    /// Storyteller's Advancement button: the host's screen opens on slot 0's character and its Next
    /// walks every player's (<see cref="WalkList"/>). A host with no screen does nothing.
    /// </summary>
    public event Action? WalkAsked;

    /// <summary>`Advance(0)` on the host.</summary>
    public void Walk() => WalkAsked?.Invoke();

    /// <summary>
    /// Whom the walk shows, in turn: slot 0's character, then every player in the
    /// game (slot state 4) with one, in slot order. Moving on from a player's character sends it its
    /// numbers and opens its own screen on its machine (<see cref="Advance"/>).
    /// </summary>
    public List<WorldThing> WalkList()
    {
        var walk = new List<WorldThing>();
        if (server.Slots[0]?.Avatar is { } mine) walk.Add(mine);
        foreach (var p in server.Players.Where(p => !p.Host && p.Ready).OrderBy(p => p.Slot))
            if (p.Avatar is { Actor: not null, InWorld: true } body) walk.Add(body);
        return walk;
    }

    /// <summary>
    /// The answer (0x36 read): the player's character takes the
    /// numbers and the disciplines their machine sends, **without checks** — 1.1's host trusts it — and
    /// the character is then theirs to save (<see cref="NetSheet"/>).
    /// </summary>
    internal void Answered(NetPlayer p, NetReader r)
    {
        var sheet = NetSheet.Read(r);
        if (p.Avatar is not { Place: { } place } me) return;                   // wherever the character is (HostPlaces)
        sheet.Apply(me, place, withPack: false);
        server.Note($"net: {p.Name} has advanced — {me.Actor?.SpendableExperience} left to spend");
        Advanced?.Invoke(p, me);
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    static byte[] Post(string key, string args, uint colour) =>
        new NetWriter(Msg.Post).Str(key).Str(args).U32(colour).Str("").ToArray();

    WorldThing? Thing(int guid) => guid == 0 ? null : server.Places.Working?.Thing(guid);   // the place the scripts work in (HostPlaces)

    /// <summary>The slot whose character this is (the flags-0 walk: any slot in use, the host's included).</summary>
    NetPlayer? Slot(int guid) =>
        server.Players.FirstOrDefault(p => p.Avatar?.Guid == guid && ReferenceEquals(p.Avatar.Place, server.Places.Working))
        ?? server.Players.FirstOrDefault(p => p.Avatar?.Guid == guid);

    void Everyone(byte[] message)
    {
        foreach (var p in server.Players.Where(p => !p.Host)) server.Reliable(p, message);
    }

    static string Cap(string? s, int most) => s is null ? "" : s.Length > most ? s[..most] : s;
}
