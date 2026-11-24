// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The host's half of 1.1's network conversations and cutscenes: a conversation or a cutscene a script starts for a
// network player goes to that player's machine, and what that machine reports back comes to the
// script as the callbacks it would have heard. It covers the start's server arm,
// message 0x2e, 0x30 read, 0x31 (the overheard line), the camera's arms and 0x2b, 0x2c, and `PlayPath`
// and 0x2d both ways. The messages' bytes are ours; what goes in them and who gets them is 1.1's.
using System.Numerics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Net;

public sealed class HostScenes(GameServer server) : ISceneRoute
{
    /// <summary>
    /// How near another player must stand to overhear a line of a conversation (800 units), measured between the characters' origins, in the same place.
    /// </summary>
    public const float EarshotRange = 800f;

    /// <summary>The network player (slot 1 or above) whose character has this GUID, or null — the slot walk each arm makes.</summary>
    NetPlayer? Remote(int guid) =>
        guid == 0 ? null : server.Players.FirstOrDefault(p => !p.Host && p.Avatar?.Guid == guid);

    /// <summary>
    /// A conversation's start on the server. **Nothing starts without Auto Storyteller** (game flag 0x800),
    /// which is what `NetIsNoAutoConversations` tells the scripts beforehand; nor with somebody who is
    /// already talking to a player, or to the host. The host's own character talks here, as in single
    /// player. A player's conversation is kept in their slot as an instance that
    /// runs nothing — the script's callbacks are wired to it — and their machine is told to run it
    /// (message 0x2e: the file, the name, and the definition's first word, which is the
    /// `CONVERSATION:` line's number, 0 in every shipped file — so a player's machine runs every
    /// conversation with no flags at all). Anybody else — a thing no player has — is refused: the slot
    /// walk finds nobody and the start answers 0.
    /// </summary>
    public RouteAnswer Converse(Conversations talk, Location place, string file, NcoConversation conversation,
                                ConversationFlag flags, int starter, int npc, Action<Conversation>? wire)
    {
        if (!server.Flags.HasFlag(GameFlags.AutoStoryteller)) return RouteAnswer.Refused;
        if (starter == 0) return RouteAnswer.Refused;
        if (npc != 0 && (server.Players.Any(p => p.Talking?.Npc == npc) || talk.Current is { Running: true, Npc: var busy } && busy == npc))
            return RouteAnswer.Refused;
        if (server.Slots[0]?.Avatar is { } mine && mine.Guid == starter) return RouteAnswer.Here;
        if (Remote(starter) is not { Avatar: { } body } p) return RouteAnswer.Refused;
        var kept = Conversation.Resolve(conversation, place.Things, body, Conversations.Speaker(place));
        kept.Flags = flags;
        kept.Id = starter;
        kept.Npc = npc;
        kept.PlayerThing = body;
        wire?.Invoke(kept);
        p.Talking = kept;
        server.Reliable(p, new NetWriter(Msg.Converse).Str(file).Str(conversation.Name).I32(conversation.Number).I32(npc).ToArray());
        server.Note($"net: {p.Name} is in the conversation {conversation.Name}");
        return RouteAnswer.Sent;
    }

    /// <summary>
    /// A player's machine's report: the slot's conversation must be there, and
    /// then a line (kind 0) is `convreached` and overheard, a reply (kind 1) is `convchosen` and
    /// overheard, and the end (kind 2) forgets the slot's conversation and is `convended`.
    /// </summary>
    internal void Said(NetPlayer p, NetReader r)
    {
        int kind = r.U8();
        if (p.Talking is not { } c) return;
        switch (kind)
        {
            case 0:
            case 1:
            {
                int ev = r.I32(), step = r.I32(), ms = r.I32(), who = r.I32();
                if (kind == 0) c.HeardLine(ev, step, ms, who);
                else c.HeardReply(ev, step, ms, who);
                Overhear(p, c.Talk, ev, step);
                break;
            }
            case 2:
            {
                int code = r.I32();
                bool aborted = r.Bool();
                p.Talking = null;
                c.HeardEnd(aborted, code);
                break;
            }
        }
    }

    /// <summary>
    /// The line overheard (message 0x31): every other player whose character is in the same
    /// place and within <see cref="EarshotRange"/> of the talker's gets the step's kind, the name to
    /// show — a line's speaker, by the cast id the receiving machine looks up, or for a
    /// reply the talker's own network name — and the line's id; and the host too, when it
    /// has a character near enough and the talker is not the host. Each machine shows it as a subtitle.
    /// </summary>
    void Overhear(NetPlayer talker, NcoConversation talk, int ev, int step)
    {
        if (ev < 0 || ev >= talk.Events.Count || step < 0 || step >= talk.Events[ev].Steps.Count) return;
        if (talker.Avatar is not { } from) return;
        var line = talk.Events[ev].Steps[step];
        bool spoken = line.Kind == NcoStep.Line;
        string name = spoken ? line.Speaker : talker.Name;
        var msg = new NetWriter(Msg.Overheard).U8((byte)(spoken ? 0 : 1)).Str(name).Str(line.Id).ToArray();
        foreach (var p in server.Players)
        {
            if (ReferenceEquals(p, talker) || p.Avatar is not { } near || !ReferenceEquals(near.Place, from.Place)) continue;
            if (Vector3.Distance(near.Position, from.Position) >= Rules.GameRules.Current.Party.ConversationEarshot) continue;
            if (p.Host) server.HearLine(spoken, name, line.Id);
            else server.Reliable(p, msg);
        }
    }

    /// <summary>
    /// `SetupCutscene` for a player (its server arm): the two targets are kept in the player's
    /// camera record on the host and nothing is sent yet — they go
    /// with the next shot. The host's own camera is left alone (the camera's arm returns for a player's GUID).
    /// </summary>
    public bool SetupCutscene(int client, WorldThing first, WorldThing? second)
    {
        if (Remote(client) is not { } p) return false;
        p.CutA = first.Guid;
        p.CutB = second?.Guid ?? 0;
        return true;
    }

    /// <summary>`SetShot` for a player (its server arm, message 0x2b): the shot and the two targets kept.</summary>
    public bool SetShot(int client, int shot, int angle, int distance, int side, int ms)
    {
        if (Remote(client) is not { } p) return false;
        server.Reliable(p, new NetWriter(Msg.CameraShot).U8((byte)shot).U8((byte)angle).U8((byte)distance).U8((byte)side)
            .I32(ms).I32(p.CutA).I32(p.CutB).ToArray());
        return true;
    }

    /// <summary>`Release` for a player (its server arm, message 0x2c).</summary>
    public bool Release(int client)
    {
        if (Remote(client) is not { } p) return false;
        server.Reliable(p, new NetWriter(Msg.CameraRelease).ToArray());
        return true;
    }

    /// <summary>
    /// `PlayPath` for a player (its server arm, message 0x2d): the script is kept in
    /// the player's camera record and the path's name and speed go to their machine; when
    /// it ends there, the machine says so (0x2d back) and the script hears `pathended`.
    /// </summary>
    public bool PlayPath(int client, string name, float speed, Action? ended)
    {
        if (Remote(client) is not { } p) return false;
        p.PathEnded = ended;
        server.Reliable(p, new NetWriter(Msg.CameraPath).Str(name).F32(speed).ToArray());
        return true;
    }

    /// <summary>The player's machine has come to the end of the path (the script's `pathended`).</summary>
    internal void PathEnded(NetPlayer p)
    {
        var ended = p.PathEnded;
        p.PathEnded = null;
        ended?.Invoke();
    }
}
