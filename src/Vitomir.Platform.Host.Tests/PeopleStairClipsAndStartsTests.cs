// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The models repository's as the program gives it: the stair clips (<c>stairClipsOff</c>, <c>--stair-clips-off</c>, in the note <c>ground</c>),
/// the starts and stops (<c>startClipsOff</c>, <c>crowdStartClipsOff</c>, <c>stopNearest</c>, the note <c>starts</c>) and the skirt (<c>skirtSplit</c>,
/// presentation only). A tape without the notes is the earlier build's. On the proving ground (skip without it, the 1.1 install or the models repository's files): on a
/// set without the clips the world has neither store and digests as with every an earlier build switch on; with them the stair clips come in
/// on <c>stairs-0.20</c> and leave the capsule where it was, and a recording carries the notes and replays digest for digest.
/// </summary>
public sealed class PeopleStairClipsAndStartsTests(ITestOutputHelper output)
{
    [Fact]
    public void The_settings_and_flags_give_the_rules_and_the_clips_off_switches_turn_them_off()
    {
        var plain = new PlatformSettings();
        var s = plain.PersonStartsFor(new Cli([]));
        Assert.Equal((true, true, true), (s.Player, s.Crowd, s.WaitForPhase));
        Assert.False(plain.PersonStartsFor(new Cli(["--start-clips-off"])).Any);
        Assert.Equal((true, false), (plain.PersonStartsFor(new Cli(["--crowd-start-clips-off"])).Player, plain.PersonStartsFor(new Cli(["--crowd-start-clips-off"])).Crowd));
        Assert.False(plain.PersonStartsFor(new Cli(["--stop-nearest"])).WaitForPhase);
        Assert.False(new PlatformSettings { StartClipsOff = true }.PersonStartsFor(new Cli([])).Any);
        Assert.True(plain.PersonGroundFor(new Cli([])).Clips);
        Assert.False(plain.PersonGroundFor(new Cli(["--stair-clips-off"])).Clips);
        Assert.False(new PlatformSettings { StairClipsOff = true }.PersonGroundFor(new Cli([])).Clips);
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-people-stair-clips-and-starts-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { StairClipsOff = true, StartClipsOff = true, CrowdStartClipsOff = true, StopNearest = true, SkirtSplit = true }.Save(path);
            string text = File.ReadAllText(path);
            foreach (var key in new[] { "stairClipsOff", "startClipsOff", "crowdStartClipsOff", "stopNearest", "skirtSplit" }) Assert.Contains($"\"{key}\": true", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.StairClipsOff && back.StartClipsOff && back.CrowdStartClipsOff && back.StopNearest && back.SkirtSplit);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_tape_without_the_notes_has_no_stair_or_start_clips()
    {
        var head = new TapeHeader();
        head.Notes[SessionOptions.GroundNote] = new PersonGroundSettings().WithoutStairClips.Note();   // an older tape's ground note
        var o = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.False(o.PersonStarts.Any);
        Assert.False(o.PersonGround.Clips);
        Assert.True(o.PersonGround.Stairs);
        head.Notes[SessionOptions.GroundNote] = new PersonGroundSettings().Note();
        head.Notes[PersonStartSettings.TapeNote] = new PersonStartSettings { WaitForPhase = false }.Note();
        var n = new SessionOptions { Level = "", Say = _ => { } }.From(head);
        Assert.True(n.PersonGround.Clips);
        Assert.Equal((true, true, false), (n.PersonStarts.Player, n.PersonStarts.Crowd, n.PersonStarts.WaitForPhase));
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-people-stair-clips-and-starts-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = PeopleBodies.Models, ModelsFolder = models, Say = _ => { },
    };

    /// <summary>Walk (or run) ahead for <paramref name="seconds"/>, stand, and go again a little, the walk held or not.</summary>
    static IEnumerable<ActionFrame> Go(float seconds, bool walk)
    {
        int go = (int)(seconds * World.TicksPerSecond);
        for (int t = 0; t < go + 2 * World.TicksPerSecond; t++)
        {
            bool on = t < go || (t > go + World.TicksPerSecond && t < go + World.TicksPerSecond + 40);
            yield return new ActionFrame(on ? new Vector2(0f, 1f) : Vector2.Zero, Vector2.Zero, on && walk ? Buttons.Walk : Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
        }
    }

    [Fact]
    public void Without_the_clips_the_world_is_as_without_them_and_with_them_the_stairs_leave_the_capsule_where_it_was()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "stairs-0.20");
        var clipsOff = options with { PersonStarts = PersonStartSettings.Off, PersonGround = options.PersonGround.WithoutStairClips, SkirtSplit = true };
        using var now = Session.Create(options);
        using var then = Session.Create(clipsOff);
        var person = now.World.Service<CharacterAssets>().Kind(now.World.Get<CharacterAnimation>(now.Player).Kind).Person!;
        bool round36 = person.Stairs is not null && person.Starts is not null;
        Assert.False(then.World.TryStore<PersonFlight>(out _) || then.World.TryStore<PersonStep>(out _));
        if (!round36)
        {
            // Nothing of the earlier build's is in the world, and it digests as the off switches give it.
            Assert.False(now.World.TryStore<PersonFlight>(out _) || now.World.TryStore<PersonStep>(out _));
            int compared = 0;
            foreach (var frame in Go(8f, walk: true))
            {
                now.Step(frame);
                then.Step(frame);
                if (now.Tick % 60 == 0) { Assert.Equal(then.Digest(), now.Digest()); compared++; }
            }
            output.WriteLine($"an older set: {compared} digests the same as the clips off's");
            Assert.True(compared > 5);
            return;
        }
        // The stair clips alone (the starts as before on both, so the capsule is driven the same): the capsule where the earlier build's is, the clips in.
        using var stairs = Session.Create(options with { PersonStarts = PersonStartSettings.Off });
        float most = 0f;
        foreach (var frame in Go(8f, walk: true))
        {
            stairs.Step(frame);
            then.Step(frame);
            Assert.Equal(then.World.Get<Body>(then.Player).Feet, stairs.World.Get<Body>(stairs.Player).Feet);
            most = MathF.Max(most, stairs.World.Get<PersonFlight>(stairs.Player).Share);
        }
        Assert.True(stairs.World.Get<Body>(stairs.Player).Feet.Z > 0.5f, "the walk went up the flight");
        output.WriteLine($"on stairs-0.20: the stair clips' share at most {most:0.00}, the capsule as the clips off's");
        Assert.True(most > 0.99f);
    }

    [Fact]
    public void A_recording_notes_the_clips_and_the_starts_and_replays_on_them()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "stairs-0.20") with { PersonStarts = new PersonStartSettings { Crowd = false } };
        Tape tape;
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            foreach (var frame in Go(6f, walk: false))
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
        }
        Assert.Equal(options.PersonStarts.Note(), tape.Header.Notes[PersonStartSettings.TapeNote]);
        Assert.Equal(options.PersonGround.Note(), tape.Header.Notes[SessionOptions.GroundNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.Equal(options.PersonStarts.Note(), again.PersonStarts.Note());
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
