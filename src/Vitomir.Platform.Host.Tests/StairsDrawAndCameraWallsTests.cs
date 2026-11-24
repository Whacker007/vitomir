// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Animation.People;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Bodies;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// (<i>"See the graphical anomalies when I go up and down stairs ... Body is jittering and popping"</i>, and
/// <i>"there's some camera insanity if I hit a wall at an odd angle"</i>). The picture between two ticks draws a person on the stairs on
/// the stair line, not on the capsule (the body dropped by the rest of a riser for a frame at every step, at any screen rate that is
/// not the tick's); the settings and flags (<c>stairsDrawLastTick</c>, <c>cameraHardWalls</c>); the camera's note <c>cameraWalls</c> on a recording
/// and a recording replayed on it. Skipped without the proving ground, the 1.1 install or the models repository's files.
/// </summary>
public sealed class StairsDrawAndCameraWallsTests(ITestOutputHelper output)
{
    [Fact]
    public void The_settings_keep_their_keys_and_a_tape_without_the_note_had_the_camera_with_hard_walls()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-cp-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "settings.json");
            new PlatformSettings { StairsDrawLastTick = true, CameraHardWalls = true }.Save(path);
            string text = File.ReadAllText(path);
            Assert.Contains("\"stairsDrawLastTick\": true", text);
            Assert.Contains("\"cameraHardWalls\": true", text);
            var back = PlatformSettings.Load(path);
            Assert.True(back.StairsDrawLastTick && back.CameraHardWalls);
            Assert.False(new PlatformSettings().StairsDrawLastTick || new PlatformSettings().CameraHardWalls);
        }
        finally { Directory.Delete(dir, recursive: true); }

        var none = new SessionOptions { Level = "", Say = _ => { } }.From(new TapeHeader());
        Assert.True(none.CameraHardWalls);
        var head = new TapeHeader();
        head.Notes[SessionOptions.CameraWallsNote] = "calm";
        Assert.False(new SessionOptions { Level = "", CameraHardWalls = true, Say = _ => { } }.From(head).CameraHardWalls);
    }

    [Fact]
    public void On_the_stairs_the_body_drawn_between_ticks_does_not_drop_a_riser()
    {
        var (course, game, models) = Needs();
        var options = On(course, game, models, "stairs-0.20", PeopleBodies.Models);
        using var now = Session.Create(options);
        using var old = Session.Create(options with { StairsDrawLastTick = true });
        var drawn = new List<(float Now, float Old, float Capsule)>();
        var view = new FrameView();
        float[] alphas = [0.25f, 0.5f, 0.75f, 1f];
        foreach (var frame in Run(6f))
        {
            now.Step(frame);
            old.Step(frame);
            foreach (float a in alphas) drawn.Add((Height(now, view, a), Height(old, view, a), now.World.Get<Body>(now.Player).Feet.Z));
        }
        Assert.True(drawn.Max(d => d.Capsule) > 1f, "the run went up the flight");
        // The drawn line's height picture to picture (four pictures a tick, as a 480 Hz screen): its bend at each picture.
        float most = 0f, mostOld = 0f;
        for (int i = 1; i < drawn.Count - 1; i++)
        {
            most = MathF.Max(most, MathF.Abs(drawn[i + 1].Now - 2f * drawn[i].Now + drawn[i - 1].Now));
            mostOld = MathF.Max(mostOld, MathF.Abs(drawn[i + 1].Old - 2f * drawn[i].Old + drawn[i - 1].Old));
        }
        output.WriteLine($"stairs-0.20 run up, four pictures a tick: the drawn body's largest kink {most * 100f:0.0} cm (the last tick's {mostOld * 100f:0.0} cm)");
        Assert.InRange(most, 0f, 0.01f);
        Assert.True(mostOld > 0.1f, $"the last tick's drop is reproduced ({mostOld:0.000} m)");

        // The drawn place's height plus the stair line, where the pose stands the hips (m).
        static float Height(Session s, FrameView view, float alpha)
        {
            s.Fill(view, alpha);
            var mine = view.Characters.First(c => c.Entity == s.Player.Packed);
            ref var g = ref s.World.Store<PersonGround>().Get(s.Player);
            return mine.World.M43 + (g.Stairs ? g.Line : 0f);
        }
    }

    [Fact]
    public void A_recording_of_the_new_body_notes_the_camera_and_replays_on_it()
    {
        var (course, game, models) = Needs();
        // Into the vault station's block and along it, the camera at its wall.
        var options = On(course, game, models, "vault", PeopleBodies.Models);
        Tape tape;
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            foreach (var frame in Run(4f, turn: 0.004f))
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
        }
        Assert.Equal("calm", tape.Header.Notes[SessionOptions.CameraWallsNote]);
        var again = new SessionOptions { Level = course, Game = game, ModelsFolder = models, Say = _ => { } }.From(tape.Header);
        Assert.False(again.CameraHardWalls);
        using var replay = Session.Create(again);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        Assert.True(compared >= 3);

        using var older = Session.Create(options with { CameraHardWalls = true });
        Assert.Equal("hard", older.TapeHeader("test").Notes[SessionOptions.CameraWallsNote]);
        using var christof = Session.Create(On(course, game, models, "vault", PeopleBodies.OneOne));
        Assert.False(christof.TapeHeader("test").Notes.ContainsKey(SessionOptions.CameraWallsNote));
    }

    static (string Course, string Game, string Models) Needs()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        var models = Environment.GetEnvironmentVariable(PeopleBodies.EnvironmentVariable);
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Assert.SkipUnless(models is not null && Directory.Exists(models), "the models repository's files are not here (VITOMIR_MODELS_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-cp-tests-userdata"));
        return (course!, game!, models!);
    }

    static SessionOptions On(string course, string game, string models, string start, string body) => new()
    {
        Level = course, Game = game, Switches = new HashSet<string>(["sandbox"], StringComparer.Ordinal), Seed = 5, People = 0,
        Start = start, Body = body, ModelsFolder = models, Say = _ => { },
    };

    /// <summary>A run ahead (the default pace) for <paramref name="seconds"/>, the look turned <paramref name="turn"/> radians a tick, then a second standing.</summary>
    static IEnumerable<ActionFrame> Run(float seconds, float turn = 0f)
    {
        int run = (int)(seconds * World.TicksPerSecond);
        for (int t = 0; t < run + World.TicksPerSecond; t++)
            yield return new ActionFrame(t < run ? new Vector2(0f, 1f) : Vector2.Zero, new Vector2(t < run ? turn : 0f, 0f), Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
    }
}
