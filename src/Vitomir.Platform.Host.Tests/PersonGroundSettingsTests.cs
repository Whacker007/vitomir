// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The people on stairs and slopes as the program gives them: the settings' keys (<c>stairLineOff</c>, <c>slopeClipsOff</c>,
/// <c>stairsHalfLife</c>, <c>stairsWindow</c>) and the flags; on the proving ground's stairs (skip without it, the 1.1 install or the
/// models repository's files) the capsule goes exactly where it goes without the rules (only the drawing changes) and the drawn hips are smooth;
/// Christof's world has no store and digests as before; a recording of the new body carries the note <c>ground</c> and replays on it, and a
/// tape without it replays as before.
/// </summary>
public sealed class PersonGroundSettingsTests(ITestOutputHelper output)
{
    [Fact]
    public void The_settings_and_flags_give_the_rules_and_the_stair_and_slope_switches_turn_them_off()
    {
        var plain = new PlatformSettings();
        Assert.Equal(new PersonGroundSettings().Note(), plain.PersonGroundFor(new Cli([])).Note());
        var tuned = new PlatformSettings { StairsHalfLife = 0.15f, StairsWindow = 0.9f };
        var t = tuned.PersonGroundFor(new Cli([]));
        Assert.Equal((0.15f, 0.9f, true, true), (t.LineHalfLife, t.Window, t.Stairs, t.Slopes));
        var f = tuned.PersonGroundFor(new Cli(["--stairs-half-life", "0.05", "--stairs-window", "0.4", "--slope-clips-off"]));
        Assert.Equal((0.05f, 0.4f, true, false), (f.LineHalfLife, f.Window, f.Stairs, f.Slopes));
        Assert.False(plain.PersonGroundFor(new Cli(["--stair-line-off"])).Stairs);
        Assert.Equal(PersonGroundSettings.Off.Note(), plain.PersonGroundFor(new Cli(["--stair-line-off", "--slope-clips-off"])).Note());
        Assert.Equal(PersonGroundSettings.Off.Note(), new PlatformSettings { StairLineOff = true, SlopeClipsOff = true }.PersonGroundFor(new Cli([])).Note());
        Assert.Throws<SessionException>(() => plain.PersonGroundFor(new Cli(["--stairs-half-life", "2"])));
        Assert.Throws<SessionException>(() => plain.PersonGroundFor(new Cli(["--stairs-window", "wide"])));
        Assert.Equal(new PersonGroundSettings().Window, new PlatformSettings { StairsWindow = 50f }.PersonGroundFor(new Cli([])).Window);

        string dir = Path.Combine(Path.GetTempPath(), "vitomir-ground-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { StairLineOff = true, StairsHalfLife = 0.12f }.Save(path);
            string text = File.ReadAllText(path);
            Assert.Contains("\"stairLineOff\": true", text);
            Assert.Contains("\"stairsHalfLife\": 0.12", text);
            var back = PlatformSettings.Load(path);
            Assert.Equal((true, false, 0.12f), (back.StairLineOff, back.SlopeClipsOff, back.StairsHalfLife));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_note_has_the_ground_rules_off_and_the_note_carries_the_numbers()
    {
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(new TapeHeader());
        Assert.Equal(PersonGroundSettings.Off, o.PersonGround);
        var head = new TapeHeader();
        var given = new PersonGroundSettings { LineHalfLife = 0.08f, Slopes = false };
        head.Notes[SessionOptions.GroundNote] = given.Note();
        var n = new SessionOptions { Level = "", PersonGround = PersonGroundSettings.Off, Say = _ => { } }.From(head);
        Assert.Equal(given.Note(), n.PersonGround.Note());
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-person-ground-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, string body) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = body, ModelsFolder = models, Say = _ => { },
        PersonStarts = PersonStartSettings.Off,   // the starts and stops change the player's capsule; these are the ground's tests
    };

    /// <summary>A walk ahead (walk held) for <paramref name="seconds"/>, then standing a second.</summary>
    static IEnumerable<ActionFrame> Walk(float seconds)
    {
        int walk = (int)(seconds * World.TicksPerSecond);
        for (int t = 0; t < walk + World.TicksPerSecond; t++)
            yield return new ActionFrame(t < walk ? new Vector2(0f, 1f) : Vector2.Zero, Vector2.Zero, t < walk ? Buttons.Walk : Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
    }

    [Fact]
    public void On_the_stairs_the_capsule_is_as_without_the_line_and_the_drawn_hips_do_not_step()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "stairs-0.20", PeopleBodies.Models);
        using var on = Session.Create(options);
        using var off = Session.Create(options with { PersonGround = PersonGroundSettings.Off });
        Assert.NotNull(on.PlayerBody);
        var assets = on.World.Service<CharacterAssets>();
        int pelvis = assets.Kind(on.World.Get<CharacterAnimation>(on.Player).Kind).Person!.Rig.Pelvis;
        var z = new List<(float Capsule, float Drawn, float Old)>();
        foreach (var frame in Walk(12f))
        {
            on.Step(frame);
            off.Step(frame);
            var a = on.World.Get<Body>(on.Player);
            var b = off.World.Get<Body>(off.Player);
            Assert.Equal(b.Feet, a.Feet);         // the simulation's place is not changed
            Assert.Equal(b.Velocity, a.Velocity);
            z.Add((a.Feet.Z, Drawn(on, pelvis), Drawn(off, pelvis)));
        }
        Assert.True(z.Max(p => p.Capsule) > 1f, "the walk went up the flight");
        // Over the flight (and a quarter second either side of it), at the walk's pace.
        int first = z.FindIndex(p => p.Capsule > 0.05f) - 30, last = z.FindLastIndex(p => p.Capsule > 0.05f) + 30;
        float most = 0f, mostOld = 0f, dt = World.TickSeconds;
        for (int i = Math.Max(1, first); i < Math.Min(z.Count - 1, last); i++)
        {
            most = MathF.Max(most, MathF.Abs(z[i + 1].Drawn - 2f * z[i].Drawn + z[i - 1].Drawn) / (dt * dt));
            mostOld = MathF.Max(mostOld, MathF.Abs(z[i + 1].Old - 2f * z[i].Old + z[i - 1].Old) / (dt * dt));
        }
        output.WriteLine($"stairs-0.20 walked up and down: the drawn pelvis's most vertical acceleration on the flight {most:0} m/s² (the line off's {mostOld:0})");
        Assert.InRange(most, 0f, 200f);
        Assert.True(mostOld > 1000f);
        // Standing a second after: back on the capsule.
        var g = on.World.Get<PersonGround>(on.Player);
        Assert.InRange(g.Line, -0.005f, 0.005f);

        static float Drawn(Session s, int bone)
        {
            var body = s.World.Get<Body>(s.Player);
            var kind = s.World.Service<CharacterAssets>().Kind(s.World.Get<CharacterAnimation>(s.Player).Kind);
            return Vector3.Transform(s.World.Service<CharacterPoses>().Bones(s.World, s.Player)[bone].Translation, kind.Placement(body.Feet, body.Yaw)).Z;
        }
    }

