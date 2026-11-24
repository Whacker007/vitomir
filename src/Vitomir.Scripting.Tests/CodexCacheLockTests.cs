// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;

namespace Vitomir.Scripting.Tests;

/// <summary>
/// **Several processes building one cache at once** (ours). `--replay-folder` starts four replays together,
/// and each builds the game's scripts into the same cache: each wrote the bridge out afresh and compiled, and
/// one read a bridge another was writing, so its game ran with no scripts. The build now holds the cache's lock.
/// </summary>
public class CodexCacheLockTests
{
    [Fact]
    public async Task Four_builds_of_one_empty_cache_at_once_all_end_with_the_same_good_build()
    {
        string? install = GameFiles.Locate();
        Assert.SkipWhen(install is null, "no game install (set VTMR_GAME_DIR to the folder holding Resource.nob)");
        Assert.SkipWhen(!Directory.Exists(Path.Combine(install!, "Codex")), "this install has no Codex script sources");
        string cache = Path.Combine(Path.GetTempPath(), "vitomir-codex-lock-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Separate compilers, as separate processes have; the lock is the file's, so it holds between them too.
            var ct = TestContext.Current.CancellationToken;
            var builds = Enumerable.Range(0, 4).Select(_ => Task.Run(() => new CodexCompiler(install!, cache).Build(), ct)).ToArray();
            var results = (await Task.WhenAll(builds)).ToList();
            Assert.Single(results.Select(r => r.OutputDir).Distinct());
            Assert.Equal(1, results.Count(r => !r.CacheHit));                // one compiled; the others waited and loaded it
            Assert.All(results, r => Assert.True(r.Classes > 0 && File.Exists(Path.Combine(r.OutputDir, ".complete"))));
        }
        finally { try { Directory.Delete(cache, true); } catch (IOException) { } }
    }

    [Fact]
    public async Task The_lock_is_held_until_it_is_let_go()
    {
        string cache = Path.Combine(Path.GetTempPath(), "vitomir-codex-lock-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var first = CodexCompiler.CacheLock(cache);
            var second = Task.Run(() => CodexCompiler.CacheLock(cache), ct);
            Assert.NotSame(second, await Task.WhenAny(second, Task.Delay(300, ct)));    // the second waits while the first holds it
            first.Dispose();
            Assert.Same(second, await Task.WhenAny(second, Task.Delay(5000, ct)));      // and has it once it is let go
            (await second).Dispose();
        }
        finally { try { Directory.Delete(cache, true); } catch (IOException) { } }
    }
}
