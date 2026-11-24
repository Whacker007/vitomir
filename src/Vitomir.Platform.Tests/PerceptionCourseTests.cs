// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using Vitomir.Platform.Perception;
using Vitomir.Platform.Space;
using static Vitomir.Platform.Tests.PerceptionTests;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The witness check on the proving ground (found by <c>VITOMIR_COURSE_LEVEL</c>, skipped
/// without it): a neonate unbound in public leaps (a full charge, 1 m) in view of a guard of the enhanced module's kind —
/// in the start square at night 8 m off, alarmed, and it runs over and challenges; 25 m off in the dark, aware at most;
/// behind the grab station's 3 m block, nothing; in the free yard, nothing (no risk is raised there); in the start
/// square's bush (conceal 0.2) at night 5 m off, nothing, and 1.5 m off, seen; by day in the open 20 m off, seen.
/// </summary>
public sealed class PerceptionCourseTests
{
    static readonly Lazy<(LevelSpace Level, LevelAreas Areas)?> Course = new(() =>
    {
        var path = Environment.GetEnvironmentVariable("VITOMIR_COURSE_LEVEL");
        if (string.IsNullOrEmpty(path) || !File.Exists(Path.Combine(path, "level.json"))) return null;
        var areas = LevelAreas.Open(path);
        // A course cooked earlier has no bush: the same one is laid over it here.
        if (areas.Conceals == 0) areas = LevelAreas.Of([], conceals: [(new TurnedBox(new(-29, 14, -1), 0, new(3, 3, 4)), 0.2f)]);
        return (LevelSpace.Open(path), areas);
    });

    /// <summary>The highest rung a guard at (<paramref name="gx"/>, <paramref name="gy"/>), facing the neonate, comes to when it leaps at <paramref name="at"/>; and the guard's reactions.</summary>
    static (Awareness Highest, List<Reaction> Reactions) Check(Vector2 at, float gx, float gy, bool night = true, bool risk = true)
    {
        Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        var (level, areas) = Course.Value!.Value;
        var r = Make(level, new(at.X, at.Y, 0.02f), areas, night);
        var g = Facing(r, "guard", gx, gy, 0.02f);
        Leap(r, after: 7f, risk);   // alarmed about 4 s after the leap, then the run and the challenge
        return (Highest(r, g), [.. r.Reactions.Where(x => x.Who == g).Select(x => x.Doing)]);
    }

    [Fact]
    public void A_leap_in_full_view_of_a_guard_8_m_off_at_night_alarms_it_and_it_runs_over_and_challenges()
    {
        var (highest, reactions) = Check(new(-20, 0), -12, 0);
        Assert.Equal(Awareness.Alarmed, highest);
        Assert.Contains(Reaction.Challenge, reactions);
    }

    [Fact]
    public void A_challenging_guard_walks_after_the_neonate_within_reach_and_runs_again_only_beyond_10_m()
    {
        Assert.SkipUnless(Course.Value is not null, "no proving ground (VITOMIR_COURSE_LEVEL: a cooked proving-ground.l2)");
        var (level, areas) = Course.Value!.Value;
        var r = Make(level, new(-20, 0, 0.02f), areas, night: false);
        var g = Facing(r, "guard", -16, 0, 0.02f);
        Leap(r, after: 0.5f);
        Until(r, g, Awareness.Alarmed);
        Run(r, 2 * Platform.Core.World.TicksPerSecond, Platform.Control.ActionFrame.Idle);
        Assert.Equal(Reaction.Challenge, r.W.Get<Observer>(g).Doing);
        // Kept inside the start square (x -28 to -12, y -12 to 12), clear of the bush and the dark corner.
        float walked = Challenged(r, g, 6f, v => new Vector2(Math.Clamp(v.X, -28f, -12f), Math.Clamp(v.Y, -12f, 12f)),
            [new(-28, -12), new(-12, -12), new(-12, 12), new(-28, 12)]);
        Assert.True(walked > 10f, $"it walked {walked:0.0} m after them");
    }

    [Fact]
    public void In_the_dark_25_m_off_a_guard_is_aware_at_most_and_behind_a_3_m_block_or_in_the_free_yard_it_sees_nothing()
    {
        Assert.True(Check(new(-20, 0), -20, -25).Highest <= Awareness.Aware);
        Assert.Equal(Awareness.Unaware, Check(new(-24, 47.5f), -16, 47.5f).Highest);   // the grab station's 3.0 m block between
        Assert.Equal(Awareness.Unaware, Check(new(24, -3), 32, -3, risk: false).Highest);   // the free yard: no risk raised
    }

    [Fact]
    public void In_the_bush_at_night_a_guard_5_m_off_sees_nothing_and_one_1_5_m_off_sees_and_by_day_one_20_m_off_in_the_open_sees()
    {
        Assert.Equal(Awareness.Unaware, Check(new(-27.5f, 15.5f), -22.5f, 15.5f).Highest);
        Assert.True(Check(new(-27.5f, 15.5f), -26f, 15.5f).Highest >= Awareness.Aware);
        Assert.True(Check(new(-20, 0), -20, -20, night: false).Highest >= Awareness.Aware);
    }
}
