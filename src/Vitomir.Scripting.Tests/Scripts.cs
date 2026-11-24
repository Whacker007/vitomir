// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;
using Vitomir.Scripting;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// One compile of the player's scripts, shared by every test in the run: the first test pays for
/// it, the rest get the cache. Skips with a reason when there is no install.
/// </summary>
public sealed class Scripts
{
    public string? Skip { get; }
    public CodexCompiler? Compiler { get; }
    public ScriptRuntime? Runtime { get; }
    public CodexHost Engine { get; } = new();
    public List<string> Warnings { get; } = [];

    public Scripts()
    {
        var dir = GameFiles.Locate();
        if (dir is null) { Skip = "no game install (set VTMR_GAME_DIR to the folder holding Resource.nob)"; return; }
        // The game's own: the shipped classes, bridged, with the Codex folder's overlay; VITOMIR_SCRIPT_SOURCES=1 the source path.
        if (CodexCompiler.SourcesBySeam ? !Directory.Exists(Path.Combine(dir, "Codex")) : !File.Exists(Path.Combine(dir, "Codex.nob")))
        {
            Skip = CodexCompiler.SourcesBySeam ? "this install has no Codex script sources (VITOMIR_SCRIPT_SOURCES asks for them)" : "this install has no Codex.nob";
            return;
        }
        Engine.Log = Warnings.Add;
        var cache = Path.Combine(Path.GetTempPath(), "vitomir-codex-tests");
        Compiler = CodexCompiler.ForInstall(dir, cache);
        Runtime = ScriptRuntime.Start(Compiler, Engine);
    }

    /// <summary>
    /// Skips without an install; otherwise starts the scripts' <c>Math.random()</c> over at
    /// <see cref="RandomSeed"/>, so every test's scripts roll the same numbers however many tests
    /// ran before it. Before, the Java runtime seeded it from the clock, and a Celerity cast
    /// fizzled (<c>Discipline.Fizzled</c>: 5 in 100 at level 0) one run in twenty.
    /// </summary>
    public void Ready()
    {
        Assert.SkipWhen(Skip is not null, Skip ?? "");
        ScriptRandom.Seed(RandomSeed);
    }

    /// <summary>The scripts' stream at the start of every test.</summary>
    public const long RandomSeed = 1;
}

[CollectionDefinition("scripts")]
public sealed class ScriptsCollection : ICollectionFixture<Scripts>;
