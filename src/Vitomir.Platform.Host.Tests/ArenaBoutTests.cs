// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Text.Json;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Host.Arena;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The arena's bouts on the proving ground's free yard: one bout is the same bout run twice — on content kept across
/// bouts and on content loaded afresh —, digest for digest; a sweep gives a row per bout with its value, and the value is on
/// the fighter's sheet through the module; a value the module does not know stops the bout by name; the tapes replay as
/// <c>--replay-check</c> replays them (a fresh session from the tape's head); the summary's numbers are the CSV's. Skips
/// without the 1.1 install (VTMR_GAME_DIR, else C:\Games\VTMR) or the proving ground (VITOMIR_COURSE_LEVEL).
/// <para>Every fighter here names its sheet (the dressing rule changes who the module dresses by what); a bout with no powers gives
/// the earlier results (its rows but the digest, its contacts and harm: the powers' contract adds stores to the digest); a power at
/// a rank whose sheet has no option of it is refused by name; a shield taken away leaves no shield to meet and the matched
/// specs' equipment is checked; a matched bout's tape replays.</para>
/// </summary>
public sealed class ArenaBoutTests
{
    static (string Course, string Game) Grounds()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        // The install found as the program finds it (VTMR_GAME_DIR, else C:\Games\VTMR), as the other tests do.
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR, or C:\\Games\\VTMR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-arena-tests-userdata"));
        return (course!, game!);
    }

    static ArenaSpec Spec(string extra = "", float seconds = 3f, int bouts = 1, int tapes = 0)
    {
        var (course, _) = Grounds();
        return ArenaSpec.Read($$"""
        { "fighters": [ { "name": "A", "template": "Christof", "weapon": "BroadSword", "sheet": { "template": "sandbox.townsperson" } },
                        { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard" } ],
          "seconds": {{seconds.ToString(CultureInfo.InvariantCulture)}}, "bouts": {{bouts}}, "tapes": {{tapes}}, "seed": 1 {{extra}} }
        """, Path.GetTempPath(), course);
    }

    static SessionOptions Baseline()
    {
        var (course, game) = Grounds();
        return new SessionOptions { Level = course, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" } };
    }

    /// <summary>
    /// The baseline with blades going through bodies (<c>--contact-stop-off</c>), for the rows pinned earlier;
    /// And without the legs' turn and speed by direction (<c>--no-leg-turn</c>, <c>--no-direction-pace</c>),
    /// which a pinned Celerity bout's brain backing off at 0.7 of its pace moved (389 ticks to 383); and before the guard (<c>guard-off</c>).
    /// </summary>
    static SessionOptions BaselineStopOff() => Baseline() with
    {
        Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox", "contact-stop-off", "guard-off", "blow-quality-off", "brain-range-off", "attack-spin", "guard-skill-off", "brain-parry-fixed", "stagger-oneone", "rebound-two-thirds", "rebound-stun-at-meeting", "swings-oneone", "pace-off", "blade-guard-everyone", "combat-tempo-off", "fighting-pace-off" }, LegTurn = false, DirectionPace = false,   // (and the earlier build's pace, no fighting pace) (and 1.1's own side blows) the quality block: pinned before blow quality and the brain's range keeping; and before directional attacks; the direction block's follow-up: and before the fight work's guard by skill, parry timing, stagger and rebound (the unchanged switches)
    };

    static string Folder(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vitomir-arena-{name}-{Environment.ProcessId}");
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        return dir;
    }

    [Fact]
    public void One_bout_is_the_same_bout_twice_digest_for_digest()
    {
        var spec = Spec();
        var kept = new ArenaRun(spec, Baseline());
        var once = kept.Bout(0, 0);
        var again = kept.Bout(0, 0);                       // on the content kept from the first
        var fresh = new ArenaRun(spec, Baseline()).Bout(0, 0);  // on content loaded afresh
        var next = kept.Bout(0, 1);                        // the next seed
        Assert.Equal(once.Digest, again.Digest);
        Assert.Equal(once.Digest, fresh.Digest);
        Assert.NotEqual(once.Digest, next.Digest);
        Assert.Equal(360, once.Ticks);
        Assert.Equal(ArenaTables.BoutRow(spec, once), ArenaTables.BoutRow(spec, fresh));
        Assert.True(once.Fighters[0].Swings > 0 && once.Fighters[1].Swings > 0, "both fought");
        // A blow begun near the end may not have struck; nor one stopped in a body (or sprung back) before its Strike mark.
        Assert.All(once.Fighters, f => Assert.InRange(f.Strikes, 0, f.Swings));
        kept.Content?.Dispose();
    }

    [Fact]
    public void A_taped_bout_writes_its_event_and_harm_traces_and_the_traces_change_nothing()
    {
        var spec = Spec(seconds: 6f);
        var run = new ArenaRun(spec, Baseline());
        var plain = run.Bout(0, 0);
        string folder = Folder("trace");
        string tape = Path.Combine(folder, "tapes", "p000-b000.tape");
        var traced = run.Bout(0, 0, tapePath: tape);
        Assert.Equal(plain.Digest, traced.Digest);                                     // a trace is not the world's state
        var lines = File.ReadAllLines(Path.ChangeExtension(tape, ".events.jsonl"));
        using var head = JsonDocument.Parse(lines[0]);
        Assert.Equal("head", head.RootElement.GetProperty("kind").GetString());
        Assert.Equal(EventTrace.Schema, head.RootElement.GetProperty("schema").GetString());
        Assert.Equal(1UL, head.RootElement.GetProperty("seed").GetUInt64());
        var people = head.RootElement.GetProperty("people").EnumerateArray().ToList();
        Assert.Equal(2, people.Count);
        Assert.All(people, p => Assert.True(p.GetProperty("sheet").GetProperty("effective").GetProperty("strength").GetInt32() >= 0));
        Assert.Contains(head.RootElement.GetProperty("activeWindows").EnumerateArray(), w => w.GetProperty("window").GetBoolean());
        long last = -1;
        var kinds = new HashSet<string>();
        foreach (var line in lines.Skip(1))
        {
            using var e = JsonDocument.Parse(line);
            long tick = e.RootElement.GetProperty("tick").GetInt64();
            Assert.True(tick >= last, "the lines in the order of the ticks");
            last = tick;
            kinds.Add(e.RootElement.GetProperty("kind").GetString()!);
            Assert.Equal(tick, e.RootElement.GetProperty("event").GetProperty("Tick").GetInt64());   // the line's tick is the event's
        }
        Assert.Contains("AttackBegun", kinds);
        Assert.Contains("SwingEnded", kinds);
        Assert.True(File.Exists(Path.ChangeExtension(tape, ".harm.jsonl")));
        Directory.Delete(folder, true);
    }

    [Fact]
    public void A_sweep_gives_a_row_per_bout_and_sets_its_value_on_the_sheet_through_the_module()
    {
        var spec = Spec(""", "sweep": { "fighter": "A", "trait": "strength", "values": [10, 40] }""", seconds: 1f, bouts: 2);
        string folder = Folder("sweep");
        var report = ArenaRunner.Execute(spec, Baseline(), folder, _ => { });
        var rows = ArenaTables.Read(Path.Combine(folder, "bouts.csv"));
        var head = rows[0];
        Assert.Equal(["point", "A.strength", "bout", "seed"], head[..4]);
        Assert.Equal(1 + 4, rows.Count);
        Assert.Equal(["0,10,0,1", "0,10,1,2", "1,40,0,1", "1,40,1,2"], rows.Skip(1).Select(r => string.Join(",", r[..4])));
        Assert.All(rows.Skip(1), r => Assert.Equal("time", r[Array.IndexOf(head, "end")]));
        Assert.Equal(4, report.Bouts.Count);

        // The value is on the fighter's sheet, as the module reads it.
        var run = new ArenaRun(spec, Baseline());
        foreach (var (point, want) in new[] { (0, 10), (1, 40) })
        {
            using var session = Session.Create(run.OptionsFor(point, 0));
            var sheets = session.World.Service<IPersonSheets>();
            Assert.True(sheets.TryRead(session.Fighters[0], "strength", out int strength));
            Assert.Equal(want, strength);
            Assert.True(sheets.TryRead(session.Fighters[1], "strength", out int other));
            Assert.Equal(60, other);   // rudolfGuard on the module's sandbox.guard, named
        }
    }

    [Fact]
    public void A_bout_is_watched_from_the_side_since_nobody_is_the_player()
    {
        var run = new ArenaRun(Spec(), Baseline());
        using var session = Session.Create(run.OptionsFor(0, 0));
        Assert.True(session.Player.IsNone);
        Assert.Equal(2, session.Fighters.Count);
        for (int i = 0; i < 12; i++) session.Step(ActionFrame.Idle);
        var view = new Presentation.FrameView();
        session.Fill(view, 1f);
        Assert.True(view.Characters.Count >= 2, "both fighters drawn (and what they hold)");
        // The fighters stand along +X at y 3: the spectator stands 4.5 m to their right (−Y), 1.7 m up, looking +Y at them.
        var spectator = session.Spectator();
        Assert.Equal(spectator, view.View);
        Assert.Equal(3f - 4.5f, view.View.Eye.Y, 2);
        Assert.InRange(view.View.Eye.Z, 1.69f, 1.73f);     // 1.7 m over the feet, on the ground
        Assert.Equal(MathF.PI / 2, view.View.Yaw, 3);
        run.Content?.Dispose();
    }

    [Fact]
    public void Two_jobs_give_the_rows_one_job_gives()
    {
        var spec = Spec(""", "sweep": { "fighter": "A", "trait": "strength", "values": [10, 30] }""", seconds: 1.5f, bouts: 3);
        string one = Folder("jobs1"), two = Folder("jobs2");
        ArenaRunner.Execute(spec, Baseline(), one, _ => { });
        var report = ArenaRunner.Execute(spec, Baseline(), two, _ => { }, jobs: 2);
        Assert.Equal(2, report.Jobs);
        Assert.Equal(File.ReadAllText(Path.Combine(one, "bouts.csv")), File.ReadAllText(Path.Combine(two, "bouts.csv")));
        Assert.Equal(File.ReadAllText(Path.Combine(one, "contacts.csv")), File.ReadAllText(Path.Combine(two, "contacts.csv")));
    }

    [Fact]
    public void A_scripted_fighter_acts_on_its_schedule_and_a_dummy_holds_its_block()
    {
        var (course, _) = Grounds();
        var spec = ArenaSpec.Read("""
            { "fighters": [ { "name": "S", "template": "rudolfGuard", "sheet": "sandbox.guard", "brain": { "kind": "script", "actions": "attack@0.5, strong@2, block@4+1" } },
                            { "name": "D", "template": "rudolfGuard", "sheet": "sandbox.guard", "brain": { "kind": "dummy", "mode": "block" } } ],
              "seconds": 5.5, "bouts": 1, "tapes": 0 }
            """, Path.GetTempPath(), course);
        var r = new ArenaRun(spec, Baseline()).Bout(0, 0);
        Assert.Equal(2, r.Fighters[0].Swings);          // the tap at 0.5 s and the hold at 2 s
        Assert.Equal(1, r.Fighters[0].BlocksRaised);    // at 4 s
        Assert.Equal(0, r.Fighters[1].Swings);
        Assert.Equal(1, r.Fighters[1].BlocksRaised);    // raised at once and held
    }

    [Fact]
    public void A_power_s_rank_is_swept_through_the_module_too()
    {
        // On a sheet that has Potence's option (sandbox.christof: potence.passive), a rank alone keeps it.
        var spec = Duel(""", "sweep": { "fighter": "A", "power": "potence", "values": [0, 3] }""");
        var run = new ArenaRun(spec, Baseline());
        foreach (var (point, want) in new[] { (0, 0), (1, 3) })
        {
            using var session = Session.Create(run.OptionsFor(point, 0));
            Assert.True(session.World.Service<IPersonSheets>().TryRead(session.Fighters[0], "potence", out int rank));
            Assert.Equal(want, rank);
        }
        run.Content?.Dispose();
    }

    [Fact]
    public void A_power_s_rank_on_a_sheet_with_no_option_of_it_is_refused_by_name()
    {
        // The survey's finding: Potence swept on the guard's sheet, which has no option of it, did nothing and said nothing.
        var spec = Spec(""", "sweep": { "fighter": "B", "power": "potence", "values": [0, 3] }""");
        var run = new ArenaRun(spec, Baseline());
        using (Session.Create(run.OptionsFor(0, 0))) { }                                  // rank 0: nothing learned, nothing to refuse
        var e = Assert.Throws<SessionException>(() => Session.Create(run.OptionsFor(1, 0)));
        Assert.Contains("fighter B: power potence = 3", e.Message);
        Assert.Contains("would do nothing", e.Message);
        run.Content?.Dispose();
    }

    [Fact]
    public void A_value_the_module_does_not_know_stops_the_bout_by_name()
    {
        var spec = Spec(""", "sweep": { "fighter": "A", "trait": "strenght", "values": [10] }""");
        var e = Assert.Throws<SessionException>(() => new ArenaRun(spec, Baseline()).Bout(0, 0));
        Assert.Contains("strenght", e.Message);
        Assert.Contains("fighter A", e.Message);
    }

    [Fact]
    public void The_tapes_replay_as_replay_check_replays_them()
    {
        var spec = Spec(""", "sweep": { "fighter": "A", "trait": "strength", "values": [10, 50] }""", seconds: 2f, bouts: 2, tapes: 1);
        string folder = Folder("tapes");
        var report = ArenaRunner.Execute(spec, Baseline(), folder, _ => { });
        var tapes = report.Bouts.Where(b => b.Tape is not null).ToList();
        Assert.Equal(2, tapes.Count);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder, "tapes"), "*.tape").Length);
        foreach (var b in tapes)
        {
            var tape = Tape.Read(Path.Combine(folder, b.Tape!));
            Assert.Equal(spec.Seed + (ulong)b.Bout, tape.Header.Seed);
            Assert.True(tape.Header.Notes.ContainsKey(SessionOptions.ArenaNote));
            // As --replay-check does: the options from the tape's head, a fresh session, every digest compared.
            var options = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(tape.Header);
            using var session = Session.Create(options);
            int checkedDigests = 0;
            foreach (var frame in tape.Frames)
            {
                session.Step(frame);
                if (tape.Digests.TryGetValue(session.Tick, out ulong want)) { Assert.Equal(want, session.Digest()); checkedDigests++; }
            }
            Assert.Equal(b.Digest, session.Digest());
            Assert.True(checkedDigests >= 8);
        }
    }

    // ---- the Abilities' channels ----

    static ArenaSpec Duel(string extra = "", float seconds = 12f, int tapes = 0)
    {
        var (course, _) = Grounds();
        return ArenaSpec.Read($$"""
        { "fighters": [ { "name": "A", "template": "Christof", "weapon": "BroadSword", "sheet": "sandbox.christof" },
                        { "name": "B", "template": "rudolfGuard", "sheet": "sandbox.guard" } ],
          "seconds": {{seconds.ToString(CultureInfo.InvariantCulture)}}, "bouts": 3, "tapes": {{tapes}}, "seed": 1 {{extra}} }
        """, Path.GetTempPath(), course);
    }

    [Fact]
    public void A_spec_s_combat_tempo_reaches_every_bout_and_its_tape_and_combat_tempo_off_plays_1_1_s_timing()
    {
        // Both fighters Melee 3 (1.082 at the earlier build's pace); the spec's global tempo 0.5 multiplies it, the default 0.70.
        float melee3 = 1.0818897f;
        float Tempo(ArenaRun run)
        {
            var rows = new List<ContactRow>();
            run.Bout(0, 0, rows);
            Assert.NotEmpty(rows);
            var tempos = rows.Select(r => r.SwingTempo!.Value).Distinct().ToList();
            return Assert.Single(tempos);
        }
        var half = new ArenaRun(Duel(""", "combat_tempo": 0.5"""), Baseline());
        Assert.Equal(0.5f, half.OptionsFor(0, 0).CombatTempo);
        Assert.Equal(melee3 * 0.5f, Tempo(half), 0.0001f);
        Assert.Equal(melee3 * 0.70f, Tempo(new ArenaRun(Duel(), Baseline())), 0.0001f);
        Assert.Equal(melee3, Tempo(new ArenaRun(Duel(""", "combat_tempo": 0.5, "switches": ["combat-tempo-off"]"""), Baseline())), 0.0001f);
        half.Content?.Dispose();
    }

    [Fact]
    public void With_the_tempo_off_and_no_contest_asked_for_a_bout_is_the_plain_bout()
    {
        // These bouts on the first module's own build (31c5082, the arena's bouts.csv), before the Abilities' channels: digests
        // 15f2995c65359129 and 184abb751c049352 until then, whose powers' contract adds stores to every world's digest; so
        // the bout's results are compared (its row of bouts.csv but the digest, the earlier columns), not its digest.
        var spec = Duel(""", "switches": ["skill-tempo-off"]""");
        var run = new ArenaRun(spec, BaselineStopOff());   // pinned before blades stopped where they stop
        var killed = run.Bout(0, 1);
        var quick = run.Bout(0, 2);
        Assert.Equal("0,1,2,A,0,kill,1141,9.508333,9.5,4,0,1,1,0,0,1,11,6,1,4,0,17,10,0,8,8,8,0,0,0,8,6,11,0,3,0,5,2.75,1,ZERO_HEALTH_STOP,0,,0,", Fourteen(spec, killed));
        Assert.Equal(1141, killed.Ticks);
        Assert.Equal("0,2,3,A,0,kill,159,1.325,1.316667,1,0,1,0,0,0,1,10,0,7,0,0,17,9,0,1,1,1,0,0,0,1,0,10,0,0,0,5,4,1,ZERO_HEALTH_STOP,0,,0,", Fourteen(spec, quick));
        Assert.Equal(159, quick.Ticks);
        Assert.All(killed.Fighters, f => Assert.Equal(0, f.ContestedHits));
        // The tempo on (the default) is another bout: the swings come at the fighters' Melee 3.
        var tempo = new ArenaRun(Duel(), Baseline()).Bout(0, 1);
        Assert.NotEqual(killed.Digest, tempo.Digest);
        run.Content?.Dispose();
    }

    [Fact]
    public void The_spec_s_switches_reach_every_bout_its_tape_carries_them_and_the_contest_fills_its_columns()
    {
        var spec = Duel(""", "switches": ["skill-contest"]""", seconds: 4f, tapes: 3);
        Assert.Equal(["skill-contest"], spec.Switches);
        string folder = Folder("contest");
        var report = ArenaRunner.Execute(spec, Baseline(), folder, _ => { });
        var hits = report.Contacts.Where(c => c.Harm is not null).ToList();
        Assert.NotEmpty(hits);
        Assert.All(hits, c => Assert.True(c.Harm!.Value.Contested));
        Assert.All(report.Bouts, b => Assert.Equal(b.Fighters.Sum(f => f.Hits), b.Fighters.Sum(f => f.ContestedHits)));
        var stopped = report.Bouts.First(b => b.KillSeconds is not null);
        Assert.Equal(ActorStop.ZeroHealthStop, stopped.Stop);
        // The tables: the new columns at the end, the old ones where they were.
        var bouts = ArenaTables.Read(Path.Combine(folder, "bouts.csv"));
        int powers = ArenaTables.PowerColumns.Length * 2 + ArenaTables.FlurryColumns.Length * 2
                     + ArenaTables.SeveranceColumns.Length * 2                                               // The earlier build's, after them
                     + ArenaTables.DodgeColumns.Length * 2
                     + ArenaTables.AngleColumns.Length * 2
                     + ArenaTables.ArmorColumns.Length * 2
                     + ArenaTables.GuardColumns.Length * 2
                     + ArenaTables.StopColumns.Length * 2                                                    // and the rules block's: each fighter's stop
                     + ArenaTables.FlurryStrikeColumns.Length * 2
                     + (ArenaTables.QualityColumns.Length + ArenaTables.ReboundColumns.Length + ArenaTables.SolverColumns.Length) * 2
                     + (ArenaTables.DirectionColumns.Length + ArenaTables.FightColumns.Length + ArenaTables.StaggerRunColumns.Length + ArenaTables.PaceColumns.Length + ArenaTables.TempoColumns.Length + ArenaTables.SideColumns.Length + ArenaTables.ShieldReadColumns.Length) * 2 + ArenaTables.GroupColumns.Length;   // the group's block after                 // and the direction block's and the fight work's
        Assert.Equal(ArenaTables.FighterColumns.Length * 2 + 10 + 1 + 4 + powers, bouts[0].Length);
        Assert.Equal(["stop_state", "A.contested_hits", "A.mean_contest_change", "B.contested_hits", "B.mean_contest_change"], bouts[0][^(5 + powers)..^powers]);
        Assert.Equal("digest", bouts[0][9]);
        Assert.Contains(bouts.Skip(1), r => r[^(5 + powers)] == "ZERO_HEALTH_STOP");
        var contacts = ArenaTables.Read(Path.Combine(folder, "contacts.csv"));
        Assert.Equal(["health_after", "contested", "attack_dice", "attack_successes", "defence_dice", "defence_successes", "contest_change"], contacts[0][..^28][^21..^14]);   // its one at the very end // the quality block: its ten at the very end // an earlier build: the flurry block's two and the rules block's three at the very end; four quality columns and four of the chain's at the end before them
        Assert.Contains(contacts.Skip(1), r => r[^48] == "1" && int.Parse(r[^47], CultureInfo.InvariantCulture) == 18);   // its one after // the quality block: its ten after // an earlier build: the flurry block's two and the rules block's three after // Christof: 300/20 + Melee 3
        // The page says the switches and, with the tempo on too, that Melee is counted twice.
        string page = File.ReadAllText(Path.Combine(folder, "summary.html"));
        Assert.Contains("skill-contest", page);
        Assert.Contains("Melee counted in the animation and after impact: a research comparator, not a rule", page);
        Assert.Contains("time to the zero-Health stop", page, StringComparison.OrdinalIgnoreCase);
        // Its tapes carry the switch, and replay as --replay-check replays them.
        var b0 = report.Bouts.First(b => b.Tape is not null);
        var tape = Tape.Read(Path.Combine(folder, b0.Tape!));
        Assert.Contains("skill-contest", tape.Header.Notes["switches"].Split(','));
        var options = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(tape.Header);
        using var session = Session.Create(options);
        foreach (var frame in tape.Frames)
        {
            session.Step(frame);
            if (tape.Digests.TryGetValue(session.Tick, out ulong want)) Assert.Equal(want, session.Digest());
        }
        Assert.Equal(b0.Digest, session.Digest());
    }

    /// <summary>
    /// A bout's row of bouts.csv as the earlier build wrote it, without its digest: the columns before the later ones, joined. Health is on the
    /// 1–1000 scale now (100 a person, 15 a wound), so each fighter's <c>health_left</c> is read back through the translator
    /// to the seven it was (<c>7 − (100 − H) / 15</c>, nothing at nothing) — exactly, or the test fails: the rows are the earlier ones
    /// and the earlier build's but for Health, which is theirs translated.
    /// </summary>
    static string Fourteen(ArenaSpec spec, BoutResult r, bool allTempo = false)
    {
        var row = ArenaTables.BoutRow(spec, r);
        int n = allTempo ? row.Count - (ArenaTables.FlurryColumns.Length + ArenaTables.SeveranceColumns.Length + ArenaTables.DodgeColumns.Length + ArenaTables.AngleColumns.Length + ArenaTables.ArmorColumns.Length + ArenaTables.GuardColumns.Length + ArenaTables.StopColumns.Length + ArenaTables.FlurryStrikeColumns.Length + ArenaTables.QualityColumns.Length + ArenaTables.ReboundColumns.Length + ArenaTables.SolverColumns.Length + ArenaTables.DirectionColumns.Length + ArenaTables.FightColumns.Length + ArenaTables.StaggerRunColumns.Length + ArenaTables.PaceColumns.Length + ArenaTables.TempoColumns.Length + ArenaTables.SideColumns.Length + ArenaTables.ShieldReadColumns.Length) * spec.Fighters.Count - ArenaTables.GroupColumns.Length   // The direction block too; every column but its own (the rules block's stop and the flurry block's)
            : 1 + spec.Sweeps.Count + 9 + ArenaTables.FighterColumns.Length * spec.Fighters.Count + 1 + ArenaTables.ContestColumns.Length * spec.Fighters.Count;
        int digest = 1 + spec.Sweeps.Count + 8;
        int left = Array.IndexOf(ArenaTables.FighterColumns, "health_left");
        for (int k = 0; k < spec.Fighters.Count; k++)
        {
            int at = 1 + spec.Sweeps.Count + 9 + k * ArenaTables.FighterColumns.Length + left;
            if (row[at].Length == 0) continue;
            int h = int.Parse(row[at], CultureInfo.InvariantCulture);
            Assert.True(h == 0 || (100 - h) % 15 == 0, $"health_left {h} is not 100 less whole wounds of 15");
            row[at] = (h == 0 ? 0 : 7 - (100 - h) / 15).ToString(CultureInfo.InvariantCulture);
        }
        return string.Join(",", row.Take(n).Where((_, i) => i != digest));
    }

    static string Tools()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tools", "platform", "arena"))) dir = dir.Parent;
        Assert.SkipUnless(dir is not null, "the repository's tools/platform/arena is not above the tests");
        return Path.Combine(dir!.FullName, "tools", "platform", "arena");
    }

    // ---- the arena with powers, equipment-matched bouts ----

    [Fact]
    public void Under_the_tempo_switch_a_Celerity_bout_is_all_tempo_and_on_the_split_it_carries_a_flurry()
    {
        // celerity-matched-tempo.json at Celerity 3 (point 4), bouts 0 and 1: their rows from the earlier build's own build (3f9c6f3,
        // celerity-matched.json, run earlier) but the digest, with every an earlier build column, Health read back through the
        // translator. (The whole sweep, 1,200 bouts and 11,304 contacts, was compared cell for cell.)
        var (course, _) = Grounds();
        var spec = ArenaSpec.ReadFile(Path.Combine(Tools(), "celerity-matched-tempo.json"), course);
        var run = new ArenaRun(spec, BaselineStopOff());   // pinned before blades stopped where they stop
        var b0 = run.Bout(4, 0);
        var b1 = run.Bout(4, 1);
        Assert.Equal("4,4,0,3000,Christof,0,kill,259,2.158333,2.15,5,4,5,0,0,0,5,8,0,7,0,0,2.8,1.6,0,1,1,1,0,0,0,1,0,8,0,0,0,3,0,1,ZERO_HEALTH_STOP,0,,0,,100,70,30,1,0,0,,1.8,5,0,,,0,0,0,0,,0,0,0.46332", Fourteen(spec, b0, allTempo: true));
        Assert.Equal("4,4,1,3001,Christof,0,kill,389,3.241667,3.233333,8,8,8,0,0,1,8,7,0,7,1,0,2.75,0.875,0,2,1,1,0,0,0,1,0,7,0,1,0,3,1,1,ZERO_HEALTH_STOP,0,,0,,100,70,30,1,0,0,,2.883333,8,0,,,0,0,0,0,,0,0,0.616967", Fourteen(spec, b1, allTempo: true));
        Assert.All(new[] { b0, b1 }, b => Assert.All(b.Fighters, f => Assert.Equal(0, f.FlurryBlows)));
        Assert.Equal((100, 0), (b0.Fighters[1].HealthStart, b0.Fighters[1].HealthEnd));         // the guard: 100 to the stop
        run.Content?.Dispose();
        // The same bouts on the default curve (split): a slower body, the blows carried — the flurry's columns fill.
        var split = ArenaSpec.ReadFile(Path.Combine(Tools(), "celerity-matched.json"), course);
        var run2 = new ArenaRun(split, Baseline());
        var s0 = run2.Bout(4, 0);
        var s1 = run2.Bout(4, 1);
        Assert.True(s0.Fighters[0].FlurryBlows + s1.Fighters[0].FlurryBlows > 0);
        Assert.Equal(0, s0.Fighters[1].FlurryBlows + s1.Fighters[1].FlurryBlows);              // the guard has no Celerity
        Assert.All(new[] { s0, s1 }, b => Assert.Equal(b.Fighters[0].Hits, b.Fighters[0].ContactsBody));
        run2.Content?.Dispose();
    }

    [Fact]
    public void A_bout_with_no_powers_gives_the_results_without_powers()
    {
        // melee-newly-embraced.json at Melee 0, bouts 0 and 1, on the earlier build (298eb91): their rows but the digest, and
        // their contacts (30: body 23, shield 6, level 1; 16 wounds). The specs name their sheets, so the dressing rule does not touch them.
        var (course, _) = Grounds();
        var spec = ArenaSpec.ReadFile(Path.Combine(Tools(), "melee-newly-embraced.json"), course);
        var run = new ArenaRun(spec, BaselineStopOff());   // pinned before blades stopped where they stop
        var contacts = new List<ContactRow>();
        var b0 = run.Bout(0, 0, contacts);
        var b1 = run.Bout(0, 1, contacts);
        Assert.Equal("0,0,0,2000,Guard,1,kill,1272,10.6,10.591667,6,1,1,3,0,0,1,0,7,0,1,0,2,0,1,9,8,9,0,0,0,9,7,0,7,1,0,3.111111,1.222222,0,ZERO_HEALTH_STOP,0,,0,", Fourteen(spec, b0));
        Assert.Equal("0,0,1,2001,Guard,1,kill,1878,15.65,15.641667,7,3,3,3,0,1,3,2,7,0,4,0,2.666667,0.666667,1,11,9,10,0,0,0,10,7,2,5,2,0,3,1.2,0,ZERO_HEALTH_STOP,0,,0,", Fourteen(spec, b1));
        Assert.Equal(30, contacts.Count);
        Assert.Equal((23, 6, 1), (contacts.Count(c => c.Contact.Met == ContactMet.Body), contacts.Count(c => c.Contact.Met == ContactMet.Shield), contacts.Count(c => c.Contact.Met == ContactMet.Level)));
        Assert.Equal(16, contacts.Sum(c => c.Harm?.Wounds ?? 0));
        // No power: its columns say none, every contact had nothing live, and a hit without a wound says why.
        Assert.All(new[] { b0, b1 }, b => Assert.All(b.Fighters, f => Assert.Equal((0, 0, 0L, 0, 0L), (f.Uses, f.PowersRefused, f.BloodSpent, f.SwingsLive, f.LiveAnyTicks))));
        Assert.All(contacts, c => Assert.Equal("", c.AttackerLive));
        Assert.All(contacts.Where(c => c.Harm is { Wounds: 0 }), c => Assert.NotEqual(ResistedBy.None, c.Harm!.Value.ResistedBy));
        Assert.All(contacts.Where(c => c.Harm is { Wounds: > 0 }), c => Assert.Equal(ResistedBy.None, c.Harm!.Value.ResistedBy));
        run.Content?.Dispose();
    }

    [Fact]
    public void A_shield_taken_away_leaves_none_to_meet_and_the_spec_s_shield_is_checked()
    {
        var (course, _) = Grounds();
        ArenaSpec Against(string weapon, string shield) => ArenaSpec.Read($$"""
            { "fighters": [ { "name": "A", "template": "Christof", "weapon": "BroadSword", "sheet": "sandbox.christof", "place": [38.8, 3], "facing": 0,
                              "brain": { "kind": "duel", "strongShare": 0, "highShare": 0, "lowShare": 0, "backOffChance": 0, "blockChance": 0 } },
                            { "name": "G", "template": "rudolfGuard", "weapon": "{{weapon}}", {{shield}} "sheet": "sandbox.guard", "place": [40.2, 3], "facing": 180,
                              "brain": { "kind": "dummy", "mode": "block" } } ],
              "seconds": 6, "bouts": 1, "tapes": 0, "seed": 4 }
            """, Path.GetTempPath(), course);
        // The guard with the Rapier (no shield): nothing he holds is a shield, and Christof's blows at his block never meet one.
        var bare = Against("Rapier", "\"shield\": false,");
        var run = new ArenaRun(bare, Baseline());
        using (var session = Session.Create(run.OptionsFor(0, 0)))
        {
            Assert.False(HeldItems.Shield(session.World, session.Fighters[1], out _));
            Assert.False(HeldItems.Shield(session.World, session.Fighters[0], out _));   // Christof's broadsword: none either
        }
        var contacts = new List<ContactRow>();
        var r = run.Bout(0, 0, contacts);
        Assert.True(r.Fighters[0].Swings >= 1 && r.Fighters[0].ContactsBody >= 1, $"{r.Fighters[0].Swings} swings, {r.Fighters[0].ContactsBody} on the body");
        Assert.NotEmpty(contacts);
        Assert.DoesNotContain(contacts, c => c.Contact.Met == ContactMet.Shield);
        run.Content?.Dispose();
        // With his own weapRapier_shield: the shield is there, and meets blows (the same bout otherwise).
        var shielded = Against("weapRapier_shield", "\"shield\": true,");
        var run2 = new ArenaRun(shielded, Baseline());
        using (var session = Session.Create(run2.OptionsFor(0, 0))) Assert.True(HeldItems.Shield(session.World, session.Fighters[1], out _));
        var withShield = new List<ContactRow>();
        run2.Bout(0, 0, withShield);
        Assert.Contains(withShield, c => c.Contact.Met == ContactMet.Shield);
        run2.Content?.Dispose();
        // A spec that says otherwise is refused by name when the bout is made.
        var wrong = new ArenaRun(Against("weapRapier_shield", "\"shield\": false,"), Baseline());
        var e = Assert.Throws<SessionException>(() => wrong.Bout(0, 0));
        Assert.Contains("fighter G: \"shield\": false", e.Message);
        Assert.Contains("Rapier", e.Message);
    }

    [Fact]
    public void A_matched_bout_runs_as_its_spec_says_and_its_tape_replays()
    {
        var (course, _) = Grounds();
        var spec = ArenaSpec.ReadFile(Path.Combine(Tools(), "matched-blade-no-shield.json"), course) with { Bouts = 1, Tapes = 1, Seconds = 5f };
        string folder = Folder("matched");
        var report = ArenaRunner.Execute(spec, Baseline(), folder, _ => { });
        var b = Assert.Single(report.Bouts);
        Assert.DoesNotContain(report.Contacts, c => c.Contact.Met == ContactMet.Shield);
        var tape = Tape.Read(Path.Combine(folder, b.Tape!));
        var options = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(tape.Header);
        using var session = Session.Create(options);
        int checkedDigests = 0;
        foreach (var frame in tape.Frames)
        {
            session.Step(frame);
            if (tape.Digests.TryGetValue(session.Tick, out ulong want)) { Assert.Equal(want, session.Digest()); checkedDigests++; }
        }
        Assert.Equal(b.Digest, session.Digest());
        Assert.True(checkedDigests >= 4);
        // Both hold the same blade and no shield, by the cast in the tape's head.
        var cast = ArenaCast.Read(tape.Header.Notes[SessionOptions.ArenaNote]);
        Assert.All(cast.Fighters, f => Assert.Equal(("Rapier", false), (f.Weapon, f.Shield)));
        Directory.Delete(folder, true);
    }

    [Fact]
    public void The_summary_s_numbers_are_the_csv_s()
    {
        var spec = Spec(""", "sweep": { "fighter": "A", "trait": "dexterity", "values": [5, 30] }""", seconds: 2f, bouts: 2);
        string folder = Folder("summary");
        ArenaRunner.Execute(spec, Baseline(), folder, _ => { });
        ArenaSummaryTests.CheckAgainstCsv(spec, folder);
        string page = File.ReadAllText(Path.Combine(folder, "summary.html"));
        Assert.Contains("<svg", page);
        Assert.DoesNotContain("src=\"http", page);
        Assert.Contains(PlatformVersion.Text, page);
    }
}
