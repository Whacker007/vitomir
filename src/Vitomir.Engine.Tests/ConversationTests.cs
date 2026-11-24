// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// A conversation: who is in it, what being in it does to them, and what moves it along. The format
/// is NCO; the runtime is the original's — the start, the end and the stand
/// for the two ends, and the step, its length, the reply, the target, the keys and the abort
/// for the middle.
/// </summary>
public class ConversationTests
{
    const string Sample = """
        CONVERSATION: Test 0
        EVENT: ev_0
        	LINE:         Anezka_1_1_1                 Anezka
        	LINE:         Wilhem_1_1_2                 Wilhem
        	ANSWERGOTO:   Player_1_1_3                 ev_1
        	LINE:         Anezka_1_1_4                 Anezka
        EVENT: ev_1
        	LINE:         Christof_1_1_5                Player
        """;

    static NcoConversation Talk(string text = Sample) =>
        NcoFile.Read(Encoding.Latin1.GetBytes(text.ReplaceLineEndings("\r\n")), "Test.nco").Conversations[0];

    static WorldThing Character(string castId, int guid, ThingType type = ThingType.Actor) => new()
    {
        Index = guid, Guid = guid, Scene = new NsdThing(), Template = "t", Type = type,
        Actor = new World.Actor { CastId = castId, Health = 100f, MaxHealth = 100f },
    };

    /// <summary>The cast a resolution looks through. No level needed: it is a name lookup.</summary>
    static WorldThing[] Place(params WorldThing[] people) => people;

    // ---- who is in it ---------------------------------------------------------------------------

    /// <summary>
    /// Each **distinct** speaker once, in the order the walk meets them — every event, every step.
    /// A reply is not a speaker: only `LINE:` puts somebody in the list.
    /// </summary>
    [Fact]
    public void Every_distinct_speaker_once_in_the_order_they_are_met()
    {
        var anezka = Character("Anezka", 1);
        var wilhem = Character("Wilhem", 2);
        var christof = Character("Christof", 3);
        var place = Place(anezka, wilhem, christof);

        var talk = Conversation.Resolve(Talk(), place, player: christof);

        Assert.Equal([anezka, wilhem, christof], talk.Actors);
        Assert.Empty(talk.Missing);
    }

    /// <summary>
    /// **The chronicle's speaker is in every conversation**, first, whether or not he has a line
    /// (the start opens the list with the metafile's `SPEAKER`). In 1.1 — Wilhem steered, Wilhem
    /// clicking — Ecaterina's one-line answer `8_1_EcatReturn2`, whose only speaker is `Ecat`, put
    /// **Christof** into the conversation, listening, with his sword away (logged).
    /// </summary>
    [Fact]
    public void The_chronicle_speaker_is_in_every_conversation_even_without_a_line()
    {
        var ecat = Character("Ecat", 1);
        var christof = Character("Christof", 3, ThingType.Player);
        var wilhem = Character("Wilhem", 2, ThingType.Player);
        var place = Place(ecat, wilhem, christof);
        var talk = Conversation.Resolve(Talk("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Ecat\n"), place,
                                        player: christof, speaker: "Christof");
        Assert.Equal([christof, ecat], talk.Actors);
        // A speaker who also has lines — here as "Player" — is still in it once, and first.
        var both = Conversation.Resolve(Talk(), Place(Character("Anezka", 4), wilhem, christof),
                                        player: christof, speaker: "Christof");
        Assert.Single(both.Actors, christof);
        Assert.Same(christof, both.Actors[0]);
    }

    /// <summary>
    /// **A speaker name is a `CASTID`**, with one exception the data makes necessary: `Player`,
    /// which is whoever the party leader is rather than a cast member. 41 shipped lines use it.
    /// </summary>
    [Fact]
    public void The_player_is_resolved_as_the_party_leader_and_not_by_cast_id()
    {
        var christof = Character("Christof", 3);
        var place = Place(christof);
        Assert.Same(christof, Conversation.Find("Player", place, christof));
        Assert.Same(christof, Conversation.Find("player", place, christof));
        // And with nobody leading, the name resolves to nobody rather than to a cast member.
        Assert.Null(Conversation.Find("Player", place, null));
    }

    /// <summary>
    /// Four shipped speakers name nobody at all — `Nun`, `Ped`, `MDNos1` and `Prague2`, six lines
    /// between them. They are kept as names rather than dropped, because a line still has to be
    /// said by somebody the subtitle can print.
    /// </summary>
    [Fact]
    public void A_speaker_that_resolves_to_nobody_is_recorded_rather_than_dropped()
    {
        var place = Place(Character("Anezka", 1));
        var talk = Conversation.Resolve(Talk("CONVERSATION: T 0\nEVENT: ev_0\n"
                                           + "\tLINE: a Anezka\n\tLINE: b Nun\n"), place);
        Assert.Single(talk.Actors);
        Assert.Equal(["Nun"], talk.Missing);
    }

    // ---- and what it does to them -----------------------------------------------------------------

