// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The story moving on: the quest log, the message log, the chapter title, and the
/// main sequence's journeys — a jump, a scene change, an advance, and coming back to a place still
/// as it was left.
/// </summary>
public class ProgressTests
{
    static byte[] Nqd(string text) => Encoding.Latin1.GetBytes(text);

    // ---- the quest files --------------------------------------------------------------------

    [Fact]
    public void Every_shipped_quest_is_one_quest_line_with_no_items()
    {
        using var files = Install.Open();
        var names = files.List("quests/").Where(p => p.EndsWith(".nqd", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(45, names.Count);
        foreach (var path in names)
        {
            var q = Formats.Nqd.Read(files.Read(path));
            Assert.True(q.Found, path);
            Assert.Equal(0, q.ItemCount);
            Assert.Empty(q.Items);
            Assert.StartsWith("q_", q.Short);                       // lower-cased with the line
        }
        var night = Formats.Nqd.Read(files.Read("quests/P1_PragueByNight.nqd"));
        Assert.Equal(("q_praguebynight_s", "q_praguebynight_l", "questcheck.tga", 300u),
                     (night.Short, night.Long, night.Icon, night.Xp));
    }

    [Fact]
    public void A_line_is_seven_fields_and_one_that_starts_with_white_space_is_not_read_at_all()
    {
        var q = Formats.Nqd.Read(Nqd(
            "# comment\r\n; comment\r\n// comment\r\n" +
            "QUEST 3 0 S L icon.tga 10\r\n" +
            "  QITEM 2 0 IGNORED L i.tga 1\r\n" +                // indented: skipped
            "QITEM 0 2 A AL a.tga 5\r\n" +                       // fewer than one step gets one
            "QITEM 4 1 B BL b.tga 6 extra words\r\n" +
            "QITEM 1 0 C CL\r\n" +                               // five fields: not a line
            "QITEM 1 0 D DL d.tga 7\r\n" +
            "QITEM 1 0 E EL e.tga 8\r\n"));                      // past the three the quest has
        Assert.True(q.Found);
        Assert.Equal(3, q.ItemCount);
        Assert.Equal(["a", "b", "d"], q.Items.Select(i => i.Short));
        Assert.Equal([1, 4, 1], q.Items.Select(i => i.Steps));
        Assert.Equal([2, 1, 0], q.Items.Select(i => i.Flags));
    }

    [Fact]
    public void Without_the_quest_s_icon_every_item_lands_in_the_first_slot()
    {
        var q = Formats.Nqd.Read(Nqd("QUEST 2 0 S L missing.tga 10\nQITEM 1 0 A AL a.tga 5\nQITEM 1 0 B BL b.tga 6\n"),
                                 icon => icon != "missing.tga");
        Assert.Equal("b", q.Items[0].Short);
        Assert.Same(Formats.Nqd.Blank, q.Items[1]);
    }

    // ---- the quest log -----------------------------------------------------------------------

    sealed class Watch
    {
        public readonly List<string> Posted = [];
        public readonly List<int> Spoken = [];
        public readonly List<int> Awarded = [];
        public int Changed, Refreshed;
    }

    static (QuestLog Log, Watch Seen) Log(Dictionary<string, string> files)
    {
        var seen = new Watch();
        var log = new QuestLog
        {
            Read = name => files.TryGetValue(name.ToLowerInvariant(), out var t) ? Nqd(t) : null,
            Text = key => key.ToUpperInvariant() + "!",
            Post = seen.Posted.Add,
            Speak = seen.Spoken.Add,
            AwardParty = seen.Awarded.Add,
        };
        log.Changed += () => seen.Changed++;
        log.Refreshed += () => seen.Refreshed++;
        return (log, seen);
    }

    [Fact]
    public void Loading_a_quest_the_first_time_adds_it_and_the_steered_character_says_so()
    {
        var (log, seen) = Log(new() { ["first"] = "QUEST 0 0 Q_A_S Q_A_L check.tga 300\n" });
        var q = log.Load("First");
        Assert.NotNull(q);
        Assert.Equal(0, q!.Slot);
        Assert.Equal("first", q.Name);
        Assert.Equal("Q_A_S!", q.Short);                                   // looked up once, at load
        Assert.Equal([FoleyEventSlots.NewQuest], seen.Spoken);
        Assert.Equal([QuestLog.ChangedKey], seen.Posted);
        // A second load is the same quest and says nothing.
        Assert.Same(q, log.Load("FIRST"));
        Assert.Single(seen.Spoken);
        Assert.Single(seen.Posted);
        // A file that is not there costs nothing.
        Assert.Null(log.Load("nothere"));
        Assert.Equal(1, log.Count);
    }

    static class FoleyEventSlots
    {
        public const int NewQuest = Audio.FoleyEvent.VoxNewQuest, Complete = Audio.FoleyEvent.VoxQuestComplete;
    }

    [Fact]
    public void Completing_a_quest_gives_its_experience_once_and_clearing_it_takes_the_reward_mark_away()
    {
        var (log, seen) = Log(new() { ["q"] = "QUEST 0 0 S L check.tga 1200\n" });
        var q = log.Load("q")!;
        seen.Posted.Clear();
        Assert.Equal(0u, log.SetFlags(q, QuestLog.Complete));
        Assert.Equal(QuestLog.Complete | QuestLog.Rewarded, q.Flags);
        Assert.Equal([1200], seen.Awarded);
        Assert.Equal(FoleyEventSlots.Complete, seen.Spoken[^1]);
        Assert.Single(seen.Posted);
        // Again: nothing changes, nothing is given, nothing is posted.
        log.SetFlags(q, QuestLog.Complete);
        Assert.Single(seen.Awarded);
        Assert.Single(seen.Posted);
        // Cleared, the reward bit goes too — so completing it again pays again.
        log.ClearFlags(q, QuestLog.Complete);
        Assert.Equal(0u, q.Flags);
        log.SetFlags(q, QuestLog.Complete);
        Assert.Equal([1200, 1200], seen.Awarded);
    }

    [Fact]
    public void Every_item_line_announces_the_log_three_times_and_a_completed_one_pays_as_it_loads()
    {
        var (log, seen) = Log(new() { ["q"] = "QUEST 2 0 S L check.tga 10\nQITEM 3 1 A AL a.tga 25\nQITEM 2 2 B BL b.tga 6\n" });
        var q = log.Load("q")!;
        Assert.Equal(3 * 2 + 1, seen.Posted.Count);
        // Item A was written complete, which marks it paid — but its experience is copied in only
        // after its flags are set, so what it pays at load is 0, which pays nothing.
        Assert.Empty(seen.Awarded);
        Assert.Equal(ItemState(1 | 4, 3, 0), ItemState(q.Items[0]));
        Assert.True(q.Items[1].Hidden);
        // An item is always announced, changed or not.
        int before = seen.Posted.Count;
        log.SetItemFlags(q.Items[1], QuestLog.ItemHidden);
        Assert.Equal(before + 1, seen.Posted.Count);
        // Steps: an absolute value, and the current step pulled under the count.
        Assert.Equal(0, log.SetItemStep(q.Items[0], -2));
        Assert.Equal(2, q.Items[0].Step);
        Assert.Equal(3, log.SetItemSteps(q.Items[0], -2));
        Assert.Equal((2, 1), (q.Items[0].Steps, q.Items[0].Step));
        Assert.Equal(1, log.SetItemStep(q.Items[0], 5));                   // not below the count: kept
        Assert.Equal(1, q.Items[0].Step);
    }

    static (uint, int, int) ItemState(uint flags, int steps, int step) => (flags, steps, step);
    static (uint, int, int) ItemState(QuestItem i) => (i.Flags, i.Steps, i.Step);

    [Fact]
    public void The_log_holds_sixty_four_and_the_pane_shows_eight()
    {
        var files = Enumerable.Range(0, 70).ToDictionary(i => $"q{i}", i => $"QUEST 0 0 S{i} L{i} c.tga 1\n");
        files["items"] = "QUEST 2 0 SI LI c.tga 1\nQITEM 1 2 H HL h.tga 1\nQITEM 1 0 V VL v.tga 1\n";
        var (log, _) = Log(files);
        Assert.NotNull(log.Load("items"));
        for (int i = 0; i < 70; i++) log.Load($"q{i}");
        Assert.Equal(QuestLog.Max, log.Count);
        Assert.Null(log.Load("q69"));

        var pane = new QuestPane(NuiPane.Parse("name=questPane")) { Log = log };
        var rows = pane.Rows();
        Assert.Equal(QuestPane.RowCount, rows.Length);
        // The quest, then its one visible item (the hidden one is skipped), then the next quests.
        Assert.Equal(["SI!", "V!", "S0!", "S1!", "S2!", "S3!", "S4!", "S5!"], rows.Select(r => r.Text));
        Assert.All(rows, r => Assert.Null(r.Icon));
        log.SetFlags(log[1]!, QuestLog.Complete);
        Assert.Equal("c.tga", pane.Rows()[2].Icon);
        Assert.Equal(QuestPane.DoneColour, pane.Describe(2)!.Value.Small.Colour);
        Assert.Equal("L0!", pane.Describe(2)!.Value.Large.Text);
        log.Clear();
        Assert.Equal(0, log.Count);
    }

    // ---- the message log ---------------------------------------------------------------------

    [Fact]
    public void A_message_fills_its_blanks_by_letter_and_the_box_keeps_a_hundred()
    {
        using var files = Install.Open();
        var log = new MessageLog { Text = new Strings(files) };
        Assert.Equal("Christof dies.", log.Format("RPG_DIED", "%PChristof"));
        Assert.Equal("You pickup 30 gold.", log.Format("RPG_PICKEDUPCASHDA", "%D30"));
        // A lower-case letter's value is itself a key; a blank with no pair is dropped.
        Assert.Equal("You have learned a new Discipline Group - Bashing", log.Format("RPG_DISCIPLINELEARNED", "%dRPG_DAMAGE_NORMAL"));
        Assert.Equal(" dies.", log.Format("RPG_DIED", ""));
        Assert.Equal("%P dies.", log.Format("RPG_DIED"));
        for (int i = 0; i < 105; i++) log.Add($"line {i}");
        Assert.Equal(MessageLog.MaxItems, log.Lines.Count);
        Assert.Equal("line 5", log.Lines[0].Text);
    }

    // ---- the chapter title ------------------------------------------------------------------

    [Fact]
    public void Entering_a_chapter_announces_it_for_eight_seconds_unless_the_table_says_nothing()
    {
        using var files = Install.Open();
        var text = new Strings(files);
        foreach (var f in new[] { "prague" }) text.Add(f);
        var title = Captions.ChapterTitle("MON1_8_2.nsd", k => text[k], (_, s) => s.Length * 10, 0);
        Assert.NotNull(title);
        Assert.Equal("Monastery 1", title!.Text);
        Assert.Equal((640 - 110) / 2, title.X);
        Assert.Equal(400, title.Y);
        // The pane is stood at 400 and is two lines tall; the line is at its top (a 1.1
        // screenshot has the baseline 60 below the top of the 480 space).
        Assert.Equal(1, title.Lines);
        Assert.Equal(420f, title.PenY(20));
        Assert.Equal(Captions.TitleFont, title.Font);
        Assert.Equal(0f, title.Alpha(0));
        Assert.Equal(0.5f, title.Alpha(1000));
        Assert.Equal(1f, title.Alpha(4000));
        Assert.Equal(0.5f, title.Alpha(7000));
        Assert.False(title.Over(8000));
        Assert.True(title.Over(8001));
        Assert.Null(Captions.ChapterTitle("CNVT_3_3", k => text[k], (_, s) => s.Length, 0));   // "<>"
        Assert.Null(Captions.ChapterTitle(null, k => text[k], (_, s) => s.Length, 0));
    }

    // ---- the quest button's flash -------------------------------------------------------------

    [Fact]
    public void The_quest_button_flashes_with_its_pane_shut()
    {
        // A video of 1.1: a quest update with the pane shut, and the button
        // blinks its red frame half a second on, half a second off, until the pane is opened.
        using var files = Install.Open();
        var place = Location.Load(files, "P1_MON1", sky: false, player: "Christof");
        var bar = new StatusBar(new UiAssets(files).Pane(StatusBar.PaneName), place);
        var button = bar["sbQuestButton"]!;
        Assert.Equal("buttonflash", button.Flash, ignoreCase: true);
        Assert.False(button.Checked);
        Assert.Null(button.Art);                                          // no file of its own…
        Assert.True(button.HasBitmap);                                    // …but flag 0x2's copy
        button.StartBlink(500, 1000, 0);
        Assert.True(button.FlashOn);
        button.TickBlink(400, 400);
        Assert.True(button.FlashOn);
        button.TickBlink(700, 300);
        Assert.False(button.FlashOn);                                     // past the period: flipped
        button.TickBlink(1001, 10);
        Assert.False(button.Blinking);                                    // and done at the end
    }

    // ---- the journeys --------------------------------------------------------------------------

    static (Campaign Campaign, GameFiles Files, Session Session) Open(string level)
    {
        var files = new GameFiles(GameFiles.Locate()!);
        var set = ChronicleSet.Load(files);
        var where = set.ForLevel(level)!.Value;
        var chronicle = where.Chronicle;
        chronicle.Current = where.Location;
        var session = new Session { Chronicle = chronicle };
        session.Messages.Text = new Strings(files);
        Location Build(string name, string? scene, int entrance)
        {
            var place = Location.Load(files, name, sky: false, scene: scene, player: "Christof", entrance: entrance);
            place.Session = session;
            return place;
        }
        var here = Build(level, where.Entry!.SceneName, -1);
        session.Here = here;
        return (new Campaign { Chronicle = chronicle, Build = Build, Here = here }, files, session);
    }

    [Fact]
    public void Going_back_to_a_place_finds_it_as_it_was_left()
    {
        var (campaign, files, _) = Open("P1_MON1");
        using var _ = files;
        var monastery = campaign.Here;
        var christof = monastery.Player!.Thing;
        // Somebody dies in the monastery before the party leaves.
        var victim = monastery.Things.First(t => t.Actor is { } a && !ReferenceEquals(t, christof) && !a.Has(Actor.Flag.Party));
        victim.Actor!.Flags |= Actor.Flag.Dead;
        int things = monastery.Things.Count;

        Assert.True(campaign.Chronicle.TakeExit(0));
        var there = campaign.Travel()!.Value;
        Assert.False(there.Returned);
        Assert.False(christof.InWorld);                                    // out of the place it left
        Assert.Contains(monastery, campaign.Resident.Values);

        Assert.True(campaign.Chronicle.Jump("Monastery", 0));
        var back = campaign.Travel()!.Value;
        Assert.True(back.Returned);
        Assert.Same(monastery, campaign.Here);                             // not built again
        Assert.True(victim.Actor.Has(Actor.Flag.Dead));
        Assert.True(christof.InWorld);
        Assert.Same(christof, campaign.Here.Player!.Thing);
        Assert.Equal(things, campaign.Here.Things.Count);
        Assert.Equal(0, campaign.Here.Entrance);
        Assert.True(campaign.Here.Walk!.Find(christof.Position) >= 0, "came back somewhere with no floor");
    }

    /// <summary>
    /// A loaded place is never built again: a scene changed since the last visit is swapped in
    /// the place still loaded (`ChapterSwapTests`), so the party comes back to it.
    /// </summary>
    [Fact]
    public void A_scene_changed_since_the_last_visit_is_swapped_in_the_place_still_loaded()
    {
        var (campaign, files, _) = Open("P1_MON1");
        using var _ = files;
        var monastery = campaign.Here;
        var thrownAway = new List<Location>();
        campaign.Discarded += thrownAway.Add;
        Assert.True(campaign.Chronicle.TakeExit(0));
        campaign.Travel();

        // Back to the level's own scene, with the chapter's people gone.
        Assert.True(campaign.Chronicle.ChangeScene("Monastery", "P1_MON1.nsd"));
        Assert.True(campaign.Chronicle.Jump("Monastery", 0));
        var back = campaign.Travel()!.Value;
        Assert.True(back.Returned);
        Assert.Same(monastery, campaign.Here);
        Assert.Empty(thrownAway);
        Assert.Equal("P1_MON1", campaign.Here.SceneFile, ignoreCase: true);
        Assert.Null(campaign.Here.ChapterScene);
        Assert.DoesNotContain(campaign.Here.Things, t => t.Index >= (campaign.Here.WorldScene?.Things.Length ?? 0) && !ReferenceEquals(t, campaign.Here.Player?.Thing) && t.Index >= 0);
    }

    [Fact]
    public void The_main_sequence_holds_one_request_and_an_advance_waits_for_its_screen()
    {
        var (campaign, files, _) = Open("P1_MON1");
        using var _ = files;
        var c = campaign.Chronicle;
        Assert.False(c.Jump("NoSuchPlace", 0));
        Assert.False(c.Jump("Monastery", -1));
        Assert.True(c.TakeExit(0));
        Assert.True(c.Jump("Monastery", 1));                                // replaces the exit
        Assert.Equal(SequenceKind.Jump, c.Pending!.Value.Kind);
        c.Take();

        Assert.True(c.SetupAdvance("Monastery", "MON1_8_3.nsd", 2));
        Assert.Null(c.Pending);                                             // remembered, not acted on
        c.Advance(5);
        Assert.Equal((SequenceKind.Advance, 5), (c.Pending!.Value.Kind, c.Pending.Value.Guid));
        c.Take();
        c.ApplyAdvanceScene();
        Assert.Equal("MON1_8_3.nsd", c.At("Monastery")!.Scene);
        Assert.Null(c.Leaving);                                             // not until the screen closes
        Assert.True(c.AdvanceDone());
        Assert.Equal((c.Find("Monastery"), 2), (c.Leaving!.Value.To, c.Leaving.Value.Entrance));
        Assert.False(c.AdvanceDone());                                      // the destination is spent
    }

    [Fact]
    public void Picking_up_cash_fills_the_party_s_purse_and_says_so()
    {
        var (campaign, files, session) = Open("P1_MON1");
        using var _ = files;
        var place = campaign.Here;
        var christof = place.Player!.Thing;
        christof.Actor!.Flags |= Actor.Flag.Party;
        var gold = place.Place("broadsword", christof.Position, default, christof.Sector)!;
        gold.Item!.Flags |= ItemFlags.InstantCash;                         // what makes it money
        gold.Item!.Amount = 30;
        int before = session.Gold;
        Assert.Equal(GroundResult.Cash, Ground.Take(place, christof, gold, InputModifier.None, steering: true, nowMs: 0));
        Assert.Equal(before + 30, session.Gold);
        Assert.False(gold.InWorld);
        Assert.Contains(session.Messages.Lines, m => m.Colour == MessageLog.Yellow && m.Text.Contains("30"));
    }
}
