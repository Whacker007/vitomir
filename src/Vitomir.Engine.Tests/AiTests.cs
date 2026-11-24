// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Engine.Ai;
using Vitomir.Engine.World;
using Vitomir.Formats;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The AI: the <c>.nai</c> file, the seventeen modes, and the two that are written. Everything
/// asserted here is 1.1's behaviour, checked against all 136 shipped
/// files, all 531 templates that name one, and every place the chronicles visit.
/// </summary>
public class AiTests
{
    static string[] NaiFiles(Vfs.GameFiles files) =>
        [.. files.List("").Where(n => n.EndsWith(".nai", StringComparison.OrdinalIgnoreCase)).Order()];

    [Fact]
    public void The_partys_AI_takes_a_swords_whirl_half_the_time_and_the_players_click_only_with_shift()
    {
        // In play Christof whirled (the broadsword's second attack, `chrisattackssword`) on
        // the first swing of an attack without Shift. In 1.1 every such whirl comes from an order of
        // Christof's own AI — while Wilhem is steered (an order struck just after
        // Christof was steered again counts) or while Christof is frenzied
        // — and none from the player's
        // un-Shifted clicks. The AI's order rolls `attackAlt` in each of its four attack
        // choices, and `player.nai` sets none, so the reader's 0.5 stands;
        // the click's index is the modifier's Shift bit, and nothing else.
        using var files = Install.Open();
        var path = NaiFiles(files).Single(n => Path.GetFileName(n).Equals("player.nai", StringComparison.OrdinalIgnoreCase));
        var ai = NaiFile.Read(files.Read(path), "player.nai");
        Assert.Equal(0.5f, ai.AttackAlt);
        Assert.Equal(0, Rpg.ClickOrders.AttackIndex(InputModifier.None));
        Assert.Equal(0, Rpg.ClickOrders.AttackIndex(InputModifier.Ctrl | InputModifier.Alt | InputModifier.Space));
        Assert.Equal(1, Rpg.ClickOrders.AttackIndex(InputModifier.Shift));
    }

    [Fact]
    public void Every_AI_file_in_the_game_reads_with_nothing_left_over()
    {
        using var files = Install.Open();
        var paths = NaiFiles(files);
        Assert.Equal(136, paths.Length);

        var unknown = new List<string>();
        int disciplines = 0, valueless = 0;
        foreach (var path in paths)
        {
            var ai = NaiFile.Read(files.Read(path), Path.GetFileName(path));
            unknown.AddRange(ai.UnknownKeys);
            disciplines += ai.Disciplines.Length;
            valueless += ai.ValuelessKeys.Length;
        }
        // Nothing in any shipped file is a key the engine would not have recognised, which is what
        // says the 39-entry keyword table is complete and in the right order.
        Assert.Empty(unknown);
        Assert.Equal(537, disciplines);
        // Fifteen lines name a key and then stop. The engine drops those whole, keeping the
        // default: interestNum x4, searchStyle x5, wakeupDelay x3, wanderRadius x3.
        Assert.Equal(15, valueless);
    }

