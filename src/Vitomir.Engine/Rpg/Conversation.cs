// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Starting and ending a conversation: who is in it, and what that does to them.
//
// The format is `Formats/Nco.cs`'s; this is the runtime around it. Only the
// two ends are built — the entry and the exit — because they are the parts that change the world
// rather than the screen, and the screen is held back.
//
// The engine's steps: one resolves the speakers, one begins,
// one ends, one makes the instance. The three script natives are
// `ExecuteConversation`, `BeginConversation` and `EndConversation`;
// the first is the interesting one and it is just *load, then begin*.
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Rpg;

/// <summary>One line, with the two things the engine works out for it when a conversation starts.</summary>
/// <param name="Step">The step as the file had it.</param>
/// <param name="Who">The thing that says it, or null when the name resolves to nobody.</param>
public readonly record struct SpokenLine(NcoLine Step, WorldThing? Who)
{
    /// <summary>The cast name the file gave, whether or not anything answers to it.</summary>
    public string Speaker => Step.Speaker;
}

/// <summary>
/// A conversation as it is about to happen: the graph from the file, and everybody in it.
/// </summary>
public sealed partial class Conversation(NcoConversation talk)
{
    public NcoConversation Talk { get; } = talk;

    /// <summary>
    /// Everybody in it, in the engine's order: **the chronicle's speaker first** — the
    /// metafile's `SPEAKER`, Christof in all four chronicles — and then everybody with a line, walking
    /// every event and every step, each **distinct** name once. The speaker is in whether or not he
    /// has a line, which is why Christof puts his sword away and listens to Ecaterina's one-line
    /// answers (in 1.1 too, with Wilhem steered and doing the clicking). This is the
    /// list `Begin` and `End` act on; the engine asserts it is not empty (`numConvActors`).
    /// </summary>
    public List<WorldThing> Actors { get; } = [];

    /// <summary>Names that had a line but matched nothing in the world, in the order first seen.</summary>
    public List<string> Missing { get; } = [];

    /// <summary>
    /// The speaker whose line was last started. `End` gives this one its `listen` back.
    /// </summary>
    public WorldThing? LastSpeaker { get; set; }

    /// <summary>
    /// How the subtitle reads: `&lt;speaker&gt; words`. The bare `%s`
    /// beside it is what a line with no speaker uses.
    /// </summary>
    public static string Subtitle(string speaker, string words) =>
        speaker.Length == 0 ? words : $"<{speaker}> {words}";

    /// <summary>
    /// How many voice lines the engine keeps playing at once: a **ring of eight** slots
    /// (an index and the slots, three dwords each). Starting a ninth
    /// stops the oldest rather than queueing it.
    /// </summary>
    public const int VoiceSlots = 8;

    /// <summary>
    /// The mode everybody in a conversation plays: **`listen`, 118**, on the *motion* channel at
    /// thirty frames a second (at the start and again at the end). Not the action channel — this is
    /// what the character *is*, not something it does over the top.
    /// </summary>
    public const int Listen = 118;

    /// <summary>What `End` falls back to when a motion set has no `listen`.</summary>
    public const int Stand = 33;

    /// <summary>
    /// Resolve every speaker to somebody in the world, and collect the distinct list.
    ///
    /// **A speaker name is a `CASTID`** — tag 81, the name the cast list keys on. That is
    /// measured, not assumed: of the 95 distinct speakers across the 310 shipped conversations,
    /// **90 are `CASTID` values** on a shipped template. The five that are not are
    /// <see cref="Player"/> (41 lines — the party leader, who is whoever you are rather than a cast
    /// member) and four that name nobody at all: `Nun`, `Ped`, `MDNos1` and `Prague2`, six lines
    /// between them.
    ///
    /// The engine also resolves the speaker's **display name** through the string table at the same
    /// time, which is what the subtitle shows — so the cast id is an internal key and
    /// the name on screen is a separate lookup, made through <see cref="Names"/>.
    /// </summary>
    public static Conversation Resolve(NcoConversation talk, IEnumerable<WorldThing> cast,
                                       WorldThing? player = null, string? speaker = null)
    {
        var made = new Conversation(talk);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // In single player the list opens with the chronicle's speaker, found by cast id like any
        // other name (the name the chronicle reader fills from the metafile's
        // SPEAKER line). Multiplayer opens it with "Player" — the steered character —
        // instead, and that is where the 41 shipped "Player" lines live.
        if (!string.IsNullOrEmpty(speaker) && seen.Add(speaker))
        {
            if (Find(speaker, cast, player) is { } first) made.Actors.Add(first);
            else made.Missing.Add(speaker);
        }
        foreach (var e in talk.Events)
            foreach (var step in e.Steps)
            {
                if (step.Kind != NcoStep.Line || step.Speaker.Length == 0) continue;
                if (!seen.Add(step.Speaker)) continue;
                // Once each: a "Player" line in a conversation that already has the speaker would
                // otherwise put him in twice — in the engine a single-player "Player" is a cast id
                // nobody has, and no single-player conversation has one.
                if (Find(step.Speaker, cast, player) is { } who) { if (!made.Actors.Contains(who)) made.Actors.Add(who); }
                else made.Missing.Add(step.Speaker);
            }
        return made;
    }

