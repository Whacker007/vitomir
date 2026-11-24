// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A player's half of 1.1's network conversations and cutscenes: the conversation the host sends is run here, in this
// machine's pane with this machine's voices, and what happens in it goes back; the camera does the
// cutscene shots and paths a script on the host named this player for; and the lines of other
// players' conversations nearby come up as subtitles. It reads 0x2e, 0x31, 0x2b, 0x2c and 0x2d, and
// holds the conversation's client arms.
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

public sealed class ClientScenes : IConversationReport
{
    readonly Action<byte[]> send;
    WorldMirror? mirror;
    WorldThing? local;
    readonly SubtitleClock subtitle = new();

    internal ClientScenes(Action<byte[]> send) => this.send = send;

    /// <summary>This machine's one running conversation (the harness's, with its pane and voices).</summary>
    public Conversations? Talk { get; set; }
    /// <summary>This machine's camera, for the cutscenes a script names this player for.</summary>
    public WorldCamera? Camera { get; set; }
    /// <summary>A line's words by its id (`strings/*.nvo`), for an overheard line; null shows the id.</summary>
    public Func<string, string?>? Words { get; set; }
    /// <summary>The string table, for the speaker's name in an overheard line; null shows the cast id.</summary>
    public Func<string, string>? Names { get; set; }

    /// <summary>A subtitle line to add to the subtitle pane (added, not replacing, and the pane's timer extended).</summary>
    public event Action<string>? Subtitled;
    /// <summary>A conversation from the host has started here; the harness puts its pane up.</summary>
    public event Action<Conversation>? Conversing;

    /// <summary>When the overheard subtitle comes down, on the place's clock, or 0 when none is up (<see cref="SubtitleClock"/>).</summary>
    public int SubtitleUntil => subtitle.Until;

    internal void Enter(WorldMirror m, WorldThing? me)
    {
        mirror = m;
        local = me;
    }

    internal void Leave()
    {
        mirror = null;
        local = null;
    }

    int Now => mirror?.Place.Milliseconds ?? 0;

    /// <summary>
    /// Message 0x2e: the conversation found in this machine's own files and
    /// started as the one running here (started with its "from the net" argument), with this player
    /// as the one talking and nobody as the one talked to, no script, and the flags the host sent.
    /// Its lines, reply and end are reported back (<see cref="IConversationReport"/>).
    /// </summary>
    internal void Converse(NetReader r)
    {
        string file = r.Str(), name = r.Str();
        int flags = r.I32();
        r.I32();                                                           // the host's npc (ours; 1.1 sends none)
        if (Talk is not { } talk || mirror?.Place is not { Files: { } files } place) return;
        if (!files.TryRead("conversations/" + file, out var data)) return;
        NcoFile read;
        try { read = NcoFile.Read(data, file); }
        catch (NodFormatException) { return; }
        if (read.Conversation(name) is not { } conversation) return;
        var made = talk.Run(place, conversation, (ConversationFlag)flags, local?.Guid ?? 0, Now, c => c.Report = this);
        Conversing?.Invoke(made);
    }

    int Host(int guid) =>
        guid == 0 || mirror is null ? 0 : mirror.Place.Thing(guid) is { } t ? mirror.HostGuid(t) : 0;

    void IConversationReport.Line(Conversation c, int eventIndex, int stepIndex, int ms, int speakerGuid) =>
        send(new NetWriter(Msg.Said).U8(0).I32(eventIndex).I32(stepIndex).I32(ms).I32(Host(speakerGuid)).ToArray());

    void IConversationReport.Reply(Conversation c, int eventIndex, int stepIndex, int ms, int speakerGuid) =>
        send(new NetWriter(Msg.Said).U8(1).I32(eventIndex).I32(stepIndex).I32(ms).I32(Host(speakerGuid)).ToArray());

    void IConversationReport.Over(Conversation c, bool aborted, int code) =>
        send(new NetWriter(Msg.Said).U8(2).I32(code).Bool(aborted).ToArray());

