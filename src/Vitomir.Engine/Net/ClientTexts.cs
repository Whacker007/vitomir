// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player's half of what scripts show to one player: the read pane a script on the host opens for
// this player (message 0x26, the close sent back as 0x27), the
// quest log kept in step with the host's (0x44, 0x45, the join's list 0x46), and the advancement screen opened here whose choices go back.
// The message-box lines need nothing here: they arrive as `Msg.Post`.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

public sealed class ClientTexts
{
    readonly Action<byte[]> send;
    readonly Func<int> me;
    (int Script, int Reader)? reading;

    internal ClientTexts(Action<byte[]> send, Func<int> me)
    {
        this.send = send;
        this.me = me;
    }

    /// <summary>Opens this machine's read pane on a document by name; false when it cannot (already up, or no such document) — the harness's `ExecuteText`.</summary>
    public Func<string, bool>? Open { get; set; }
    /// <summary>This machine's quest log, which the host's quests are copied into.</summary>
    public QuestLog? Quests { get; set; }

    /// <summary>
    /// The host asks this machine to open its advancement screen on this player's character, with what
    /// the host says it has to spend already set.
    /// </summary>
    public event Action<WorldThing>? AdvanceAsked;

    /// <summary>
    /// Whether this machine's advancement screen is up: the host's sheets do not overwrite the
    /// numbers being spent meanwhile (ours — 1.1's host sends no running sheet to fight with).
    /// </summary>
    public bool Advancing { get; set; }

    /// <summary>The document the pane was opened on for the host, or null.</summary>
    public string? Reading { get; private set; }

    /// <summary>
    /// Message 0x26: only for the character this machine steers — the reader must be it —
    /// and then the text native with its "from the net" argument, which opens the pane and keeps
    /// the script and the reader for the close.
    /// </summary>
    internal void Text(NetReader r)
    {
        int script = r.I32(), reader = r.I32();
        r.I32();                                                        // the thing (1.1 keeps it; the pane does not need it)
        string name = r.Str();
        if (reader == 0 || reader != me()) return;
        if (Open?.Invoke(name) != true) return;
        reading = (script, reader);
        Reading = name;
    }

    /// <summary>
    /// This machine's read pane has closed (on a player's machine): when the host opened it,
    /// the host is told (message 0x27 — the reader and the script), for the script's
    /// `textended`. A book read from the pack has no script, and nothing is sent.
    /// </summary>
    public void Closed()
    {
        if (reading is not { } r) return;
        reading = null;
        Reading = null;
        if (r.Script != 0) send(new NetWriter(Msg.TextEnded).I32(r.Reader).I32(r.Script).ToArray());
    }

    /// <summary>Message 0x44: the quest loaded here too (with its "from the net" argument).</summary>
    internal void QuestLoaded(NetReader r)
    {
        string name = r.Str();
        Quests?.Load(name);
    }

    /// <summary>Message 0x45: the quest by that name, when this machine has it, takes the host's flags.</summary>
    internal void QuestFlags(NetReader r)
    {
        string name = r.Str();
        uint flags = r.U32();
        if (Quests?.Find(name) is { } q) Quests.TakeFlags(q, flags);
    }

    /// <summary>Message 0x46: every quest here ended, then each of the host's loaded and given its flags.</summary>
    internal void QuestList(NetReader r)
    {
        int n = r.U8();
        var list = new List<(string, uint)>();
        for (int i = 0; i < n; i++) list.Add((r.Str(), r.U32()));
        if (Quests is not { } log) return;
        log.Clear();
        foreach (var (name, flags) in list.Take(HostTexts.QuestListLimit))
            if (log.Load(name) is { } q) log.TakeFlags(q, flags);
    }

    /// <summary>
    /// Message 0x37: the character must be this machine's own player; it takes the
    /// experience the host says it has to spend and the advancement screen opens on it.
    /// </summary>
    internal void Advance(NetReader r, WorldMirror? mirror)
    {
        int guid = r.I32(), spend = r.I32();
        r.I32();
        if (guid == 0 || guid != me() || mirror?.Find(guid) is not { Actor: { } a } body || AdvanceAsked is null) return;
        a.SpendableExperience = Math.Max(0, spend);
        Advancing = true;
        AdvanceAsked?.Invoke(body);
    }

    /// <summary>
    /// The advancement screen here has closed (on a player's machine): the numbers and the
    /// disciplines chosen go to the host (message 0x36; 0x38), which takes them as
    /// they are. The pack does not go (the host has it). Answers the sheet sent.
    /// </summary>
    public NetSheet? Advanced(WorldThing body)
    {
        Advancing = false;
        if (body.Actor is null) return null;
        var sheet = NetSheet.From(body) with { Pack = [] };
        var w = new NetWriter(Msg.Advanced);
        sheet.Write(w);
        send(w.ToArray());
        return sheet;
    }
}
