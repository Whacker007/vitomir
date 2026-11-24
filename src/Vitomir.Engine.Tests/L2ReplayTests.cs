// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.Vfs;

namespace Vitomir.Engine.Tests;

/// <summary>
/// **The recorded runs, replayed on levels read from L2** (ours). The two
/// replay baselines that play in one level — the Monastery's opening (<c>P1_MON1</c>) and the fight
/// in the third archive (<c>p1_arc3</c>) — are replayed with <c>--replay-check</c> as
/// <see cref="ReplayBaselineTests"/> does, but with their levels converted to L2 in a folder of their
/// own and handed to the game through <c>VITOMIR_L2</c>: the simulation must agree with the
/// recording at every frame, and the game must say it read the level from the package. Like the
/// baselines, they run the built platform's program beside this project's on the install (<c>vitomir.exe</c> until then),
/// skip without either, and are left out by <c>VITOMIR_NO_REPLAYS=1</c>. Where the tape's digest was recorded on this
/// platform it is the standard, the strongest check. A digest recorded on another platform cannot be
/// (the C runtimes' <c>sinf</c> and its fellows may round differently; both tapes were recorded on linux-x64),
/// so the tape is first replayed on the plain level, with no L2 handed to the game, and that run's own digest
/// stands in for the recording's: the level read from L2 is checked against the plain level on this machine.
/// <see cref="ReplayBaselineTests"/> is the other half of the chain, the plain level against the recording.
/// </summary>
[Collection(nameof(L2ReplayTests))]
[CollectionDefinition(nameof(L2ReplayTests), DisableParallelization = true)]
public class L2ReplayTests
{
    static readonly string Here = AppContext.BaseDirectory;

    [Theory]
    [InlineData("mon1-opening.tape", "P1_MON1")]
    [InlineData("arc3-fight.tape", "p1_arc3")]
    public void A_recorded_run_replays_the_same_every_frame_with_its_level_read_from_L2(string tape, string level)
    {
        var game = GameFiles.Locate();
        Assert.SkipWhen(game is null, "game install not found (set VTMR_GAME_DIR to the folder holding Resource.nob)");
        Assert.SkipWhen(Environment.GetEnvironmentVariable("VITOMIR_NO_REPLAYS") == "1", "VITOMIR_NO_REPLAYS=1: the replays left out of this run");
        var app = ReplayBaselineTests.Program();
        Assert.SkipWhen(app is null, "the platform's program is not built beside the tests (build src/Vitomir.Platform.Host/Vitomir.Platform.Host.csproj in the same configuration)");
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-l2-replay-{Guid.NewGuid():N}")).FullName;
        try
        {
            foreach (var suffix in new[] { "", ".digest" })
                File.Copy(Path.Combine(Here, "Replays", tape + suffix), Path.Combine(dir, tape + suffix));
            var l2 = Path.Combine(dir, "l2");
            using (var files = new GameFiles(game!)) L2Converter.Convert(files, level, l2, anchors: false);

            var tapePath = Path.Combine(dir, tape);
            if (!RecordedHere(tapePath + ".digest"))
            {
                // Another platform's digest: this machine's own run of the plain level takes its place.
                var (plainCode, plainOutput) = Run(app!, "--replay", tapePath, game!, l2: null);
                Assert.True(plainCode == 0, Said(plainOutput));
                File.Copy(tapePath + ".replay.digest", tapePath + ".digest", overwrite: true);
            }
            var (exitCode, output) = Run(app!, "--replay-check", tapePath, game!, l2);
            var said = Said(output);
            Assert.True(output.Contains($"l2: {level} read from", StringComparison.OrdinalIgnoreCase), said);
            Assert.True(exitCode == 0 && output.Contains("replay-check: the same"), said);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Whether the digest's header (<c>recorded on</c> and a runtime identifier, in its first lines) names this platform.
    /// A digest this machine wrote itself says <c>replayed on</c> and is not a recording's.
    /// </summary>
    static bool RecordedHere(string digest) =>
        File.ReadLines(digest).Take(4).Any(l => l.EndsWith("recorded on " + RuntimeInformation.RuntimeIdentifier, StringComparison.Ordinal));

    /// <summary>
    /// The game for <paramref name="tape"/> with <paramref name="mode"/> (<c>--replay</c> or <c>--replay-check</c>), run by the platform's
    /// program as the baselines are (<c>vitomir.exe</c> before), a user data folder of its own. With <paramref name="l2"/>
    /// the level is read from that folder through <c>VITOMIR_L2</c>; without, the variable is taken out of the child's environment (the
    /// test host's own may hold one), so the level is the plain one.
    /// </summary>
    static (int ExitCode, string Output) Run(string app, string mode, string tape, string game, string? l2) =>
        ReplayBaselineTests.RunWith(new() { [L2Levels.Variable] = l2 ?? "" }, app, game, mode, tape);

    /// <summary>What the game said that a failure needs: its replay lines and its L2 lines.</summary>
    static string Said(string output) =>
        string.Join('\n', output.Split('\n').Where(l => l.StartsWith("replay", StringComparison.Ordinal) || l.StartsWith("l2:", StringComparison.Ordinal)));
}
