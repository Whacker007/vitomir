// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// Foes to fight in play (<c>--foe &lt; who&gt; @x, y, z[,yaw]</c>): two foes placed in the proving ground's free
/// yard both come for the player and fight him with the duelling brain, named and dressed by their presets; a recording of it
/// replays from its head, digest for digest; a foe that is no preset stops the start by name; the presets are the arena's
/// <c>fight-*.json</c> fighters. Skips without the proving ground (VITOMIR_COURSE_LEVEL) and the 1.1 install.
/// </summary>
public sealed class FoeTests
{
    static SessionOptions Options(params string[] foes)
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-foe-tests-userdata"));
        return new SessionOptions
        {
            Level = course!, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" }, Seed = 5, People = 0,
            Place = "40,3,0,0", Foes = foes,
        };
    }

    [Fact]
    public void Two_foes_placed_both_fight_the_player_and_a_tape_of_it_replays()
    {
        var options = Options("townsman-club@42.5,2,0,180", "szlachta@42.5,4.5,0,180");
        Tape tape;
        var swings = new int[2];
        var met = new int[2];
        using (var session = Session.Create(options))
        {
            var w = session.World;
            Assert.Equal(2, session.Foes.Count);
            Assert.Equal("Townsman (foe 1)", session.NameOf(session.Foes[0].Packed));
            Assert.Equal("Szlachta (foe 2)", session.NameOf(session.Foes[1].Packed));
            foreach (var f in session.Foes)
            {
                Assert.Equal(session.Player, w.Get<DuelBrain>(f).Foe);                            // each comes for the player
                Assert.Equal(BrainKind.Stand, w.Get<DuelBrain>(f).Kind);                          // waiting for him to come near (8 m)
                Assert.Equal(8f, w.Get<FoeWaits>(f).Metres);
            }
            int woke = 0;
            tape = new Tape { Header = session.TapeHeader("test") };
            Assert.Equal(string.Join(";", options.Foes), tape.Header.Notes[FoeRequest.TapeNote]);
            for (int t = 0; t < 10 * World.TicksPerSecond; t++)
            {
                session.Step(ActionFrame.Idle);
                tape.Frames.Add(ActionFrame.Idle);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                foreach (var x in w.Events<FoeWoke>().ThisTick) { woke++; Assert.False(x.Struck); Assert.True(x.Tick <= 2, "2.5 m away: at once"); }
                foreach (var a in w.Events<AttackBegun>().ThisTick)
                    for (int i = 0; i < 2; i++) if (a.Entity == session.Foes[i]) swings[i]++;
                foreach (var c in w.Events<ContactMade>().ThisTick)
                    for (int i = 0; i < 2; i++) if (c.Attacker == session.Foes[i] && c.Target == session.Player) met[i]++;
            }
            tape.Digests[session.Tick] = session.Digest();
            Assert.Equal(2, woke);
            Assert.All(session.Foes, f => Assert.Equal(BrainKind.Duel, w.Get<DuelBrain>(f).Kind));
        }
        Assert.True(swings[0] > 0 && swings[1] > 0, $"both foes swung at the player ({swings[0]}, {swings[1]})");
        Assert.True(met[0] + met[1] > 0, $"their blows met the player ({met[0]}, {met[1]} contacts)");
        // As --replay-check does: the options from the tape's head, a fresh session, every digest compared.
        var again = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(tape.Header);
        Assert.Equal(options.Foes, again.Foes);
        using var replay = Session.Create(again);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        Assert.True(compared >= 30, $"{compared} digests compared");
    }

    /// <summary>A foe 15 m away stands where it was put, facing where it was put, until the player comes within 8 m.</summary>
    [Fact]
    public void A_foe_far_away_waits_for_the_player_to_come_near()
    {
        using var session = Session.Create(Options("watchman@55,3,0,180"));
        var w = session.World;
        var foe = session.Foes[0];
        var feet = w.Get<Bodies.Body>(foe).Feet;
        for (int t = 0; t < 2 * World.TicksPerSecond; t++) session.Step(ActionFrame.Idle);
        Assert.Equal(BrainKind.Stand, w.Get<DuelBrain>(foe).Kind);
        Assert.True(Vector3.Distance(feet, w.Get<Bodies.Body>(foe).Feet) < 0.05f, "it stood");
        // The player walks toward it (forward, +X) until it wakes; then it comes.
        int woke = -1;
        for (int t = 0; t < 12 * World.TicksPerSecond && woke < 0; t++)
        {
            session.Step(new ActionFrame(new Vector2(0, 1), default, Buttons.None, default));
            foreach (var x in w.Events<FoeWoke>().ThisTick) woke = (int)x.Tick;
        }
        Assert.True(woke > 0, "it woke as the player came near");
        float gap = Vector3.Distance(w.Get<Bodies.Body>(session.Player).Feet, w.Get<Bodies.Body>(foe).Feet);
        Assert.InRange(gap, 7.5f, 8.5f);
        Assert.Equal(BrainKind.Duel, w.Get<DuelBrain>(foe).Kind);
        Assert.False(w.Has<FoeWaits>(foe));
    }

    [Fact]
    public void A_session_without_foes_writes_no_foes_note_and_a_foe_that_is_no_preset_stops_the_start_by_name()
    {
        using (var session = Session.Create(Options()))
        {
            Assert.Empty(session.Foes);
            Assert.False(session.TapeHeader("test").Notes.ContainsKey(FoeRequest.TapeNote));
        }
        var e = Assert.Throws<SessionException>(() => Session.Create(Options("dragon@42,3,0")));
        Assert.Contains("there is no foe 'dragon'", e.Message, StringComparison.Ordinal);
        Assert.Contains("szlachta", e.Message, StringComparison.Ordinal);
        e = Assert.Throws<SessionException>(() => Session.Create(Options("szlachta@42,3")));
        Assert.Contains("--foe wants", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_foe_request_reads_and_writes_as_given()
    {
        Assert.True(FoeRequest.TryParse("szlachta@40,3.5,0,180", out var f, out _));
        Assert.Equal("szlachta", f.Who);
        Assert.Equal(new Vector3(40f, 3.5f, 0f), f.Feet);
        Assert.Equal(180f, f.YawDegrees);
        Assert.Equal("szlachta@40,3.5,0,180", f.ToString());
        Assert.True(FoeRequest.TryParse("guard@1,2,3", out f, out _));
        Assert.Equal(0f, f.YawDegrees);
        Assert.False(FoeRequest.TryParse("guard", out _, out _));
        Assert.False(FoeRequest.TryParse("@1,2,3", out _, out _));
        Assert.False(FoeRequest.TryParse("guard@1,2,x", out _, out _));
    }

    /// <summary>The presets dress and arm the foes as the arena's <c>fight-*.json</c> fighters are (template, weapon, shield, sheet).</summary>
    [Fact]
    public void The_presets_are_the_arena_s_fighters()
    {
        string data = Path.Combine(AppContext.BaseDirectory, "Modules", "Enhanced", "Data", "combat", "foes.json");
        Assert.SkipUnless(File.Exists(data), "the module's data is not beside the tests");
        var problems = new List<string>();
        var presets = FoePresets.Read(File.ReadAllText(data), "foes.json", problems);
        Assert.Empty(problems);
        Assert.Equal(["archer", "guard", "szlachta", "townsman-club", "townsman-dagger", "watchman"], presets.Keys.Order(StringComparer.Ordinal));   // the archer (no arena spec of its own)
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "tools", "platform", "arena"))) dir = Path.GetDirectoryName(dir);
        Assert.SkipUnless(dir is not null, "the repository's tools/platform/arena is not above the tests");
        foreach (var (preset, spec) in new[] { ("szlachta", "fight-szlachta.json"), ("guard", "fight-guard-rapier-shield.json"), ("watchman", "fight-watchman-broadsword.json"),
                                               ("townsman-club", "fight-townsman-club.json"), ("townsman-dagger", "fight-townsman-dagger.json") })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!, "tools", "platform", "arena", spec)), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var f = doc.RootElement.GetProperty("fighters")[1];
            var p = presets[preset];
            Assert.Equal(f.GetProperty("template").GetString(), p.Template);
            Assert.Equal(f.TryGetProperty("weapon", out var wv) ? wv.GetString() : null, p.Weapon);
            Assert.Equal(f.TryGetProperty("shield", out var sv) ? sv.GetBoolean() : null, p.Shield);
            Assert.Equal(f.GetProperty("sheet").GetString(), p.Sheet);
            // How it moves and comes on (the szlachta: its 1.1 template's speeds and 1.1's approach; the others the human's, the duel's).
            Assert.Equal(f.TryGetProperty("movement", out var mv) ? mv.GetString() : null, p.Movement);
            var approach = f.TryGetProperty("brain", out var bv) && bv.ValueKind == JsonValueKind.Object && bv.TryGetProperty("approach", out var av) ? av.GetString() : null;
            Assert.Equal(approach, p.Approach is { } pa ? Arena.ArenaSpec.NameOf(pa) : null);
        }
        Assert.Equal(("template", Combat.DuelApproach.OneOne), (presets["szlachta"].Movement, presets["szlachta"].Approach));
        problems = [];
        FoePresets.Read("{ \"schema\": \"VITOMIR_ENHANCED_FOES\", \"status\": \"SANDBOX\", \"foes\": { \"a\": { \"template\": \"Szlachta\", \"sheet\": \"s\", \"approach\": \"rush\" } } }", "bad", problems);
        Assert.Contains("approach is duel or oneone", Assert.Single(problems));
        // A file with the wrong schema, a status other than SANDBOX and a preset without its template and sheet: each named.
        problems = [];
        FoePresets.Read("{ \"schema\": \"x\", \"status\": \"RULE\", \"foes\": { \"a\": { } } }", "bad", problems);
        Assert.Equal(3, problems.Count);
    }
}