    [Fact]
    public void The_shipped_files_use_a_narrow_slice_of_what_the_format_allows()
    {
        using var files = Install.Open();
        var all = NaiFiles(files).Select(p => NaiFile.Read(files.Read(p), Path.GetFileName(p))).ToList();

        // attackType, attackFlags, moveType and moveFlags are set by every single file and every
        // single file says the same thing, so nothing in the game exercises them.
        Assert.All(all, a => Assert.Equal(5, a.AttackType));
        Assert.All(all, a => Assert.Equal(4u, (uint)a.MoveType));
        Assert.All(all, a => Assert.Equal(1u, a.AttackFlags));
        Assert.All(all, a => Assert.Equal(0x20u, a.MoveFlags));
        // Seconds on disk, milliseconds in memory: every file writes attackRate 0.5.
        Assert.All(all, a => Assert.Equal(500, a.AttackRate));

        // behaviorClass, and so the mode everyone starts in.
        Assert.Equal(130, all.Count(a => a.BehaviorClass == 0));
        Assert.Equal(3, all.Count(a => a.BehaviorClass == 1));
        Assert.Equal(2, all.Count(a => a.BehaviorClass == 3));
        Assert.Equal(1, all.Count(a => a.BehaviorClass == 4));
        Assert.Equal(130, all.Count(a => AiClasses.StartMode(false, a.BehaviorClass) == AiMode.EnemySearch));
        Assert.Equal(3, all.Count(a => AiClasses.StartMode(false, a.BehaviorClass) == AiMode.PedWander));
        Assert.Equal(2, all.Count(a => AiClasses.StartMode(false, a.BehaviorClass) == AiMode.PedFly));
        Assert.Equal(1, all.Count(a => AiClasses.StartMode(false, a.BehaviorClass) == AiMode.DoNothing));
        // A THING_TYPE_PLAYER ignores behaviorClass entirely.
        Assert.All(all, a => Assert.Equal(AiMode.Party, AiClasses.StartMode(true, a.BehaviorClass)));

        // No shipped file sets any of the item-use keys, breakDist, or the three the engine added
        // last (discFeedRate, discFeedPercent, feedDuration): every one is still at its default.
        Assert.All(all, a => Assert.Equal(500f, a.BreakDist));
        Assert.All(all, a => Assert.Equal(2000, a.ItemHealRate));
        Assert.All(all, a => Assert.Equal(12000, a.DiscFeedRate));
        Assert.All(all, a => Assert.Equal(5000, a.FeedDuration));
        // mindFlags is the only bitfield anything varies, and never with the hurry bit.
        Assert.All(all, a => Assert.Equal(0u, a.MindFlags & Minds.MindHurry));
        Assert.Equal(108, all.Count(a => a.MindFlags == 0));
    }

