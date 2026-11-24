// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// The one test that fails when there is no game data, so that a run which tested almost nothing
/// cannot report success.
///
/// Most of this suite reads the player's own install and **skips** without it, which is the right
/// behaviour for a tree that ships no game content — but it has a failure mode nobody wants. On
/// A run came back `failed: 0, Build succeeded` having **skipped 165 of its 316 tests**,
/// because the install was not found that minute; the summary looked exactly like a healthy run.
/// A suite that silently tests nothing is worse than one that fails, because it is believed.
///
/// So: if the install is missing, this test fails and names the paths it looked in. A tree that
/// genuinely has no game data — CI, or somebody reading the source — sets `VTMR_NO_GAME_DATA` and
/// gets the old behaviour back for this test too.
/// </summary>
public class InstallTests
{
    /// <summary>Set this when a run is *expected* to have no game data.</summary>
    public const string Opt = "VTMR_NO_GAME_DATA";

    [Fact]
    public void The_game_data_these_tests_read_is_actually_there()
    {
        if (Environment.GetEnvironmentVariable(Opt) is { Length: > 0 })
        {
            Assert.Skip($"{Opt} is set, so a run with no game data is expected");
            return;
        }

        var found = GameFiles.Locate();
        Assert.True(found is not null,
            $"no game install, so most of this suite will skip and the run will look like a pass "
            + $"while testing almost nothing. Looked for Resource.nob in: "
            + $"$VTMR_GAME_DIR ({Environment.GetEnvironmentVariable("VTMR_GAME_DIR") ?? "unset"}), "
            + $@"then C:\Games\VTMR. Set {Opt} if that is deliberate.");

        // And it is the install, not an empty folder that happens to have the name.
        using var files = new GameFiles(found!);
        Assert.True(files.TryRead("misc/effects.ned", out _), $"{found} has Resource.nob but not its contents");
    }
}
