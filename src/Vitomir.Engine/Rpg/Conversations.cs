// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The game's one running conversation and the native that
// starts it, `Codex.ExecuteConversation`.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>
/// Where a script's conversation comes from and goes to. The engine keeps exactly one running
/// conversation; starting a new one while one runs **ends the old one without
/// telling its script** (the start calls the teardown, not the finish that fires
/// <c>convended</c>) and puts the new one in its place.
///
/// The pieces a conversation needs that are not the engine's to own — the words, the display
/// names, the pane that shows them — are handed in, and <see cref="Started"/> is where the pane
/// hooks itself onto each new one.
/// </summary>
public sealed class Conversations
{
    /// <summary>The conversation running now, if any.</summary>
    public Conversation? Current { get; private set; }

    /// <summary>A line's words and its recording's length, by line id (`strings/*.nvo`).</summary>
    public Func<string, ConversationVoice?>? Voices { get; set; }

    /// <summary>The string table, for the names in the subtitle brackets (<see cref="Conversation.Names"/>).</summary>
    public Func<string, string>? Names { get; set; }

    /// <summary>The engine's subtitle setting, handed on to each conversation.</summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>A new conversation is about to start: hook the pane onto it here.</summary>
    public event Action<Conversation>? Started;

    /// <summary>
    /// `ExecuteConversation`: load <paramref name="conversation"/> out of
    /// <c>conversations/<paramref name="file"/></c>, cast it from the place's
    /// characters, and start it with <paramref name="flags"/> and <paramref name="id"/> — the id
    /// being what every callback hands back as its first argument, which the shipped scripts set to
    /// the starter's GUID. <paramref name="wire"/> attaches the calling script's callbacks before
    /// anything is said. Answers false when the file or the conversation is not there, which is
    /// the native's own <c>false</c>.
    /// </summary>
    /// <remarks>
    /// In a network game: a player's machine starts none of its own — they
    /// come from the host (on a network client, a start not from the net answers 0) — and
    /// on the host <see cref="Net.SceneRoutes"/> decides whether it is the host's own or a player's,
    /// whose machine runs it. <paramref name="npc"/> is the sixth argument, the one being talked to.
    /// </remarks>
    public bool Execute(Location place, string file, string conversation, ConversationFlag flags, int id,
                        int nowMs, Action<Conversation>? wire = null, int npc = 0)
    {
        if (Net.NetOrders.Mode(place) == Net.NetMode.Client) return false;
        if (place.Files is null) return false;
        var name = file.EndsWith(".nco", StringComparison.OrdinalIgnoreCase) ? file : file + ".nco";
        if (!place.Files.TryRead("conversations/" + name, out var data)) return false;
        NcoFile read;
        try { read = NcoFile.Read(data, name); }
        catch (NodFormatException) { return false; }
        if (read.Conversation(conversation) is not { } talk) return false;
        switch (Net.SceneRoutes.For(place)?.Converse(this, place, name, talk, flags, id, npc, wire) ?? Net.RouteAnswer.Here)
        {
            case Net.RouteAnswer.Refused: return false;
            case Net.RouteAnswer.Sent: return true;
        }
        Run(place, talk, flags, id, nowMs, made => { made.Npc = npc; wire?.Invoke(made); });
        return true;
    }

    /// <summary>
    /// Start a conversation already read (the start from its second step on): replace whatever is
    /// running without telling its script, cast it from the place's characters, and begin. What
    /// <see cref="Execute"/> does once it has found the conversation, and what the harness's
    /// <c>--talk</c> uses to start one with no script behind it.
    /// </summary>
    public Conversation Run(Location place, NcoConversation talk, ConversationFlag flags, int id, int nowMs,
                            Action<Conversation>? wire = null)
    {
        // The one running conversation is replaced, and its script is not told.
        if (Current is { Running: true } old) old.Stop(place.Motions);

        // Speakers are found through the cast lists, the party's first, once the
        // party's has been rebuilt — which `CastList.Of` does. The place's list
        // is the one made on arrival: a character spawned since, or one that has left the party, is no
        // speaker (`CastList`).
        var made = Conversation.Resolve(talk, CastList.Of(place), place.Player?.Thing, Speaker(place));
        made.Flags = flags;
        made.Id = id;
        made.PlayerThing = place.Player?.Thing;
        made.PlayerName = place.Player?.Thing.Actor?.CastId ?? Conversation.Player;
        made.Voices = Voices;
        made.Audio = place.Audio;
        made.Names = Names;
        made.Subtitles = Subtitles;
        wire?.Invoke(made);
        Started?.Invoke(made);
        Current = made;
        made.Start(nowMs, place.Motions);
        return made;
    }

    /// <summary>
    /// The chronicle's speaker — its metafile's `SPEAKER` — who is in every conversation (see
    /// <see cref="Conversation.Actors"/>). A place loaded without its chronicle (a harness run) falls
    /// back on the player character's own cast id, which is what every shipped chronicle names.
    /// </summary>
    public static string? Speaker(Location place) =>
        place.Session?.Chronicle?.Meta?.Speaker is { Length: > 0 } named ? named : place.Player?.Thing.Actor?.CastId;

    /// <summary>One frame of the running conversation, and forget it once it has ended.</summary>
    public bool Tick(int nowMs)
    {
        if (Current is not { } c) return false;
        bool moved = c.Tick(nowMs);
        // A script's convended may start the next conversation on the spot — several chain them —
        // so only the one that has just ended is forgotten.
        if (!c.Running && ReferenceEquals(Current, c)) Current = null;
        return moved;
    }

    /// <summary>
    /// `Codex.EndConversation`: the teardown on whatever is
    /// running, and **no <c>convended</c>** — the script that ends a conversation this way is not
    /// told it ended. The engine does not free the instance either; here it is simply dropped.
    /// </summary>
    public bool End(Location place)
    {
        if (Current is not { } c) return false;
        c.Stop(place.Motions);
        Current = null;
        return true;
    }

    /// <summary>
    /// The place changing under a running conversation: the same teardown as
    /// <see cref="End"/>, but a script that asked for callbacks **is** told —
    /// <c>convended(id, aborted, code)</c> with aborted true. A conversation does not travel.
    /// </summary>
    public bool Abandon(Location place)
    {
        if (Current is not { } c) return false;
        c.Abandon(place.Motions);
        Current = null;                  // after the callback, as 1.1 clears its global
        return true;
    }
}