    [Fact]
    public void A_files_disciplines_are_read_with_their_levels_clamped()
    {
        using var files = Install.Open();
        var all = NaiFiles(files).Select(p => NaiFile.Read(files.Read(p), Path.GetFileName(p))).ToList();
        var lines = all.SelectMany(a => a.Disciplines).ToList();
        Assert.Equal(537, lines.Count);
        Assert.Equal(56, lines.Select(d => d.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(lines, d => Assert.InRange(d.Level, 0, NaiFile.MaxDisciplineLevel));
        Assert.Equal(13, lines.Count(d => d.Level == 4));
        // Names are written however the author felt: "Mesmerize" and "mesmerize" both appear, and
        // the engine lower-cases before it looks the discipline up.
        Assert.Contains(lines, d => d.Name == "mesmerize");
        Assert.Contains(lines, d => d.Name == "Mesmerize");
    }

    [Fact]
    public void The_parsers_conversions_are_the_engines_own()
    {
        // atoi and atof take what they can off the front and stop.
        Assert.Equal(12, NaiFile.Int("12abc"));
        Assert.Equal(-3, NaiFile.Int("-3.9"));
        Assert.Equal(0, NaiFile.Int("abc"));
        Assert.Equal(0.5f, NaiFile.Float("0.5units"));
        // Seconds to milliseconds, truncated toward zero rather than rounded.
        Assert.Equal(500, NaiFile.Rate("0.5"));
        Assert.Equal(1500, NaiFile.Rate("1.5"));
        Assert.Equal(0, NaiFile.Rate("0.0009"));
        // A count written as a float truncates without the scale.
        Assert.Equal(8, NaiFile.Truncate("8.9"));
        // Percentages are clamped, both ends.
        Assert.Equal(0f, NaiFile.Percent("-0.5"));
        Assert.Equal(1f, NaiFile.Percent("2.0"));
        Assert.Equal(0.3f, NaiFile.Percent("0.30"));
        // sscanf("%x") takes an optional 0x and answers nothing at all when nothing converts, so
        // the field keeps its default instead of being zeroed.
        Assert.Equal(0x1fu, NaiFile.Hex("0x1f"));
        Assert.Equal(0xfu, NaiFile.Hex("F"));
        Assert.Equal(0xfu, NaiFile.Hex("0x000f"));
        Assert.Null(NaiFile.Hex("zz"));
    }

    [Fact]
    public void The_three_comment_forms_and_a_valueless_key_are_all_dropped()
    {
        var text = """
            ; a semicolon comment
            # a hash comment
            // a slash comment
            attackType 9
            searchStyle
            notAKeyword 4
            discipline Potence 7
            discipline Celerity
            MINDFLAGS 0x20
            """.ReplaceLineEndings("\r\n");
        var ai = NaiFile.Read(System.Text.Encoding.Latin1.GetBytes(text), "test.nai");
        Assert.Equal(9, ai.AttackType);
        Assert.Equal(0, ai.SearchStyle);                       // named, no value: dropped whole
        Assert.Equal(["searchStyle"], ai.ValuelessKeys);
        Assert.Equal(["notAKeyword"], ai.UnknownKeys);
        Assert.Equal(0x20u, ai.MindFlags);                     // keys match case-insensitively
        Assert.Equal(2, ai.Disciplines.Length);
        Assert.Equal(NaiFile.MaxDisciplineLevel, ai.Disciplines[0].Level);   // 7 clamps to 4
        Assert.Equal(0, ai.Disciplines[1].Level);                            // no level at all is 0
    }

    [Fact]
    public void The_defaults_are_the_engines_constructor_including_the_two_that_are_swapped()
    {
        var ai = NaiFile.Read([], "empty.nai");
        Assert.Equal(2000, ai.AttackRate);
        Assert.Equal(0.5f, ai.AttackAlt);
        Assert.Equal(100f, ai.MoveDist);
        Assert.Equal(200f, ai.ChaseDist);
        Assert.Equal(1000f, ai.SearchDist);
        Assert.Equal(1000, ai.SearchRate);
        Assert.Equal(-1, ai.InterestNum);
        Assert.Equal(0.75f, ai.DiscBuffPercent);
        // The engine's own constructor gives verylowhealth 0.30 and lowhealth 0.15, which is the
        // wrong way round. Every shipped file that sets one sets both, so it never shows.
        Assert.Equal(0.3f, ai.VeryLowHealth);
        Assert.Equal(0.15f, ai.LowHealth);
        Assert.True(ai.VeryLowHealth > ai.LowHealth);
    }

    [Fact]
    public void The_engines_mode_name_table_prints_the_wrong_name_for_every_mode_above_none()
    {
        // A missing comma merged "none" and "party" into one string, so 15 names sit in a table of
        // 17. This is only a debug log line, but it is what pins the mode numbers: the mind's set-mode puts a
        // behaviorClass 1 character into mode 7 and logs the table's entry 7, "pedfly".
        Assert.Equal("noneparty", AiModes.Logged(AiMode.None));
        Assert.Equal("pet", AiModes.Logged(AiMode.Party));
        Assert.Equal("asleep", AiModes.Logged(AiMode.EnemySearch));
        Assert.Equal("pedfly", AiModes.Logged(AiMode.PedWander));
        Assert.Equal("frenzied", AiModes.Logged(AiMode.PedFly));
        Assert.Equal("(null)", AiModes.Logged(AiMode.DoNothing));
        Assert.Equal("(null)", AiModes.Logged(AiMode.Approach));
        // What each mode is actually called.
        Assert.Equal("party", AiModes.Name(AiMode.Party));
        Assert.Equal("pedwander", AiModes.Name(AiMode.PedWander));
        Assert.Equal(AiModes.Count, AiModes.Names.Length);
        Assert.Equal(AiModes.Count, AiModes.LogNames.Length);
    }

    [Fact]
    public void Every_template_that_names_an_AI_file_gets_one()
    {
        using var files = Install.Open();
        var templates = new TemplateSet(files);
        var classes = new AiClasses(files);
        int named = 0, resolved = 0;
        foreach (var name in templates.Names)
        {
            var tags = templates.Resolve(name);
            if (!tags.TryGetValue(Tags.ActorAi, out var t) || t.AsString().Length == 0) continue;
            named++;
            if (classes.Get(t.AsString()) is not null) resolved++;
        }
        Assert.Equal(531, named);
        Assert.Equal(531, resolved);
        Assert.Empty(classes.Missing);
        // 24 of the 136 files are named by no template at all.
        Assert.Equal(112, classes.Count);
    }

    [Fact]
    public void Interest_spots_are_grouped_the_way_the_engine_reads_them()
    {
        using var files = Install.Open();
        int scenes = 0, withSpots = 0, spots = 0, stranded = 0;
        foreach (var path in files.List("scenes/").Where(p => p.EndsWith(".nsd", StringComparison.OrdinalIgnoreCase)))
        {
            scenes++;
            var scene = NsdScene.Read(files.Read(path), Path.GetFileName(path));
            if (scene.InterestSpots.Length == 0) continue;
            withSpots++;
            spots += scene.InterestSpots.Length;
            stranded += new InterestSpots(scene, null).Stranded;
        }
        Assert.Equal(291, scenes);
        // Only fifteen scenes have any, and all fifteen are level scenes rather than chapter ones.
        Assert.Equal(15, withSpots);
        Assert.Equal(251, spots);
        // The engine takes a contiguous run of one group, so a group split across the file would
        // be partly unreachable. No shipped scene does that.
        Assert.Equal(0, stranded);
    }

    [Fact]
    public void A_ped_walks_between_the_spots_of_its_own_group()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files));

