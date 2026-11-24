// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Presentation;

namespace Vitomir.Platform.Tests;

/// <summary>
/// The combat log (<c>Presentation/CombatLog.cs</c>): it takes the session's lines about the fight and no other,
/// keeps the last <see cref="CombatLog.Capacity"/> of them newest first, fades a line after its hold to the floor, and takes and
/// reads lines without allocating.
/// </summary>
public sealed class CombatLogTests
{
    static readonly CombatLogSettings Defaults = new();

    [Theory]
    [InlineData("1.06 s: guard: Christof (e0)'s blow (contact #126000) met Guard (witness, e9)'s shield: held", true)]
    [InlineData("1.06 s: contact 126000: Christof (e0)'s blow #1 meets Guard (witness, e9)'s shield", true)]
    [InlineData("1.06 s: contact: Christof (e0)'s blow #1: the blade rebounds off Guard (witness, e9)'s shield", true)]
    [InlineData("1.06 s: fight: Christof (e0) stunned 0.25 s (blocked)", true)]
    [InlineData("2.10 s: harm (quick): Christof (e0) on Guard (e9): cut, 12 Health", true)]
    [InlineData("2.10 s: dodge refused: Christof (e0) — mid-swing", true)]
    [InlineData("2.10 s: power: Christof (e0): Celerity 3 used", true)]
    [InlineData("2.10 s: stop: Guard (e9) — at zero Health: a harness stop (ENGINE_SANDBOX), not a death", true)]
    [InlineData("2.10 s: stagger: Guard (e9) staggers 0.30 s", true)]
    [InlineData("2.10 s: Christof (e0)'s extra blow 2 of 3 missed: Guard (e9) was out of the blade's reach (moved 0.40 m)", true)]
    [InlineData("2.10 s: stop: at 40.00 3.00 0.00 (at a run), a wall", false)]
    [InlineData("2.10 s: move: a lip held at 1.20 m", false)]
    [InlineData("2.10 s: seen: Guard (e9): unaware → alerted, a blow", false)]
    [InlineData("2.10 s: pointer: talk — Guard (the reticle)", false)]
    [InlineData("2.10 s: swing arc: kept as predicted now: quick", false)]
    [InlineData("2.10 s: guardian angel: not a topic", false)]
    [InlineData("", false)]
    public void It_takes_the_lines_about_the_fight_and_no_other(string line, bool wanted) =>
        Assert.Equal(wanted, CombatLog.Wanted(line, Defaults.Topics));

    [Fact]
    public void It_keeps_the_last_lines_newest_first()
    {
        var log = new CombatLog();
        for (int i = 0; i < CombatLog.Capacity + 10; i++) log.Add($"{i}.00 s: fight: line {i}", i);
        Assert.Equal(CombatLog.Capacity, log.Count);
        Assert.Equal(CombatLog.Capacity + 10, log.Total);
        int newest = CombatLog.Capacity + 9;
        Assert.Equal($"{newest}.00 s: fight: line {newest}", log.Line(0));
        Assert.Equal(newest - 1, log.SaidAt(1));
        Assert.Equal(newest, log.Order(0));
        Assert.Equal("10.00 s: fight: line 10", log.Line(CombatLog.Capacity - 1));
        Assert.Equal("", log.Line(CombatLog.Capacity));
        Assert.False(log.Offer("3.00 s: move: a hop", 3));
        Assert.True(log.Offer("3.00 s: guard: held", 3));
        Assert.Equal("3.00 s: guard: held", log.Line(0));
        log.Clear();
        Assert.Equal(0, log.Count);
        Assert.False(log.Shown(0));
    }

    [Fact]
    public void A_line_is_full_for_its_hold_then_fades_to_the_floor()
    {
        var log = new CombatLog { Settings = new CombatLogSettings { HoldSeconds = 6f, FadeSeconds = 6f, Floor = 0.4f } };
        log.Add("1.00 s: guard: held", 1.0);
        Assert.Equal(1f, log.Strength(0, 1.0));
        Assert.Equal(1f, log.Strength(0, 7.0));
        Assert.Equal(0.7f, log.Strength(0, 10.0), 4);
        Assert.Equal(0.4f, log.Strength(0, 13.0), 4);
        Assert.Equal(0.4f, log.Strength(0, 100.0), 4);
        Assert.True(log.Shown(100.0));
        log.Settings = log.Settings with { Floor = 0f };
        Assert.Equal(0f, log.Strength(0, 100.0));
        Assert.False(log.Shown(100.0));
        Assert.Equal(0f, log.Strength(1, 1.0));
    }

    [Fact]
    public void Taking_and_reading_lines_allocates_nothing()
    {
        var log = new CombatLog();
        var lines = new string[200];
        for (int i = 0; i < lines.Length; i++) lines[i] = i % 3 == 0 ? $"{i}.00 s: move: step {i}" : $"{i}.00 s: guard: held {i}";
        for (int i = 0; i < 100; i++) log.Offer(lines[i], i);       // warmed
        // Measured as the allocation tests measure (Allocations.None: the window measured more than once).
        Allocations.None("taking and reading 100 lines", () =>
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            float sum = 0f;
            for (int i = 100; i < 200; i++)
            {
                log.Offer(lines[i], i);
                for (int age = 0; age < 10; age++) sum += log.Strength(age, i) + log.Line(age).Length + log.Order(age);
            }
            long spent = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(sum > 0f);
            return spent;
        });
    }
}
