// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launch's random seed, and the streams that carry on from it for as long as the program runs.

namespace Vitomir.Engine.Ai;

/// <summary>
/// **Every random stream a game draws on, started once when the program starts and carried on
/// through every load, new game and place after that**.
///
/// 1.1 has one generator for everything that rolls, MSVC's <c>rand()</c>. Its state
/// is 1 at start and nothing ever seeds it:
/// 1.1 never calls <c>srand</c>. So 1.1's stream starts at 1 when the program starts and runs on
/// through the AI, the fights, the particles, the sounds and the gore, across every load; what a
/// companion rolls at a given moment depends on how many rolls came before it, which depends on
/// the frame rate and what was on screen — in practice it is never the same twice. The treasure and
/// the shops draw on a second generator (<see cref="Rpg.TreasureRng"/>) that 1.1 does seed, from the
/// clock.
///
/// Earlier, Vitomir gave every place its own streams, each restarted at a fixed number whenever
/// the place was built — the AI's at 1 (<c>--ai-seed</c>) on every load — so a companion did the
/// same thing at the same moment every time a save was loaded. That was a bug: the outcomes
/// should not be fixed; the random numbers are there to vary them.
///
/// So a game now takes **one seed at launch, from the clock** (<see cref="FromClock"/>), and every
/// stream below is made from it once and then only carries on. The seed is written in the log's
/// header (<c># random seed: N</c>), and <c>--seed N</c> starts the same streams again.
///
/// **Ours, where it departs from 1.1's single stream:** the streams are several, one a subsystem,
/// each seeded from the launch's seed (<see cref="Derive"/>) — so the torches on screen do not move a
/// fight's rolls, and a run given the same seed repeats as far as its timing does (exactly, in the
/// headless harness, whose steps are fixed). And with <c>--seed</c> the treasure's clock readings come
/// from the seed as well (<see cref="TreasureClock"/>), so a repeated run fills the same chests; a
/// player's game reads the clock as 1.1 does.
///
/// **Tests and the harness stay repeatable.** Nothing in the engine reads this unless the program
/// has called <see cref="Start"/>: every subsystem's own default (a place's AI at 1, the particles at
/// 1, …) is what a test constructs, as before. And the program's harness runs — <c>--headless</c>,
/// <c>--sweep</c>, <c>--story-sweep</c> — take <see cref="HarnessSeed"/> unless given <c>--seed</c>
/// (<see cref="Choose"/>), and with that seed every stream starts where it always has.
/// </summary>
public static class Chance
{
    /// <summary>The seed a harness run takes when it is not given one: every stream starts where it did earlier.</summary>
    public const int HarnessSeed = 1;

    /// <summary>The launch's streams, or null — a test, or a tool that never started a game — when each subsystem keeps its own.</summary>
    public static Streams? Game { get; private set; }

    /// <summary>Installs the launch's streams. Once for the process: the program's first run.</summary>
    public static Streams Start(Streams streams) => Game = streams;

    /// <summary>Takes them away again — for a test, which must run alone while they are in.</summary>
    public static void Stop() => Game = null;

    /// <summary>
    /// Whether a launch with these flags is the harness, which is repeatable unless told otherwise: a still
    /// (<c>--headless</c>) or a sweep (<c>--sweep</c>, <c>--story-sweep</c>). A program hosting the runs can name more
    /// (its additions' own rule, asked beside this one).
    /// </summary>
    public static bool IsHarness(Func<string, bool> flag) =>
        flag("--headless") || flag("--sweep") || flag("--story-sweep");

    /// <summary>
    /// The seed a launch takes: <c>--seed N</c> when given; else <see cref="HarnessSeed"/> for a
    /// harness run; else the clock's (<see cref="FromClock"/>). <c>--ai-seed M</c> is the AI's part
    /// alone — its stream starts at <c>M</c> (as <c>--ai-seed</c> always started it), once, and then
    /// carries on like the others. A value that does not parse is ignored, as the flags always were.
    /// </summary>
    public static Streams Choose(string? seed, string? aiSeed, bool harness, DateTime now)
    {
        bool given = int.TryParse(seed, out int s);
        int chosen = given ? s : harness ? HarnessSeed : FromClock(now);
        return new Streams(chosen, given ? "--seed" : harness ? "the harness's" : "the clock's",
                           int.TryParse(aiSeed, out int ai) ? ai : null, repeatable: given || harness);
    }

    /// <summary>
    /// A seed from the clock: the ticks' low 31 bits, never 0 — a different number each launch,
    /// short enough to type back after <c>--seed</c>. Ours.
    /// </summary>
    public static int FromClock(DateTime now) => (int)(now.Ticks & 0x7fffffff) is var t && t != 0 ? t : 1;

