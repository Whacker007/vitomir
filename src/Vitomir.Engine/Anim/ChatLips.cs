// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A chat line moves the speaker's lips  — built.
using Vitomir.Engine.World;

namespace Vitomir.Engine.Anim;

/// <summary>
/// **A network chat line moves its speaker's lips**. Every line a machine
/// shows — said, whispered, team, lobby, a roll, an emote — does it to the speaker's character (a thing of
/// type 0 or 7 found by the line's GUID), except an emote that named one of the four gestures, which played
/// that instead (the chat's flag, cleared with the gesture). On the speaker:
/// <list type="bullet">
/// <item>when it is **not walking** (the walker's bit 0), motion <see cref="TalkMode"/> (`talk`) is
/// played at 30 frames a second (mode 0x75) and **the mouth is busy** until the game clock plus
/// the time (<see cref="WorldThing.MouthBusyUntil"/>) — so a voice-flagged sound on it is refused
/// meanwhile; a walking speaker keeps its walk and the mouth's old time;</item>
/// <item>the **`Speaking`** bit (<see cref="Actor.Flag2.Speaking"/>) is set, walking or not;</item>
/// <item>and when the speaker has a model with **no shell on** (the model's bit 0x40; or its bit
/// 0x10000, which nothing read sets), **the white shell flashes** over it — the same call as the steered
/// character's (`shellSprite_white`, 0x5000, 0, 1.0, 0.2, 1.0, <see cref="Location.SetSteered"/>).</item>
/// </list>
/// **The end** is the actor's frame: once the bit is set and the mouth's time is past, the actor's frame
/// clears the time and the bit and chooses the locomotion again (<see cref="Player.Reanimate"/>),
/// which takes the talk off the body. Nothing else in 1.1 sets the bit — only the chat line does.
/// The bit is each machine's own (`Net.HostSeen.LocalActor2`): 1.1 runs it on each machine the line
/// reaches.
/// <para>**So is the talk**. 1.1's snapshots carry no action motions, so the speaker's mouth moves only on
/// the machines the line reaches — a whisper's two (the one whispered to, and the sender's own copy while the
/// `whisperecho` QoL is on), a said line's hearers. Here the talk <see cref="Start"/> plays is marked
/// <see cref="Local"/>: the host leaves it out of its snapshots (`Net.WorldWatch`) and a player's machine does
/// not let the host's word that the body is doing nothing stop it (`Net.WorldMirror.Apply`), and each machine
/// starts it as it shows the line (`GameServer.Say` on the host, `GameClient` on a player's). Earlier, the host
/// started it for every line and its snapshot carried it to every machine.</para>
/// </summary>
public static class ChatLips
{
    /// <summary>The motion a chat line plays (the mode table's `talk`).</summary>
    public const int TalkMode = 0x75;

    /// <summary>How long a chat line keeps the mouth busy, ms (3000).</summary>
    public const int LineMs = 3000;

    /// <summary>The shell a chat line flashes: the steered character's (`shellSprite_white`).</summary>
    public const string Shell = Location.SteerShell;

    /// <summary>
    /// The lips moved for <paramref name="ms"/> from <paramref name="nowMs"/> (the game clock,
    /// the game clock). <paramref name="motions"/> null leaves the motion out. The talk is this machine's own
    /// (<see cref="Local"/>).
    /// </summary>
    public static void Start(WorldThing who, int nowMs, MotionSets? motions, int ms = LineMs)
    {
        if (who.Actor is not { } actor) return;
        if (!Walking(who))
        {
            if (motions is not null && who.Animation is { } anim)
            {
                anim.Play(TalkMode, motions, onMotion: false);                       // mode 0x75 on the action channel at 30
                if (anim.Action is { } talk) local.AddOrUpdate(talk, Marker);
            }
            who.MouthBusyUntil = nowMs + ms;
        }
        actor.Flags2 |= Actor.Flag2.Speaking;                                        // the speaking bit
        if (who.Animation is not null && who.Fx is not { Shell: not null })          // a model and no shell
            who.Model3D.SetShell(Shell, 0x5000, 0f, 1f, Location.SteerShellRamp);
    }

    /// <summary>
    /// The actor frame's test, each frame: <see cref="End"/> once the bit is set and the mouth's time is past
    /// (the mouth's time before the game clock, unsigned). True when it ended.
    /// </summary>
    public static bool Tick(WorldThing who, int nowMs)
    {
        if (who.Actor is not { } actor || !actor.Has2(Actor.Flag2.Speaking)) return false;
        if ((uint)who.MouthBusyUntil >= (uint)nowMs) return false;
        End(who);
        return true;
    }

    /// <summary>
    /// The mouth's time cleared, the bit off, and the locomotion chosen again. A copy
    /// of another machine's character has no locomotion of its own to choose, so its talk is simply stopped
    /// (ours: what its host's snapshots say it is doing takes over again).
    /// </summary>
    public static void End(WorldThing who)
    {
        who.MouthBusyUntil = 0;                                                      // the mouth's time cleared
        if (who.Actor is { } actor) actor.Flags2 &= ~Actor.Flag2.Speaking;
        if (who.Locomotion is { } walker) walker.Reanimate();
        else if (who.Animation is { Action: { } talk } anim && Local(talk)) anim.StopAction();
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ThingAnimation.Channel, object> local = new();
    static readonly object Marker = new();

    /// <summary>
    /// Whether <paramref name="action"/> is a talk a chat line started on this machine — each machine's own,
    /// never sent in a snapshot nor stopped by one (1.1's snapshots carry no action motions).
    /// </summary>
    public static bool Local(ThingAnimation.Channel? action) => action is not null && local.TryGetValue(action, out _);

    /// <summary>A motion a chat line started on this machine marked <see cref="Local"/> — an emote's gesture (`Net.ChatGesture`).</summary>
    public static void Own(ThingAnimation.Channel? action)
    {
        if (action is not null) local.AddOrUpdate(action, Marker);
    }

    /// <summary>The walker's bit 0 — a walk under way. <b>Ours</b> for a thing with no walker of its own (a player's machine's copy): the moving bit.</summary>
    static bool Walking(WorldThing who) => who.Locomotion is { } walker ? walker.Walking : who.Walking;
}
