// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The Storyteller's side of the chat and the cast (a comparison with 1.1, *The Storyteller's*): the
// lines only the host's own chat box shows, and the cast the host's steered body may not make.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// A line for **the host's own chat box only** — the multiplayer bar's `sbChatBox`, or, with no bar up, the room's list (`mpChat`) — written there straight,
/// not sent to anybody and not shown as a said line (no lips, no noise: the line's display is not run). Its key, the
/// key's arguments (`%S` …) and its colour.
/// </summary>
public readonly record struct HostLine(string Key, string Args, uint Colour);

public static class StoryChat
{
    /// <summary>`MP_TRIEDTOSAY`'s colour.</summary>
    public const uint TriedToSayColour = 0xffdd4040u;

    /// <summary>
    /// **A possessed character's player tried to speak**: a player (not slot 0)
    /// whose character is the host's steered body (the Storyteller's possessed one) is not heard;
    /// the host's chat box alone shows `MP_TRIEDTOSAY` (*"%S tried to say '%T' (posessed)"*, 1.1's spelling)
    /// with the whole typed line, and the player is told `MP_SAYFAILED`. Earlier, Vitomir showed the
    /// first to the whole room.
    /// </summary>
    public static HostLine TriedToSay(string speaker, string typed) => new("MP_TRIEDTOSAY", $"%S{speaker}%T{typed}", TriedToSayColour);

    /// <summary>
    /// **The moderated host's copy of a line not meant for it** (only in a game made with Storyteller
    /// moderation, 0x400):
    /// <list type="bullet">
    /// <item>a **whisper** delivered to anybody but slot 0: `MP_WHISPERALT`, *"%S whispers to
    /// %R, '%T'"*, in the whisper's colour 0xff40d040 — the host's own whisper included, which is how the host
    /// sees what it whispered;</item>
    /// <item>**team talk** when the host's character is on another team than the speaker:
    /// `MP_TEAMSAYALT`, *"%S tells team %D, '%T'"*, the speaker's team as a number, in the team's colour 0xffdd80ff.
    /// **Ours**: a host with no character of its own (Vitomir's, not 1.1's) is on no team, so it is shown every
    /// team's talk.</item>
    /// </list>
    /// Null when there is none. <paramref name="recipient"/> is the whispered-to player's name (null when the
    /// whisper reached nobody); <paramref name="hostTeam"/> is the host's character's team, null without one.
    /// </summary>
    public static HostLine? HostCopy(ChatLine line, string speaker, int speakerTeam, string? recipient, int? hostTeam, GameFlags flags)
    {
        if ((flags & GameFlags.Moderated) == 0) return null;
        return line.Kind switch
        {
            ChatKind.Whisper when line.To > 0 && recipient is not null =>
                new HostLine("MP_WHISPERALT", $"%S{speaker}%R{recipient}%T{line.Text}", NetChat.Colour(ChatKind.Whisper)),
            ChatKind.Team when hostTeam != speakerTeam =>
                new HostLine("MP_TEAMSAYALT", $"%S{speaker}%D{speakerTeam}%T{line.Text}", NetChat.Colour(ChatKind.Team)),
            _ => null,
        };
    }
}

/// <summary>
/// **A player's cast on the host** (message 0x18): the caster found by its GUID must be a character
/// (type 0 or 7) and **not the host's steered body** — which, the host being in the
/// Storyteller's seat, is the body the Storyteller has possessed. Refused, 1.1 returns and says nothing. It does
/// not ask about the pause at all: a cast sent while the Storyteller has the game paused is taken like any
/// other (an order, is refused with `UI_STSBPAUSE`; a cast is not). Earlier, Vitomir refused a cast as
/// it refuses an order, answering `UI_NET_POSSESSED` and `UI_STSBPAUSE`.
/// </summary>
public static class NetStoryCast
{
    /// <summary>Whether the cast of <paramref name="caster"/> is refused, <paramref name="steered"/> being the body the Storyteller has possessed (or null).</summary>
    public static bool Refused(WorldThing caster, WorldThing? steered) => steered is not null && ReferenceEquals(caster, steered);
}