        Assert.Equal(22, minds.Spots.All.Length);
        var peds = minds.All.Where(m => m.Mode == AiMode.PedWander).ToList();
        Assert.Equal(7, peds.Count);
        // Every wanderer here is in group 0, and takes the whole run of it.
        Assert.All(peds, m => Assert.Equal(0, m.Class.InterestNum));
        Assert.All(peds, m => Assert.True(m.SpotCount > 0));

        var start = peds.ToDictionary(m => m.Thing.Guid, m => m.Thing.Position);
        for (int i = 0; i < 60 * 30; i++) { minds.Tick(1f / 30f); minds.Follow(1f / 30f); }
        // A minute is enough for most of them to have been somewhere.
        int moved = peds.Count(m => Vector3.Distance(start[m.Thing.Guid], m.Thing.Position) > 1f);
        Assert.True(moved >= 4, $"only {moved} of {peds.Count} wanderers moved");
        Assert.True(minds.All.Sum(m => m.Moves) > 0);
    }

    [Fact]
    public void A_character_the_player_is_steering_does_not_think_unless_it_has_lost_control()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files));
        var ped = minds.All.First(m => m.Mode == AiMode.PedWander);

        minds.Steered = ped.Thing;
        var start = ped.Thing.Position;
        for (int i = 0; i < 60 * 30; i++) { minds.Tick(1f / 30f); minds.Follow(1f / 30f); }
        Assert.Equal(0, ped.Moves);
        Assert.Equal(start, ped.Thing.Position);

        // The four modes that take the controls away think anyway (as 1.1's think does).
        ped.Enter(AiMode.Frenzied);
        Assert.True(Minds.FlagAlwaysThink == 0x4);
    }

    [Fact]
    public void A_paused_or_dead_character_stands_still()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files));
        foreach (var m in minds.All) m.Thing.Actor!.Flags |= Actor.Flag.AiPaused;
        for (int i = 0; i < 60 * 30; i++) { minds.Tick(1f / 30f); minds.Follow(1f / 30f); }
        Assert.Equal(0, minds.All.Sum(m => m.Moves));
    }

    /// <summary>
    /// **`wanderRadius` is measured from where the character came into the world** — the spawn
    /// transform the finalize copies, whose translation the mind reads as
    /// the thing's position. Once this test was called "measured from a point the engine
    /// never sets" and asserted every wanderer's home was the origin; London West turned down 294
    /// spots in two minutes for five people because of it. The write that reading missed is a block
    /// copy with no offset in it.
    /// </summary>
    [Fact]
    public void The_wander_radius_is_measured_from_where_the_character_came_into_the_world()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "l1_west", things: true, sky: false, scene: "WEST_28_1");
        var minds = place.Awaken(new AiClasses(files));
        var peds = minds.All.Where(m => m.Mode == AiMode.PedWander).ToList();
        Assert.NotEmpty(peds);
        Assert.All(peds, m => Assert.Equal(m.Thing.Scene.Position, m.Thing.SpawnPosition));

        var start = peds.ToDictionary(m => m.Thing.Guid, m => m.Thing.Position);
        for (int i = 0; i < 120 * 30; i++) { minds.Tick(1f / 30f); minds.Follow(1f / 30f); }

        // The think copies the spawn position into the mind as its home.
        Assert.All(peds, m => Assert.Equal(m.Thing.SpawnPosition, m.Home));
        // London's spots are thousands of units from the origin and within reach of where these
        // five stand, so almost none is turned down — the old reading refused 294 here.
        int refused = minds.All.Sum(m => m.OutOfRange);
        Assert.True(refused < 60, $"{refused} spots refused");
        Assert.True(peds.Count(m => Vector3.Distance(start[m.Thing.Guid], m.Thing.Position) > 1f) >= 4,
                    "the wanderers should be wandering");
    }

    [Fact]
    public void Nobody_stands_still_playing_a_walk()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files));
        var peds = minds.All.Where(m => m.Mode == AiMode.PedWander && m.SpotCount > 0).ToList();
        Assert.NotEmpty(peds);

        // The invariant has to hold on every frame, not at one sampled instant: a wanderer stands
        // for only one to six seconds between walks, so a single check at the end mostly catches
        // everybody mid-stride and proves nothing.
        int frozen = 0, seenStanding = 0, seenWalking = 0;
        string worst = "";
        for (int i = 0; i < 180 * 30; i++)
        {
            minds.Tick(1f / 30f);
            minds.Follow(1f / 30f);
            foreach (var m in minds.All)
            {
                if (m.Walker.Walking) { if (peds.Contains(m)) seenWalking++; continue; }
                if (peds.Contains(m) && Playing(m) == "stand") seenStanding++;
                if (Playing(m) is "walk" or "run") { frozen++; worst = $"{m.Thing.Template} playing {Playing(m)} at a standstill"; }
            }
        }
        Assert.True(seenWalking > 0, "nobody set off at all");
        Assert.True(seenStanding > 0, "nobody was ever seen standing between walks");
        // This is the bug play showed: everyone who came to a stop kept their walking animation.
        // The walker has to be run on the frames it has nowhere left to go — that step is what
        // drops the speed to zero and puts the character back on its stand animation.
        Assert.True(frozen == 0, $"{frozen} frames with a character frozen mid-stride ({worst})");
        Assert.True(peds.Sum(m => m.Moves) > 0);
    }

    /// <summary>Which motion-set mode a character is actually playing.</summary>
    static string Playing(Mind mind) => mind.Thing.Animation?.Motion?.Mode.Name ?? "";

    [Fact]
    public void Only_Pragues_metafile_names_a_player()
    {
        using var files = Install.Open();
        var set = ChronicleSet.Load(files);
        var named = set.All.Where(c => !string.IsNullOrEmpty(c.Meta?.Player)).Select(c => c.Name).ToList();
        // Prague is the campaign's start; the others are chained from it, so you arrive as whoever
        // you already were and their metafiles carry PLAYER with an empty value. Anything reaching
        // for a player template has to treat empty as absent — `?? "Christof"` does not.
        Assert.Equal(["Prague"], named);
        Assert.Equal("Christof", set["Prague"]!.Meta!.Player);
        Assert.All(set.All.Where(c => c.Name != "Prague"), c => Assert.Equal("", c.Meta?.Player ?? ""));
    }

    [Fact]
    public void The_engines_random_numbers_are_reproduced_exactly()
    {
        // MSVC's rand(), which is the only chance the AI has.
        var rng = new Rng(1);
        Assert.Equal([41, 18467, 6334, 26500, 19169], Enumerable.Range(0, 5).Select(_ => rng.Next()).ToArray());
        var unit = new Rng(1);
        Assert.All(Enumerable.Range(0, 1000).Select(_ => unit.Unit()), u => Assert.InRange(u, 0f, 1f));
    }

    [Fact]
    public void Every_place_the_chronicles_visit_wakes_up_without_complaint()
    {
        using var files = Install.Open();
        var classes = new AiClasses(files);
        int places = 0, minds = 0, wander = 0, fly = 0, doNothing = 0, search = 0, party = 0, withRun = 0;
        foreach (var path in files.List("").Where(p => p.EndsWith(".nsc", StringComparison.OrdinalIgnoreCase))
                                  .Concat(files.List("chronicles/")).Distinct().Order())
        {
            if (Path.GetFileName(path).StartsWith("MP_", StringComparison.OrdinalIgnoreCase)) continue;
            NscChronicle chronicle;
            try { chronicle = NscChronicle.Read(files.Read(path), Path.GetFileName(path)); } catch { continue; }
            foreach (var loc in chronicle.Locations)
            {
                if (loc.LevelName.Length == 0 || !files.TryRead($"levels/{loc.LevelName}.nil", out _)) continue;
                string? scene = loc.SceneName.Length > 0 && files.TryRead($"scenes/{loc.SceneName}.nsd", out _)
                    ? loc.SceneName : null;
                var place = Location.Load(files, loc.LevelName, things: true, sky: false, scene: scene);
                var m = place.Awaken(classes);
                places++;
                minds += m.All.Length;
                wander += m.All.Count(x => x.Mode == AiMode.PedWander);
                fly += m.All.Count(x => x.Mode == AiMode.PedFly);
                doNothing += m.All.Count(x => x.Mode == AiMode.DoNothing);
                search += m.All.Count(x => x.Mode == AiMode.EnemySearch);
                party += m.All.Count(x => x.Mode == AiMode.Party);
                withRun += m.All.Count(x => x.SpotCount > 0);
            }
        }
        Assert.True(places >= 90, $"only {places} places loaded");
        Assert.True(minds > 1000, $"only {minds} minds");
        // Almost everybody in the game starts looking for a fight; the town is the small remainder.
        // The five modes StartMode can return, and nothing else. Party is the handful of
        // THING_TYPE_PLAYER things the scenes place themselves.
        Assert.Equal(minds, search + wander + fly + doNothing + party);
        Assert.True(party is > 0 and < 10, $"{party} party minds");
        Assert.True(search > minds * 0.85, $"{search} of {minds} in enemysearch");
        Assert.True(wander > 80 && fly > 0 && doNothing > 0);
        Assert.True(withRun > 0);
    }

    // ---- going home --------------------------------------------------------------------------

    /// <summary>A place with its minds awake and one of them moved <paramref name="by"/> from its post.</summary>
    static (Location Place, Minds Minds, Simulation Sim, Mind Guard) Lured(Vfs.GameFiles files, string who, Vector3 by)
    {
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files), 1);
        var sim = new Simulation(place);
        sim.Step(1f / 30f);
        var guard = minds.All.First(m => m.Thing.Template == who);
        var to = guard.Thing.Position + by;
        int node = place.Walk!.Find(to);
        Assert.True(node >= 0, $"no ground {by} from {who}");
        to.Z = place.Walk.Nodes[node].Height(to.X, to.Y);
        place.Move(guard.Thing, to);
        guard.Walker.Ground();
        guard.Thing.Orientation = guard.Thing.Orientation + new Vector3(0f, 0f, 90f);
        return (place, minds, sim, guard);
    }

    /// <summary>
    /// **A guard with nobody to fight walks back to its post and faces the way it was placed**
    /// (the tail of the enemy mode). As in 1.1: when they lose contact with the player,
    /// they actively go back to their standing places or patrol routes. The post is the
    /// spawn transform the finalize copies, which the think turns into the mind's home
    /// and a walk node under it. The knight by the convent is one that was lured away
    /// in a play-test and found standing by the mines exit afterwards.
    /// </summary>
    [Fact]
    public void A_guard_with_nobody_to_fight_walks_back_to_its_post()
    {
        using var files = Install.Open();
        var (_, _, sim, guard) = Lured(files, "convknight", new Vector3(-300f, 0f, 0f));
        var post = guard.Thing.SpawnPosition;
        Assert.True(Vector3.Distance(post, guard.Thing.Position) > 250f);

        for (int i = 0; i < 20 * 30; i++) sim.Step(1f / 30f);

        Assert.Equal(post, guard.Home);
        Assert.True(guard.HomeNode >= 0);
        Assert.True(Vector3.Distance(post, guard.Thing.Position) <= Minds.HomeReach,
                    $"{Vector3.Distance(post, guard.Thing.Position):0} from its post");
        Assert.Equal(guard.Thing.SpawnOrientation, guard.Thing.Orientation);
    }

    /// <summary>
    /// `THING_AF_AI_NORETURNHOME` stops it — the flag every patrol script sets, because its own
    /// `arrived` puts the character back on its round instead ("override the AI behavior to return
    /// home on losing target", `WalkCycle.java`).
    /// </summary>
    [Fact]
    public void A_guard_told_not_to_go_home_stays_where_it_lost_you()
    {
        using var files = Install.Open();
        var (_, _, sim, guard) = Lured(files, "convknight", new Vector3(-300f, 0f, 0f));
        guard.Thing.Actor!.Flags |= Actor.Flag.AiNoReturnHome;
        var there = guard.Thing.Position;
        for (int i = 0; i < 20 * 30; i++) sim.Step(1f / 30f);
        Assert.True(Vector3.Distance(there, guard.Thing.Position) < 1f);
    }

    /// <summary>
    /// The spawn transform is taken once, as the thing comes into the world, and moving it after
    /// that does not move its home.
    /// </summary>
    [Fact]
    public void The_spawn_transform_is_where_a_thing_came_into_the_world_and_stays_there()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        place.Awaken(new AiClasses(files), 1);
        var knight = place.Things.First(t => t.Template == "convknight");
        Assert.True(knight.Finalized);
        var placed = knight.SpawnPosition;
        Assert.Equal(knight.Scene.Position.X, placed.X);
        Assert.Equal(knight.Scene.Position.Y, placed.Y);
        place.Move(knight, placed + new Vector3(100f, 0f, 0f));
        Assert.False(knight.Finish());
        Assert.Equal(placed, knight.SpawnPosition);
    }

    /// <summary>
    /// **A destroyed target reads back as nothing**. 1.1's mind holds its
    /// target as a handle, and a thing destroyed is nobody's handle. Vitomir held the thing itself and
    /// never let go of one destroyed alive: Serena kept a summoned hopper whose time had run out as her
    /// target and cast Feed at it twenty seconds on, and the Feed script complained of an invalid target.
    /// </summary>
    [Fact]
    public void A_mind_whose_target_is_destroyed_has_no_target()
    {
        using var files = Install.Open();
        var place = Location.Load(files, "P1_OTDY", things: true, sky: false, scene: "OTDY_4_1");
        var minds = place.Awaken(new AiClasses(files));
        var hunter = minds.All.First();
        var prey = minds.All.First(m => !ReferenceEquals(m, hunter)).Thing;
        minds.SetTarget(hunter, prey);
        Assert.Same(prey, hunter.Target);
        Assert.True(place.Remove(prey));
        Assert.Null(hunter.Target);
    }
}