    /// <summary>
    /// **This is why a conversation freezes a room.** Beginning one sets
    /// `THING_AF2_CONVERSATION` on everybody in it, and the AI's think gate tests that bit second,
    /// right after death — so a character talking to you does not wander, does not notice you and
    /// does not fight.
    /// </summary>
    [Fact]
    public void Beginning_a_conversation_takes_everybody_in_it_out_of_the_ai()
    {
        var anezka = Character("Anezka", 1);
        var wilhem = Character("Wilhem", 2);
        var bystander = Character("Ahzra", 9);
        var place = Place(anezka, wilhem, bystander);
        var talk = Conversation.Resolve(Talk(), place);

        Assert.False(Conversation.Talking(anezka));
        Assert.Equal(2, talk.Begin());            // Christof is not in this world, so two of the three

        Assert.True(Conversation.Talking(anezka));
        Assert.True(Conversation.Talking(wilhem));
        Assert.False(Conversation.Talking(bystander));   // and everybody else carries on

        talk.End();
        Assert.False(Conversation.Talking(anezka));
        Assert.False(Conversation.Talking(wilhem));
    }

    /// <summary>
    /// Whatever they were in the middle of is cancelled — **unforced** (the cancel with (1, 0),
    /// read in a comparison with 1.1), so one kept busy keeps its order and its busy time, and one that is
    /// free has it dropped. Vitomir dropped both and cleared the busy time, which 1.1 never touches.
    /// </summary>
    [Fact]
    public void Beginning_one_cancels_what_they_were_doing_unless_they_are_busy()
    {
        var anezka = Character("Anezka", 1);
        var wilhem = Character("Wilhem", 2);
        anezka.Actor!.Action = ActionMode.Attack;
        anezka.Actor.BusyUntil = 10_000;
        wilhem.Actor!.Action = ActionMode.Attack;
        var talk = Conversation.Resolve(Talk(), Place(anezka, wilhem));

        talk.Begin();

        Assert.Equal(ActionMode.Attack, anezka.Actor.Action);
        Assert.Equal(10_000, anezka.Actor.BusyUntil);
        Assert.Equal(ActionMode.None, wilhem.Actor.Action);
    }

    /// <summary>
    /// **A fallen companion stands up for a conversation and vanishes after it** (the
    /// A comparison with 1.1's gap). A dead player-type member of the cast with a
    /// model has its held death pose stopped, `DON'T RENDER` cleared and its alpha put to 1 at once,
    /// so it listens with everybody; at the end it is given `DON'T RENDER` — and a revive clears it.
    /// A dead character that is not a player's, or has no model, is left alone.
    /// </summary>
    [Fact]
    public void A_fallen_companion_stands_for_the_talk_and_is_hidden_after_it()
    {
        var anezka = Character("Anezka", 1);
        var wilhem = Character("Wilhem", 2, ThingType.Player);
        wilhem.Model = "wilhem.nod";
        wilhem.Actor!.Flags |= World.Actor.Flag.Dead;
        wilhem.Model3D.SetAlpha(0f, 0f);                          // faded where he fell
        wilhem.RenderFlags |= FallenListener.DontRender;
        anezka.Actor!.Flags |= World.Actor.Flag.Dead;             // dead, but not a party member's kind
        var talk = Conversation.Resolve(Talk(), Place(anezka, wilhem));

        talk.Begin();
        Assert.Equal(0u, wilhem.RenderFlags & FallenListener.DontRender);
        Assert.Equal(1f, wilhem.ModelAlpha);

        talk.End();
        Assert.NotEqual(0u, wilhem.RenderFlags & FallenListener.DontRender);
        Assert.Equal(0u, anezka.RenderFlags & FallenListener.DontRender);

        Damage.Revive(wilhem, 0.1f, 0.1f);
        Assert.Equal(0u, wilhem.RenderFlags & FallenListener.DontRender);
    }

    /// <summary>The subtitle is `&lt;speaker&gt; words`, and a line with no speaker is just the words.</summary>
    [Theory]
    [InlineData("Anezka", "Christof, wait.", "<Anezka> Christof, wait.")]
    [InlineData("", "Christof, wait.", "Christof, wait.")]
    public void The_subtitle_puts_the_speaker_in_angle_brackets(string who, string words, string shown) =>
        Assert.Equal(shown, Conversation.Subtitle(who, words));

    [Fact]
    public void The_two_modes_and_the_voice_ring_are_the_engines_numbers()
    {
        Assert.Equal(118, Conversation.Listen);      // `listen`
        Assert.Equal(33, Conversation.Stand);        // `stand`, the fallback
        Assert.Equal(8, Conversation.VoiceSlots);
        Assert.Equal("listen", NmsMotionSet.BodyModes[Conversation.Listen]);
        Assert.Equal("stand", NmsMotionSet.BodyModes[Conversation.Stand]);
    }

    // ---- against the shipped conversations ---------------------------------------------------------

