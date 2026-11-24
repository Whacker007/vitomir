// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// A person's planted feet, heel lift and bend rule as the program gives them: the settings' keys (<c>feetOff</c>,
/// <c>footLockSeconds</c>, <c>footReplant</c>, <c>heelLift</c>, <c>bendRuleOff</c>) and the flags; a recording of the models repository's body
/// carries them (the note <c>feet</c>) and replays on them digest for digest; a tape without the note replays on the earlier build's feet;
/// Christof's tapes carry no note. The sessions' tests skip without the proving ground, the 1.1 install or the models repository's files.
/// </summary>
public sealed class PersonFeetSettingsTests
{
    const float Deg = DetMath.DegToRad;

    [Fact]
    public void The_settings_and_flags_give_the_feet_s_numbers_and_feet_off_turns_them_off()
    {
        var plain = new PlatformSettings();
        Assert.Equal(new PersonFeetSettings().Note(), plain.PersonFeetFor(new Cli([])).Note());
        var tuned = new PlatformSettings { FootLockSeconds = 0.2f, FootReplant = 0.3f, HeelLift = 35f };
        var t = tuned.PersonFeetFor(new Cli([]));
        Assert.True(t.Locks);
        Assert.Equal((0.2f, 0.3f), (t.LockSeconds, t.Replant));
        Assert.Equal(35f * Deg, t.HeelLift, 5);
        Assert.True(t.Bend);
        // A flag over the setting, for one run.
        var f = tuned.PersonFeetFor(new Cli(["--heel-lift", "0", "--foot-replant", "0.15", "--bend-rule-off"]));
        Assert.Equal(0f, f.HeelLift);
        Assert.Equal(0.15f, f.Replant);
        Assert.False(f.Bend);
        // The earlier build's feet: no locks, no lift (unless asked), no soft limit; the bend rule its own switch.
        var old = plain.PersonFeetFor(new Cli(["--feet-off", "--bend-rule-off"]));
        Assert.Equal(PersonFeetSettings.Off.Note(), old.Note());
        Assert.False(old.Any);
        Assert.Equal(PersonFeetSettings.Off with { Bend = true }, new PlatformSettings { FeetOff = true }.PersonFeetFor(new Cli([])));
        Assert.Equal(20f * Deg, plain.PersonFeetFor(new Cli(["--feet-off", "--heel-lift", "20"])).HeelLift, 5);
        // A flag out of its sense is refused; a setting out of it is the default.
        Assert.Throws<SessionException>(() => plain.PersonFeetFor(new Cli(["--heel-lift", "80"])));
        Assert.Throws<SessionException>(() => plain.PersonFeetFor(new Cli(["--foot-lock-seconds", "slow"])));
        Assert.Throws<SessionException>(() => plain.PersonFeetFor(new Cli(["--foot-replant", "0"])));
        Assert.Equal(new PersonFeetSettings().HeelLift, new PlatformSettings { HeelLift = 400f }.PersonFeetFor(new Cli([])).HeelLift, 5);

        // The settings file keeps them.
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-feet-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { HeelLift = 22f, FootReplant = 0.25f, FeetOff = true, BendRuleOff = true }.Save(path);
            string text = File.ReadAllText(path);
            Assert.Contains("\"heelLift\": 22", text);
            Assert.Contains("\"feetOff\": true", text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((22f, 0.25f, true, true), (back.HeelLift, back.FootReplant, back.FeetOff, back.BendRuleOff));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_note_has_the_feet_off_and_the_note_carries_the_numbers()
    {
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(new TapeHeader());
        Assert.Equal(PersonFeetSettings.Off, o.PersonFeet);
        var head = new TapeHeader();
        var given = new PersonFeetSettings { HeelLift = 31f * Deg, Replant = 0.25f };
        head.Notes[SessionOptions.FeetNote] = given.Note();
        var n = new SessionOptions { Level = "", PersonFeet = PersonFeetSettings.Off, Say = _ => { } }.From(head);   // the tape's numbers win
        Assert.Equal(given.Note(), n.PersonFeet.Note());
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-person-feet-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions Person(string course, string game, string models) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Place = "40,3,0,0", Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    /// <summary>Standing, the view swung left and back (a turn on the spot), a crouch held: a few seconds that plant the feet and lift the heels.</summary>
    static Tape Record(SessionOptions options, int seconds = 3)
    {
        using var session = Session.Create(options);
        Assert.NotNull(session.PlayerBody);
        var tape = new Tape { Header = session.TapeHeader("test") };
        int s = World.TicksPerSecond;
        for (int t = 0; t < seconds * s; t++)
        {
            var look = t < s / 2 ? new Vector2(-6f, 0f) : t < s ? new Vector2(4f, 0f) : Vector2.Zero;
            var buttons = t > 3 * s / 2 ? Buttons.Crouch : Buttons.None;
            var frame = new ActionFrame(Vector2.Zero, look, buttons, new Vector2(0.5f, 0.5f), 16f / 9f);
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
    public void A_recording_of_the_new_body_notes_the_feet_and_replays_on_them()
    {
        var (course, game, models) = Needs();
        var tuned = Person(course, game, models) with { PersonFeet = new PersonFeetSettings { HeelLift = 33f * Deg, LockSeconds = 0.12f } };
        var tape = Record(tuned);
        Assert.Equal(tuned.PersonFeet.Note(), tape.Header.Notes[SessionOptions.FeetNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(tuned.PersonFeet.Note(), again.PersonFeet.Note());
        Assert.True(Replay(tape, again) >= 5);
        using (var session = Session.Create(again))
        {
            Assert.True(session.World.TryStore<PersonFeet>(out _));
            for (int t = 0; t < World.TicksPerSecond; t++) session.Step(new ActionFrame(Vector2.Zero, Vector2.Zero, Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f));
            var feet = session.World.Get<PersonFeet>(session.Player);
            Assert.True(feet.On && feet.LeftHold > 0.99f && feet.RightHold > 0.99f, "standing, both feet held");
        }

        // The earlier build's feet, noted and replayed so; the world as before (no store of the feet).
        var old = Record(Person(course, game, models) with { PersonFeet = PersonFeetSettings.Off });
        Assert.Equal("off", old.Header.Notes[SessionOptions.FeetNote]);
        var oldAgain = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(old.Header);
        Assert.True(Replay(old, oldAgain) >= 5);
        using (var session = Session.Create(oldAgain))
            Assert.False(session.World.TryStore<PersonFeet>(out _));
        // The two part: the drawn body is the simulation's (it moves the hit volumes).
        Assert.NotEqual(tape.Digests[World.TicksPerSecond / 2], old.Digests[World.TicksPerSecond / 2]);

        // Christof's tapes carry no note (they are as before), and his world has no store.
        using var christof = Session.Create(Person(course, game, models) with { Body = PeopleBodies.OneOne });
        Assert.Null(christof.PlayerBody);
        Assert.False(christof.TapeHeader("test").Notes.ContainsKey(SessionOptions.FeetNote));
    }
}
