// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Who hears a chat line (1.1's host): a
// said line, an emote and a roll carry only so far, and team talk only to the team; the Storyteller's
// Comm Lock and a whisper's two ends.
using System.Numerics;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// Where a player is, as the host asks it of their slot (the slot's state; 1.1's numbers): **in the
/// room** (2) — joined, with a character made, not yet in the game (the room before the start, or a joiner of
/// a game under way who has not asked in, message 0x40); **loading** (3, travelling between places;
/// or 5, going in); **playing** (4).
/// </summary>
public enum ChatSlot : byte { Room = 2, Loading = 3, Playing = 4 }

/// <summary>
/// One end of a chat line as the host weighs it: the slot, the character in the host's world (none
/// while the player has none there — before their first arrival, or travelling), the place it is in,
/// where it stands, its team, and the slot's state.
/// </summary>
public readonly record struct ChatEar(int Slot, bool HasCharacter, object? Place, Vector3 At, int Team, ChatSlot State = ChatSlot.Playing)
{
    /// <summary>
    /// A player's ear: their character when it is in the world, in <paramref name="place"/>. Before the game has
    /// <paramref name="started"/> everybody is in the room; after it, a player with a character in the world is
    /// playing, one let in without one is loading, and **one not yet let in is still in the room** (slot state 2 until
    /// its Start is answered; earlier, Vitomir had no room after the start).
    /// </summary>
    public static ChatEar Of(NetPlayer p, Location? place, bool started = true) =>
        p.Avatar is { InWorld: true } body && started
            ? new ChatEar(p.Slot, true, place, body.Position, body.Actor?.Team ?? Actor.Teams.None)
            : new ChatEar(p.Slot, false, null, default, Actor.Teams.None,
                          started && (p.Host || p.LetIn) ? ChatSlot.Loading : ChatSlot.Room);
}

public static class ChatHearing
{
    /// <summary>How far a said line, an emote or a roll carries: 5000 units.</summary>
    public const float SayRange = 5000f;

    /// <summary>
    /// **Whether <paramref name="listener"/> hears <paramref name="speaker"/>'s line** (by kind).
    /// **Comm Lock** is the Storyteller's (`UI_STCPCOMMSLOCKED`, the game's flag 0x80, <see cref="GameFlags.CommLocked"/>):
    /// it keeps the room out of the game's talk and the room's talk out of the game.
    /// <list type="bullet">
    /// <item>**say, emote, roll** (0, 3, 6): a player in the room hears it **wherever the speaker is** unless Comm
    /// Lock is on; otherwise a listener with a character — the host when the game
    /// is moderated (0x400; slot 0 is the Storyteller's seat), anybody else only in the speaker's place and within
    /// <see cref="SayRange"/> of the speaker (the distance in three dimensions);</item>
    /// <item>**team** (7): a listener with a character on the speaker's team;</item>
    /// <item>**whisper** (2): **the one whispered to, and nobody else** — its sender's own copy is Vitomir's QoL
    /// (<see cref="Echoes"/>), the moderated host's `MP_WHISPERALT` is <see cref="StoryChat.HostCopy"/>;</item>
    /// <item>**shout, the server's own** (1, 5): everybody, but the room while Comm Lock is on;</item>
    /// <item>**lobby** (8): everybody; while Comm Lock is on, only those not playing — the room and those
    /// loading — and the host when the game is moderated.</item>
    /// </list>
    /// A speaker with no character playing speaks to the lobby (the slot not in state 4, or the line
    /// without a GUID; `GameServer.Say` parses it so). **Ours**: the host's own line when the host has no body
    /// (Vitomir's host without a character, which 1.1's has not — its host drops such a line) is heard by all.
    /// </summary>
    public static bool Hears(ChatLine line, ChatEar speaker, ChatEar listener, GameFlags flags)
    {
        bool commLock = (flags & GameFlags.CommLocked) != 0, moderated = (flags & GameFlags.Moderated) != 0;
        switch (line.Kind)
        {
            case ChatKind.Say or ChatKind.Emote or ChatKind.Roll:
                if (!speaker.HasCharacter) return true;
                if (listener.State == ChatSlot.Room && !commLock) return true;
                if (!listener.HasCharacter) return false;
                if (listener.Slot == 0 && moderated) return true;
                return ReferenceEquals(listener.Place, speaker.Place) && Vector3.Distance(listener.At, speaker.At) <= Rules.GameRules.Current.Party.SayRange;
            case ChatKind.Team:
                if (!speaker.HasCharacter) return true;
                return listener.HasCharacter && listener.Team == speaker.Team;
            case ChatKind.Whisper:
                return listener.Slot == line.To;
            case ChatKind.Shout or ChatKind.Narrate:
                return !(commLock && listener.State == ChatSlot.Room);
            case ChatKind.Lobby:
                return !commLock || (listener.Slot == 0 && moderated) || listener.State != ChatSlot.Playing;
            default:
                return true;
        }
    }

    /// <summary>
    /// **Whether a whisper's sender is sent their own copy** — the QoL `whisperecho` (ours; *"Yes leave
    /// it on."*): a whisper to somebody else, unless the sender is the moderated host, whose own copy is 1.1's
    /// (<see cref="StoryChat.HostCopy"/>). The host sends it always; the sender's machine shows it by its own switch
    /// (<see cref="World.Qol.WhisperEcho"/>), since it changes only what that player sees.
    /// </summary>
    public static bool Echoes(ChatLine line, int speakerSlot, GameFlags flags) =>
        line.Kind == ChatKind.Whisper && line.To != speakerSlot
        && !(speakerSlot == 0 && (flags & GameFlags.Moderated) != 0);
}

/// <summary>
/// **The interface noise a chat line makes as it is shown**, on
/// every machine, the host's too: `ui.nag`'s `MPMESSAGE1` (slot 0xf) for a said, shouted, whispered or
/// team line, `MPMESSAGE2` (0x10) for a roll, none for an emote or the server's own. A lobby line's
/// noise turns on the game's state (none in state 2), which is unread here, so it
/// is left silent (ours).
/// </summary>
public static class ChatNoise
{
    /// <summary>`ui.nag`'s slots (counted from 0): `MPMESSAGE1`, `MPMESSAGE2`.</summary>
    public const int MpMessage1 = 0xf, MpMessage2 = 0x10;

    /// <summary>The slot a line of <paramref name="kind"/> plays, or null.</summary>
    public static int? For(ChatKind kind) => kind switch
    {
        ChatKind.Say or ChatKind.Shout or ChatKind.Whisper or ChatKind.Team => MpMessage1,
        ChatKind.Roll => MpMessage2,
        _ => null,
    };
}