    /// <summary>The cast name the party leader answers to, which is not a `CASTID`.</summary>
    public const string Player = "Player";

    /// <summary>Whoever answers to a cast name, or null.</summary>
    public static WorldThing? Find(string speaker, IEnumerable<WorldThing> cast, WorldThing? player) =>
        string.Equals(speaker, Player, StringComparison.OrdinalIgnoreCase)
            ? player
            : cast.FirstOrDefault(t => t.Actor is { } a
                  && string.Equals(a.CastId, speaker, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Everybody stops and listens. For each of <see cref="Actors"/>:
    ///
    /// <list type="bullet">
    /// <item>whatever they were doing is cancelled — unforced, so one busy, dead or held keeps it — and their walk order with it;</item>
    /// <item><b>`THING_AF2_CONVERSATION` goes on</b> — and that is the whole of why a conversation
    /// freezes a room. The AI's think gate tests exactly this bit, second after death,
    /// so a character in a conversation does not think at all: it does not wander, it
    /// does not notice you, it does not fight;</item>
    /// <item>and they play <see cref="Listen"/> on the motion channel.</item>
    /// </list>
    ///
    /// Answers how many were put into it.
    /// </summary>
    public int Begin(Anim.MotionSets? motions = null)
    {
        // CONV_XFLAG_STOPACTORS: the current world's stop bit, before the speakers.
        if ((Flags & ConversationFlag.StopActors) != 0 && Actors.Select(a => a.Place).FirstOrDefault(p => p is not null) is { } world)
            ConversationStop.Raise(world);
        foreach (var who in Actors)
        {
            if (who.Actor is not { } actor) continue;
            actor.Flags2 |= World.Actor.Flag2.Conversation;
            // Its order cancelled **unforced**, a feed told to let its victim go
            // (One busy, dead or held keeps what it is doing — a swing runs out —
            // and nothing touches the busy time. (A comparison with 1.1; Vitomir cleared the action and
            // the busy time outright, and a feeding member's victim stayed held.)
            Actions.Cancel(who, release: true);
            who.Mind?.Walker.Stop();
            Play(who, Listen, motions);
            Weapons(who, actor, away: true);
            FallenListener.Up(who);                                     // A fallen member stands
        }
        return Actors.Count;
    }

    /// <summary>
    /// **A player character puts its weapon and shield away to talk, and takes them out again
    /// afterwards** (the player-type arm of each loop). Only a
    /// `THING_TYPE_PLAYER` speaker does it — everybody else keeps what they are holding — and the
    /// weapon is left alone on the way in when it is overridden or one that takes the hands off
    /// (`THING_AF2_WEAPONOVERRIDE | THING_AF2_WEAPONNOHANDS`); the way out is unconditional.
    ///
    /// And the talking hands go on in place of the fists, and come off again
    /// (<see cref="TalkingHands"/>), under the same two flags.
    /// </summary>
    void Weapons(WorldThing who, Actor actor, bool away)
    {
        if (who.Type != ThingType.Player) return;
        if (away)
        {
            if ((Flags & ConversationFlag.NoWeaponsAway) != 0) return;
            const uint keep = World.Actor.Flag2.WeaponOverride | World.Actor.Flag2.WeaponNoHands;
            if ((actor.Flags2 & keep) == 0) actor.WeaponHidden = true;
            actor.OffhandHidden = true;
            TalkingHands.On(who);
            return;
        }
        if ((Flags & ConversationFlag.NoRestoreWeapons) != 0) return;
        actor.WeaponHidden = false;
        actor.OffhandHidden = false;
        TalkingHands.Off(who);
    }

    /// <summary>
    /// And it is over. The bit comes off everybody — so the room starts thinking again —
    /// and the **last speaker** is given a `listen` of their own, falling back to
    /// <see cref="Stand"/> when their motion set has no `listen` at all. The engine does that one
    /// last, after the loop, and only for the one character.
    /// </summary>
    public int End(Anim.MotionSets? motions = null)
    {
        foreach (var who in Actors)
            if (who.Actor is { } actor)
            {
                actor.Flags2 &= ~World.Actor.Flag2.Conversation;
                Weapons(who, actor, away: false);
                FallenListener.Hide(who);                               // And is hidden after
            }
        if ((Flags & ConversationFlag.NoAnimation) == 0 &&
            LastSpeaker is { Actor: not null } last && !Play(last, Listen, motions))
            Play(last, Stand, motions);
        LastSpeaker = null;
        // And then every one of them gets the actor's own animation update (inside the
        // loop), which puts the locomotion back on the motion channel — so the last speaker's
        // `listen` above lasts no time at all. Both are the original's, in its order.
        foreach (var who in Actors) who.Mind?.Walker.Reanimate();
        return Actors.Count;
    }

    /// <summary>
    /// Whether anybody is in a conversation right now — which is the same question as "is the AI
    /// switched off for this character".
    /// </summary>
    public static bool Talking(WorldThing thing) =>
        thing.Actor is { } a && (a.Flags2 & World.Actor.Flag2.Conversation) != 0;

    /// <summary>Puts a mode on the motion channel at 30 fps, and says whether the set had it.</summary>
    static bool Play(WorldThing who, int mode, Anim.MotionSets? motions)
    {
        if (who.Animation is not { } animation || motions is null) return false;
        if (motions.Find(animation.Set, mode) is not { } found) return false;
        animation.Play(found.Mode, found.Animation, motions, onMotion: true,
                       rate: Anim.ThingAnimation.Rate);
        return true;
    }
}

/// <summary>
/// What a script asks for when it starts a conversation — `ExecuteConversation`'s fourth argument,
/// kept in the instance as it is made.
/// </summary>
[Flags]
public enum ConversationFlag
{
    None = 0,

    /// <summary>
    /// **Tell the script what is happening.** Without it the three callbacks are never fired —
    /// every dispatch is guarded by <c>flags &amp; 4</c>.
    /// </summary>
    Callbacks = 0x04,

    /// <summary>
    /// **The player may abort it.** <see cref="Conversation.Abort"/> does nothing unless
    /// this is set or it is forced.
    /// </summary>
    Abortable = 0x08,

    /// <summary>
    /// **Lines may not be skipped.** The polarity is the awkward way round: the skip skips the
    /// current line when this bit is <b>clear</b>, so setting it is what makes a conversation sit
    /// through every line.
    /// </summary>
    NoSkip = 0x10,

    /// <summary>
    /// **Nobody plays a talking animation.** With this set, `Begin`, `End` and every step skip the
    /// `listen`/`talk` work entirely and only keep <see cref="Conversation.LastSpeaker"/> cleared
    /// (`~flags &amp; 0x20`, tested in five places).
    /// </summary>
    NoAnimation = 0x20,

    /// <summary>
    /// **Everybody in the place stops walking** (`CONV_XFLAG_STOPACTORS`): the begin raises the
    /// world's stop bit, and every walker ticked before the thing tick clears it stops
    /// (<see cref="ConversationStop"/>). No shipped script passes it.
    /// </summary>
    StopActors = 0x100,

    /// <summary>
    /// **The player characters keep their weapons out.** Without it, a conversation starting puts
    /// every player-type speaker's weapon and shield away and gives them their talking hands;
    /// with it, nothing of that happens. Read
    /// Without it Christof was seen holding a sword he had just dropped.
    /// </summary>
    NoWeaponsAway = 0x200,

    /// <summary>
    /// **And they are not given back at the end**, which
    /// is how the convent's collapse leaves Christof empty-handed for the scene that follows. Note
    /// what the *unset* bit does: the end clears the hide outright, so an ordinary conversation
    /// hands a weapon back that a script had hidden with `EnableActorWeapon(false)`.
    /// </summary>
    NoRestoreWeapons = 0x400,

    /// <summary>
    /// **What the shipped game passes, 296 times out of 301** — callbacks and nothing else.
    /// Measured across the bytecode of all 465 Codex classes; the
    /// exceptions are two conversations at <c>0x006</c>, two at <c>0x404</c>, and the generic
    /// `RpgConversation` thing script, whose flags come from level data.
    ///
    /// The consequence is that <see cref="Abortable"/>, <see cref="NoSkip"/> and
    /// <see cref="NoAnimation"/> are **never set by anything the game ships**: no conversation in
    /// the original can be abandoned, every line can be skipped, and everybody always acts it out.
    /// </summary>
    Shipped = Callbacks,
}

/// <summary>
/// A voice line as the engine finds one: the words from the string table, and how long the
/// recording is. The voice table looks the identifier up; the sound loader then
/// loads its `.mp3`, and answers null for the **43% of lines that were never recorded**.
/// </summary>
/// <param name="Words">The subtitle text. Empty for the eleven identifiers that resolve to nothing.</param>
/// <param name="LengthMs">The recording's length, or <b>0 when there is no recording</b>.</param>
/// <param name="Recording">The recording itself, when there is sound to play it on.</param>
public readonly record struct ConversationVoice(string Words, int LengthMs, Audio.Sound? Recording = null);

/// <summary>
/// One reply on offer. The engine keeps these in an array of **36-byte records at the instance's
/// the step's slot** and never checks the count against the eight slots there is room for.
/// </summary>
/// <param name="Kind"><see cref="NcoStep.AnswerGoto"/> or <see cref="NcoStep.AnswerReturn"/>.</param>
/// <param name="Target">The resolved event index for a goto, or the code for a return.</param>
/// <param name="Words">What is listed on screen — the bare text, with no speaker in front of it.</param>
/// <param name="Volume">Per cent, from the step's <see cref="NcoLine.Flags"/>.</param>
/// <param name="DurationMs">How long the conversation waits after it is picked.</param>
/// <param name="FromEvent">The event it was offered in, and the step within it.</param>
/// <param name="FromStep"><inheritdoc cref="FromEvent"/></param>
/// <param name="Voiced">Whether a recording was found for it. 81% of replies have one.</param>
/// <param name="Recording">The recording, loaded when the reply is offered and let go
/// when another is picked.</param>
public readonly record struct ConversationAnswer(
    NcoStep Kind, int Target, string Words, int Volume, int DurationMs, int FromEvent, int FromStep,
    bool Voiced, Audio.Sound? Recording = null);

/// <summary>
/// The middle of a conversation: what advances it, how replies are offered, and what reaches the
/// script. This is the original's tick, its step, its answer
/// list, the pick, the skip, the abort
/// and the finish.
///
/// It stops at the pane: <see cref="Subtitle"/> and <see cref="Offered"/> say what `vuiConversation`
/// would be drawing, and nothing draws it.
/// </summary>
public sealed partial class Conversation
{
    /// <summary>The instance's flags.</summary>
    public ConversationFlag Flags { get; set; }

    /// <summary>
    /// The number a script passed as `ExecuteConversation`'s **fifth** argument, which is
    /// echoed back as the first argument of all three callbacks and means nothing to the engine.
    /// </summary>
    public int Id { get; set; }

    /// <summary>Where the string table and the recordings come from. Null means neither exists.</summary>
    public Func<string, ConversationVoice?>? Voices { get; set; }

    /// <summary>The event being played, and the step within it. Both start at −1.</summary>
    public int EventIndex { get; private set; } = -1;
    /// <inheritdoc cref="EventIndex"/>
    public int StepIndex { get; private set; } = -1;

    /// <summary>
    /// When the next step is due, on the engine's millisecond clock. **Zero means the
    /// conversation is not running** — the tick's second test — and <see cref="Begin"/> sets it to
    /// *now*, so the first step happens on the very next frame (the start's last step).
    /// </summary>
    public int NextAt { get; private set; }

    /// <summary>
    /// The code an `ANSWERRETURN:` left behind, handed to `convended` when it finishes.
    /// **This is what an `ANSWERRETURN:`'s 1, 2 or 3 is returned to** — not `convreached`.
    /// </summary>
    public int ReturnCode { get; private set; }

    /// <summary>What the player may say, in the order the steps offered it.</summary>
    public IReadOnlyList<ConversationAnswer> Offered => answers;
    readonly List<ConversationAnswer> answers = [];

    /// <summary>Waiting for the player to pick one, and whether they have.</summary>
    public bool Waiting { get; private set; }
    /// <inheritdoc cref="Waiting"/>
    public bool Picked { get; private set; }

    /// <summary>Whether what ends the conversation is a return rather than a fall off the end.</summary>
    public bool Returning { get; private set; }

    /// <summary>Whether this is still the conversation the world is having.</summary>
    public bool Running { get; private set; }

    /// <summary>
    /// The subtitle as it stands, formatted by <see cref="Subtitle(string, string)"/>, or null when
    /// nothing is up. This is what `vuiConversation` would be drawing.
    /// </summary>
    public string? Onscreen { get; private set; }

    /// <summary>Who is speaking right now, when the speaker resolved to somebody.</summary>
    public WorldThing? Speaking { get; private set; }

    /// <summary>`convreached(id, event, step, ms, who)` — fired for **every step as it is spoken**.</summary>
    public event Action<int, int, int, int, int>? Reached;

    /// <summary>
    /// A line has been put on screen, already formatted as <c>&lt;speaker&gt; words</c>. The engine
    /// *appends* to its subtitle box rather than replacing it, so the pane wants the
    /// event rather than <see cref="Onscreen"/>: the box is a running transcript, not one line.
    /// Nothing is raised for a line the subtitle setting suppressed.
    /// </summary>
    public event Action<string>? Spoke;

    /// <summary>`convchosen(id, event, answer, ms, who)` — fired when the player picks a reply.</summary>
    public event Action<int, int, int, int, int>? Chosen;

    /// <summary>
    /// `convended(id, aborted, code)` — fired once, by all three ways out: running off the end of an
    /// event, an `ANSWERRETURN:`, and <see cref="Abort"/>. Only the abort passes <c>true</c>.
    /// </summary>
    public event Action<int, bool, int>? Ended;

    /// <summary>The mode a speaker plays while it is their line: **`talk`, 117**.</summary>
    public const int TalkMode = 117;

    /// <summary>
    /// How long an unvoiced line lasts: **350&#160;ms a word plus 500**
    /// (`strtok` on <c>" \r\t\n"</c>, count the tokens, <c>count * 0x15e + 500</c>), and then the
    /// caller adds **another 500**. So a ten-word line with no recording is 5 seconds.
    /// </summary>
    public const int WordMs = 350;

    /// <inheritdoc cref="WordMs"/>
    public const int ReadPadMs = 500;

    /// <summary>What a step adds on top of the voice or the reading time.</summary>
    public const int StepPadMs = 500;

    /// <summary>How long a step that only adds a reply to the list takes: **50&#160;ms** (the step
    /// returns 0x32), so replies appear one at a time rather than all at once.</summary>
    public const int AnswerStepMs = 50;

    /// <summary>The volume a line plays at when its <see cref="NcoLine.Flags"/> ask for nothing.</summary>
    public const int DefaultVolume = 75;

    /// <summary>
    /// Room for **eight** replies (36 bytes each) and **32** speakers
    /// (36 bytes each — a GUID and a 32-byte name). Neither is checked. The
    /// shipped data never comes close: the most replies one event offers is **five**
    /// (`6_1_Unorna`, `ev_0`) and the most speakers one conversation has is **seven**
    /// (`13_1_Good`), plus the player.
    /// </summary>
    public const int MaxAnswers = 8;

    /// <inheritdoc cref="MaxAnswers"/>
    public const int MaxCast = 32;

    /// <summary>
    /// The volume bits, tested in the order the engine tests them (the step, and the same ladder
    /// again in the answer list): 1 → 50, 2 → 63, 4 → 87, 8 → 100, nothing → <see cref="DefaultVolume"/>.
    /// </summary>
    public static int Volume(int flags) =>
        (flags & 1) != 0 ? 50 : (flags & 2) != 0 ? 63 : (flags & 4) != 0 ? 87 : (flags & 8) != 0 ? 100
        : DefaultVolume;

    /// <summary>The reading time for text with no recording behind it.</summary>
    public static int ReadingTime(string words) =>
        words.Split(' ', '\r', '\t', '\n').Count(w => w.Length > 0) * WordMs + ReadPadMs;

    /// <summary>
    /// How long a spoken step takes. A **voiced** line waits the recording out plus
    /// <see cref="StepPadMs"/>; an **unvoiced** one waits <see cref="ReadingTime"/> plus the same;
    /// and the step's own <see cref="NcoLine.ExtraMs"/> is added to both.
    ///
    /// The "do not wait" bit is honoured **only for a voiced line** — the engine re-reads the flags
    /// inside the branch that found a recording and nowhere else, so an unvoiced line
    /// carrying <c>0x10</c> waits anyway. Nothing ships a fourth token, so the quirk is unreachable;
    /// it is here because it is what the code does.
    /// </summary>
    public int StepDuration(NcoLine step, ConversationVoice? voice)
    {
        if (voice is { LengthMs: > 0 } sound)
            return (step.Flags & 0x10) != 0 ? step.ExtraMs
                 : sound.LengthMs + StepPadMs + step.ExtraMs;
        return ReadingTime(voice?.Words ?? "") + StepPadMs + step.ExtraMs;
    }
}

public sealed partial class Conversation
{
    Anim.MotionSets? motions;

    /// <summary>
    /// Start it running. <see cref="Begin"/> does the actors; this puts the state machine at the
    /// beginning and sets <see cref="NextAt"/> to now, so the first step happens on the next tick.
    /// </summary>
    public void Start(int now, Anim.MotionSets? sets = null)
    {
        motions = sets;
        Begin(sets);
        // The music and the ambient drop to half for the length of it, in a
        // single-player game.
        Audio?.Duck(true);
        EventIndex = -1;
        StepIndex = -1;
        ReturnCode = 0;
        answers.Clear();
        Waiting = Picked = Returning = false;
        Onscreen = null;
        Speaking = null;
        Running = true;
        // **Never zero.** A start time of 0 is the engine's own "not running" — the tick's second test —
        // and it gets away with it because the game clock is a millisecond clock that is never zero
        // by the time anything starts a conversation. Vitomir's simulation clock *does* start at 0,
        // so a conversation started on the first frame would set the sentinel and never tick again.
        // One millisecond is the whole difference and it is invisible.
        NextAt = Math.Max(1, now);
    }

    /// <summary>
    /// One frame of it. Answers whether anything happened — nothing does when it is not
    /// running, when <see cref="NextAt"/> has not come round, or while it is
    /// <see cref="Waiting"/> on the player.
    ///
    /// The order is the engine's, and the first three tests are the ones that matter:
    ///
    /// <list type="number">
    /// <item><see cref="NextAt"/> of zero, or still in the future — nothing.</item>
    /// <item>a reply was <see cref="Picked"/> — clear the wait, and if it was an `ANSWERRETURN:`
    /// **finish here**.</item>
    /// <item>still <see cref="Waiting"/> — nothing.</item>
    /// </list>
    ///
    /// then advance one step, and **when the step index runs past the event's last, the whole
    /// conversation is over** — the events are not a list you fall through, each one ends it.
    /// </summary>
    public bool Tick(int now)
    {
        if (!Running || NextAt == 0 || now < NextAt) return false;

        if (Picked)
        {
            bool wasReturn = Returning;
            Waiting = false;
            Picked = false;
            if (wasReturn) { Returning = false; Finish(aborted: false); return true; }
        }
        if (Waiting) return false;

        if (EventIndex == -1)
        {
            // The just-started state the constructor leaves behind (the event −1).
            answers.Clear();
            EventIndex = 0;
            StepIndex = 0;
        }
        else
        {
            var running = Talk.Events[EventIndex];
            int was = StepIndex;
            StepIndex = was + 1;
            if (running.Steps.Count <= was + 1) { Finish(aborted: false); return true; }
        }

        var step = Talk.Events[EventIndex].Steps[StepIndex];
        switch (step.Kind)
        {
            case NcoStep.Jump:
                // Resolved to an event index when the file was read; a name that matched
                // nothing would have been left as it was, and every shipped one matches.
                EventIndex = Index(step.Event);
                StepIndex = -1;
                answers.Clear();
                NextAt = now + 1;
                return true;

            case NcoStep.Delay:
                // Straight onto the millisecond clock: the parse already turned the page's seconds
                // into milliseconds (NcoLine.Pause).
                NextAt = now + step.Pause;
                // **And the script hears about it, exactly as it hears about a spoken line.** The
                // delay case notifies (the pause, and a zero
                // speaker — then the line's report, which is the same `convreached` the line case calls,
                // behind the same CONV_XFLAG_WANTFEEDBACK gate).
                //
                // This is not a detail. **A delay step is how the shipped scripts hang an action on
                // a beat of silence**, and skipping the callback silently threw those actions away:
                // `CNVT_3_1`'s collapse is a delay step that plays MOTION_DEATHSLOW and the one
                // after it is the fade to black, so Christof never fell over at all and the scene
                // simply cut (*"it happens way too fast and it basically skips
                // that animation"* — it was skipping it entirely). `CNVT_3_3`'s swing onto the
                // archbishop is the same shape, which is why the camera never looked at Geza.
                Notify(step.Pause, null);
                return true;
        }

        var who = step.Kind == NcoStep.Line ? Find(step.Speaker, Actors, PlayerThing) : PlayerThing;
        int ms = Speak(step, who);
        var last = Talk.Events[EventIndex];
        if (StepIndex < last.Steps.Count - 1)
        {
            NextAt = now + ms;
            Notify(ms, who);
            return true;
        }
        if (step.Kind is NcoStep.AnswerGoto or NcoStep.AnswerReturn)
        {
            // The last step of an event offered a reply, so stop and wait for one.
            Waiting = true;
            Notify(ms, who);
            return true;
        }
        // A `LINE:` as the last step of an event is how a conversation normally ends: the engine
        // arms the return path and finishes on the next tick, once the line has been heard.
        Returning = true;
        Picked = true;
        NextAt = now + ms;
        Notify(ms, who);
        return true;
    }

    /// <summary>The party leader, for the lines whose speaker is <see cref="Player"/>.</summary>
    public WorldThing? PlayerThing { get; set; }

    /// <summary>
    /// Speak one step and answer how long it takes. A `LINE:` puts a subtitle up and
    /// moves the talking animation along; an `ANSWERGOTO:` or `ANSWERRETURN:` adds itself to
    /// <see cref="Offered"/> instead and takes <see cref="AnswerStepMs"/>.
    /// </summary>
    int Speak(NcoLine step, WorldThing? who)
    {
        var voice = Voices?.Invoke(step.Id);
        int ms = StepDuration(step, voice);

        if (step.Kind != NcoStep.Line)
        {
            // Record the reply and list it, bare — a plain `%s`, not the
            // `<%s> %s` a line gets. The previous speaker drops back to listening.
            answers.Add(new ConversationAnswer(
                step.Kind,
                step.Kind == NcoStep.AnswerGoto ? Index(step.Event) : step.Code,
                voice?.Words ?? "", Volume(step.Flags), ms, EventIndex, StepIndex,
                voice is { LengthMs: > 0 }, voice?.Recording));
            ReleaseLastSpeaker();
            return AnswerStepMs;
        }

        Speaking = who;
        // The recording: flat, centred, at the line's own volume, onto the next slot of
        // the ring of eight — which stops whatever the slot eight lines ago is still playing — and
        // on the speaker's mouth.
        if (voice?.Recording is { } recording) Say(recording, Volume(step.Flags), who);
        // The subtitle is skipped when subtitles are switched off — but **only for a line that has
        // a recording**. 43% of the game has none, and those are shown whatever the setting, or
        // two lines in five would be silent and invisible (the step's two branches, and the pick
        // again for a chosen reply).
        Onscreen = Subtitles || voice is not { LengthMs: > 0 }
            ? Subtitle(Display(step.Speaker, who), voice?.Words ?? "")
            : null;
        if (Onscreen is { } shown) Spoke?.Invoke(shown);

        if ((Flags & ConversationFlag.NoAnimation) == 0)
        {
            ReleaseLastSpeaker();
            if (who is not null) { Play(who, TalkMode, motions); LastSpeaker = who; }
        }
        else LastSpeaker = null;
        return ms;
    }

    /// <summary>The game's sound, which the voices go to. Null plays nothing.</summary>
    public Audio.GameAudio? Audio { get; set; }

    /// <summary>
    /// The ring of eight voices (three words a slot: in use, the recording, the channel),
    /// and where the next one goes.
    /// </summary>
    readonly (Audio.Sound? Sound, int Channel)[] ring = new (Audio.Sound?, int)[VoiceSlots];
    int ringAt;

    /// <summary>The voices still in the ring, for a test to look at.</summary>
    public IEnumerable<int> VoiceChannels => ring.Where(r => r.Sound is not null).Select(r => r.Channel);

    /// <summary>
    /// Plays a recording onto the ring: the slot is filled, the ring moves
    /// on, and if the slot it moves to is still in use, **that** voice is stopped and let go. The
    /// speaker's mouth is kept busy for the length of it.
    /// </summary>
    void Say(Audio.Sound recording, int volume, WorldThing? speaker)
    {
        if (Audio is not { } audio) return;
        int id = audio.Mixer.Play2D(recording, volume * 0.01f, 0.5f, 0);
        if (speaker is not null)
        {
            speaker.MouthBusyUntil = audio.Mixer.GameMs + recording.LengthMs;
            speaker.MouthChannel = id;
        }
        ring[ringAt] = (recording, id);
        ringAt = (ringAt + 1) % VoiceSlots;
        if (ring[ringAt].Sound is { } old)
        {
            audio.Mixer.Stop(ring[ringAt].Channel);
            audio.Bank.Release(old);
            ring[ringAt] = default;
        }
    }

    /// <summary>Every voice in the ring stopped and let go, and the ring back to its start.</summary>
    void Hush()
    {
        for (int i = 0; i < ring.Length; i++)
            if (ring[i].Sound is { } sound)
            {
                Audio?.Mixer.Stop(ring[i].Channel);
                Audio?.Bank.Release(sound);
                ring[i] = default;
            }
        ringAt = 0;
    }

    /// <summary>The end of it: the voices stop and the music comes back up.</summary>
    void Quiet()
    {
        Hush();
        Audio?.Duck(false);
    }

    /// <summary>The engine's own subtitle setting (bit 0 of its options), on by default.</summary>
    public bool Subtitles { get; set; } = true;

    /// <summary>
    /// The name in the brackets. The engine looks the line's cast id up in the string table
    /// (the cast id's string, looked up at the start) and uses **that**, not the id — so a character
    /// can be `Anezka` to the data and something else on screen, and a line whose speaker is
    /// `Player` reads `&lt;Player&gt;`, because `PLAYER` is a key in `game.nls` like any other. With
    /// no string table wired in (<see cref="Names"/>), the speaking character's cast id stands in.
    /// </summary>
    string Display(string castId, WorldThing? who) =>
        Names is { } names ? names(castId) : who?.Actor?.CastId ?? castId;

    /// <summary>
    /// The string table, keyed by cast id, for the names in the brackets. The engine's
    /// lookup answers the key itself when the table has no such key.
    /// </summary>
    public Func<string, string>? Names { get; set; }

    /// <summary>Put whoever spoke last back to `listen`, or `stand` when they have no `listen`.</summary>
    void ReleaseLastSpeaker()
    {
        if ((Flags & ConversationFlag.NoAnimation) != 0) { LastSpeaker = null; return; }
        if (LastSpeaker is { Actor: not null } last && !Play(last, Listen, motions))
            Play(last, Stand, motions);
        LastSpeaker = null;
    }

    /// <summary>The index of an event by name, or 0 — which is what an unresolved target becomes.</summary>
    int Index(string name)
    {
        for (int i = 0; i < Talk.Events.Count; i++)
            if (string.Equals(Talk.Events[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    /// <summary>
    /// The player says one of the replies on offer. Refused unless the conversation is
    /// <see cref="Waiting"/>, nothing has been <see cref="Picked"/> yet, and the index is one that
    /// was offered.
    ///
    /// Picking speaks the reply — the subtitle goes up as **`&lt;the player&gt; words`**, which is
    /// the one place a reply gets a speaker in front of it — and then either moves to the goto's
    /// event or arms the return with its code.
    /// </summary>
    public bool Pick(int index, int now)
    {
        if (!Waiting || Picked || index < 0 || index >= answers.Count) return false;
        var answer = answers[index];

        // Every voice still going is stopped, the ring starts again, the reply is said on
        // the player's mouth, and the replies not taken are let go.
        Hush();
        if (answer.Recording is { } said) Say(said, answer.Volume, PlayerThing);
        for (int i = 0; i < answers.Count; i++)
            if (i != index && answers[i].Recording is { } unsaid) Audio?.Bank.Release(unsaid);

        Speaking = PlayerThing;
        Onscreen = Subtitles || !answer.Voiced ? Subtitle(PlayerName, answer.Words) : null;
        if (Onscreen is { } chosen) Spoke?.Invoke(chosen);

        if (answer.Kind == NcoStep.AnswerGoto)
        {
            EventIndex = answer.Target;
            StepIndex = -1;
            answers.Clear();
            Returning = false;
        }
        else
        {
            ReturnCode = answer.Target;
            Returning = true;
        }
        NextAt = now + answer.DurationMs;
        Picked = true;

        if ((Flags & ConversationFlag.NoAnimation) == 0)
        {
            ReleaseLastSpeaker();
            if (PlayerThing is not null) { Play(PlayerThing, TalkMode, motions); LastSpeaker = PlayerThing; }
        }
        else LastSpeaker = null;

        // `convchosen`'s event is the instance's event **as it stands after the pick** (the report
        // reads it, and the goto branch has already put the goto's target there):
        // so a goto reports the event it leads to, a return the event it was offered in.
        // `29_1_MeetLily` is written to this — its script asks for event 2, answer 2; the answer is
        // offered in ev_0 and goes to ev_2, and that is where Christof's Humanity for protecting
        // Lily comes from.
        if ((Flags & ConversationFlag.Callbacks) != 0)
            Chosen?.Invoke(Id, EventIndex, index, answer.DurationMs, Guid(PlayerThing));
        Report?.Reply(this, answer.FromEvent, answer.FromStep, answer.DurationMs, Guid(PlayerThing));   // the reply's client branch
        return true;
    }

    /// <summary>
    /// The name the player's replies are shown under — cast entry 0's display name, which the
    /// engine fills at the same time as the rest of the cast. In a single
    /// player game it is whoever you are playing; in multiplayer it is literally `Player`.
    /// </summary>
    public string PlayerName { get; set; } = Player;

    /// <summary>
    /// Skip the line being spoken: stop the voices and make the next step due
    /// immediately. Refused when <see cref="ConversationFlag.NoSkip"/> is set, unless forced.
    /// </summary>
    public bool Skip(int now, bool force = false)
    {
        if (!Running) return false;
        if ((Flags & ConversationFlag.NoSkip) != 0 && !force) return false;
        ReleaseLastSpeaker();
        Hush();                                                // The voices stop too
        Onscreen = null;
        NextAt = now + 1;
        return true;
    }

    /// <summary>
    /// Give up on it — the escape key, not anything a script does. Refused unless
    /// <see cref="ConversationFlag.Abortable"/> is set or it is forced, and it is the **only** way
    /// out that tells the script it was aborted.
    /// </summary>
    public bool Abort(bool force = false)
    {
        if (!Running) return false;
        if ((Flags & ConversationFlag.Abortable) == 0 && !force) return false;
        ReleaseLastSpeaker();
        Finish(aborted: true);
        return true;
    }

    /// <summary>
    /// The one exit. Everybody is let go, the callback goes out with the code that is standing, and
    /// the conversation stops being the world's.
    /// </summary>
    void Finish(bool aborted)
    {
        End(motions);
        Quiet();
        Onscreen = null;
        Speaking = null;
        Waiting = Picked = Returning = false;
        Running = false;
        NextAt = 0;
        Closed?.Invoke();
        if ((Flags & ConversationFlag.Callbacks) != 0) Ended?.Invoke(Id, aborted, ReturnCode);
        Report?.Over(this, aborted, ReturnCode);                           // the finish's client branch
    }

    /// <summary>
    /// The teardown on its own, without the finish around it: everybody is let go and
    /// the panes close, and <b>the script is not told</b>. What <c>EndConversation</c> does, and what
    /// happens to a running conversation when a script starts another.
    /// </summary>
    public void Stop(Anim.MotionSets? sets = null)
    {
        if (!Running) return;
        End(sets ?? motions);
        Quiet();
        Onscreen = null;
        Speaking = null;
        Waiting = Picked = Returning = false;
        Running = false;
        NextAt = 0;
        Closed?.Invoke();
    }

    /// <summary>
    /// A change of place with a conversation running (from the place
    /// change and its kin, after every script has heard `endscene`): the teardown,
    /// and then — unlike <see cref="Stop"/> — a script that asked for callbacks is told
    /// <c>convended(id, aborted = true, code)</c>, and a network player's machine
    /// says so to the host (kind 2).
    /// </summary>
    public void Abandon(Anim.MotionSets? sets = null)
    {
        if (!Running) return;
        Stop(sets);
        if ((Flags & ConversationFlag.Callbacks) != 0) Ended?.Invoke(Id, true, ReturnCode);
        Report?.Over(this, true, ReturnCode);
    }

    /// <summary>
    /// It has stopped, however it stopped — the one event that does not depend on the callback
    /// flag, because it is for the panes (`vuiConversation::Close`), not the script.
    /// </summary>
    public event Action? Closed;

    void Notify(int ms, WorldThing? who)
    {
        if ((Flags & ConversationFlag.Callbacks) != 0)
            Reached?.Invoke(Id, EventIndex, StepIndex, ms, Guid(who));
        ReportLine(ms, who);                                               // the line's client branch
    }

    static int Guid(WorldThing? thing) => thing?.Guid ?? 0;
}
