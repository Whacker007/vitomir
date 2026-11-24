// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>The player's install for tests that need game data; they skip without one.</summary>
public static class Install
{
    static readonly string? Dir = GameFiles.Locate();

    public static GameFiles Open()
    {
        Assert.SkipWhen(Dir is null, "game install not found (set VTMR_GAME_DIR to the folder holding Resource.nob)");
        return new GameFiles(Dir!);
    }
}
