// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The people's turn as a chain and the crouch without the kneel, as the program gives them: the settings' keys
/// (<c>turnHeadRate</c> … <c>turnChainOff</c>, <c>crouchKneel</c>) and the <c>--turn-…</c>, <c>--turn-chain-off</c> and <c>--crouch-kneel</c> flags;
/// a recording of the models repository's body carries them (the notes <c>turn</c> and <c>crouchKneel</c>) and replays on them digest for
/// digest; a tape without them replays on the earlier build's turn and kneel; Christof's tapes carry neither. The sessions' tests skip without
/// the proving ground, the 1.1 install or the models repository's files.
/// </summary>
public sealed class PersonTurnSettingsTests
{
    const float Deg = DetMath.DegToRad;

    [Fact]
    public void The_settings_and_flags_give_the_chain_s_numbers_and_turn_chain_off_turns_it_off()
    {
        var plain = new PlatformSettings();
        Assert.Equal(new LegTurnSettings().Note(), plain.PersonTurnFor(new Cli([])).Note());
        Assert.False(plain.CrouchKneel);
        Assert.False(plain.TurnChainOff);

        var tuned = new PlatformSettings { TurnHeadRate = 250f, TurnNeckMost = 25f, TurnFrom = 35f, TurnMostSpeedup = 1.5f, TurnChestLag = 0.1f };
        var t = tuned.PersonTurnFor(new Cli([]));
        Assert.True(t.Chain);
        Assert.Equal(250f * Deg, t.HeadRate, 5);
        Assert.Equal(25f * Deg, t.NeckMost, 5);
        Assert.Equal(35f * Deg, t.PersonTurnFrom, 5);
        Assert.Equal(1.5f, t.TurnMostSpeedup);
        Assert.Equal(0.1f, t.ChestLag);
        // A flag over the setting, for one run.
        var f = tuned.PersonTurnFor(new Cli(["--turn-neck-most", "40", "--turn-pivot-rate", "300", "--turn-on", "15"]));
        Assert.Equal(40f * Deg, f.NeckMost, 5);
        Assert.Equal(300f * Deg, f.PivotRate, 5);
        Assert.Equal(15f * Deg, f.PersonTurnOn, 5);
        Assert.Equal(250f * Deg, f.HeadRate, 5);
        // The earlier build's turn: the setting or the flag; a flag's number still over it.
        Assert.Equal(LegTurnSettings.NoChain.Note(), new PlatformSettings { TurnChainOff = true, TurnFrom = 20f }.PersonTurnFor(new Cli([])).Note());
        var old = plain.PersonTurnFor(new Cli(["--turn-chain-off"]));
        Assert.False(old.Chain);
        Assert.Equal(45f * Deg, old.PersonTurnFrom, 5);
        Assert.Equal(50f * Deg, plain.PersonTurnFor(new Cli(["--turn-chain-off", "--turn-from", "50"])).PersonTurnFrom, 5);
        // A flag out of its sense is refused; a setting out of it is the default.
        Assert.Throws<SessionException>(() => plain.PersonTurnFor(new Cli(["--turn-head-rate", "0"])));
        Assert.Throws<SessionException>(() => plain.PersonTurnFor(new Cli(["--turn-neck-most", "fast"])));
        Assert.Throws<SessionException>(() => plain.PersonTurnFor(new Cli(["--turn-most-speedup", "9"])));
        Assert.Equal(25f * Deg, new PlatformSettings { TurnNeckMost = 400f }.PersonTurnFor(new Cli([])).NeckMost, 5);

        // The settings file keeps them.
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-turn-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { TurnHeadRate = 220f, TurnPivotFrom = 60f, TurnChainOff = true, CrouchKneel = true }.Save(path);
            string text = File.ReadAllText(path);
            Assert.Contains("\"turnHeadRate\": 220", text);
            Assert.Contains("\"crouchKneel\": true", text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((220f, 60f, true, true), (back.TurnHeadRate, back.TurnPivotFrom, back.TurnChainOff, back.CrouchKneel));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_notes_has_the_turn_without_the_chain_and_the_kneel_and_the_notes_carry_the_numbers()
    {
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(new TapeHeader());
        Assert.False(o.PersonTurn.Chain);
        Assert.Equal(45f * Deg, o.PersonTurn.PersonTurnFrom, 5);
        Assert.True(o.CrouchKneel);
        var head = new TapeHeader();
        var given = new LegTurnSettings { HeadRate = 260f * Deg, PivotFrom = 70f * Deg };
        head.Notes[SessionOptions.TurnNote] = given.Note();
        head.Notes[SessionOptions.CrouchKneelNote] = "false";
        var n = new SessionOptions { Level = "", PersonTurn = new LegTurnSettings { HeadRate = 1f }, Say = _ => { } }.From(head);   // the tape's numbers win
        Assert.Equal(given.Note(), n.PersonTurn.Note());
        Assert.False(n.CrouchKneel);
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-person-turn-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions Person(string course, string game, string models) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Place = "40,3,0,0", Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    /// <summary>Standing, the view swung left and back, a crouch held, a walk: a few seconds that turn and crouch the body.</summary>
    static Tape Record(SessionOptions options, int seconds = 4)
    {
        using var session = Session.Create(options);
        Assert.NotNull(session.PlayerBody);
        var tape = new Tape { Header = session.TapeHeader("test") };
        int s = World.TicksPerSecond;
        for (int t = 0; t < seconds * s; t++)
        {
            var look = t < s / 2 ? new Vector2(-6f, 0f) : t < s ? new Vector2(4f, 0f) : Vector2.Zero;
            var buttons = t > 3 * s / 2 && t < 5 * s / 2 ? Buttons.Crouch : Buttons.None;
            var move = t > 3 * s ? new Vector2(0f, 1f) : Vector2.Zero;
            var frame = new ActionFrame(move, look, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);
            session.Step(frame);
            tape.Frames.Add(frame);
            if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
        }
        return tape;
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

    [Fact]
    public void A_recording_of_the_new_body_notes_the_turn_and_the_kneel_and_replays_on_them()
    {
        var (course, game, models) = Needs();
        var tuned = Person(course, game, models) with { PersonTurn = new LegTurnSettings { HeadRate = 240f * Deg, SpineMost = 25f * Deg } };
        var tape = Record(tuned);
        Assert.Equal(tuned.PersonTurn.Note(), tape.Header.Notes[SessionOptions.TurnNote]);
        Assert.Equal("false", tape.Header.Notes[SessionOptions.CrouchKneelNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(tuned.PersonTurn.Note(), again.PersonTurn.Note());
        Assert.True(Replay(tape, again) >= 10);
        using (var session = Session.Create(again))
            Assert.True(session.World.TryStore<PersonTurn>(out _));

        // The earlier build's turn and kneel, noted and replayed so; the world as before (no store of the chain).
        var old = Record(Person(course, game, models) with { PersonTurn = LegTurnSettings.NoChain, CrouchKneel = true });
        Assert.StartsWith("nochain", old.Header.Notes[SessionOptions.TurnNote]);
        Assert.Equal("true", old.Header.Notes[SessionOptions.CrouchKneelNote]);
        var oldAgain = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(old.Header);
        Assert.True(Replay(old, oldAgain) >= 10);
        using (var session = Session.Create(oldAgain))
            Assert.False(session.World.TryStore<PersonTurn>(out _));
        // The two part: the drawn body is the simulation's (it moves the hit volumes).
        Assert.NotEqual(tape.Digests[World.TicksPerSecond / 2], old.Digests[World.TicksPerSecond / 2]);

        // Christof's tapes carry neither note (they are as before).
        using var christof = Session.Create(Person(course, game, models) with { Body = PeopleBodies.OneOne });
        Assert.Null(christof.PlayerBody);
        var head = christof.TapeHeader("test");
        Assert.False(head.Notes.ContainsKey(SessionOptions.TurnNote));
        Assert.False(head.Notes.ContainsKey(SessionOptions.CrouchKneelNote));
    }

    [Fact]
    public void The_leash_and_the_waist_are_settings_and_flags()
    {
        // The leash (90°) and the waist (spread over the chest, the spine on the pelvis) by default.
        var plain = new PlatformSettings().PersonTurnFor(new Cli([]));
        Assert.Equal(90f * Deg, plain.Leash, 5);
        Assert.True(plain.Leashed);
        Assert.False(plain.WaistEven);
        Assert.Equal(60f * Deg, new PlatformSettings { TurnLeash = 60f }.PersonTurnFor(new Cli([])).Leash, 5);
        Assert.Equal(120f * Deg, new PlatformSettings { TurnLeash = 60f }.PersonTurnFor(new Cli(["--turn-leash", "120"])).Leash, 5);
        Assert.False(new PlatformSettings().PersonTurnFor(new Cli(["--turn-leash", "0"])).Leashed);      // 0 or 180: off, the earlier build's
        Assert.False(new PlatformSettings().PersonTurnFor(new Cli(["--turn-leash", "180"])).Leashed);
        Assert.Throws<SessionException>(() => new PlatformSettings().PersonTurnFor(new Cli(["--turn-leash", "200"])));
        Assert.Equal(90f * Deg, new PlatformSettings { TurnLeash = -5f }.PersonTurnFor(new Cli([])).Leash, 5);
        Assert.True(new PlatformSettings().PersonTurnFor(new Cli(["--turn-waist-even"])).WaistEven);
        Assert.True(new PlatformSettings { TurnWaistEven = true }.PersonTurnFor(new Cli([])).WaistEven);

        string dir = Path.Combine(Path.GetTempPath(), "vitomir-leash-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { TurnLeash = 75f, TurnWaistEven = true }.Save(path);
            string text = File.ReadAllText(path);
            Assert.Contains("\"turnLeash\": 75", text);
            Assert.Contains("\"turnWaistEven\": true", text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((75f, true), (back.TurnLeash, back.TurnWaistEven));
        }
        finally { Directory.Delete(dir, recursive: true); }

        // A tape (its note without them): the leash off and the earlier build's waist, whatever the settings.
        var head = new TapeHeader();
        head.Notes[SessionOptions.TurnNote] = "chain head=300 headLag=0.03 neck=25 chest=200 chestLag=0.06 spine=30 from=30 on=20 speedup=2 pivotFrom=75 pivotRate=240";
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.True(o.PersonTurn.Chain);
        Assert.False(o.PersonTurn.Leashed);
        Assert.True(o.PersonTurn.WaistEven);
    }

    [Fact]
    public void A_recording_notes_the_leash_and_the_waist_and_replays_on_them_and_one_without_on_the_even_waist()
    {
        var (course, game, models) = Needs();
        var now = Record(Person(course, game, models));
        Assert.EndsWith(" leash=90 waist=1", now.Header.Notes[SessionOptions.TurnNote]);
        var nowAgain = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(now.Header);
        Assert.True(Replay(now, nowAgain) >= 10);
        using (var session = Session.Create(nowAgain))
            Assert.False(session.World.Service<CharacterAssets>().PersonWaistEven);

        // As the earlier build's: noted so and replayed so; the drawn body (the simulation's) parts from the earlier build's once the chest is turned over the hips.
        var cm = Person(course, game, models) with { PersonTurn = new LegTurnSettings { Leash = 0f, WaistEven = true } };
        var old = Record(cm);
        Assert.EndsWith(" leash=0 waist=0", old.Header.Notes[SessionOptions.TurnNote]);
        var oldAgain = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(old.Header);
        Assert.True(oldAgain.PersonTurn.WaistEven);
        Assert.True(Replay(old, oldAgain) >= 10);
        using (var session = Session.Create(oldAgain))
            Assert.True(session.World.Service<CharacterAssets>().PersonWaistEven);
        Assert.Contains(now.Digests.Keys, t => now.Digests[t] != old.Digests[t]);
    }
}
