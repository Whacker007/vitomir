// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The scripts' own random stream, Math.random(), given a seed.

namespace Vitomir.Scripting;

/// <summary>
/// **The scripts' <c>Math.random()</c>, seeded**. The game's Java scripts roll with the Java
/// runtime's own generator — 257 calls in the shipped Codex: a discipline's fizzle
/// (<c>Discipline.Fizzled</c>, <c>(5 − level) / 100</c>), a pedestrian's wander, a stinger's sound, a
/// torchbearer's pause. No native is involved; in 1.1 it is whatever JVM the game ran the scripts
/// in, whose <c>java.util.Random</c> seeds itself from the clock and runs on for as long as the
/// program does. Vitomir's scripts run on IKVM's Java 8 runtime, which does the same: one generator,
/// <c>java.lang.Math$RandomNumberGeneratorHolder.randomNumberGenerator</c>, made on the first call
/// from the clock, for the whole process — across every load, since the runtime's class loader is
/// rebuilt but <c>java.lang.Math</c> is the runtime's own.
///
/// That was already 1.1's behaviour, and a game keeps it: it only carries on. What it was not was
/// **repeatable** — a test that cast a discipline failed one run in twenty when the cast fizzled
/// (<c>SaveScriptTests</c>) — nor covered by the launch's seed. So the program seeds it
/// once, from <see cref="Engine.Ai.Streams.Scripts"/>, and a test seeds it before each game.
/// </summary>
public static class ScriptRandom
{
    static readonly Lock gate = new();
    static bool seeded;

    /// <summary>The generator <c>Math.random()</c> draws on, made now if no script has rolled yet.</summary>
    static java.util.Random Generator()
    {
        java.lang.Math.random();                                    // makes the holder's generator, if not yet made
        var holder = java.lang.Class.forName("java.lang.Math$RandomNumberGeneratorHolder");
        var field = holder.getDeclaredField("randomNumberGenerator");
        field.setAccessible(true);
        return (java.util.Random)field.get(null);
    }

    /// <summary>Sets <c>Math.random()</c>'s stream to start at <paramref name="seed"/> (<c>java.util.Random.setSeed</c>).</summary>
    public static void Seed(long seed)
    {
        lock (gate) { Generator().setSeed(seed); seeded = true; }
    }

    /// <summary>
    /// Seeds it the first time only, so the stream carries on through every later run of the
    /// process — a load, a new game — as the launch's other streams do.
    /// </summary>
    public static void Once(long seed)
    {
        lock (gate) { if (seeded) return; Generator().setSeed(seed); seeded = true; }
    }

    /// <summary>The next <c>Math.random()</c>, as a script would draw it (for tests).</summary>
    public static double Next() => java.lang.Math.random();
}