    /// <summary>
    /// **90 of the 95 distinct speakers in the 310 shipped conversations are `CASTID` values** on a
    /// shipped template. That is what says a speaker name is a cast key rather than a label. The
    /// five that are not are `Player` and four that name nobody: `Nun`, `Ped`, `MDNos1`, `Prague2`.
    /// </summary>
    [Fact]
    public void A_speaker_name_is_a_cast_id()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);

        var cast = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in templates.Names)
            if (templates.Resolve(name).TryGetValue(Tags.CastId, out var id) && id.AsString().Length > 0)
                cast.Add(id.AsString());

        var speakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in files.List("conversations"))
            foreach (var talk in NcoFile.Read(files.Read(name), name).Conversations)
                foreach (var e in talk.Events)
                    foreach (var step in e.Steps)
                        if (step.Kind == NcoStep.Line && step.Speaker.Length > 0) speakers.Add(step.Speaker);

        var strangers = speakers.Where(s => !cast.Contains(s)).OrderBy(s => s).ToArray();
        Assert.Equal(95, speakers.Count);
        Assert.Equal(["MDNos1", "Nun", "Ped", "Player", "Prague2"], strangers);
    }
    // ---- the middle: what moves it along -----------------------------------------------------------

    /// <summary>A string table and recordings, for tests that need the timings to be predictable.</summary>
    static Func<string, ConversationVoice?> Voice(int lengthMs = 0, string words = "one two three") =>
        _ => new ConversationVoice(words, lengthMs);

    static Conversation Running(string text, out WorldThing player, int now = 1000,
                                ConversationFlag flags = ConversationFlag.Callbacks,
                                Func<string, ConversationVoice?>? voices = null)
    {
        var anezka = Character("Anezka", 1);
        var wilhem = Character("Wilhem", 2);
        player = Character("Christof", 3);
        var talk = Conversation.Resolve(Talk(text), Place(anezka, wilhem, player), player);
        talk.Flags = flags;
        talk.Id = 77;
        talk.PlayerThing = player;
        talk.Voices = voices ?? Voice();
        talk.Start(now);
        return talk;
    }

    /// <summary>
    /// **An unvoiced line waits to be read**: 350&#160;ms a word plus 500, and then the step adds
    /// another 500. A **voiced** one waits the recording out plus 500. That is what fills the
    /// silence for the two lines in five the game never recorded.
    /// </summary>
    [Fact]
    public void A_line_lasts_its_recording_or_the_time_it_takes_to_read()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n", out _);
        var step = new NcoLine { Kind = NcoStep.Line, Id = "a", Speaker = "Anezka" };

        // three words, no recording: 3*350 + 500, and 500 more from the step
        Assert.Equal(3 * 350 + 500 + 500, talk.StepDuration(step, new ConversationVoice("one two three", 0)));
        // a recording: its own length plus the step's 500, whatever the words say
        Assert.Equal(2_400 + 500, talk.StepDuration(step, new ConversationVoice("one two three", 2_400)));
        // and the fifth token, which nothing ships, is added to both
        Assert.Equal(2_400 + 500 + 120,
            talk.StepDuration(step with { ExtraMs = 120 }, new ConversationVoice("x", 2_400)));
    }

    /// <summary>
    /// The "do not wait" bit (`0x10` of the fourth token) is honoured **only for a voiced line** —
    /// the engine re-reads the flags inside the branch that found a recording and nowhere else, so
    /// an unvoiced line carrying it waits anyway. Unreachable in the shipped data; recorded because
    /// it is what the code does.
    /// </summary>
    [Fact]
    public void The_do_not_wait_flag_is_only_honoured_when_there_is_a_recording()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n", out _);
        var step = new NcoLine { Kind = NcoStep.Line, Id = "a", Speaker = "Anezka", Flags = 0x10 };
        Assert.Equal(0, talk.StepDuration(step, new ConversationVoice("one two", 2_400)));
        Assert.Equal(2 * 350 + 500 + 500, talk.StepDuration(step, new ConversationVoice("one two", 0)));
    }

    /// <summary>
    /// The volume ladder, in the order the engine tests it. Nothing ships a fourth token, so
    /// **every line in the game plays at 75%**.
    /// </summary>
    [Theory]
    [InlineData(0, 75)]
    [InlineData(1, 50)]
    [InlineData(2, 63)]
    [InlineData(4, 87)]
    [InlineData(8, 100)]
    [InlineData(3, 50)]     // bit 1 is tested first, so it wins
    public void The_volume_comes_off_the_line_flags(int flags, int percent) =>
        Assert.Equal(percent, Conversation.Volume(flags));

    /// <summary>
    /// **A conversation started on the very first frame still runs.** `NextAt == 0` is the engine's
    /// own "not running" sentinel, and its clock is never zero when anything starts one; Vitomir's
    /// starts at zero, so `Start` clamps. Found by starting one in the viewer, where it silently
    /// never spoke a word.
    /// </summary>
    [Fact]
    public void A_conversation_started_at_clock_zero_is_still_running()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                           out _, now: 0);
        Assert.NotEqual(0, talk.NextAt);
        Assert.True(talk.Tick(1));
        Assert.NotNull(talk.Onscreen);
    }

    /// <summary>
    /// Nothing happens before its time. The tick's first two tests are `NextAt == 0` — not
    /// running — and `now &lt; NextAt`.
    /// </summary>
    [Fact]
    public void A_conversation_does_nothing_until_its_step_is_due()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                           out _, now: 1000);
        Assert.Equal(1000, talk.NextAt);
        Assert.False(talk.Tick(999));
        Assert.True(talk.Tick(1000));
        Assert.Equal("<Anezka> one two three", talk.Onscreen);
        Assert.False(talk.Tick(talk.NextAt - 1));
    }

    /// <summary>
    /// A `LINE:` puts the speaker on `talk` and drops whoever spoke before back to `listen`, so
    /// exactly one character is talking at a time.
    /// </summary>
    [Fact]
    public void Each_line_hands_the_talking_over()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n"
                         + "\tLINE: c Anezka\n", out _);
        talk.Tick(talk.NextAt);
        Assert.Equal(1, talk.LastSpeaker!.Guid);
        talk.Tick(talk.NextAt);
        Assert.Equal(2, talk.LastSpeaker!.Guid);
        Assert.Equal("<Wilhem> one two three", talk.Onscreen);
    }

    /// <summary>
    /// **`DELAY:` is written in seconds and kept in milliseconds.** The parser multiplies by 1000.0
    /// and truncates, so `DELAY: 3` is a three-second beat — which is what the convent's
    /// collapse is made of.
    /// </summary>
    [Fact]
    public void A_delay_is_seconds_on_the_page_and_milliseconds_in_the_engine()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tDELAY: 3\n\tLINE: a Anezka\n", out _,
                           now: 1000);
        Assert.True(talk.Tick(1000));
        Assert.Equal(4000, talk.NextAt);          // three seconds
        Assert.Null(talk.Onscreen);               // and nothing was said
        Assert.True(talk.Tick(4000));
        Assert.NotNull(talk.Onscreen);
    }

    /// <summary>A fraction survives the multiply, where it used to truncate to nothing.</summary>
    [Fact]
    public void A_fractional_delay_is_kept()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tDELAY: 0.5\n\tLINE: a Anezka\n", out _,
                           now: 1000);
        Assert.True(talk.Tick(1000));
        Assert.Equal(1500, talk.NextAt);
    }

    /// <summary>A `JUMP:` goes to its event and is due on the very next tick.</summary>
    [Fact]
    public void A_jump_moves_to_its_event_immediately()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tJUMP: ev_1\n\tLINE: skipped Anezka\n"
                         + "EVENT: ev_1\n\tLINE: a Wilhem\n\tLINE: b Wilhem\n", out _, now: 1000);
        Assert.True(talk.Tick(1000));
        Assert.Equal(1, talk.EventIndex);
        Assert.Equal(1001, talk.NextAt);
        talk.Tick(1001);
        Assert.Equal(0, talk.StepIndex);
    }

    /// <summary>
    /// Replies are **added one at a time, 50&#160;ms apart**, and the conversation then stops on the
    /// last step of the event and waits. Both kinds can be offered together — eleven shipped events
    /// do it.
    /// </summary>
    [Fact]
    public void Replies_accumulate_one_per_fifty_milliseconds_and_then_it_waits()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n"
                         + "\tANSWERGOTO: g ev_1\n\tANSWERRETURN: r 2\n"
                         + "EVENT: ev_1\n\tLINE: b Wilhem\n", out _, now: 1000);

        talk.Tick(talk.NextAt);                     // the line
        Assert.Empty(talk.Offered);
        Assert.False(talk.Waiting);

        int at = talk.NextAt;
        talk.Tick(at);                              // the first reply
        Assert.Single(talk.Offered);
        Assert.Equal(at + Conversation.AnswerStepMs, talk.NextAt);
        Assert.False(talk.Waiting);

        talk.Tick(talk.NextAt);                     // the second, which is the event's last step
        Assert.Equal(2, talk.Offered.Count);
        Assert.True(talk.Waiting);
        Assert.Equal([NcoStep.AnswerGoto, NcoStep.AnswerReturn], talk.Offered.Select(a => a.Kind));

        // and the tick does nothing at all while it waits, however long you leave it
        Assert.False(talk.Tick(talk.NextAt + 100_000));
    }

    /// <summary>
    /// Picking an `ANSWERGOTO:` speaks the reply — **the one place a reply gets a speaker in front
    /// of it** — and then moves to its event once the reply has been heard.
    /// </summary>
    [Fact]
    public void Picking_a_goto_speaks_it_and_then_moves_to_its_event()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tANSWERGOTO: g ev_1\n"
                         + "EVENT: ev_1\n\tLINE: b Wilhem\n\tLINE: c Wilhem\n", out var player,
                           now: 1000, voices: Voice(2_000));
        talk.PlayerName = "Christof";

        talk.Tick(1000);
        Assert.True(talk.Waiting);
        Assert.False(talk.Pick(1, talk.NextAt));      // there is no second reply
        Assert.True(talk.Pick(0, 2000));

        Assert.Equal("<Christof> one two three", talk.Onscreen);
        Assert.Same(player, talk.Speaking);
        Assert.Equal(1, talk.EventIndex);
        Assert.Equal(2000 + 2_000 + Conversation.StepPadMs, talk.NextAt);
        Assert.False(talk.Pick(0, talk.NextAt));      // and only once

        Assert.True(talk.Tick(talk.NextAt));
        Assert.Equal(0, talk.StepIndex);
        Assert.True(talk.Running);
    }

    /// <summary>
    /// **An `ANSWERRETURN:`'s code is what reaches `convended`** — its third argument. Not
    /// `convreached`, which is the per-step progress callback and carries no code.
    /// </summary>
    [Fact]
    public void An_answerreturn_ends_the_conversation_and_hands_its_code_to_convended()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tANSWERRETURN: r 2\n", out _, now: 1000);
        (int id, bool aborted, int code)? ended = null;
        talk.Ended += (i, a, c) => ended = (i, a, c);

        talk.Tick(1000);
        Assert.True(talk.Pick(0, 2000));
        Assert.Equal(2, talk.ReturnCode);
        Assert.Null(ended);                     // not until the reply has been heard

        Assert.True(talk.Tick(talk.NextAt));
        Assert.Equal((77, false, 2), ended);
        Assert.False(talk.Running);
        Assert.Equal(0, talk.NextAt);
    }

    /// <summary>
    /// **A change of place mid-conversation tells the script it was aborted** (the
    /// teardown, then `convended` with aborted 1 and the standing code) — where a script's own
    /// `EndConversation` (<see cref="Conversation.Stop"/>) tells it nothing.
    /// </summary>
    [Fact]
    public void A_change_of_place_mid_conversation_tells_the_script_it_was_aborted()
    {
        const string two = "CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Anezka\n";
        var talk = Running(two, out _, now: 1000);
        (int id, bool aborted, int code)? ended = null;
        talk.Ended += (i, a, c) => ended = (i, a, c);
        talk.Tick(1000);
        talk.Abandon();
        Assert.Equal((77, true, talk.ReturnCode), ended);
        Assert.False(talk.Running);
        talk.Abandon();                                            // once only
        Assert.Equal((77, true, talk.ReturnCode), ended);

        var quiet = Running(two, out _, now: 1000);
        bool heard = false;
        quiet.Ended += (_, _, _) => heard = true;
        quiet.Tick(1000);
        quiet.Stop();
        Assert.False(heard);
    }

    /// <summary>
    /// A `LINE:` as the last step of its event is the ordinary way out, and 324 of the 386 shipped
    /// events end that way. The line is heard first, and only then does `convended` go out.
    /// </summary>
    [Fact]
    public void Running_off_the_end_of_an_event_ends_the_conversation()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n", out _, now: 1000);
        bool? aborted = null;
        talk.Ended += (_, a, _) => aborted = a;

        talk.Tick(1000);
        Assert.Null(aborted);
        Assert.True(talk.Tick(talk.NextAt));
        Assert.False(aborted);                 // ended, and not as an abort
        Assert.False(talk.Running);
    }

    /// <summary>
    /// `convreached` fires for **every step as it is spoken**, carrying the id the script passed,
    /// where the conversation is, how long the step takes and who is saying it. And **nothing at all
    /// is fired without the `0x04` flag**.
    /// </summary>
    [Fact]
    public void Convreached_fires_per_step_and_only_when_the_script_asked_for_it()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                           out _, now: 1000, voices: Voice(1_000));
        var seen = new List<(int, int, int, int, int)>();
        talk.Reached += (id, e, s, ms, who) => seen.Add((id, e, s, ms, who));

        talk.Tick(1000);
        talk.Tick(talk.NextAt);
        Assert.Equal([(77, 0, 0, 1_500, 1), (77, 0, 1, 1_500, 2)], seen);

        var quiet = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                            out _, now: 1000, flags: ConversationFlag.None);
        var none = 0;
        quiet.Reached += (_, _, _, _, _) => none++;
        quiet.Ended += (_, _, _) => none++;
        quiet.Tick(1000);
        quiet.Tick(quiet.NextAt);
        quiet.Tick(quiet.NextAt);
        Assert.Equal(0, none);
    }

    /// <summary>`convchosen` fires when a reply is picked, and says which one and where from.</summary>
    [Fact]
    public void Convchosen_says_which_reply_was_picked()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tANSWERGOTO: g ev_1\n"
                         + "\tANSWERRETURN: r 3\nEVENT: ev_1\n\tLINE: b Wilhem\n", out _, now: 1000);
        (int, int, int, int, int)? chosen = null;
        talk.Chosen += (id, e, a, ms, who) => chosen = (id, e, a, ms, who);

        talk.Tick(1000);
        talk.Tick(talk.NextAt);
        Assert.True(talk.Pick(1, 5000));
        Assert.Equal((77, 0, 1, talk.Offered[1].DurationMs, 3), chosen);
    }

    /// <summary>
    /// A goto's `convchosen` names the event the reply **leads to**, not the one it was offered in:
    /// The step puts the target in the conversation before the camera reads it. This is
    /// `29_1_MeetLily`'s shape — three replies in ev_0, the third going to ev_2, and a script that
    /// gives Humanity for event 2, answer 2.
    /// </summary>
    [Fact]
    public void A_goto_reply_is_reported_in_the_event_it_leads_to()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tANSWERGOTO: a ev_1\n\tANSWERGOTO: b ev_2\n"
                         + "\tANSWERGOTO: c ev_2\nEVENT: ev_1\n\tLINE: x Wilhem\nEVENT: ev_2\n\tLINE: y Wilhem\n",
                           out _, now: 1000);
        (int Event, int Answer)? chosen = null;
        talk.Chosen += (_, e, a, _, _) => chosen = (e, a);

        talk.Tick(1000);
        talk.Tick(talk.NextAt);
        talk.Tick(talk.NextAt);
        Assert.True(talk.Waiting);
        Assert.True(talk.Pick(2, 5000));
        Assert.Equal((2, 2), chosen);
    }

    /// <summary>
    /// The skip and the abort, and their two flags — which are the awkward pair. `0x10` set means a
    /// line **cannot** be skipped; `0x08` set means the conversation **can** be aborted. So a
    /// conversation with no flags at all is skippable and unabortable.
    /// </summary>
    [Fact]
    public void Skipping_and_aborting_are_gated_the_opposite_ways_round()
    {
        var plain = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                            out _, now: 1000, flags: ConversationFlag.None);
        plain.Tick(1000);
        Assert.True(plain.Skip(4000));            // skippable with no flags
        Assert.Equal(4001, plain.NextAt);
        Assert.Null(plain.Onscreen);
        Assert.False(plain.Abort());              // but not abortable
        Assert.True(plain.Running);

        var locked = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                             out _, now: 1000,
                             flags: ConversationFlag.NoSkip | ConversationFlag.Abortable);
        locked.Tick(1000);
        Assert.False(locked.Skip(4000));
        Assert.True(locked.Skip(4000, force: true));
        Assert.True(locked.Abort());
        Assert.False(locked.Running);
    }

    /// <summary>
    /// The abort is the **only** way out that tells the script it was one, and it lets the room
    /// start thinking again like any other end.
    /// </summary>
    [Fact]
    public void An_abort_is_the_only_end_that_says_it_was_aborted()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                           out _, now: 1000, flags: ConversationFlag.Callbacks | ConversationFlag.Abortable);
        bool? aborted = null;
        talk.Ended += (_, a, _) => aborted = a;
        talk.Tick(1000);
        Assert.True(Conversation.Talking(talk.Actors[0]));

        Assert.True(talk.Abort());
        Assert.True(aborted);
        Assert.False(Conversation.Talking(talk.Actors[0]));
    }

    /// <summary>
    /// **Turning subtitles off still shows the text of a line that was never recorded** — the step's length
    /// only consults the setting inside the branch that found a recording. Two lines in five would
    /// otherwise be silent and invisible.
    /// </summary>
    [Fact]
    public void Subtitles_off_still_shows_a_line_that_has_no_recording()
    {
        var voiced = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                             out _, now: 1000, voices: Voice(2_000));
        voiced.Subtitles = false;
        voiced.Tick(1000);
        Assert.Null(voiced.Onscreen);

        var silent = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                             out _, now: 1000, voices: Voice(0));
        silent.Subtitles = false;
        silent.Tick(1000);
        Assert.Equal("<Anezka> one two three", silent.Onscreen);
    }

    /// <summary>
    /// With `0x20` set nobody plays anything, and the last-speaker slot is simply kept clear — so a
    /// conversation can happen without a single character moving.
    /// </summary>
    [Fact]
    public void The_no_animation_flag_keeps_the_last_speaker_clear()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                           out _, now: 1000, flags: ConversationFlag.NoAnimation);
        talk.Tick(1000);
        Assert.Null(talk.LastSpeaker);
        Assert.NotNull(talk.Onscreen);            // it is still said, it is just not acted
    }

    /// <summary>
    /// **Three of the four gate flags are never set by anything the game ships**, measured over the
    /// bytecode of all 465 Codex classes: 296 of 301 `ExecuteConversation` calls pass exactly
    /// `0x04`. So in the original no conversation can be abandoned, every line can be skipped, and
    /// everybody always plays the talking animations — which is what
    /// <see cref="ConversationFlag.Shipped"/> stands for.
    /// </summary>
    [Fact]
    public void What_the_shipped_game_passes_leaves_three_of_the_four_gates_open()
    {
        Assert.Equal(ConversationFlag.Callbacks, ConversationFlag.Shipped);
        Assert.Equal(0, (int)(ConversationFlag.Shipped & ConversationFlag.Abortable));
        Assert.Equal(0, (int)(ConversationFlag.Shipped & ConversationFlag.NoSkip));
        Assert.Equal(0, (int)(ConversationFlag.Shipped & ConversationFlag.NoAnimation));

        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tLINE: a Anezka\n\tLINE: b Wilhem\n",
                           out _, now: 1000, flags: ConversationFlag.Shipped);
        talk.Tick(1000);
        Assert.False(talk.Abort());          // the original's own answer to the escape key
        Assert.True(talk.Running);
        Assert.True(talk.Skip(4000));        // and its answer to clicking past a line
        talk.Tick(talk.NextAt);
        Assert.NotNull(talk.LastSpeaker);    // and everybody acts it out, always
    }

    [Fact]
    public void The_steps_own_numbers_are_the_engines()
    {
        Assert.Equal(117, Conversation.TalkMode);
        Assert.Equal("talk", NmsMotionSet.BodyModes[Conversation.TalkMode]);
        Assert.Equal(350, Conversation.WordMs);
        Assert.Equal(500, Conversation.ReadPadMs);
        Assert.Equal(500, Conversation.StepPadMs);
        Assert.Equal(50, Conversation.AnswerStepMs);
        Assert.Equal(8, Conversation.MaxAnswers);
        Assert.Equal(32, Conversation.MaxCast);
    }

    // ---- against the shipped conversations ---------------------------------------------------------

    /// <summary>
    /// The engine's two fixed-size arrays are never checked against their counts, so the shipped
    /// data is the only thing keeping them in bounds. It does, comfortably: **the most replies one
    /// event offers is five** against room for eight, and **the most speakers one conversation has
    /// is seven** against room for thirty-two. Every reply is also the tail of its event — no
    /// shipped event puts a reply in the middle and then carries on talking.
    /// </summary>
    [Fact]
    public void The_shipped_data_stays_inside_the_engines_two_fixed_arrays()
    {
        using var files = Install.Open();
        int mostReplies = 0, mostSpeakers = 0, strays = 0, endingInReplies = 0;

        foreach (var name in files.List("conversations"))
            foreach (var conversation in NcoFile.Read(files.Read(name), name).Conversations)
            {
                var speakers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in conversation.Events)
                {
                    int tail = 0;
                    for (int i = e.Steps.Count - 1; i >= 0; i--, tail++)
                        if (e.Steps[i].Kind is not (NcoStep.AnswerGoto or NcoStep.AnswerReturn)) break;
                    strays += e.Steps.Take(e.Steps.Count - tail)
                                     .Count(s => s.Kind is NcoStep.AnswerGoto or NcoStep.AnswerReturn);
                    if (tail > 0) endingInReplies++;
                    mostReplies = Math.Max(mostReplies, tail);
                    foreach (var step in e.Steps)
                        if (step.Kind == NcoStep.Line && step.Speaker.Length > 0) speakers.Add(step.Speaker);
                }
                mostSpeakers = Math.Max(mostSpeakers, speakers.Count);
            }

        Assert.Equal(5, mostReplies);                       // 6_1_Unorna, ev_0
        Assert.True(mostReplies < Conversation.MaxAnswers);
        Assert.Equal(7, mostSpeakers);                      // 13_1_Good
        Assert.True(mostSpeakers + 1 <= Conversation.MaxCast);
        Assert.Equal(0, strays);
        Assert.Equal(49, endingInReplies);
    }

    /// <summary>
    /// **A conversation is a short exchange that ends, and nothing in the game loops back.** Which
    /// is why asking a character one question and being returned to the world is faithful rather
    /// than a fault: an event that runs off its end ends the conversation, and the shipped data
    /// leans on that completely. Asking a second question means clicking the character again.
    /// </summary>
    [Fact]
    public void No_shipped_conversation_loops_back_to_an_earlier_event()
    {
        using var files = Install.Open();
        var lengths = new List<int>();
        int loops = 0;
        foreach (var name in files.List("conversations"))
            foreach (var talk in NcoFile.Read(files.Read(name), name).Conversations)
            {
                lengths.Add(talk.Events.Sum(e => e.Steps.Count));
                int Index(string e) => talk.Events.FindIndex(
                    x => string.Equals(x.Name, e, StringComparison.OrdinalIgnoreCase));
                bool looped = false;
                for (int i = 0; i < talk.Events.Count; i++)
                    foreach (var step in talk.Events[i].Steps)
                    {
                        if (step.Kind == NcoStep.Jump && Index(step.Event) is var j && j >= 0 && j <= i) looped = true;
                        if (step.Kind == NcoStep.AnswerGoto && Index(step.Event) is var g && g >= 0 && g <= i) looped = true;
                    }
                if (looped) loops++;
            }

        lengths.Sort();
        Assert.Equal(310, lengths.Count);
        Assert.Equal(0, loops);                                        // not one, anywhere
        Assert.Equal(3, lengths[lengths.Count / 2]);                   // the median conversation is three steps
        Assert.Equal(88, lengths.Count(n => n == 1));                  // and 88 are a single line
        Assert.Equal(208, lengths.Count(n => n <= 5));
    }

    /// <summary>
    /// **The 58 shipped `DELAY:` steps are seconds, and the reader gives them milliseconds.** They
    /// are written 0.5 to 8; the parser multiplies by 1000 and truncates, so the
    /// shortest is half a second and the longest eight seconds. This test replaced one that
    /// asserted the opposite.
    /// </summary>
    [Fact]
    public void Every_shipped_delay_is_a_real_pause()
    {
        using var files = Install.Open();
        var pauses = new List<int>();
        foreach (var name in files.List("conversations"))
            foreach (var conversation in NcoFile.Read(files.Read(name), name).Conversations)
                foreach (var e in conversation.Events)
                    foreach (var step in e.Steps)
                        if (step.Kind == NcoStep.Delay) pauses.Add(step.Pause);

        Assert.Equal(58, pauses.Count);
        Assert.Equal(8_000, pauses.Max());
        Assert.Equal(500, pauses.Min());                 // the `0.5`s, no longer truncated to nothing
        Assert.All(pauses, p => Assert.True(p >= 1000 / 30, $"a delay of {p} ms is under a frame"));
    }

    // ---- what the speakers do with their weapons ---------------------------------------------------

    /// <summary>
    /// **A player character puts its weapon and shield away to talk** (the start's player
    /// branch) and takes them out again at the end.
    /// Everybody else keeps what they are holding. This is why the convent's collapse looks the way
    /// it does: the script drops a broadsword on the floor and the engine takes the one in his hand
    /// out of the picture — and Vitomir, until then, left Christof holding a sword he had just
    /// dropped.
    /// </summary>
    [Fact]
    public void A_player_puts_the_weapon_away_to_talk_and_gets_it_back_at_the_end()
    {
        var anezka = Character("Anezka", 1);
        var christof = Character("Christof", 3, ThingType.Player);
        var talk = Conversation.Resolve(Talk(), Place(anezka, christof), player: christof);
        talk.Flags = ConversationFlag.Callbacks;
        talk.PlayerThing = christof;
        talk.Voices = Voice();
        talk.Start(1000);

        Assert.True(christof.Actor!.WeaponHidden);
        Assert.True(christof.Actor.OffhandHidden);
        Assert.False(anezka.Actor!.WeaponHidden);              // an ordinary character keeps it out

        talk.Stop();
        Assert.False(christof.Actor.WeaponHidden);
        Assert.False(christof.Actor.OffhandHidden);
    }

    /// <summary>
    /// The two flags the shipped game passes at the convent: `NORESTOREWEAPONS` leaves the weapon
    /// away afterwards — which is how Christof stays empty-handed for the scene that follows — and
    /// `0x200` leaves the whole thing alone. An overridden weapon, or one that takes the hands off,
    /// is not hidden on the way in either (`THING_AF2_WEAPONOVERRIDE | THING_AF2_WEAPONNOHANDS`).
    /// </summary>
    [Fact]
    public void The_two_weapon_flags_hold_the_hiding_and_the_giving_back()
    {
        Conversation Start(ConversationFlag flags, out WorldThing player, uint flags2 = 0)
        {
            var anezka = Character("Anezka", 1);
            player = Character("Christof", 3, ThingType.Player);
            player.Actor!.Flags2 = flags2;
            var made = Conversation.Resolve(Talk(), Place(anezka, player), player);
            made.Flags = flags;
            made.PlayerThing = player;
            made.Voices = Voice();
            made.Start(1000);
            return made;
        }

        var kept = Start(ConversationFlag.Callbacks | ConversationFlag.NoRestoreWeapons, out var one);
        Assert.True(one.Actor!.WeaponHidden);
        kept.Stop();
        Assert.True(one.Actor.WeaponHidden);                   // still away: the convent's 0x404

        var never = Start(ConversationFlag.Callbacks | ConversationFlag.NoWeaponsAway, out var two);
        Assert.False(two.Actor!.WeaponHidden);
        Assert.False(two.Actor.OffhandHidden);
        never.Stop();
        Assert.False(two.Actor.WeaponHidden);

        var overridden = Start(ConversationFlag.Callbacks, out var three, World.Actor.Flag2.WeaponOverride);
        Assert.False(three.Actor!.WeaponHidden);               // the weapon is somebody else's business
        Assert.True(three.Actor.OffhandHidden);                // the off hand is hidden either way
        overridden.Stop();
        Assert.False(three.Actor.WeaponHidden);
    }

    /// <summary>
    /// **A delay step tells the script about itself.** The shipped scenes hang real actions on a
    /// beat of silence — the convent's collapse animation and its fade to black are both delay
    /// steps in `3_1_Collapse`, and the swing of the camera onto the archbishop in `3_3_C&amp;A` is
    /// another — so a tick that advanced past a delay without calling `convreached` threw those
    /// actions away and the scene appeared to skip them (*"it happens way too
    /// fast and it basically skips that animation"*). 1.1 notifies from the delay case itself
    /// (the delay passes the pause and a zero — the same `convreached` a line
    /// goes through, behind the same CONV_XFLAG_WANTFEEDBACK gate), so the speaker is **nobody**.
    /// </summary>
    [Fact]
    public void A_delay_step_reaches_the_script_with_no_speaker()
    {
        var talk = Running("CONVERSATION: T 0\nEVENT: ev_0\n\tDELAY: 3\n\tLINE: a Anezka\n", out _, now: 1000);
        var heard = new List<(int Step, int Ms, int Speaker)>();
        talk.Reached += (_, _, step, ms, speaker) => heard.Add((step, ms, speaker));

        Assert.True(talk.Tick(1000));
        var delay = Assert.Single(heard);
        Assert.Equal(0, delay.Step);          // the delay is step 0 of the event
        Assert.Equal(3000, delay.Ms);         // the pause it is about to wait, as the engine passes it
        Assert.Equal(0, delay.Speaker);       // a delay has no speaker
    }

}