    /// <summary>
    /// Message 0x31: somebody else's line nearby. A spoken line's name is the speaker's
    /// cast id, looked up in this machine's string table; a reply's is the player's name as it is.
    /// The words are the line's, or the id itself when there are none, and both show as
    /// `&lt;name&gt; words`.
    /// </summary>
    internal void Overheard(NetReader r)
    {
        bool spoken = r.U8() == 0;
        string name = r.Str(), id = r.Str();
        Subtitle(Overheard(spoken, name, id, Names, Words));
    }

    /// <summary>The subtitle an overheard line makes.</summary>
    public static string Overheard(bool spoken, string name, string id, Func<string, string>? names, Func<string, string?>? words)
    {
        string who = spoken && names is not null ? names(name) : name;
        return Conversation.Subtitle(who, words?.Invoke(id) is { Length: > 0 } said ? said : id);
    }

    /// <summary>A subtitle line added and the pane's timer extended.</summary>
    public void Subtitle(string text)
    {
        subtitle.Add(text, Now);
        Subtitled?.Invoke(text);
    }

    /// <summary>The overheard subtitle has had its time: forget it (the harness takes the pane down).</summary>
    public bool SubtitleDone() => subtitle.Done(Now);

    /// <summary>
    /// Message 0x2b: only with a character of this player's in the world. The two things
    /// are framed (`SetupCutscene` on this machine, which enters the cutscene and takes its default shot) and
    /// then the shot is taken.
    /// </summary>
    internal void Shot(NetReader r)
    {
        int shot = r.U8(), angle = r.U8(), distance = r.U8(), side = r.U8();
        int ms = r.I32(), a = r.I32(), b = r.I32();
        if (Camera is not { } camera || mirror is null || local is not { InWorld: true }) return;
        var first = mirror.Find(a);
        if (first is null) return;
        camera.SetupCutscene(first, mirror.Find(b));
        camera.SetShot(shot, angle, distance, side, ms);
    }

    /// <summary>Message 0x2c: the camera back to the game, behind this player's character.</summary>
    internal void Released() => Camera?.Release(local);

    /// <summary>
    /// Message 0x2d: the path, from this machine's `paths/`, played on this camera for this
    /// player; when it ends, the host is told (0x2d back) so the script hears `pathended`.
    /// </summary>
    internal void Path(NetReader r)
    {
        string name = r.Str();
        float speed = r.F32();
        if (Camera is not { } camera || mirror?.Place.Files is not { } files) return;
        if (!files.TryRead("paths/" + name, out var data)) return;
        NcpCameraPath ncp;
        try { ncp = NcpCameraPath.Read(data, name); }
        catch (NodFormatException) { return; }
        camera.PlayPath(ncp, speed, () => send(new NetWriter(Msg.PathEnded).ToArray()));
    }
}

/// <summary>
/// The subtitle pane's timer as 1.1 runs it for a line added from outside a conversation: each
/// line adds **1,500 ms and its reading time** (<see cref="Conversation.ReadingTime"/>) to
/// what is left, at most **25 seconds** in all; a pane that had come down starts again from nothing.
/// </summary>
public sealed class SubtitleClock
{
    int left, at;

    /// <summary>What the pane adds for each line, before its reading time.</summary>
    public const int PadMs = 1500;
    /// <summary>The most the timer holds (clamped at 25,000).</summary>
    public const int MaxMs = 25_000;

    /// <summary>When the pane comes down, or 0 when it is not up.</summary>
    public int Until => left > 0 ? at + left : 0;

    public void Add(string text, int now)
    {
        int still = left > 0 ? Math.Max(0, at + left - now) : 0;
        left = Math.Min(Rules.GameRules.Current.Party.ClientSceneTiming[1], still + Rules.GameRules.Current.Party.ClientSceneTiming[0] + Conversation.ReadingTime(text));
        at = now;
    }

    /// <summary>True once, when the time is up.</summary>
    public bool Done(int now)
    {
        if (left <= 0 || now < at + left) return false;
        left = 0;
        return true;
    }
}
