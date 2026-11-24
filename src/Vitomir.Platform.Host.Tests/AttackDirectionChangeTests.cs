// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The attack direction's mode applied the moment it is changed (<i>"I set the movement to
/// keys, and I noticed that if I was just running around after killing the guard, I could only swing right to left"</i> — an earlier build took
/// the setting once at the run's start). The Controls page's change rides on the next live frame (<see cref="ActionFrame.AttackMode"/>):
/// the session applies it before that tick and logs it, a tape records it at that tick (version 3) and a replay changes it there.
/// The session's test skips without the proving ground and the 1.1 install.
/// </summary>
public sealed class AttackDirectionChangeTests
{
    [Fact]
    public void The_page_asks_for_a_change_only_when_its_mode_differs_from_the_run_s()
    {
        Assert.Equal(AttackDirectionMode.Keys, AttackDirectionInput.ChangeTo(AttackDirectionMode.Automatic, "keys"));
        Assert.Equal(AttackDirectionMode.Mouse, AttackDirectionInput.ChangeTo(AttackDirectionMode.Keys, " Mouse "));
        Assert.Null(AttackDirectionInput.ChangeTo(AttackDirectionMode.Keys, "keys"));
        Assert.Null(AttackDirectionInput.ChangeTo(AttackDirectionMode.Keys, "spin"));
        Assert.Null(AttackDirectionInput.ChangeTo(AttackDirectionMode.Keys, null));
    }

    [Fact]
    public void A_tape_keeps_a_change_of_mode_on_its_tick_and_nothing_on_the_others()
    {
        var tape = new Tape { Header = new TapeHeader { Notes = { [AttackDirectionInput.TapeNote] = "automatic" } } };
        for (int t = 0; t < 10; t++)
            tape.Frames.Add(new ActionFrame(new Vector2(0f, t % 2), Vector2.Zero, Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f,
                                            t == 3 ? AttackDirectionMode.Keys : t == 7 ? AttackDirectionMode.Mouse : null));
        tape.Digests[10] = 42;
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-ch-control-{Environment.ProcessId}.tape");
        tape.Write(path);
        var back = Tape.Read(path);
        File.Delete(path);
        Assert.Equal(3, back.Header.Version);
        Assert.Equal(tape.Frames, back.Frames);
        Assert.Equal([3, 7], back.Frames.Select((f, i) => (f, i)).Where(x => x.f.AttackMode is not null).Select(x => x.i));
        Assert.Equal(42ul, back.Digests[10]);
    }

    static SessionOptions Options(List<string> log)
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-ch-control-tests-userdata"));
        // As in a play-test: the setting automatic at the start (the earlier build's default then), no foe in front.
        return new SessionOptions
        {
            Level = course!, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" }, Seed = 7, People = 0,
            Place = "40,3,0,0", AttackDirection = new AttackDirectionInput { Mode = AttackDirectionMode.Automatic }, Say = log.Add,
        };
    }

    /// <summary>
    /// The frames of the run: two blows pressed with W held (forward) and let go — the first under automatic, then the page's change
    /// to keys on its own tick, then the second.
    /// </summary>
    static List<ActionFrame> Frames(out int changeAt)
    {
        var frames = new List<ActionFrame>();
        int s = World.TicksPerSecond;
        changeAt = 2 * s;
        for (int t = 0; t < 4 * s; t++)
        {
            bool attack = (t >= s / 2 && t < s / 2 + 6) || (t >= changeAt + s / 2 && t < changeAt + s / 2 + 6);
            var move = (t >= s / 2 - 10 && t < s / 2 + 10) || (t >= changeAt + s / 2 - 10 && t < changeAt + s / 2 + 10) ? new Vector2(0f, 1f) : Vector2.Zero;
            frames.Add(new ActionFrame(move, Vector2.Zero, attack ? Buttons.Attack : Buttons.None, new Vector2(0.5f, 0.5f), 16f / 9f,
                                       t == changeAt ? AttackDirectionMode.Keys : null));
        }
        return frames;
    }

    [Fact]
    public void Keys_set_on_the_page_mid_run_make_W_at_the_next_press_an_overhead_and_the_tape_replays()
    {
        var log = new List<string>();
        var options = Options(log);
        var frames = Frames(out int changeAt);
        var tape = new Tape();
        var blows = new List<(long Tick, AttackDirection Direction)>();
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            Assert.Equal(AttackDirectionMode.Automatic, session.AttackDirectionNow);
            foreach (var frame in frames)
            {
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                foreach (var b in session.World.Events<AttackBegun>().ThisTick)
                    if (b.Entity == session.Player) blows.Add((b.Tick, b.Direction));
            }
            tape.Digests[session.Tick] = session.Digest();
            Assert.Equal(AttackDirectionMode.Keys, session.AttackDirectionNow);
        }
        // Before the change, W did not choose (automatic, no one in front: the forehand, from the right); after it, W is the overhead.
        Assert.Equal(2, blows.Count);
        Assert.True(blows[0].Tick < changeAt && blows[1].Tick > changeAt);
        Assert.Equal(AttackDirection.Right, blows[0].Direction);
        Assert.Equal(AttackDirection.Overhead, blows[1].Direction);
        Assert.Single(log, l => l.EndsWith("attack direction: keys (from the next blow)", StringComparison.Ordinal));
        Assert.Equal("automatic", tape.Header.Notes[AttackDirectionInput.TapeNote]);   // the head keeps the run's start

        // Written and read back (the mode's record at its tick), then replayed as --replay-check does: the options from the head.
        var path = Path.Combine(Path.GetTempPath(), $"vitomir-ch-control-change-{Environment.ProcessId}.tape");
        tape.Write(path);
        var back = Tape.Read(path);
        File.Delete(path);
        Assert.Equal(frames, back.Frames);
        var again = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(back.Header);
        using var replay = Session.Create(again);
        int compared = 0;
        var replayed = new List<AttackDirection>();
        foreach (var frame in back.Frames)
        {
            replay.Step(frame);
            foreach (var b in replay.World.Events<AttackBegun>().ThisTick)
                if (b.Entity == replay.Player) replayed.Add(b.Direction);
            if (back.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        Assert.True(compared >= 10, $"{compared} digests compared");
        Assert.Equal([AttackDirection.Right, AttackDirection.Overhead], replayed);
    }
}
