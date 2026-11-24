// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using Vitomir.Platform.Assets;
using Vitomir.Platform.PlayerModules;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The player's modules through the platform's program** (a process a run, <c>VITOMIR_USERDATA</c> a folder of the test's own, packs
/// made by the test): the setup's module steps (added, code and program files refused and named, listed, checked, accepted, the
/// command line's mistakes); a game's start refused for an error (exit code 7) and for warnings not accepted (8), and played once
/// they are; a made-up texture pack drawn in a still, and turned off changing nothing; a save naming its rules modules, and its load
/// without them warning.
/// </summary>
public sealed class PlayerModuleStartTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("vitomir-player-module-start-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    static string Platform => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");

    string User => Path.Combine(dir, "user");

    /// <summary>The platform's program run to its end with the test's user data and the install as VTMR_GAME_DIR: its exit code, its log and what it printed.</summary>
    (int Code, string Log, string Output) Run(params string[] args)
    {
        string log = Path.Combine(dir, $"platform-{Guid.NewGuid():N}.log");
        var start = new ProcessStartInfo(Platform) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = dir };
        foreach (var a in (string[])["--log-file", log, .. args]) start.ArgumentList.Add(a);
        start.Environment["VITOMIR_USERDATA"] = User;
        start.Environment["VTMR_GAME_DIR"] = Game();
        start.Environment["SDL_AUDIODRIVER"] = "dummy";
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && Environment.GetEnvironmentVariable("DISPLAY") is null
            && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") is null && Environment.GetEnvironmentVariable("SDL_VIDEO_DRIVER") is null)
            start.Environment["SDL_VIDEO_DRIVER"] = "offscreen";
        using var p = new Process { StartInfo = start };
        var err = new System.Text.StringBuilder();
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (err) err.AppendLine(e.Data); };
        p.Start();
        p.BeginErrorReadLine();
        var output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(600_000)) { p.Kill(entireProcessTree: true); Assert.Fail($"the platform did not end: {string.Join(' ', args)}"); }
        p.WaitForExit();
        lock (err) return (p.ExitCode, File.Exists(log) ? File.ReadAllText(log) : "", output + err);
    }

    static string Game()
    {
        var game = GameInstall.Locate();
        Assert.SkipWhen(game is null, "the 1.1 install is not here (VTMR_GAME_DIR)");
        return Path.GetFullPath(game!);
    }

    /// <summary>A checked TGA of red and yellow, 24 bits, of the given size: nothing like a texture of 1.1's.</summary>
    static byte[] Checks(int width, int height)
    {
        var data = new byte[18 + width * height * 3];
        data[2] = 2;
        data[12] = (byte)width; data[13] = (byte)(width >> 8); data[14] = (byte)height; data[15] = (byte)(height >> 8);
        data[16] = 24;
        for (int y = 0, i = 18; y < height; y++)
            for (int x = 0; x < width; x++, i += 3)
            {
                bool red = ((x / 16) + (y / 16)) % 2 == 0;
                data[i] = 0; data[i + 1] = red ? (byte)0 : (byte)255; data[i + 2] = 255;   // B, G, R
            }
        return data;
    }

    string Pack(string name, params (string Path, byte[] Data)[] files)
    {
        string pack = Path.Combine(dir, "packs", name);
        foreach (var (path, data) in files)
        {
            string full = Path.Combine(pack, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, data);
        }
        return pack;
    }

    /// <summary>A made-up pack over the haven's walls: each of the four textures at the size of 1.1's, in red and yellow checks.</summary>
    string HavenChecks(string name = "Checked Haven")
    {
        using var install = GameInstall.Open(Game());
        var files = new List<(string, byte[])>();
        foreach (var wall in new[] { "Uwall1_1_p", "Uwall1_2_p", "Uwall1_3_p", "Uwall2_p" })
        {
            Assert.True(install.TryReadMaterialFile(wall + ".tga", out var original, out var path), wall);
            files.Add((path, Checks(original[12] | original[13] << 8, original[14] | original[15] << 8)));
        }
        return Pack(name, [.. files]);
    }

    [Fact]
    public void The_setup_adds_lists_checks_and_accepts_player_modules_and_names_what_it_refuses()
    {
        Game();
        var (code, log, output) = Run("--setup", "module-add", HavenChecks());
        Assert.True(code == 0, $"exit {code}\n{output}");
        Assert.Contains("setup: imported the module checked-haven 0.0.0 (look): 4 files", output);

        string crimson = Pack("Crimsonish", ("Vampire.exe", [1]), ("DDraw.dll", [1]), ("Miles/FontFix.asi", [1]), ("Masquerade.ini", [1]),
            ("materials/Prague/Uwall2_p.TGA", Checks(64, 64)));
        (code, _, output) = Run("--setup", "module-add", crimson);
        Assert.True(code == 0, $"exit {code}\n{output}");
        Assert.Contains("  refused (program): Vampire.exe: the game's program, patched (Vitomir is a program of its own and does not run it)", output);
        Assert.Contains("  refused (program): DDraw.dll: a graphics wrapper the game's program loads (dgVoodoo's): Vitomir draws with its own renderer", output);
        Assert.Contains("  refused (program): Miles/FontFix.asi: a plug-in folder of the game's sound library (Miles)", output);
        Assert.Contains("  kept aside (a document, never read by the game): Masquerade.ini", output);
        Assert.Contains("; 3 refused (code and program files: player modules carry data only)", output);

        (code, _, output) = Run("--setup", "module-list");
        Assert.Equal(0, code);
        Assert.Contains("  1. on  checked-haven 0.0.0 \"Checked Haven\": look, 4 files", output);
        Assert.Contains("  2. on  crimsonish 0.0.0 \"Crimsonish\": look, 1 file (", output);

        (code, _, output) = Run("--setup", "module-check");
        Assert.True(code == SetupCommand.Exit.ModuleWarnings, $"exit {code}\n{output}");
        Assert.Contains("  warning: module crimsonish: materials/Prague/Uwall2_p.TGA: checked-haven and crimsonish both give 1 file: crimsonish's is used, as it loads later", output);
        Assert.Contains("setup: 1 warning, not accepted for this combination", output);
        (code, _, output) = Run("--setup", "module-accept");
        Assert.True(code == 0, $"exit {code}\n{output}");
        Assert.Contains("setup: accepted the warning of this combination", output);
        Assert.Equal(0, Run("--setup", "module-check").Code);
        (code, _, output) = Run("--setup", "module-order", "crimsonish");
        Assert.Contains("setup: the game's list is now: crimsonish, checked-haven", output);
        Assert.Equal(SetupCommand.Exit.ModuleWarnings, Run("--setup", "module-check").Code);    // another order asks again

        Assert.Equal(SetupCommand.Exit.Usage, Run("--setup", "module-enable", "nobody").Code);
        Assert.Equal(SetupCommand.Exit.Usage, Run("--setup", "module-order", "nobody").Code);
        Assert.Equal(SetupCommand.Exit.Usage, Run("--setup", "module-remove", "nobody").Code);
        Assert.Equal(SetupCommand.Exit.Usage, Run("--setup", "module-add").Code);
        (code, _, output) = Run("--setup", "module-add", Pack("Readme Only", ("readme.txt", [1])));
        Assert.Equal(SetupCommand.Exit.NotImported, code);
        Assert.Contains("setup: not imported: no file of it is in one of 1.1's folders", output);
        Assert.Equal(0, Run("--setup", "module-remove", "crimsonish").Code);
        Assert.False(Directory.Exists(Path.Combine(User, PlayerModuleStore.FolderName, "crimsonish")));
        Assert.True(File.Exists(Path.Combine(crimson, "Vampire.exe")));                          // the pack itself is never touched
    }

    [Fact]
    public void Module_add_of_an_id_there_from_another_folder_refuses_in_the_command_lines_words_and_replace_takes_it()
    {
        string first = Pack("Texturemod", ("materials/Prague/Uwall2_p.TGA", Checks(64, 64)));
        Assert.Equal(0, Run("--setup", "module-add", first).Code);
        string other = Path.Combine(dir, "elsewhere", "Texturemod");
        Directory.CreateDirectory(Path.Combine(other, "materials", "Prague"));
        File.WriteAllBytes(Path.Combine(other, "materials", "Prague", "Uwall2_p.TGA"), Checks(64, 64));
        var (code, _, output) = Run("--setup", "module-add", other);
        Assert.Equal(SetupCommand.Exit.NotImported, code);
        Assert.Contains($"setup: not imported: a module texturemod is there already (from {Path.GetFullPath(first)}): remove it first (module-remove texturemod), "
                        + "give this one another id (--id <id>), or --replace", output);
        (code, _, output) = Run("--setup", "module-add", other, "--replace");
        Assert.True(code == 0, $"exit {code}\n{output}");
        Assert.Contains("setup: imported the module texturemod 0.0.0 (look): 1 file", output);
    }

    [Fact]
    public void A_start_with_an_error_or_warnings_not_accepted_is_refused_naming_them_and_plays_once_they_are_accepted()
    {
        string game = Game();
        string[] start = ["--game", "classic", "--developer", "--oneone", game, "--level", "P1_HAVN", "--headless", "--frames", "3", "--size", "320x240", "--mute"];
        Assert.Equal(0, Run("--setup", "module-add", Pack("Broken", ("materials/Prague/broken.tga", [1, 2, 3]))).Code);
        var (code, log, output) = Run(start);
        Assert.True(code == SetupCommand.Exit.ModuleErrors, $"exit {code}\n{log}\n{output}");
        Assert.Contains("game classic: error: module broken: materials/Prague/broken.tga: a texture that cannot be read as a texture", log);
        Assert.Contains("game classic: not started: The player modules on have 1 error", log);
        Assert.DoesNotContain("plays in a window", log);

        Assert.Equal(0, Run("--setup", "module-disable", "broken").Code);
        Assert.Equal(0, Run("--setup", "module-add", Pack("One", ("materials/Prague/Uwall2_p.TGA", Checks(64, 64)))).Code);
        Assert.Equal(0, Run("--setup", "module-add", Pack("Two", ("materials/Prague/Uwall2_p.TGA", Checks(64, 64)))).Code);
        (code, log, output) = Run(start);
        Assert.True(code == SetupCommand.Exit.ModuleWarnings, $"exit {code}\n{log}\n{output}");
        Assert.Contains("game classic: warning: module two: materials/Prague/Uwall2_p.TGA: one and two both give 1 file", log);
        Assert.Contains("module-accept accepts them", log);

        Assert.Equal(0, Run("--setup", "module-accept").Code);
        (code, log, output) = Run(start);
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.Contains("game classic: player modules on, in load order: one 0.0.0 (look), two 0.0.0 (look); the checks of this combination kept from the last", log);
        Assert.Contains("game classic: 1 warning of the player modules, accepted for this combination", log);
        Assert.Contains($"--player-module {Path.Combine(User, PlayerModuleStore.FolderName, "one")} --player-module {Path.Combine(User, PlayerModuleStore.FolderName, "two")}", log);
        Assert.Contains("player module: two 0.0.0 (look):", output);
    }

    [Fact]
    public void A_made_up_texture_pack_is_drawn_in_a_still_and_turned_off_changes_nothing()
    {
        string game = Game();
        string Still(string name)
        {
            string png = Path.Combine(dir, name + ".png");
            var (code, log, output) = Run("--game", "classic", "--developer", "--oneone", game, "--level", "P1_HAVN", "--ui-layout", "fit", "--size", "640x360", "--mute", "--headless", png);
            Assert.True(code == 0 && File.Exists(png), $"exit {code}\n{log}\n{output}");
            return png;
        }
        string without = Still("without");
        Assert.Equal(0, Run("--setup", "module-add", HavenChecks()).Code);
        string with = Still("with");
        Assert.Equal(0, Run("--setup", "module-disable", "checked-haven").Code);
        string off = Still("off");
        Assert.Equal(File.ReadAllBytes(without), File.ReadAllBytes(off));
        Assert.NotEqual(File.ReadAllBytes(without), File.ReadAllBytes(with));
        // The checks are there: far more strongly red pixels (red well over green and blue) than the haven's own walls have.
        static int Reds(string png)
        {
            var rgba = Vitomir.Formats.Png.PngDecoder.Decode((ReadOnlySpan<byte>)File.ReadAllBytes(png), out _, out _)!;
            int n = 0;
            for (int i = 0; i < rgba.Length; i += 4)
                if (rgba[i] > 60 && rgba[i] > 3 * rgba[i + 1] && rgba[i] > 3 * rgba[i + 2]) n++;
            return n;
        }
        Assert.True(Reds(with) > 20 * Math.Max(1, Reds(without)), $"{Reds(with)} red pixels with the pack, {Reds(without)} without");
    }

    [Fact]
    public void A_save_names_its_rules_modules_and_loading_it_without_them_warns_naming_them()
    {
        string game = Game();
        Assert.Equal(0, Run("--setup", "module-add", Pack("Story Bits", ("paths/madeup_path.ncp", [1, 2, 3, 4]))).Code);
        var (code, log, output) = Run("--game", "classic", "--developer", "--oneone", game, "--level", "P1_OLDT", "--player", "--size", "320x240",
                                      "--mute", "--headless", Path.Combine(dir, "saved.png"), "--save", "SPM");
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.Contains("player module: story-bits 0.0.0 (world, a rules module):", output);
        string save = Path.Combine(User, "Modules", "classic", "Saves", "SPM.vsave");
        // The save's header (a zip of readable parts; save.jsonc is the header), read as text: this program's tests do not reference the engine.
        using (var zip = System.IO.Compression.ZipFile.OpenRead(save))
        using (var reader = new StreamReader(zip.GetEntry("save.jsonc")!.Open()))
        using (var json = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd(), new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true }))
        {
            var mods = json.RootElement.EnumerateObject().Single(p => p.Name.Equals("mods", StringComparison.OrdinalIgnoreCase)).Value;
            var stamp = Assert.Single(mods.EnumerateArray()).GetString()!;
            Assert.Matches(@"^story-bits 0\.0\.0 [0-9a-f]{16}$", stamp);
        }

        Assert.Equal(0, Run("--setup", "module-disable", "story-bits").Code);
        (code, log, output) = Run("--game", "classic", "--developer", "--oneone", game, "--load", "SPM", "--size", "320x240", "--mute",
                                  "--headless", Path.Combine(dir, "loaded.png"));
        Assert.True(code == 0, $"exit {code}\n{log}\n{output}");
        Assert.Contains("load: the save was made with the rules module story-bits 0.0.0, which is not on here: the game may not play as it was saved", output);
    }
}
