// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// What a recording says and the player's body. Tapes made on an earlier build (no note <c>accel</c>, no note
/// <c>arrows</c>) replay on the ramp with the earlier build's arrows, digest for digest (a drive with townsfolk and blows, and the bow scenario
/// loosing arrows); a new tape notes the spring and the arrows and replays on them; <c>--body</c> falls back to 1.1's Christof in one
/// line when the models repository's person cannot be built (a folder that is not its, or the contract's kinds not merged yet), and a tape
/// naming a body that cannot be built is refused; the settings keep the body and the folder; the body's word is checked. The sessions'
/// tests skip without the proving ground and the 1.1 install.
/// </summary>
public sealed class PeopleBodyTests
{
    static string Data(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "older-tapes", name);

    static (string Course, string Game) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-body-tests-userdata"));
        return (course!, game!);
    }

    static int Replay(Tape tape, SessionOptions options)
    {
        using var session = Session.Create(options);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            session.Step(frame);
            if (tape.Digests.TryGetValue(session.Tick, out ulong want))
            {
                Assert.True(want == session.Digest(), $"tick {session.Tick}: digest {session.Digest():x16}, recorded {want:x16}");
                compared++;
            }
        }
        return compared;
    }

    [Theory]
    [InlineData("drive-proving-ground.tape", 40)]          // --record-drive --fights, 12 s, three townsfolk (the player's bow carried)
    [InlineData("bow-scenario-01-p000-r000.tape", 100)]    // the example scenario 1 at 10 m: the arrows loosed, the guard closing
    public void A_tape_made_before_the_spring_replays_on_the_ramp_with_the_heavy_arrows_digest_for_digest(string name, int least)
    {
        var (now, game) = Needs();
        // A tape replays on the course it was made on (the proving ground earlier rebuilt its ladders and stations): proving-ground-older.l2
        // beside the configured course (the proving-ground.l2 copied to that name before it is cooked again).
        string course = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(now.TrimEnd('/', '\\')))!, "proving-ground-older.l2");
        Assert.SkipUnless(File.Exists(Path.Combine(course, "level.json")),
                          $"the older tapes replay on the course they were made on: copy the proving ground from before the walls to {course} (none there)");
        var tape = Tape.Read(Data(name));
        Assert.False(tape.Header.Notes.ContainsKey(StartStopSprings.TapeNote));
        Assert.False(tape.Header.Notes.ContainsKey(PeopleBodies.ArrowsNote));
        var options = new SessionOptions { Level = course, Game = game, Say = _ => { } }.From(tape.Header);
        Assert.Contains(PeopleBodies.AccelRampSwitch, options.Switches);
        Assert.Contains(PeopleBodies.ArrowsHeavySwitch, options.Switches);
        Assert.Null(options.Body);
        Assert.True(Replay(tape, options) >= least);
    }

    static SessionOptions Sandbox(string course, string game, List<string> log, params string[] switches) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox", .. switches], StringComparer.Ordinal), Seed = 5, People = 2,
        Place = "40,3,0,0", PlayerBow = SessionOptions.DefaultPlayerBow, Say = log.Add,
    };

    static Tape Record(SessionOptions options, int seconds = 4)
    {
        using var session = Session.Create(options);
        var tape = new Tape { Header = session.TapeHeader("test") };
        int s = World.TicksPerSecond;
        for (int t = 0; t < seconds * s; t++)
        {
            // Run on, stop, turn about and run, stop: the spring's starts and stops.
            var move = t < s ? new Vector2(0f, 1f) : t < 2 * s ? Vector2.Zero : t < 3 * s ? new Vector2(1f, 0f) : Vector2.Zero;
            var frame = new ActionFrame(move, t > 2 * s && t < 2 * s + 30 ? new Vector2(0.05f, 0f) : Vector2.Zero, Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
            session.Step(frame);
            tape.Frames.Add(frame);
            if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
        }
        return tape;
    }

    [Fact]
    public void A_new_tape_notes_the_spring_and_the_arrows_and_replays_on_them_and_the_ramp_s_notes_none()
    {
        var (course, game) = Needs();
        var log = new List<string>();
        var sprung = Record(Sandbox(course, game, log));
        Assert.Equal(StartStopSprings.NoteValue, sprung.Header.Notes[StartStopSprings.TapeNote]);
        Assert.Equal("items", sprung.Header.Notes[PeopleBodies.ArrowsNote]);
        Assert.Equal(StartStopSprings.LandingStops, sprung.Header.Notes[StartStopSprings.LandingNote]);
        var again = new SessionOptions { Level = course, Game = game, Say = _ => { } }.From(sprung.Header);
        Assert.DoesNotContain(PeopleBodies.AccelRampSwitch, again.Switches);
        Assert.DoesNotContain(PeopleBodies.ArrowsHeavySwitch, again.Switches);
        Assert.DoesNotContain(PeopleBodies.LandingSlideSwitch, again.Switches);
        Assert.True(Replay(sprung, again) >= 10);
        // With the earlier build's landings the head has no landing note, and a tape on the spring without it replays with them.
        var slides = Record(Sandbox(course, game, log, PeopleBodies.LandingSlideSwitch), seconds: 1);
        Assert.False(slides.Header.Notes.ContainsKey(StartStopSprings.LandingNote));
        var notes = new Dictionary<string, string>(sprung.Header.Notes);
        notes.Remove(StartStopSprings.LandingNote);
        var fourteenCl = new SessionOptions { Level = course, Game = game, Say = _ => { } }.From(sprung.Header with { Notes = notes });
        Assert.Contains(PeopleBodies.LandingSlideSwitch, fourteenCl.Switches);

        var ramp = Record(Sandbox(course, game, log, PeopleBodies.AccelRampSwitch, PeopleBodies.ArrowsHeavySwitch));
        Assert.False(ramp.Header.Notes.ContainsKey(StartStopSprings.TapeNote));
        Assert.False(ramp.Header.Notes.ContainsKey(StartStopSprings.LandingNote));
        Assert.Equal("eh1", ramp.Header.Notes[PeopleBodies.ArrowsNote]);
        Assert.True(Replay(ramp, new SessionOptions { Level = course, Game = game, Say = _ => { } }.From(ramp.Header)) >= 10);
        // The two runs part: the spring is slower off the mark and longer to stop.
        Assert.NotEqual(sprung.Digests[World.TicksPerSecond / 2], ramp.Digests[World.TicksPerSecond / 2]);
    }

    [Fact]
    public void The_half_life_given_for_a_run_is_the_player_s_rides_on_the_tape_s_note_and_0_is_the_ramp()
    {
        // A development control (--start-stop-half-life): the player's half-life for the run, in place of the profile's.
        var (course, game) = Needs();
        var log = new List<string>();
        var given = Record(Sandbox(course, game, log) with { StartStopHalfLife = 0.12f }, seconds: 2);
        Assert.Equal("spring 0.12", given.Header.Notes[StartStopSprings.TapeNote]);
        var again = new SessionOptions { Level = course, Game = game, StartStopHalfLife = 0.3f, Say = _ => { } }.From(given.Header);
        Assert.Equal(0.12f, again.StartStopHalfLife);   // the tape's note wins
        using (var session = Session.Create(again))
        {
            session.Step(ActionFrame.Idle);
            Assert.Equal(0.12f, session.World.Get<StartStopSpring>(session.Player).HalfLife);
        }
        Assert.True(Replay(given, again) >= 5);
        // 0: the player on the ramp (no store of the spring, no note), as accel-ramp.
        var ramp = Record(Sandbox(course, game, log) with { StartStopHalfLife = 0f }, seconds: 1);
        Assert.False(ramp.Header.Notes.ContainsKey(StartStopSprings.TapeNote));
        using (var session = Session.Create(Sandbox(course, game, log) with { StartStopHalfLife = 0f }))
            Assert.False(StartStopSprings.In(session.World));
        // The note's forms.
        Assert.Null(StartStopSprings.GivenIn("spring"));
        Assert.Null(StartStopSprings.GivenIn("spring fast"));
        Assert.Null(StartStopSprings.GivenIn(null));
        Assert.Equal(0.2f, StartStopSprings.GivenIn(StartStopSprings.Note(0.2f)));
        Assert.Equal("spring", StartStopSprings.Note(null));
    }

    [Fact]
    public void The_body_falls_back_to_christof_in_one_line_when_the_models_stream_s_person_cannot_be_built()
    {
        var (course, game) = Needs();
        string empty = Path.Combine(Path.GetTempPath(), "vitomir-not-the-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var log = new List<string>();
            var options = Sandbox(course, game, log) with { Body = PeopleBodies.Models, ModelsFolder = empty };
            using (var session = Session.Create(options))
            {
                Assert.Null(session.PlayerBody);
                var said = Assert.Single(log, l => l.StartsWith("people:", StringComparison.Ordinal));
                Assert.StartsWith("people: the player is 1.1's Christof: ", said);
                Assert.Contains("not the models repository's", said);
                var head = session.TapeHeader("test");
                Assert.False(head.Notes.ContainsKey(SessionOptions.BodyNote));
                Assert.Equal("war_bow:24", head.Notes[SessionOptions.PlayerBowNote]);   // Christof keeps his bow
                for (int t = 0; t < 30; t++) session.Step(new ActionFrame(new Vector2(0f, 1f), Vector2.Zero, t == 5 ? Buttons.Attack : Buttons.None, new Vector2(0.5f, 0.5f)));
            }
            // The models repository's own files: while the kinds are the contract's stub, Christof again, in one
            // line; once merged the man is built, noted in the head, carries no bow, and his attack does nothing (said once).
            if (Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable) is { } models && Directory.Exists(models))
            {
                log.Clear();
                using var session = Session.Create(options with { ModelsFolder = models });
                var head = session.TapeHeader("test");
                if (session.PlayerBody is null)
                {
                    Assert.Single(log, l => l.StartsWith("people:", StringComparison.Ordinal));
                    Assert.False(head.Notes.ContainsKey(SessionOptions.BodyNote));
                }
                else
                {
                    Assert.Equal(PeopleBodies.Models, head.Notes[SessionOptions.BodyNote]);
                    Assert.False(head.Notes.ContainsKey(SessionOptions.PlayerBowNote) && head.Notes[SessionOptions.PlayerBowNote].Length > 0);
                    for (int t = 0; t < 30; t++) session.Step(new ActionFrame(new Vector2(0f, 1f), Vector2.Zero, t is 5 or 9 ? Buttons.Attack : Buttons.None, new Vector2(0.5f, 0.5f)));
                    Assert.Single(log, l => l == "the new body has no combat yet: --body oneone for combat");
                }
            }
        }
        finally { Directory.Delete(empty, recursive: true); }
    }

    [Fact]
    public void A_tape_naming_a_body_that_cannot_be_built_here_is_refused_not_replayed_as_christof()
    {
        var (course, game) = Needs();
        var head = new TapeHeader();
        head.Notes[SessionOptions.BodyNote] = PeopleBodies.Models;
        var options = new SessionOptions { Level = course, Game = game, Say = _ => { } }.From(head);
        Assert.Equal(PeopleBodies.Models, options.Body);
        Assert.True(options.BodyRequired);
        string empty = Path.Combine(Path.GetTempPath(), "vitomir-no-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try
        {
            var x = Assert.Throws<SessionException>(() => Session.Create(options with { ModelsFolder = empty, Switches = new HashSet<string>(options.Switches) { "sandbox" } }));
            Assert.Contains("cannot be built here", x.Message);
        }
        finally { Directory.Delete(empty, recursive: true); }
    }

    [Fact]
    public void The_new_body_crouches_to_its_own_height_by_a_profile_of_its_own_in_either_mode()
    {
        // The models repository's its crouch is low on the move and a kneel when still; an earlier build: every crouch clip's
        // head at or below 1.10 m, so 1.15 m — the same as 1.1's, yet a profile of its own, so the earlier build's 1.25 (a tape's) is one number away.
        var profiles = new Modules.MovementProfiles();
        var human = Locomotion.Sandbox with { RunSpeed = 3f };
        int h = profiles.Add(new Modules.MovementProfile(Modules.AssetId.Of("test", "movement/human"), "a human", human, true) { HalfLife = 0.2f }, isHuman: true);
        int crouched = PeopleBodies.Crouched(profiles, h, PeopleBodies.Models);
        Assert.NotEqual(h, crouched);
        Assert.Equal("test:movement/human-models", profiles.All[crouched].Id.ToString());
        Assert.Equal(crouched, PeopleBodies.Crouched(profiles, h, PeopleBodies.Models));   // added once
        Assert.Equal(1.15f, PeopleBodies.CrouchHeight);
        Assert.Equal(PeopleBodies.CrouchHeight, profiles.Numbers(crouched, MasqueradeMode.Unbound).CrouchHeight);
        Assert.Equal(PeopleBodies.CrouchHeight, profiles.Numbers(crouched, MasqueradeMode.Masquerade).CrouchHeight);   // the masquerade keeps the body's own crouch
        var older = new Modules.MovementProfiles();
        int h2 = older.Add(new Modules.MovementProfile(Modules.AssetId.Of("test", "movement/human"), "a human", human, true) { HalfLife = 0.2f }, isHuman: true);
        int crouchedTall = PeopleBodies.Crouched(older, h2, PeopleBodies.Models, PeopleBodies.CrouchHeightTall);   // a tape's
        Assert.Equal(1.25f, older.Numbers(crouchedTall, MasqueradeMode.Masquerade).CrouchHeight);
        Assert.Equal(1.15f, profiles.Numbers(h, MasqueradeMode.Masquerade).CrouchHeight);           // 1.1's bodies keep 1.15 m
        Assert.Equal(0.2f, profiles.HalfLife(crouched, MasqueradeMode.Unbound));
        Assert.Equal(human.RunSpeed, profiles.Numbers(crouched, MasqueradeMode.Unbound).RunSpeed);
    }

    [Fact]
    public void The_settings_keep_the_body_and_the_models_folder_and_the_body_s_word_is_checked()
    {
        Assert.Equal("models", new PlatformSettings().PlayerBody);
        Assert.Equal("", new PlatformSettings().ModelsFolder);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { PlayerBody = "woman", ModelsFolder = dir }.Save(path);
            var back = PlatformSettings.Load(path);
            Assert.Equal(("woman", dir), (back.PlayerBody, back.ModelsFolder));
            Assert.Equal(dir, PeopleBodies.FindFolder(null, back.ModelsFolder));
            Assert.Equal(dir, PeopleBodies.FindFolder(dir, "/no/such/folder"));
            Assert.Equal(dir, PeopleBodies.FindFolder("/no/such/folder", dir));   // the first that is a folder
        }
        finally { Directory.Delete(dir, recursive: true); }
        Assert.Equal("woman", PeopleBodies.Choose("woman", playAs: true, combat: true, "oneone"));
        Assert.Equal("oneone", PeopleBodies.Choose(null, playAs: true, combat: false, "models"));
        Assert.Equal("oneone", PeopleBodies.Choose(null, playAs: false, combat: true, "models"));
        Assert.Equal("models", PeopleBodies.Choose(null, playAs: false, combat: false, "nonsense"));
        Assert.Equal("woman", PeopleBodies.Choose(null, playAs: false, combat: false, "woman"));
        Assert.Throws<SessionException>(() => PeopleBodies.Choose("giant", false, false, null));
        Assert.Same(Assets.People.PersonRecipe.Player, PeopleBodies.RecipeOf("models"));
        Assert.Same(Assets.People.PersonRecipe.Woman, PeopleBodies.RecipeOf("woman"));
        Assert.Null(PeopleBodies.RecipeOf("oneone"));
        Assert.Null(PeopleBodies.RecipeOf(null));
    }
}
