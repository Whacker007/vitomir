// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Ai;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.World;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The launch's random seed (<see cref="Chance"/>). 1.1's <c>rand()</c> is never
/// seeded and runs on through everything, so its outcomes vary from play to play; Vitomir restarted
/// the AI's stream at 1 on every load, so a companion did the same thing every time a save was
/// loaded. These pin the new rules: a player's game takes the clock's seed, the harness a fixed one,
/// <c>--seed</c> repeats a run, and with the harness's seed every stream starts where it always did,
/// so the comparisons and the stills made before still hold.
/// </summary>
public class ChanceTests
{
    [Fact]
    public void A_player_s_game_takes_its_seed_from_the_clock_and_a_different_one_each_launch()
    {
        var at = new DateTime(2026, 9, 25, 15, 0, 0, 123);
        var game = Chance.Choose(null, null, harness: false, at);
        Assert.Equal(Chance.FromClock(at), game.Seed);
        Assert.False(game.Repeatable);
        Assert.Equal("the clock's", game.Source);
        Assert.NotEqual(game.Seed, Chance.Choose(null, null, harness: false, at.AddMilliseconds(17)).Seed);
    }

    [Fact]
    public void The_harness_takes_a_fixed_seed_and_seed_repeats_any_run()
    {
        var harness = Chance.Choose(null, null, harness: true, DateTime.Now);
        Assert.Equal(Chance.HarnessSeed, harness.Seed);
        Assert.True(harness.Repeatable);
        var given = Chance.Choose("77", null, harness: false, DateTime.Now);
        Assert.Equal(77, given.Seed);
        Assert.True(given.Repeatable);
        Assert.Equal("--seed", given.Source);
        // Two launches given the same seed draw the same numbers from every stream.
        var again = Chance.Choose("77", null, harness: true, DateTime.Now.AddHours(1));
        Assert.Equal(given.Ai.Next(), again.Ai.Next());
        Assert.Equal(given.Combat.Next(), again.Combat.Next());
        Assert.Equal(given.Lights.Next(), again.Lights.Next());
        Assert.Equal(given.Scripts, again.Scripts);
    }

    [Fact]
    public void With_the_harness_s_seed_every_stream_starts_where_it_always_did()
    {
        var s = new Streams(Chance.HarnessSeed);
        Assert.Equal(new Rng(1).Next(), s.Ai.Next());
        Assert.Equal(new Rng(1).Next(), s.Combat.Next());
        Assert.Equal(new Rng(1).Next(), s.Particles.Next());
        Assert.Equal(0x1234u, s.ParticleLcg.State);
        Assert.Equal(new Random(1).Next(100), s.Lights.Next(100));
        Assert.Equal(new Random(20260913).Next(8), s.Detours.Next(8));
        // And any other seed moves every one of them.
        var other = new Streams(2);
        Assert.NotEqual(new Rng(1).Next(), other.Ai.Next());
        Assert.NotEqual(0x1234u, other.ParticleLcg.State);
    }

    [Fact]
    public void Ai_seed_is_the_AI_s_part_alone()
    {
        var s = Chance.Choose("5", "9", harness: false, DateTime.Now);
        Assert.Equal(9, s.AiSeed);
        Assert.Equal(new Rng(9).Next(), s.Ai.Next());
        Assert.Equal(new Rng(Chance.Derive(5, 1)).Next(), s.Combat.Next());
    }

    /// <summary>A player's game reads the clock for the treasure as 1.1 does; a repeat run draws from its seed.</summary>
    [Fact]
    public void Without_a_game_the_treasure_s_seed_is_the_clock_s()
    {
        var at = new DateTime(2026, 9, 25, 15, 4, 5, 6);
        Assert.Null(Chance.Game);
        Assert.Equal(TreasureRng.Clock(at), Chance.TreasureClock(at));
    }
}

/// <summary>
/// The streams **carry on**: installed for the process, every place built and woken afterwards —
/// a travel, a load, a new game — rolls on from where the last one stopped, rather than starting
/// again at a fixed number. Runs alone, because it installs the process-wide streams for a moment
/// and every other test must keep its own.
/// </summary>
[Collection(nameof(ChanceGameTests))]
public class ChanceGameTests
{
    [CollectionDefinition(nameof(ChanceGameTests), DisableParallelization = true)]
    public sealed class Alone;

    [Fact]
    public void Every_place_and_load_rolls_on_from_the_launch_s_streams()
    {
        using var files = Install.Open();
        var streams = Chance.Start(new Streams(1234));
        try
        {
            var first = Location.Load(files, "P1_MON1", sky: false);
            var a = first.Awaken(new AiClasses(files));
            a.Random.Next();
            // The same place loaded again, as a load of a save does: the AI does not start over.
            var again = Location.Load(files, "P1_MON1", sky: false);
            var b = again.Awaken(new AiClasses(files), fromSave: true);
            Assert.Same(streams.Ai, a.Random);
            Assert.Same(streams.Ai, b.Random);
            Assert.Same(streams.Combat, new Simulation(again).Random);
            Assert.Same(streams.Particles, again.Particles.Rand);
            Assert.Same(streams.ParticleLcg, again.Particles.Lcg);
            Assert.Same(streams.Explosions, again.Explosions.Rand);
            Assert.Same(streams.Decals, again.Decals.Rand);
            Assert.True(streams.Repeatable);
            // A repeatable launch draws the treasure's seed from its own stream, not the clock: the
            // same moment twice gives two numbers.
            var at = new DateTime(2026, 9, 25, 15, 4, 5, 6);
            Assert.NotEqual(Chance.TreasureClock(at), Chance.TreasureClock(at));
        }
        finally { Chance.Stop(); }
    }
}
