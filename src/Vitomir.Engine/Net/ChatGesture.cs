// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// An emote's gesture, played on each machine that shows the line.
using Vitomir.Engine.Anim;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Net;

/// <summary>
/// **An emote's gesture** (case 3). A line typed `:bow`, `:menace`, `:taunt` or `:wave` — the line
/// beginning with the word, <see cref="NetChat.EmoteMode"/> — plays mode 0xbf … 0xc2 on the speaker (once, at
/// 30 frames a second), the speaker being the thing the line names by its GUID when it is an actor or a player (type 0 or 7),
/// walking or not; and the lips are then left alone (<see cref="ChatLips"/>). The chat line's display plays it, so
/// the gesture is played **on each machine that shows the line** — the host's when it hears it (its own
/// send to slot 0), each player's as its message 7 arrives — and, 1.1's snapshots carrying no action
/// motions, on no other. Here it is marked <see cref="ChatLips.Local"/>, as the lips' talk is: the host's snapshots
/// leave it out and a player's machine does not let them stop it. Earlier, Vitomir's host played it whether or not it
/// heard the line and its snapshot carried it to every machine that could see the speaker.
/// </summary>
public static class ChatGesture
{
    /// <summary>The rate a gesture plays at, frames a second (30.0).</summary>
    public const float Rate = 30f;

    /// <summary>
    /// The gesture a line of <paramref name="kind"/> saying <paramref name="text"/> plays on <paramref name="speaker"/>,
    /// played with <paramref name="motions"/> (null leaves the motion out). True when the line is a gesture — whether or
    /// not a speaker was there to make it — so the lips are not moved.
    /// </summary>
    public static bool Play(WorldThing? speaker, ChatKind kind, string text, Func<string, string?> find, MotionSets? motions)
    {
        int mode = kind == ChatKind.Emote ? NetChat.EmoteMode(text, find) : -1;
        if (mode < 0) return false;
        if (speaker is { Type: ThingType.Actor or ThingType.Player, Animation: { } anim } && motions is not null)
        {
            anim.Play(mode, motions, onMotion: false, rate: Rate);
            ChatLips.Own(anim.Action);
        }
        return true;
    }
}
