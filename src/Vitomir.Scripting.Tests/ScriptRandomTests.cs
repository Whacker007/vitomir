// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Scripting.Tests;

/// <summary>
/// The scripts' <c>Math.random()</c> can be given a seed (<see cref="ScriptRandom"/>): the
/// launch's for a game, the fixture's for a test. It is the Java runtime's own generator, reached by
/// reflection, so these pin that the reach works on IKVM's runtime and that the stream is Java's.
/// </summary>
[Collection("scripts")]
public class ScriptRandomTests(Scripts s)
{
    [Fact]
    public void A_seed_starts_the_scripts_stream_where_java_s_own_generator_starts()
    {
        s.Ready();
        ScriptRandom.Seed(42);
        var java = new java.util.Random(42);
        for (int i = 0; i < 5; i++) Assert.Equal(java.nextDouble(), ScriptRandom.Next());
    }

    [Fact]
    public void Every_test_starts_the_scripts_stream_at_the_fixture_s_seed()
    {
        s.Ready();
        double first = ScriptRandom.Next();
        ScriptRandom.Next();
        s.Ready();
        Assert.Equal(first, ScriptRandom.Next());
        Assert.Equal(new java.util.Random(Scripts.RandomSeed).nextDouble(), first);
    }

    /// <summary>The program's seeding happens once: a later run of the process (a load, a new game) carries on.</summary>
    [Fact]
    public void Once_does_not_start_a_stream_that_is_already_running_over()
    {
        s.Ready();
        double expected = new java.util.Random(Scripts.RandomSeed).nextDouble();
        ScriptRandom.Once(99);
        Assert.Equal(expected, ScriptRandom.Next());
    }
}
