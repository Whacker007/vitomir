// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The game remembers the last camera view between runs, added
/// where it makes sense: play starts in the view the last session was left in (the setting <c>lastView</c>; <c>--view</c> for one
/// run), a tape's head notes it when it is not the right shoulder, and a replay starts as its tape did. The session's test skips
/// without the proving ground and the 1.1 install.
/// </summary>
public sealed class StartViewTests
{
    [Fact]
    public void The_three_views_have_names_and_the_view_key_goes_on_from_any_of_them()
    {
        var rig = CameraRig.Facing(0.5f);
        Assert.Equal("right", CameraRig.ViewName(rig));
        Assert.Equal("left", CameraRig.ViewName(CameraRig.NextView(rig)));
        Assert.Equal("first", CameraRig.ViewName(CameraRig.NextView(CameraRig.NextView(rig))));
        foreach (var name in new[] { "first", "right", "left" })
        {
            Assert.True(CameraRig.TryInView(rig, name, out var inView));
            Assert.Equal(name, CameraRig.ViewName(inView));
            Assert.Equal(rig.Yaw, inView.Yaw);
        }
        Assert.True(CameraRig.TryInView(rig, "left", out var left));
        Assert.Equal("first", CameraRig.ViewName(CameraRig.NextView(left)));
        Assert.False(CameraRig.TryInView(rig, "behind", out _));
        Assert.False(CameraRig.TryInView(rig, null, out _));
    }

    [Fact]
    public void A_head_notes_a_start_view_only_when_it_is_not_the_right_shoulder_and_gives_it_back()
    {
        Assert.False(new TapeHeader().Viewed(null).Notes.ContainsKey(SessionOptions.StartViewNote));
        Assert.False(new TapeHeader().Viewed("right").Notes.ContainsKey(SessionOptions.StartViewNote));
        var head = new TapeHeader().Viewed("left");
        Assert.Equal("left", head.Notes[SessionOptions.StartViewNote]);
        Assert.Equal("left", new SessionOptions { Level = "", StartView = "first" }.From(head).StartView);
        Assert.Null(new SessionOptions { Level = "", StartView = "first" }.From(new TapeHeader()).StartView);   // a tape from before: the right shoulder
    }

    [Fact]
    public void The_setting_is_the_right_shoulder_by_default_and_keeps_the_view_it_is_given()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vitomir-start-view-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal("right", new PlatformSettings().LastView);
            string path = Path.Combine(dir, "settings.json");
            var settings = new PlatformSettings { LastView = "first" };
            settings.Save(path);
            Assert.Equal("first", PlatformSettings.Load(path).LastView);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    static SessionOptions Options(List<string> log, string? view)
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-start-view-tests-userdata"));
        return new SessionOptions
        {
            Level = course!, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" }, Seed = 7, People = 0,
            Place = "40,3,0,0", StartView = view, Say = log.Add,
        };
    }

    [Fact]
    public void Play_starts_in_the_view_given_and_its_tape_replays_from_there()
    {
        var log = new List<string>();
        using (var plain = Session.Create(Options(log, null)))
            Assert.Equal("right", plain.PlayerView);
        Assert.DoesNotContain(log, l => l.StartsWith("view: ", StringComparison.Ordinal));

        log.Clear();
        var tape = new Tape();
        int s = World.TicksPerSecond;
        using (var session = Session.Create(Options(log, "left")))
        {
            Assert.Equal("left", session.PlayerView);
            Assert.Single(log, l => l == "view: starts over the left shoulder (the view key goes on from there)");
            tape = new Tape { Header = session.TapeHeader("test") };
            // A walk forward, a press of the view key at 1 s (left → first person), on to 2 s.
            for (int t = 0; t < 2 * s; t++)
            {
                var frame = new ActionFrame(new Vector2(0f, 1f), Vector2.Zero, t == s ? Buttons.SwitchView : Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f);
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
            }
            Assert.Equal("first", session.PlayerView);
        }
        Assert.Equal("left", tape.Header.Notes[SessionOptions.StartViewNote]);

        var again = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(tape.Header);
        Assert.Equal("left", again.StartView);
        using var replay = Session.Create(again);
        Assert.Equal("left", replay.PlayerView);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        Assert.True(compared >= 3, $"{compared} digests compared");
        Assert.Equal("first", replay.PlayerView);
    }
}
