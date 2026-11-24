// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Engine.Tests;

/// <summary>
/// **The levels fork's cooking tests, run on their own** (ours). The cell-level tests cook levels — Recast's
/// navigation meshes, BC7 texture compression, collision trees — and are heavy on the processor. Run beside the
/// rest they slowed the time-bound tests of the main line (the convent's sound read-ahead waits at most a minute,
/// <see cref="FileCacheTests"/>), which failed on a busy laptop; in this collection, with parallelization off,
/// they run after the parallel tests rather than among them (the pattern of <see cref="GltfExportTests"/>).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LevelCooksCollection
{
    /// <summary>The collection's name, for the test classes' <c>[Collection]</c>.</summary>
    public const string Name = "level cooks";
}