    /// <summary>
    /// One stream's own seed from the launch's: <paramref name="legacy"/> — the number that stream
    /// always started at — for seed 1, and spread across the numbers for any other
    /// (<c>legacy + (seed − 1) × 0x9e3779b9</c>, the golden ratio's step). Ours.
    /// </summary>
    public static int Derive(int seed, int legacy) => unchecked(legacy + (seed - 1) * (int)0x9e3779b9u);

    /// <summary>
    /// The number the treasure's and the shops' generator is seeded with: the
    /// clock's, as 1.1 reads it — unless the launch was given a seed (or is the harness), when it is
    /// drawn from the seed's own stream, so a repeated run rolls the same chests.
    /// </summary>
    public static uint TreasureClock(DateTime now) =>
        Game is { Repeatable: true } g ? (uint)g.Treasure.Next() << 15 | (uint)g.Treasure.Next() : Rpg.TreasureRng.Clock(now);
}

/// <summary>
/// The streams of one launch, each made once from its seed (<see cref="Chance.Derive"/>) — so the
/// ones a test makes from a seed are the ones a game given <c>--seed</c> that number has.
/// </summary>
public sealed class Streams(int seed, string source = "--seed", int? aiSeed = null, bool repeatable = true)
{
    /// <summary>The launch's seed, as the log's header writes it.</summary>
    public int Seed { get; } = seed;
    /// <summary>Where it came from: the clock's, the harness's, or <c>--seed</c>.</summary>
    public string Source { get; } = source;
    /// <summary>The AI's own start when <c>--ai-seed</c> gave one, else null (derived from <see cref="Seed"/>).</summary>
    public int? AiSeed { get; } = aiSeed;
    /// <summary>Whether a rerun is meant to repeat this one — given a seed, or the harness — and so the clock is not read.</summary>
    public bool Repeatable { get; } = repeatable;

    /// <summary>The AI's rolls, the sounds' picks and the scripts' natives that roll (was: a place's <c>Minds</c>, at <c>--ai-seed</c> or 1, every place and load).</summary>
    public Rng Ai { get; } = new(aiSeed ?? Chance.Derive(seed, 1));
    /// <summary>The fights' rolls (was: every <c>Simulation</c>, at 1).</summary>
    public Rng Combat { get; } = new(Chance.Derive(seed, 1));
    /// <summary>The animation variants (was: every place's <c>MotionSets</c>, at 1).</summary>
    public Rng Motions { get; } = new(Chance.Derive(seed, 1));
    /// <summary>The particles' <c>rand()</c> (was: every place's <c>ParticleSystem</c>, at 1).</summary>
    public Rng Particles { get; } = new(Chance.Derive(seed, 1));
    /// <summary>The particles' steps of the shared generator (was: every place's, at 0x1234).</summary>
    public Rpg.TreasureRng ParticleLcg { get; } = new(unchecked((uint)Chance.Derive(seed, 0x1234)));
    /// <summary>The explosions' rolls (was: every place's, at 1).</summary>
    public Rng Explosions { get; } = new(Chance.Derive(seed, 1));
    /// <summary>The impact marks' turns (was: every place's <c>Decals</c>, at 1).</summary>
    public Rng Decals { get; } = new(Chance.Derive(seed, 1));
    /// <summary>The front end's sound picks before a game's AI exists (was: a new one at 1).</summary>
    public Rng Sounds { get; } = new(Chance.Derive(seed, 1));
    /// <summary>The console's own commands that roll — <c>damageme</c>, <c>gibme</c> (was: the tick count, each command).</summary>
    public Rng Console { get; } = new(Chance.Derive(seed, 1));
    /// <summary>Where the treasure's clock readings come from in a repeatable run (<see cref="Chance.TreasureClock"/>).</summary>
    public Rng Treasure { get; } = new(Chance.Derive(seed, 1));
    /// <summary>The light styles' flicker (ours; was: every place's, at 1).</summary>
    public Random Lights { get; } = new(Chance.Derive(seed, 1));
    /// <summary>A driven character's detour round what blocks it (ours; was: every player, at 20260913).</summary>
    public Random Detours { get; } = new(Chance.Derive(seed, 20260913));
    /// <summary>A network game's <c>/roll</c> dice, one seed a hosted game (was: 1).</summary>
    public Random NetDice { get; } = new(Chance.Derive(seed, 1));
    /// <summary>The seed the scripts' <c>Math.random()</c> is given (was: the Java runtime's own, from the clock).</summary>
    public long Scripts { get; } = Chance.Derive(seed, 1);
}
