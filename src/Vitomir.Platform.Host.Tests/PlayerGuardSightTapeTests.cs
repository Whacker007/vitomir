// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Combat;
using Vitomir.Platform.Control;
using Vitomir.Platform.Core;
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// The player's guard sees by the player's view (by design): a session where the player
/// holds block, turning the view, while a guard in front swings at him on a schedule — recorded as a tape and replayed from the
/// tape's head as <c>--replay-check</c> replays it, every digest the same (the rule reads only the simulation's view: the rig's
/// yaw, orbit and pitch, never the field-of-view setting). Skips without the proving ground and the 1.1 install.
/// </summary>
public sealed class PlayerGuardSightTapeTests
{
    static SessionOptions Options()
    {
        var course = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        var game = GameInstall.Locate();
        Assert.SkipUnless(course is not null && File.Exists(Path.Combine(course, "level.json")), "no proving ground (VITOMIR_COURSE_LEVEL)");
        Assert.SkipUnless(game is not null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        Environment.SetEnvironmentVariable("VITOMIR_USERDATA", Path.Combine(Path.GetTempPath(), "vitomir-guard-sight-tests-userdata"));
        return new SessionOptions
        {
            Level = course!, Game = game, Switches = new HashSet<string>(StringComparer.Ordinal) { "sandbox" }, Seed = 7, People = 0,
            Place = "40,3,0,0", Witnesses = ["guard@41.3,3,0,180"],
            NpcActions = "Guard (witness):attack@0.6,Guard (witness):attack@1.8,Guard (witness):attack@3,Guard (witness):attack@4.2",
        };
    }

    [Fact]
    public void A_tape_of_the_players_guard_against_a_scheduled_blow_replays_digest_for_digest()
    {
        var options = Options();
        var tape = new Tape();
        int parries = 0, seen = 0, raised = 0;
        using (var session = Session.Create(options))
        {
            tape = new Tape { Header = session.TapeHeader("test") };
            int ticks = 6 * World.TicksPerSecond;
            for (int t = 0; t < ticks; t++)
            {
                // Block held from 0.3 s; pressed again each second (a parry window, whose threat the event names); the view
                // turning left and right by turns, so the threat goes in and out of the player's sight.
                bool block = t >= World.TicksPerSecond * 3 / 10 && t % World.TicksPerSecond != 0;
                float turn = (t / World.TicksPerSecond) % 2 == 0 ? 0.01f : -0.01f;
                var frame = new ActionFrame(System.Numerics.Vector2.Zero, new System.Numerics.Vector2(turn, 0f), block ? Buttons.Block : Buttons.None, new System.Numerics.Vector2(0.5f, 0.5f));
                session.Step(frame);
                tape.Frames.Add(frame);
                if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
                var w = session.World;
                foreach (var p in w.Events<ParryBegun>().ThisTick) { parries++; if (!p.Threat.IsNone) seen++; }
                raised += w.Events<BlockRaised>().ThisTick.Count;
            }
            tape.Digests[session.Tick] = session.Digest();
        }
        Assert.True(raised > 0, "the player raised his block");
        Assert.True(parries > 0, "the presses opened parry windows (each names the threat it turned to, if one was seen)");
        // As --replay-check does: the options from the tape's head, a fresh session, every digest compared.
        var again = new SessionOptions { Level = "", Game = GameInstall.Locate() }.From(tape.Header);
        using var replay = Session.Create(again);
        int compared = 0;
        foreach (var frame in tape.Frames)
        {
            replay.Step(frame);
            if (tape.Digests.TryGetValue(replay.Tick, out ulong want)) { Assert.Equal(want, replay.Digest()); compared++; }
        }
        Assert.True(compared >= 20, $"{compared} digests compared");
        Assert.True(seen > 0, $"of {parries} parry windows, {seen} named a threat the player's view saw");
    }
}