    [Fact]
    public void Christof_on_the_stairs_has_no_store_and_digests_as_before()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "stairs-0.20", PeopleBodies.OneOne);
        using var on = Session.Create(options);
        using var off = Session.Create(options with { PersonGround = PersonGroundSettings.Off });
        Assert.Null(on.PlayerBody);
        Assert.False(on.World.TryStore<PersonGround>(out _));
        int compared = 0;
        foreach (var frame in Walk(6f))
        {
            on.Step(frame);
            off.Step(frame);
            if (on.Tick % 60 == 0) { Assert.Equal(off.Digest(), on.Digest()); compared++; }
        }
        Assert.True(compared > 5);
        Assert.False(on.TapeHeader("test").Notes.ContainsKey(SessionOptions.GroundNote));
    }

    [Fact]
    public void A_recording_of_the_new_body_notes_the_rules_and_replays_on_them()
    {
        var (course, game, models) = Needs();
        var tuned = On(course, game, models, "slopes-15", PeopleBodies.Models) with { PersonGround = new PersonGroundSettings { LineHalfLife = 0.12f } };
        Tape tape;
        using (var session = Session.Create(tuned))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            foreach (var frame in Walk(9f))
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
            Assert.Equal(1f, session.World.Get<PersonGround>(session.Player).Slope, 1);   // on the ramp, the uphill clip whole
        }
        Assert.Equal(tuned.PersonGround.Note(), tape.Header.Notes[SessionOptions.GroundNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(tuned.PersonGround.Note(), again.PersonGround.Note());
        using var replay = Session.Create(again);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        Assert.True(compared >= 5);
    }
}
