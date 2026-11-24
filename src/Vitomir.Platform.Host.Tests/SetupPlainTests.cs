// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Text;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **What <c>--setup</c> prints is plain ASCII, and says a replacement apart from an addition** (a console showed "&#x2426; "
/// for an arrow): the platform's program a process a step, on a stand-in install with a developer's manifest of its own, each step
/// the setup has (and the whole run) with every line it prints, the program's own first lines among them, held to ASCII; the files
/// left out said as the one that would replace one of 1.1's own (the path of an entry of 1.1's archive) and the additions.
/// </summary>
public sealed class SetupPlainTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("vitomir-setup-plain-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static string Platform => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    (int Code, List<string> Lines) Run(params string[] args)
    {
        var start = new ProcessStartInfo(Platform) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = dir };
        foreach (var a in (string[])["--log-file", Path.Combine(dir, $"platform-{Guid.NewGuid():N}.log"), .. args]) start.ArgumentList.Add(a);
        start.Environment["VITOMIR_USERDATA"] = Path.Combine(dir, "user");
        start.Environment["SDL_AUDIODRIVER"] = "dummy";
        start.Environment.Remove("VTMR_GAME_DIR");
        start.StandardOutputEncoding = new UTF8Encoding(false);
        start.StandardErrorEncoding = new UTF8Encoding(false);
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") is null) start.Environment["SDL_VIDEO_DRIVER"] = "offscreen";
        using var p = new Process { StartInfo = start };
        var err = new StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (err) err.AppendLine(e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        string output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(600_000)) { p.Kill(entireProcessTree: true); Assert.Fail($"the platform did not end: {string.Join(' ', args)}"); }
        p.WaitForExit();
        lock (err) return (p.ExitCode, [.. (output + err).Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)]);
    }

    [Fact]
    public void Every_line_each_setup_step_prints_is_ASCII_and_a_replacement_is_told_from_an_addition()
    {
        // A stand-in install: 1.1's archives (LMaterials.nob a real one, with an entry's path), its loose files, a save, the SDK's sources,
        // and loose files left out: one with the path of 1.1's own entry, two new ones.
        string install = Path.Combine(dir, "install");
        foreach (var a in DataManifest.Archives.Where(a => a != "LMaterials.nob")) Write(Path.Combine(install, a), "archive " + a);
        Directory.CreateDirectory(install);
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(install, "LMaterials.nob"), System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(zip.CreateEntry("materials/General/ankhs.tga", System.IO.Compression.CompressionLevel.NoCompression).Open())) w.Write("1.1's");
        Write(Path.Combine(install, "classes.zip"), "classes");
        Write(Path.Combine(install, "Chronicles", "Prague.nsc"), "chronicle");
        Write(Path.Combine(install, "Video", "intro.bik"), "film");
        Write(Path.Combine(install, "Sounds", "Dialog", "line.mp3"), "a line");
        Write(Path.Combine(install, "Codex", "Door.java"), "class Door {}");
        var head = new byte[0x88];
        Encoding.ASCII.GetBytes("Monastery 1 (01:28:05)").CopyTo(head, 0);
        Directory.CreateDirectory(Path.Combine(install, "SaveGames", "SP4"));
        File.WriteAllBytes(Path.Combine(install, "SaveGames", "SP4", "!main.nsg"), head);
        Write(Path.Combine(install, "Crypt", "npf000001.npf"), "an actor");
        string manifest = Path.Combine(dir, "test.tsv");
        File.WriteAllText(manifest, DataManifest.Make(install, "test-data", 1, "a test's", cancel: TestContext.Current.CancellationToken).Text());
        Write(Path.Combine(install, "Materials", "General", "ankhs.tga"), "a texture pack's");
        Write(Path.Combine(install, "Materials", "SDK", "grid.tga"), "the SDK's");
        Write(Path.Combine(install, "Materials", "SDK", "marker.tga"), "the SDK's");

        string[] dev = ["--from", install, "--developer", "--manifest", manifest];
        var runs = new List<(string Step, int Code, List<string> Lines)>();
        foreach (var step in new[] { "find", "check", "check --fast", "saves", "import-saves SP4", "import", "status", "import-saves SP4", "ffmpeg", "all" })
        {
            var (code, lines) = Run(["--setup", .. step.Split(' '), .. dev]);
            runs.Add((step, code, lines));
        }
        foreach (var (step, _, lines) in runs)
        {
            Assert.True(lines.Count > 3, $"--setup {step} printed {lines.Count} lines");
            foreach (var line in lines) Assert.True(line.All(c => c < 128), $"--setup {step} printed a line that is not ASCII: {line}");
        }
        string Of(string step) => string.Join("\n", runs.First(r => r.Step == step).Lines);
        Assert.Contains("setup: left out, 1 file that would replace 1.1's own (a texture pack's or a mod's: add one as a player module, vitomir-platform --setup module-add <its folder>): Materials/ (1)", Of("check"), StringComparison.Ordinal);
        Assert.Contains("  left out (replaces 1.1's): Materials/General/ankhs.tga", Of("check"), StringComparison.Ordinal);
        Assert.Contains("setup: left out, 2 files that only add to the game, new files 1.1 does not have (a mod's or the SDK's: they replace nothing of 1.1's; a mod is added as a player module): Materials/ (2)", Of("check"), StringComparison.Ordinal);
        Assert.Contains("  left out (adds): Materials/SDK/grid.tga", Of("check"), StringComparison.Ordinal);
        Assert.DoesNotContain("would override", Of("check"), StringComparison.Ordinal);
        Assert.Contains("SP4", Of("saves"), StringComparison.Ordinal);
        Assert.Contains("-> SP4.vsave", Of("saves"), StringComparison.Ordinal);                // the arrow, plain
        Assert.Contains(" - not a GOG or Steam copy", Of("find"), StringComparison.Ordinal);   // the dash, plain
        Assert.Equal(SetupCommand.Exit.Done, runs.First(r => r.Step == "import").Code);
        // The import's two passes each by its name (two runs of 10% to 100% once showed with nothing to tell them apart).
        Assert.Contains("  import, sizes: 100%", Of("import"), StringComparison.Ordinal);
        Assert.Contains("  import, copy and hash: 100%", Of("import"), StringComparison.Ordinal);
        Assert.Contains("  check, sizes and hashes: 100%", Of("check"), StringComparison.Ordinal);
        Assert.Contains("  check, sizes: 100%", Of("check --fast"), StringComparison.Ordinal);

        // Verify and repair: all good; then a file changed in the imported copy, named, and copied again from the copy of the game.
        var (goodCode, good) = Run(["--setup", "verify", .. dev]);
        Assert.Equal(SetupCommand.Exit.Done, goodCode);
        Assert.Contains(good, l => l.StartsWith("setup: all ", StringComparison.Ordinal) && l.Contains(" imported game files are as the manifest has them", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(dir, "user", "Imported", "vtmr-1.1", "Video", "intro.bik"), "FILM");
        var (badCode, bad) = Run(["--setup", "verify", .. dev]);
        Assert.Equal(SetupCommand.Exit.NotTheData, badCode);
        Assert.Contains("setup: 1 of the imported game files are not right: 1 changed", bad);
        Assert.Contains("  changed: Video/intro.bik (other bytes of the same size (SHA-256 ", string.Join("\n", bad), StringComparison.Ordinal);
        var (fixedCode, repaired) = Run(["--setup", "repair", .. dev]);
        Assert.Equal(SetupCommand.Exit.Done, fixedCode);
        Assert.Contains(repaired, l => l.StartsWith($"setup: repaired: 1 file copied again from {Path.GetFullPath(install)}", StringComparison.Ordinal));
        foreach (var line in good.Concat(bad).Concat(repaired)) Assert.True(line.All(c => c < 128), $"--setup verify or repair printed a line that is not ASCII: {line}");
        Assert.Equal(SetupCommand.Exit.Done, Run(["--setup", "verify", .. dev]).Code);
    }
}
