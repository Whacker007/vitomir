// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **<c>vitomir-platform --setup</c> and the Classic game's start without <c>--oneone</c>**, the platform's program
/// a process a run, <c>VITOMIR_USERDATA</c> a folder of the test's own: the real check of this machine's install against the real
/// manifest (each missing or changed file named); a real import (opt-in, <c>VITOMIR_SETUP_IMPORT_CHECK=1</c>: it copies the install's
/// data, 0.76 GB in the cloud) and a Classic still from it with no <c>--oneone</c>, byte for byte the still from the install; a start
/// with nothing set up saying so; 1.1's own saves listed and one imported, never over one there without <c>--replace</c>; the
/// command line's refusals; FFmpeg.
/// </summary>
public sealed class SetupCommandTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("vitomir-setup-command-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static string Platform => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");

    string User => Path.Combine(dir, "user");

    /// <summary>The platform's program run to its end with the test's user data; <paramref name="install"/> as VTMR_GAME_DIR (null: none).</summary>
    (int Code, string Output) Run(string? install, params string[] args)
    {
        var start = new ProcessStartInfo(Platform) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = dir };
        // A setup step is pointed at the install with --from: a machine with GOG's or Steam's copy as well finds those
        // first, and the setup uses a store's copy before VTMR_GAME_DIR's (SetupService.Choose).
        if (install is not null && args is ["--setup", ..] && !args.Contains("--from")) args = [.. args, "--from", install];
        foreach (var a in (string[])["--log-file", Path.Combine(dir, $"platform-{Guid.NewGuid():N}.log"), .. args]) start.ArgumentList.Add(a);
        start.Environment["VITOMIR_USERDATA"] = User;
        start.Environment["SDL_AUDIODRIVER"] = "dummy";
        if (install is null) start.Environment.Remove("VTMR_GAME_DIR");
        else start.Environment["VTMR_GAME_DIR"] = install;
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("DISPLAY") is null
            && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null && Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") is null)
            start.Environment["SDL_VIDEO_DRIVER"] = "offscreen";
        using var p = new Process { StartInfo = start };
        var err = new System.Text.StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (err) err.AppendLine(e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        var output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(900_000)) { p.Kill(entireProcessTree: true); Assert.Fail($"the platform did not end: {string.Join(' ', args)}"); }
        p.WaitForExit();
        lock (err) return (p.ExitCode, output + err);
    }

    static string Install()
    {
        var game = GameInstall.Locate(Environment.GetEnvironmentVariable("VTMR_GAME_DIR") ?? "");
        Assert.SkipWhen(game is null || !InstallFinder.IsInstall(game), "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Path.GetFullPath(game!);
    }

    [Fact]
    public void This_machines_install_is_checked_against_the_real_manifest_each_file_not_as_it_says_named()
    {
        string install = Install();
        var expected = DataCheck.Run(install, DataManifest.Builtin, CheckDepth.Full, cancel: TestContext.Current.CancellationToken);
        var (code, output) = Run(install, "--setup", "check");
        Assert.Matches(new System.Text.RegularExpressions.Regex(@"^  \d+\. " + System.Text.RegularExpressions.Regex.Escape(install) + @": .*VTMR_GAME_DIR \(a developer's\).*the folder named",
            System.Text.RegularExpressions.RegexOptions.Multiline), output);                       // not the first where a store's copy is found too
        Assert.Contains("setup: using " + install, output, StringComparison.Ordinal);
        Assert.Contains("== check: " + install + " against vtmr-1.1-data v1 (every file's size and SHA-256", output, StringComparison.Ordinal);
        if (expected.GameWhole)
        {
            Assert.Equal(SetupCommand.Exit.Done, code);
            Assert.Contains("setup: the game's files: all 3,133 as the manifest has them", output, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(SetupCommand.Exit.NotTheData, code);                       // the cloud's copy has no films and 2 of the loose sounds
            Assert.Contains($"setup: the game's files: {expected.Problems.Count:N0} of 3,133 are not as the manifest has them, so this copy is refused:", output, StringComparison.Ordinal);
            foreach (var p in expected.Problems) Assert.Contains($"  {p.Path}: {p.Why}\n", output.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        }
        Assert.Contains(expected.Scripts == PartState.Whole
            ? "setup: the game's scripts: Codex.nob's own compiled classes, as 1.1 runs them (this install also has the SDK's 465 sources, unchanged: not needed, not imported)"
            : "setup: the game's scripts: Codex.nob's own compiled classes, as 1.1 runs them", output, StringComparison.Ordinal);
        Assert.DoesNotContain("none of its scripts", output, StringComparison.Ordinal);       // no warning that the scripts will not run
        Assert.False(Directory.Exists(Path.Combine(User, ImportedData.ImportedFolderName)));     // a check writes nothing
    }

    /// <summary>The real manifest cut to the files this install has (the cloud's has no films): the developer's <c>--manifest</c>, named as one.</summary>
    string Subset(string install)
    {
        var real = DataManifest.Builtin;
        var have = DataCheck.Index(install);
        var cut = new DataManifest
        {
            Name = "vtmr-1.1-data-test-subset", Version = 1, Made = "the tests' cut of vtmr-1.1-data v1 to this machine's install",
            Parts = real.Parts, OverrideFolders = real.OverrideFolders, Files = [.. real.Files.Where(f => have.ContainsKey(f.Path))],
        };
        string file = Path.Combine(dir, "subset.tsv");
        File.WriteAllText(file, cut.Text());
        return file;
    }

    [Fact]
    public void A_real_import_is_played_with_no_oneone_and_draws_what_the_install_draws()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("VITOMIR_SETUP_IMPORT_CHECK") != "1",
            "the real import copies the install's data (0.76 GB in the cloud): set VITOMIR_SETUP_IMPORT_CHECK=1 to run it");
        string install = Install();
        string subset = Subset(install);
        var (code, output) = Run(install, "--setup", "import", "--developer", "--manifest", subset);
        Assert.True(code == SetupCommand.Exit.Done, output);
        Assert.Contains("each read back and its SHA-256 the manifest's", output, StringComparison.Ordinal);
        string data = ImportedData.Folder(User);
        Assert.NotNull(ImportedData.ReadMarker(data));
        Assert.Equal(SetupCommand.Exit.NotTheData, Run(null, "--setup", "status").Code);   // a player's start knows a developer's manifest
        Assert.Equal(SetupCommand.Exit.Done, Run(null, "--setup", "status", "--developer").Code);

        // A Classic still with no --oneone and no VTMR_GAME_DIR: the imported data; then the same from the install itself.
        string[] still = ["--game", "classic", "--developer", "--level", "P1_OLDT", "--player", "--hud", "--size", "640x480", "--no-log-file", "--headless"];
        var imported = Run(null, [.. still, Path.Combine(dir, "imported.png")]);
        Assert.True(imported.Code == 0, imported.Output);
        Assert.Contains($"game classic: the base game's files: {data}, imported ", imported.Output, StringComparison.Ordinal);
        Assert.Contains("taken (--developer)", imported.Output, StringComparison.Ordinal);
        Assert.Contains($"--game {data} ", imported.Output, StringComparison.Ordinal);       // 1.1's command line names the imported data
        var direct = Run(null, [.. still, Path.Combine(dir, "install.png"), "--oneone", install]);
        Assert.True(direct.Code == 0, direct.Output);
        Assert.Contains("the 1.1 install --oneone names (a developer's override of the imported data)", direct.Output, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(Path.Combine(dir, "install.png")), File.ReadAllBytes(Path.Combine(dir, "imported.png")));
        Directory.Delete(Path.Combine(User, ImportedData.ImportedFolderName), recursive: true);   // disk: one real import at a time
    }

    [Fact]
    public void A_start_with_nothing_set_up_says_plainly_that_the_setup_is_needed()
    {
        var (code, output) = Run(null, "--game", "classic", "--developer", "--level", "P1_HAVN", "--headless", "--frames", "1");
        Assert.Equal(SetupCommand.Exit.NotTheData, code);
        Assert.Contains("game classic: " + SetupCommand.NotSetUp, output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(User, "Modules")));                // nothing of the game's made
        var wrong = Run(null, "--game", "classic", "--developer", "--headless", "--frames", "1", "--oneone", Path.Combine(dir, "nowhere"));
        Assert.Equal(SetupCommand.Exit.Usage, wrong.Code);
        Assert.Contains("is not a 1.1 install (it has no Resource.nob)", wrong.Output, StringComparison.Ordinal);
        // Imported data that is not whole is not used, and the start says what is wrong with it.
        string data = ImportedData.Folder(User);
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, ImportedData.ManifestCopyName), DataManifest.Builtin.Text());
        File.WriteAllText(Path.Combine(data, ImportedData.MarkerName), "{\"format\":1,\"manifest\":\"vtmr-1.1-data\",\"manifestVersion\":1,\"source\":\"x\",\"scripts\":false}");
        var damaged = Run(null, "--game", "classic", "--developer", "--headless", "--frames", "1");
        Assert.Equal(SetupCommand.Exit.NotTheData, damaged.Code);
        Assert.Contains($"the imported data in {data} is not usable (Chronicles/London.nsc: missing; ", damaged.Output, StringComparison.Ordinal);
        Assert.Contains("Run vitomir-platform --setup again to mend it", damaged.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void One_one_s_saves_are_listed_and_imported_never_over_one_there_without_replace()
    {
        string install = Install();
        var originals = OriginalGames.List(install);
        Assert.SkipWhen(!originals.Any(o => o.Id == "SP4"), "this install has no 1.1 save in SP4");
        var (listed, list) = Run(install, "--setup", "saves");
        Assert.Equal(SetupCommand.Exit.Done, listed);
        foreach (var o in originals) Assert.Contains($"  {o.Id,-10} ", list, StringComparison.Ordinal);
        // The arrow is matched as any mark: Windows hands a redirected console's output over in its own code page, not UTF-8.
        Assert.Matches(System.Text.RegularExpressions.Regex.Escape($"\"{originals.First(o => o.Id == "SP4").Description}\"") + @"  \S+ SP4\.vsave", list);

        string saves = Path.Combine(User, "Modules", "classic", "Saves");
        var first = Run(install, "--setup", "import-saves", "SP4");
        Assert.True(first.Code == SetupCommand.Exit.Done, first.Output);
        Assert.Contains("imported as " + Path.Combine(saves, "SP4.vsave"), first.Output, StringComparison.Ordinal);
        Assert.Contains("import: ", first.Output, StringComparison.Ordinal);                      // the save import's own line
        byte[] made = File.ReadAllBytes(Path.Combine(saves, "SP4.vsave"));
        var again = Run(install, "--setup", "import-saves", "SP4");
        Assert.Equal(SetupCommand.Exit.NotImported, again.Code);
        Assert.Contains("setup: refused: SP4: SP4.vsave is there already", again.Output, StringComparison.Ordinal);
        Assert.Equal(made, File.ReadAllBytes(Path.Combine(saves, "SP4.vsave")));
        var replaced = Run(install, "--setup", "import-saves", "SP4", "--replace");
        Assert.Equal(SetupCommand.Exit.Done, replaced.Code);
        Assert.True(File.Exists(Path.Combine(saves, "SP4.vsave.previous")));
        var elsewhere = Run(install, "--setup", "import-saves", "SP4=SP12");
        Assert.Equal(SetupCommand.Exit.Done, elsewhere.Code);
        Assert.True(File.Exists(Path.Combine(saves, "SP12.vsave")));
        Assert.Empty(Directory.EnumerateFiles(saves, ".setup-*"));
        // It loads: the Classic game started from the imported save, a still (the install's files, as nothing is imported here).
        var load = Run(install, "--game", "classic", "--developer", "--load", "SP12", "--size", "640x480", "--no-log-file", "--headless", Path.Combine(dir, "loaded.png"));
        Assert.True(load.Code == 0, load.Output);
        Assert.True(File.Exists(Path.Combine(dir, "loaded.png")));
    }

    /// <summary>
    /// **1.1's multiplayer character imported by the game's own code**: the install's <c>Crypt\npf000000.npf</c> (a player's own, where it
    /// is) listed by its name and clan, imported as <c>Crypt\&lt;name&gt;.vplayer</c> through the Classic game's <c>--import</c>, refused
    /// a second time (exit code 5), replaced with <c>--replace</c>, and imported under another name.
    /// </summary>
    [Fact]
    public void One_one_s_character_is_imported_by_its_name_never_over_one_kept_without_replace()
    {
        string install = Install();
        var character = OriginalGames.List(install).FirstOrDefault(o => o.Kind == OriginalKind.Character && o.ImportsAs is not null);
        Assert.SkipWhen(character is null, "this install has no 1.1 character in Crypt");
        var (listed, list) = Run(install, "--setup", "saves");
        Assert.Equal(SetupCommand.Exit.Done, listed);
        Assert.Contains($"\"{character!.Description}\"  -> Crypt\\{character.ImportsAs}", list, StringComparison.Ordinal);

        string crypt = Path.Combine(User, "Modules", "classic", "Crypt");
        var first = Run(install, "--setup", "import-saves", character.Id);
        Assert.True(first.Code == SetupCommand.Exit.Done, first.Output);
        Assert.Contains($"imported as {character.Name}, {Path.Combine(crypt, character.ImportsAs!)}", first.Output, StringComparison.Ordinal);
        Assert.Contains($"  import: {character.Name}: MP_", first.Output, StringComparison.Ordinal);           // the import's own summary
        string kept = File.ReadAllText(Path.Combine(crypt, character.ImportsAs!));
        Assert.Contains($"\"name\": \"{character.Name}\"", kept, StringComparison.Ordinal);
        Assert.Contains("\"saved\": {", kept, StringComparison.Ordinal);
        var again = Run(install, "--setup", "import-saves", character.Id);
        Assert.Equal(SetupCommand.Exit.NotImported, again.Code);
        Assert.Contains($"setup: refused: {character.Id}: a character called {character.Name} is kept already", again.Output, StringComparison.Ordinal);
        var replaced = Run(install, "--setup", "import-saves", character.Id, "--replace");
        Assert.Equal(SetupCommand.Exit.Done, replaced.Code);
        Assert.True(File.Exists(Path.Combine(crypt, character.ImportsAs + ".previous")));
        var renamed = Run(install, "--setup", "import-saves", character.Id + "=Somebody Else");
        Assert.Equal(SetupCommand.Exit.Done, renamed.Code);
        Assert.Contains("\"name\": \"Somebody Else\"", File.ReadAllText(Path.Combine(crypt, "Somebody Else.vplayer")), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(crypt, ".setup-*"));
    }

    [Fact]
    public void The_command_line_s_mistakes_are_refused_with_exit_code_2_and_FFmpeg_is_found_or_said_missing()
    {
        Assert.Equal(SetupCommand.Exit.Usage, Run(null, "--setup", "nonsense").Code);
        var player = Run(null, "--setup", "check", "--manifest", Path.Combine(dir, "m.tsv"));
        Assert.Equal(SetupCommand.Exit.Usage, player.Code);
        Assert.Contains("--manifest is a developer's", player.Output, StringComparison.Ordinal);
        Assert.Equal(SetupCommand.Exit.Usage, Run(null, "--setup", "import-saves").Code);
        Assert.Equal(SetupCommand.Exit.NotTheData, Run(null, "--setup", "check", "--from", Path.Combine(dir, "nowhere")).Code);

        var (code, output) = Run(null, "--setup", "ffmpeg");
        if (SetupService.FFmpeg() is { } found)
        {
            Assert.Equal(SetupCommand.Exit.Done, code);
            Assert.Contains($"setup: ffmpeg: {found.FFmpeg} ", output, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(SetupCommand.Exit.NoFFmpeg, code);
            Assert.Contains(FFmpegSearch.WingetLine, output, StringComparison.Ordinal);
        }
        Assert.False(Directory.Exists(Path.Combine(User, "Modules")));
    }
}
