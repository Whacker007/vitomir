// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Security.Cryptography;
using System.Text;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The setup and import tool's working half**, on fake folder trees, a small fake manifest and a fake machine
/// (the cloud has no registry): the manifest's text and its rules, the one this program carries, finding GOG's and Steam's copies, the
/// check (missing, changed by size, changed in place, the overriding files left out, the rest ignored), the import (copied, verified,
/// the marker last, carried on, set aside, refused by name, no room, the scripts whole or not at all), the imported data at a start,
/// 1.1's own saves listed and imported without writing over one, and FFmpeg not found.
/// </summary>
public sealed class SetupTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-setup-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A machine of the test's own: its registry, its drives, its environment and its free space.</summary>
    sealed class FakeMachine : ISetupMachine
    {
        public Dictionary<string, string> Registry { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Drives { get; } = [];
        public Dictionary<string, string> Env { get; } = [];
        public long Free { get; set; } = long.MaxValue / 2;

        public string? RegistryValue(string key, string name) => Registry.TryGetValue(key + "|" + name, out var v) ? v : null;
        public IReadOnlyList<string> FixedDrives() => Drives;
        public string? Environment(string name) => Env.TryGetValue(name, out var v) ? v : null;
        public long FreeBytes(string folder) => Free;
    }

    /// <summary>The importer of a test: writes a small file where it is asked to, or fails.</summary>
    sealed class FakeImporter(int code = 0) : ISaveImporter
    {
        public List<(string Slot, string Target, string Data)> Calls { get; } = [];
        public List<string?> Names { get; } = [];
        public (int Code, IReadOnlyList<string> Lines) Import(string slotFolder, string target, string dataFolder, CancellationToken cancel)
        {
            Calls.Add((slotFolder, target, dataFolder));
            if (code == 0) File.WriteAllText(target, "imported " + Path.GetFileName(slotFolder));
            return (code, [$"import: {Path.GetFileName(slotFolder)}"]);
        }
        public (int Code, IReadOnlyList<string> Lines) Import(string slotFolder, string target, string dataFolder, string? name, CancellationToken cancel)
        {
            Names.Add(name);
            return Import(slotFolder, target, dataFolder, cancel);
        }
    }

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A stand-in install: the six archives, classes.zip, a chronicle, a film, a loose sound, two scripts, and what Vitomir does not read.</summary>
    string MakeInstall(string name, bool scripts = true)
    {
        string dir = Path.Combine(root, name);
        foreach (var a in DataManifest.Archives) Write(Path.Combine(dir, a), "archive " + a);
        Write(Path.Combine(dir, "classes.zip"), "classes");
        Write(Path.Combine(dir, "Chronicles", "Prague.nsc"), "chronicle");
        Write(Path.Combine(dir, "Video", "intro.bik"), "film film film");
        Write(Path.Combine(dir, "Sounds", "Dialog", "line.mp3"), "a line");
        if (scripts)
        {
            Write(Path.Combine(dir, "Codex", "System", "CodexAI.java"), "class CodexAI {}");
            Write(Path.Combine(dir, "Codex", "Door.java"), "class Door {}");
        }
        Write(Path.Combine(dir, "Vampire.exe"), "a program");
        Write(Path.Combine(dir, "SaveGames", "SP0", "!main.nsg"), "a save");
        Write(Path.Combine(dir, "Extras", "art.jpg"), "extras");
        return dir;
    }

    /// <summary>The manifest of a stand-in install made with every file it has (the scripts too).</summary>
    DataManifest ManifestOf(string install) => DataManifest.Make(install, "test-data", 1, "a test's", cancel: TestContext.Current.CancellationToken);

    // ---- the manifest ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_manifest_names_only_what_Vitomir_reads_and_reads_back_its_own_text()
    {
        string install = MakeInstall("install");
        var m = ManifestOf(install);
        Assert.Equal(["Chronicles/Prague.nsc", "Codex.nob", "LMaterials.nob", "Levels.nob", "Local_Eng.nob", "Resource.nob", "Sounds.nob", "Sounds/Dialog/line.mp3", "Video/intro.bik", "classes.zip"],
                     m.Of(DataManifest.Game).Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.Equal(["Codex/Door.java", "Codex/System/CodexAI.java"], m.Of(DataManifest.Scripts).Select(f => f.Path).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(m.Files, f => f.Path.Contains("Vampire.exe", StringComparison.Ordinal) || f.Path.StartsWith("SaveGames", StringComparison.Ordinal) || f.Path.StartsWith("Extras", StringComparison.Ordinal));
        var film = m.Files.Single(f => f.Path == "Video/intro.bik");
        Assert.Equal(14, film.Size);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("film film film"))), film.Sha256);

        var again = DataManifest.Parse(m.Text(), "text");
        Assert.Equal(m.Text(), again.Text());
        Assert.Equal(("test-data", 1), (again.Name, again.Version));
        Assert.Equal(m.Files, again.Files);
        Assert.Equal(DataManifest.StandardOverrideFolders, again.OverrideFolders);
        Assert.True(again.Parts.Single(p => p.Name == DataManifest.Game).Required);
        Assert.False(again.Parts.Single(p => p.Name == DataManifest.Scripts).Required);
    }

    [Theory]
    [InlineData("file\tgame\t4\t" + "00000000000000000000000000000000000000000000000000000000000000zz" + "\ta.nob", "not a SHA-256")]
    [InlineData("file\tmods\t4\t" + "0000000000000000000000000000000000000000000000000000000000000000" + "\ta.nob", "not declared")]
    [InlineData("file\tgame\t4\t" + "0000000000000000000000000000000000000000000000000000000000000000" + "\t../a.nob", "not a path inside")]
    [InlineData("file\tgame\tfour\t" + "0000000000000000000000000000000000000000000000000000000000000000" + "\ta.nob", "not a size")]
    [InlineData("files\tgame", "not a manifest's line")]
    public void A_manifest_line_it_does_not_understand_is_refused_naming_the_line(string line, string why)
    {
        string text = "manifest\tm\nversion\t1\npart\tgame\trequired\tthe game\n" + line + "\n";
        var e = Assert.Throws<InvalidDataException>(() => DataManifest.Parse(text, "m.tsv"));
        Assert.Contains("m.tsv:4", e.Message, StringComparison.Ordinal);
        Assert.Contains(why, e.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => DataManifest.Parse("version\t1\npart\tgame\trequired\tx\n", "no name"));
        string twice = "manifest\tm\nversion\t1\npart\tgame\trequired\tx\nfile\tgame\t1\t" + new string('0', 64) + "\ta.nob\nfile\tgame\t1\t" + new string('0', 64) + "\tA.NOB\n";
        Assert.Contains("named twice", Assert.Throws<InvalidDataException>(() => DataManifest.Parse(twice, "t")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manifest_this_program_carries_is_1_1s_data_as_GOG_and_Steam_ship_it_by_name_size_and_hash_only()
    {
        var m = DataManifest.Builtin;
        Assert.Equal("vtmr-1.1-data", m.Name);
        Assert.Equal(1, m.Version);
        var game = m.Of(DataManifest.Game).ToList();
        Assert.Equal(3133, game.Count);                                  // the survey's 3,133 files, 1.26 GB
        Assert.Equal(1_258_199_186L, m.Bytes(DataManifest.Game));
        Assert.Equal(465, m.Of(DataManifest.Scripts).Count());           // the SDK's Codex sources
        foreach (var a in DataManifest.Archives.Append("classes.zip")) Assert.Contains(game, f => f.Path == a);
        Assert.Equal(11, game.Count(f => f.Path.StartsWith("Video/", StringComparison.Ordinal)));
        Assert.Equal(31, game.Count(f => f.Path.StartsWith("Chronicles/", StringComparison.Ordinal)));
        Assert.Equal(3084, game.Count(f => f.Path.StartsWith("Sounds/", StringComparison.Ordinal)));
        Assert.All(m.Files, f => Assert.Equal(f.Part, DataManifest.PartOf(f.Path)));
        // The resource is the source tree's file, as it is (when the tree is beside the tests).
        if (SourceFile() is { } source) Assert.Equal(File.ReadAllText(source).ReplaceLineEndings("\n"), m.Text());
    }

    static string? SourceFile()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string f = Path.Combine(d.FullName, "src", "Vitomir.Platform", "Setup", "Manifests", DataManifest.BuiltinName);
            if (File.Exists(f)) return f;
        }
        return null;
    }

    // ---- finding ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void GOG_and_Steam_copies_are_found_by_their_records_and_the_usual_folders_each_with_how()
    {
        var machine = new FakeMachine();
        // GOG: its registry entry names a folder with its goggame file.
        string gog = MakeInstall("gog-place");
        Write(Path.Combine(gog, InstallFinder.GogInfo), "{}");
        machine.Registry[InstallFinder.GogKeys[0] + "|path"] = gog;
        // Steam: the user's Steam folder, whose libraryfolders.vdf names a second library holding the game's app manifest.
        string steam = Path.Combine(root, "Steam"), library = Path.Combine(root, "Library2");
        Write(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
              "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + library.Replace("\\", "\\\\") + "\"\n\t}\n}\n");
        Write(Path.Combine(library, "steamapps", $"appmanifest_{InstallFinder.SteamApp}.acf"),
              "\"AppState\"\n{\n\t\"appid\"\t\t\"559680\"\n\t\"installdir\"\t\t\"" + InstallFinder.StoreFolderName + "\"\n}\n");
        string steamGame = Path.Combine(library, "steamapps", "common", InstallFinder.StoreFolderName);
        MakeInstallAt(steamGame);
        machine.Registry[InstallFinder.SteamKeys[0].Key + "|" + InstallFinder.SteamKeys[0].Value] = steam;
        // A usual folder on a drive: the GOG copy again (found twice: one row, both ways), and a copy that is neither store's.
        string drive = Path.Combine(root, "D");
        string usual = Path.Combine(drive, "Games", "VTMR");
        MakeInstallAt(usual);
        string notAnInstall = Path.Combine(drive, "GOG Games", InstallFinder.StoreFolderName);
        Write(Path.Combine(notAnInstall, "readme.txt"), "no Resource.nob here");
        machine.Drives.Add(drive);
        machine.Env["VTMR_GAME_DIR"] = gog;

        var found = InstallFinder.Find(machine, named: usual);
        Assert.Equal([gog, steamGame, usual], found.Select(f => f.Folder));
        Assert.Equal(Store.Gog, found[0].Store);
        Assert.Equal(["GOG's registry entry", "VTMR_GAME_DIR (a developer's)"], found[0].FoundBy);
        Assert.Equal(Store.Steam, found[1].Store);
        Assert.Contains("Steam's app manifest in the library " + library, found[1].FoundBy);
        Assert.Equal(Store.Unknown, found[2].Store);
        Assert.False(found[2].Supported);
        Assert.Equal(["the folder named"], found[2].FoundBy);                     // a player's own copy is found only when named
        Assert.Equal([gog, steamGame], InstallFinder.Find(machine).Select(f => f.Folder));
        // Nor is a copy in a store's usual folder that carries neither store's marks (a copy put there by hand, perhaps modified).
        string unmarked = Path.Combine(drive, "SteamLibrary", "steamapps", "common", InstallFinder.StoreFolderName);
        MakeInstallAt(unmarked);
        Assert.Equal([gog, steamGame], InstallFinder.Find(machine).Select(f => f.Folder));
        Assert.Equal(gog, SetupService.Choose(found)!.Folder);                     // the first GOG or Steam copy
        Assert.Equal(usual, SetupService.Choose(found, usual)!.Folder);            // or the one named
        Assert.Equal(Store.Steam, InstallFinder.StoreOf(steamGame));               // by its library's app manifest
        Write(Path.Combine(usual, "installscript.vdf"), "\"InstallScript\" {}");
        Assert.Equal(Store.Steam, InstallFinder.StoreOf(usual));                   // or Steam's own file beside the game
    }

    void MakeInstallAt(string dir)
    {
        foreach (var a in DataManifest.Archives) Write(Path.Combine(dir, a), "archive " + a);
    }

    [Fact]
    public void Valve_key_values_are_read_with_their_escapes()
    {
        Assert.Equal([@"D:\Games\Steam", @"E:\SteamLibrary"],
                     InstallFinder.VdfValues("\"path\"\t\t\"D:\\\\Games\\\\Steam\"\n\"PATH\" \"E:\\\\SteamLibrary\"\n\"label\" \"\"", "path"));
    }

    // ---- the check -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_check_names_each_missing_or_changed_file_and_the_overriding_ones_and_ignores_the_rest()
    {
        string install = MakeInstall("install");
        var m = ManifestOf(install);
        var whole = DataCheck.Run(install, m, CheckDepth.Full, cancel: TestContext.Current.CancellationToken);
        Assert.True(whole.GameWhole);
        Assert.Equal(PartState.Whole, whole.Scripts);
        Assert.Empty(whole.Overriding);
        Assert.Equal(3, whole.Ignored);                                           // the game's program, a save, the extras: not looked at

        File.Delete(Path.Combine(install, "Video", "intro.bik"));
        File.WriteAllText(Path.Combine(install, "classes.zip"), "classes, longer");
        File.WriteAllText(Path.Combine(install, "Chronicles", "Prague.nsc"), "chronicXe");   // the same size, other bytes
        Write(Path.Combine(install, "Mod.nob"), "a mod's archive");
        Write(Path.Combine(install, "Materials", "Prague", "wall.tga"), "a texture pack's");
        Write(Path.Combine(install, "3d", "models", "chair.nod"), "a texture pack's model");
        Write(Path.Combine(install, "Sounds", "extra.mp3"), "a mod's sound");
        Write(Path.Combine(install, "NodSDK", "readme.txt"), "the SDK's");
        Write(Path.Combine(install, "Codex", "ModScript.java"), "a mod's script");

        var sizes = DataCheck.Run(install, m, CheckDepth.Sizes, cancel: TestContext.Current.CancellationToken);
        Assert.Equal(["Video/intro.bik: missing", "classes.zip: 15 bytes, not 7"], sizes.Problems.Select(p => p.ToString()).Order(StringComparer.Ordinal));
        var full = DataCheck.Run(install, m, CheckDepth.Full, cancel: TestContext.Current.CancellationToken);
        Assert.Equal(3, full.Problems.Count);
        Assert.Contains(full.Problems, p => p.Path == "Chronicles/Prague.nsc" && p.Why.StartsWith("other bytes of the same size", StringComparison.Ordinal));
        Assert.Equal(["3d/models/chair.nod", "Codex/ModScript.java", "Materials/Prague/wall.tga", "Mod.nob", "Sounds/extra.mp3"], full.Overriding);
        Assert.Equal(4, full.Ignored);                                            // and the SDK's readme
        Assert.False(full.GameWhole);
        Assert.Equal(Store.Unknown, full.Store);
    }

    /// <summary>A real archive (a ZIP, stored) holding <paramref name="entries"/>, at <paramref name="path"/>.</summary>
    static void Archive(string path, params string[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Delete(path);                                                          // a stand-in archive's text there before it
        using var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        foreach (var e in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(e, System.IO.Compression.CompressionLevel.NoCompression).Open());
            w.Write("an entry");
        }
    }

    [Fact]
    public void A_file_left_out_is_said_to_replace_only_when_it_has_the_path_of_one_of_1_1s_own_and_else_to_add()
    {
        // The SDK's loose Materials files only add new ones; a texture pack's replace 1.1's. 1.1's own paths are the manifest's and
        // its archives' entries (the archives' directories read).
        string install = MakeInstall("replace-or-add", scripts: false);
        Archive(Path.Combine(install, "LMaterials.nob"), "materials/Prague/wall.tga", "materials/General/ankhs.tga");
        var m = ManifestOf(install);
        Write(Path.Combine(install, "Materials", "Prague", "WALL.tga"), "a texture pack's wall");   // 1.1's, any spelling: replaces
        Write(Path.Combine(install, "Materials", "SDK", "grid.tga"), "the SDK's");                 // new: adds
        Write(Path.Combine(install, "Materials", "SDK", "marker.tga"), "the SDK's");
        Write(Path.Combine(install, "Sounds", "extra.mp3"), "a mod's new sound");                  // adds
        Archive(Path.Combine(install, "Mod.nob"), "materials/General/ankhs.tga", "materials/mod/new.tga");   // holds one of 1.1's: replaces
        Archive(Path.Combine(install, "Extra.nob"), "materials/extra/only.tga");                   // only new paths: adds
        Write(Path.Combine(install, "Broken.nob"), "not a zip");                                   // does not read: adds

        var report = DataCheck.Run(install, m, CheckDepth.Sizes, cancel: TestContext.Current.CancellationToken);
        Assert.True(report.GameWhole);
        Assert.Equal(["Broken.nob", "Extra.nob", "Materials/Prague/WALL.tga", "Materials/SDK/grid.tga", "Materials/SDK/marker.tga", "Mod.nob", "Sounds/extra.mp3"],
                     report.Overriding);
        Assert.Equal(["Materials/Prague/WALL.tga", "Mod.nob"], report.Replacing);
        Assert.Equal(["Broken.nob", "Extra.nob", "Materials/SDK/grid.tga", "Materials/SDK/marker.tga", "Sounds/extra.mp3"], report.Adding);

        // None left out: none either way, and no archive is opened for it.
        string plain = MakeInstall("nothing-left-out", scripts: false);
        var none = DataCheck.Run(plain, ManifestOf(plain), CheckDepth.Sizes, cancel: TestContext.Current.CancellationToken);
        Assert.Empty(none.Replacing);
        Assert.Empty(none.Adding);
    }

    [Fact]
    public void Each_pass_is_named_and_its_progress_names_only_the_files_it_reads()
    {
        // (A GOG copy, which has no Codex sources: the check's bar ended on Codex/*.java, files never read).
        string install = MakeInstall("no-sdk", scripts: true);
        var m = ManifestOf(install);                                               // knows the SDK's sources
        Directory.Delete(Path.Combine(install, "Codex"), recursive: true);         // a GOG copy has none
        foreach (var depth in new[] { CheckDepth.Sizes, CheckDepth.Full })
        {
            var seen = new List<SetupProgress>();
            DataCheck.Run(install, m, depth, new SyncProgress(seen.Add), TestContext.Current.CancellationToken);
            Assert.All(seen, p => Assert.Equal(depth == CheckDepth.Full ? SetupProgress.Hashes : SetupProgress.Sizes, p.Step));
            Assert.DoesNotContain(seen, p => p.Item.StartsWith("Codex/", StringComparison.Ordinal));
            Assert.Equal(seen[^1].Total, seen[^1].Done);                            // and it reaches its end
        }
        var steps = new List<SetupProgress>();
        DataImport.Run(install, ImportedData.Folder(UserData), m, new FakeMachine(), "the tests", new SyncProgress(steps.Add), TestContext.Current.CancellationToken);
        Assert.Equal([SetupProgress.Sizes, SetupProgress.Copy], steps.Select(p => p.Step).Distinct());
        Assert.DoesNotContain(steps, p => p.Item.StartsWith("Codex/", StringComparison.Ordinal));
        Assert.All(steps.GroupBy(p => p.Step), g => Assert.Equal(g.Last().Total, g.Last().Done));
    }

    [Fact]
    public void The_setups_words_are_plain_ASCII()
    {
        Assert.Equal("SP4 -> SP4.vsave ... - \"a\" 'b' 2x3", SetupWords.Plain("SP4 \u2192 SP4.vsave \u2026 \u2014 \u201ca\u201d \u2018b\u2019 2\u00d73"));
        Assert.Equal("plain words", SetupWords.Plain("plain words"));
        Assert.Equal("C:\\Jeux\\caf\u00e9", SetupWords.Plain("C:\\Jeux\\caf\u00e9"));        // a folder's own letters stay
        Assert.All(new[] { SetupWords.Unsupported, FFmpegSearch.Skipped, FFmpegSearch.WingetLine },
                   w => Assert.True(w.All(c => c < 128), w));
    }

    [Fact]
    public void The_check_finds_a_file_however_its_name_is_spelt_and_the_scripts_whole_or_not()
    {
        string install = MakeInstall("install");
        var m = ManifestOf(install);
        // Another copy of the same files, its names spelt otherwise (as an install copied by hand can have them).
        string other = Path.Combine(root, "spelt");
        foreach (var f in m.Files)
        {
            string spelt = f.Path.StartsWith("Chronicles/", StringComparison.Ordinal) ? "chronicles/" + Path.GetFileName(f.Path).ToUpperInvariant() : f.Path.ToLowerInvariant();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(other, spelt))!);
            File.Copy(Path.Combine(install, f.Path), Path.Combine(other, spelt));
        }
        var spelt2 = DataCheck.Run(other, m, CheckDepth.Full, cancel: TestContext.Current.CancellationToken);
        Assert.True(spelt2.GameWhole);
        Assert.Equal(Path.Combine(other, "chronicles", "PRAGUE.NSC"), spelt2.Found["Chronicles/Prague.nsc"]);

        File.Delete(Path.Combine(install, "Codex", "Door.java"));
        Assert.Equal(PartState.Broken, DataCheck.Run(install, m, CheckDepth.Sizes, cancel: TestContext.Current.CancellationToken).Scripts);
        Directory.Delete(Path.Combine(install, "Codex"), recursive: true);
        var none = DataCheck.Run(install, m, CheckDepth.Sizes, cancel: TestContext.Current.CancellationToken);
        Assert.Equal(PartState.Absent, none.Scripts);
        Assert.True(none.GameWhole);                                              // the scripts are optional
    }

    // ---- the import ------------------------------------------------------------------------------------------------------------

    string UserData => Path.Combine(root, "user");

    [Fact]
    public void The_import_copies_the_manifests_files_checks_each_and_writes_its_marker_last()
    {
        string install = MakeInstall("install");
        Write(Path.Combine(install, "masquerade.ini"), "[gameOptions]\n");
        var m = ManifestOf(install);
        Write(Path.Combine(install, "Materials", "wall.tga"), "a texture pack's");          // left out
        string target = ImportedData.Folder(UserData);
        var steps = new List<string>();
        var report = DataImport.Run(install, target, m, new FakeMachine(), "the tests", new SyncProgress(p => steps.Add(p.Step)), cancel: TestContext.Current.CancellationToken);
        Assert.True(report.Accepted, string.Join("\n", report.Problems));
        var game = m.Files.Where(f => f.Part == DataManifest.Game).ToList();
        Assert.Equal(game.Count, report.Copied);                                  // the game's files only
        Assert.Equal(PartState.Whole, report.Scripts);
        Assert.True(report.OptionsCopied);
        Assert.Equal(["Materials/wall.tga"], report.Overriding);
        Assert.Equal([SetupProgress.Sizes, SetupProgress.Copy], steps.Distinct());       // the two passes by their names, in order
        foreach (var f in game) Assert.Equal(File.ReadAllBytes(Path.Combine(install, f.Path)), File.ReadAllBytes(DataImport.Place(target, f.Path)));
        Assert.False(Directory.Exists(Path.Combine(target, "Codex")));            // the SDK's sources: known, never imported
        Assert.False(File.Exists(Path.Combine(target, "Materials", "wall.tga")));
        Assert.False(File.Exists(Path.Combine(target, "Vampire.exe")));
        Assert.False(Directory.Exists(Path.Combine(target, "SaveGames")));
        var marker = ImportedData.ReadMarker(target)!;
        Assert.Equal(("test-data", 1, Path.GetFullPath(install), "unknown"), (marker.Manifest, marker.ManifestVersion, marker.Source, marker.Store));
        Assert.Equal(game.Count, marker.Files);
        Assert.Equal(m.Bytes(DataManifest.Game), marker.Bytes);
        Assert.True(!marker.Scripts && marker.Options);
        Assert.Equal(m.Text(), File.ReadAllText(Path.Combine(target, ImportedData.ManifestCopyName)));
        Assert.Equal(Path.GetFullPath(install), ImportedData.SourceOf(target));
        Assert.True(marker.MadeUtc > new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        // Run again: everything is checked again and nothing copied; a file put there by hand is set aside, never read.
        Write(Path.Combine(target, "Materials", "by-hand.tga"), "put there by hand");
        Write(Path.Combine(target, "Video", "intro.bik.partial"), "a stopped copy's");
        var again = DataImport.Run(install, target, m, new FakeMachine(), "the tests", cancel: TestContext.Current.CancellationToken);
        Assert.True(again.Accepted);
        Assert.Equal((0, game.Count, 1), (again.Copied, again.Kept, again.SetAside));
        Assert.False(File.Exists(Path.Combine(target, "Materials", "by-hand.tga")));
        Assert.False(File.Exists(Path.Combine(target, "Video", "intro.bik.partial")));
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "user", ImportedData.ImportedFolderName, "set-aside"), "by-hand.tga", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(install, ImportedData.MarkerName)));         // the install is only read
    }

    /// <summary>A progress that reports on the caller's thread, in order.</summary>
    sealed class SyncProgress(Action<SetupProgress> on) : IProgress<SetupProgress>
    {
        public void Report(SetupProgress value) => on(value);
    }

    [Fact]
    public void An_import_is_refused_by_name_before_anything_is_written_when_a_file_is_missing_or_there_is_no_room()
    {
        string install = MakeInstall("install");
        var m = ManifestOf(install);
        string target = ImportedData.Folder(UserData);
        File.Delete(Path.Combine(install, "Levels.nob"));
        var missing = DataImport.Run(install, target, m, new FakeMachine(), "the tests", cancel: TestContext.Current.CancellationToken);
        Assert.Equal(ImportRefusal.DataNotWhole, missing.Refusal);
        Assert.Equal(["Levels.nob: missing"], missing.Problems);
        Assert.False(Directory.Exists(target));
        File.WriteAllText(Path.Combine(install, "Levels.nob"), "archive Levels.nob");

        var full = DataImport.Run(install, target, m, new FakeMachine { Free = 1000 }, "the tests", cancel: TestContext.Current.CancellationToken);
        Assert.Equal(ImportRefusal.NoRoom, full.Refusal);
        Assert.Contains("not enough room", full.Problems.Single(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));

        Assert.Equal(ImportRefusal.Inside, DataImport.Run(install, Path.Combine(install, "imported"), m, new FakeMachine(), "t", cancel: TestContext.Current.CancellationToken).Refusal);
        Assert.Equal(ImportRefusal.NotAnInstall, DataImport.Run(Path.Combine(root, "nowhere"), target, m, new FakeMachine(), "t", cancel: TestContext.Current.CancellationToken).Refusal);
    }

    [Fact]
    public void A_file_changed_in_place_is_caught_by_its_hash_as_it_is_copied_and_the_import_is_not_accepted()
    {
        string install = MakeInstall("install");
        var m = ManifestOf(install);
        File.WriteAllText(Path.Combine(install, "Sounds", "Dialog", "line.mp3"), "a lime");     // the same size
        string target = ImportedData.Folder(UserData);
        var report = DataImport.Run(install, target, m, new FakeMachine(), "the tests", cancel: TestContext.Current.CancellationToken);
        Assert.Equal(ImportRefusal.DataNotWhole, report.Refusal);
        Assert.StartsWith("Sounds/Dialog/line.mp3: other bytes in the install than the manifest's", report.Problems.Single(), StringComparison.Ordinal);
        Assert.Null(ImportedData.ReadMarker(target));
        Assert.False(File.Exists(Path.Combine(target, "Sounds", "Dialog", "line.mp3")));
        Assert.Empty(Directory.EnumerateFiles(target, "*.partial", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_stopped_import_is_not_used_and_the_next_one_carries_on()
    {
        string install = MakeInstall("install");
        var m = ManifestOf(install);
        string target = ImportedData.Folder(UserData);
        using var stop = new CancellationTokenSource();
        int imported = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => DataImport.Run(install, target, m, new FakeMachine(), "the tests",
            new SyncProgress(p => { if (p.Step == SetupProgress.Copy && ++imported == 4) stop.Cancel(); }), stop.Token));
        Assert.Null(ImportedData.ReadMarker(target));
        Assert.True(ImportedData.Open(UserData, m).None);
        var carried = DataImport.Run(install, target, m, new FakeMachine(), "the tests", cancel: TestContext.Current.CancellationToken);
        Assert.True(carried.Accepted);
        Assert.Equal(4, carried.Kept);
        Assert.Equal(m.Of(DataManifest.Game).Count() - 4, carried.Copied);
    }

    /// <summary>
    /// The game runs Codex.nob's own classes, so **the SDK's sources are never imported**, whole, changed or absent; the check
    /// still says how they stand. An older import's sources (a marker that says it took them) are set aside by the next import, and a
    /// start of the older one still checks them.
    /// </summary>
    [Fact]
    public void The_scripts_sources_are_never_imported()
    {
        string install = MakeInstall("install");
        var m = ManifestOf(install);
        string target = ImportedData.Folder(UserData);
        var whole = DataImport.Run(install, target, m, new FakeMachine(), "t", cancel: TestContext.Current.CancellationToken);
        Assert.True(whole.Accepted);
        Assert.Equal(PartState.Whole, whole.Scripts);
        Assert.False(Directory.Exists(Path.Combine(target, "Codex")));
        Assert.False(ImportedData.ReadMarker(target)!.Scripts);
        Assert.True(ImportedData.Open(UserData, m).Ready);                       // a start does not ask for what was not taken

        // An import made earlier, with the sources: its start still checks them; the next import sets them aside.
        foreach (var f in m.Of(DataManifest.Scripts)) { Directory.CreateDirectory(Path.GetDirectoryName(DataImport.Place(target, f.Path))!); File.Copy(Path.Combine(install, f.Path), DataImport.Place(target, f.Path)); }
        var older = ImportedData.ReadMarker(target)! with { Scripts = true };
        File.WriteAllText(Path.Combine(target, ImportedData.MarkerName), System.Text.Json.JsonSerializer.Serialize(older, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        Assert.True(ImportedData.Open(UserData, m).Ready);
        File.Delete(DataImport.Place(target, "Codex/Door.java"));
        Assert.Contains("Codex/Door.java: missing", ImportedData.Open(UserData, m).Problems);

        File.Delete(Path.Combine(install, "Codex", "Door.java"));
        var broken = DataImport.Run(install, target, m, new FakeMachine(), "t", cancel: TestContext.Current.CancellationToken);
        Assert.True(broken.Accepted);
        Assert.Equal(PartState.Broken, broken.Scripts);
        Assert.Equal(1, broken.SetAside);                                         // the older import's CodexAI.java
        Assert.False(Directory.Exists(Path.Combine(target, "Codex")));
        Assert.False(ImportedData.ReadMarker(target)!.Scripts);
        Assert.True(ImportedData.Open(UserData, m).Ready);
    }

    [Fact]
    public void A_start_finds_the_imported_data_ready_damaged_or_of_another_manifest()
    {
        string install = MakeInstall("install");
        var m = ManifestOf(install);
        Assert.True(ImportedData.Open(UserData, m).None);
        Assert.True(DataImport.Run(install, ImportedData.Folder(UserData), m, new FakeMachine(), "t", cancel: TestContext.Current.CancellationToken).Accepted);
        var ready = ImportedData.Open(UserData, m);
        Assert.True(ready.Ready);
        Assert.Empty(ready.Notes);

        var other = new DataManifest { Name = "vtmr-1.1-data", Version = 2, Parts = m.Parts, Files = m.Files };
        Assert.Contains("import it again", ImportedData.Open(UserData, other).Problems.Single(), StringComparison.Ordinal);
        var developer = ImportedData.Open(UserData, other, developer: true);
        Assert.True(developer.Ready);
        Assert.Contains("taken (--developer)", developer.Notes.Single(), StringComparison.Ordinal);

        File.Delete(DataImport.Place(ImportedData.Folder(UserData), "Video/intro.bik"));
        var damaged = ImportedData.Open(UserData, m);
        Assert.False(damaged.Ready);
        Assert.Equal(["Video/intro.bik: missing"], damaged.Problems);
    }

    // ---- 1.1's own saves -------------------------------------------------------------------------------------------------------

    string MakeSaves()
    {
        string install = MakeInstall("with-saves");
        foreach (var (slot, said) in new[] { ("SP0", "Autosave"), ("SP4", "Monastery 1 (01:28:05)"), ("MP1", "The Salon"), ("SP31", "beyond 1.1's slots") })
        {
            var head = new byte[0x78 + 10];
            Encoding.ASCII.GetBytes(said).CopyTo(head, 0);
            Directory.CreateDirectory(Path.Combine(install, "SaveGames", slot));
            File.WriteAllBytes(Path.Combine(install, "SaveGames", slot, "!main.nsg"), head);
        }
        Directory.CreateDirectory(Path.Combine(install, "SaveGames", "SP7"));         // no !main.nsg: not a save
        Write(Path.Combine(install, "Crypt", "npf000001.npf"), "an actor");
        File.WriteAllBytes(Path.Combine(install, "Crypt", "npf000002.npf"), new Vitomir.TestData.NpfBytes { Login = "npf000002" }.Build());
        File.WriteAllBytes(Path.Combine(install, "Crypt", "npf000003.npf"), new Vitomir.TestData.NpfBytes { Name = "Old", Version = 4 }.Build());
        return install;
    }

    [Fact]
    public void One_one_s_saves_network_games_and_characters_are_listed_with_their_names()
    {
        var items = OriginalGames.List(MakeSaves());
        Assert.Equal(["SP0", "SP4", "MP1", "npf000001", "npf000002", "npf000003"], items.Select(i => i.Id));
        Assert.Equal(["Autosave", "Monastery 1 (01:28:05)", "The Salon"], items.Take(3).Select(i => i.Description));
        Assert.Equal(["SP0.vsave", "SP4.vsave", "MP1.vsave", null, "Anna.vplayer", null], items.Select(i => i.ImportsAs));
        Assert.Equal([OriginalKind.Save, OriginalKind.Save, OriginalKind.NetworkSave, OriginalKind.Character, OriginalKind.Character, OriginalKind.Character], items.Select(i => i.Kind));
        // A character as 1.1's select list has it (its name and clan's name), its experience; one 1.1 itself would not list, with why.
        Assert.Equal("Anna (Brujah), 1,250 experience", items[4].Description);
        Assert.Equal("Anna", items[4].Name);
        Assert.Contains("shorter than the 108-byte header", items[3].Why, StringComparison.Ordinal);
        Assert.Equal("1.1's own select list leaves it out: it is version 4; 1.1 reads only 5", items[5].Why);
    }

    [Fact]
    public void A_character_is_imported_into_the_Crypt_by_its_name_never_over_one_kept_without_being_told()
    {
        string install = MakeSaves();
        var importer = new FakeImporter();
        var service = new SetupService(UserData, ManifestOf(install), new FakeMachine(), importer, "the tests");
        var cancel = TestContext.Current.CancellationToken;
        var first = service.ImportOriginals(install, [new("npf000002")], replace: false, cancel).Single();
        Assert.True(first.Imported, first.Why);
        Assert.Equal(Path.Combine(service.CryptFolder, "Anna.vplayer"), first.Target);
        Assert.Equal("imported npf000002.npf", File.ReadAllText(first.Target!));
        Assert.Equal([null], importer.Names);                                         // its own name

        File.Move(first.Target!, Path.Combine(service.CryptFolder, "anna.vplayer"));   // kept under another spelling: the same character
        File.WriteAllText(Path.Combine(service.CryptFolder, "anna.vplayer"), "the player's own Anna");
        var refused = service.ImportOriginals(install, [new("npf000002")], replace: false, cancel).Single();
        Assert.True(refused.Refused);
        Assert.Contains("a character called Anna is kept already", refused.Why, StringComparison.Ordinal);
        Assert.Contains("npf000002=<name>", refused.Why, StringComparison.Ordinal);
        Assert.Single(importer.Calls);                                                 // refused before anything ran

        var replaced = service.ImportOriginals(install, [new("npf000002")], replace: true, cancel).Single();
        Assert.True(replaced.Imported, replaced.Why);
        string kept = Directory.GetFiles(service.CryptFolder, "*.vplayer").Single();
        Assert.Equal("imported npf000002.npf", File.ReadAllText(kept));
        Assert.Equal("the player's own Anna", File.ReadAllText(kept + ".previous"));

        var renamed = service.ImportOriginals(install, [new("npf000002", "Anna the Second")], replace: false, cancel).Single();
        Assert.True(renamed.Imported, renamed.Why);
        Assert.Equal(Path.Combine(service.CryptFolder, "Anna the Second.vplayer"), renamed.Target);
        Assert.Equal("Anna the Second", importer.Names[^1]);

        var others = service.ImportOriginals(install, [new("npf000003"), new("npf000002", "  ")], replace: true, cancel);
        Assert.All(others, r => Assert.True(r.Refused));
        Assert.Contains("version 4", others[0].Why, StringComparison.Ordinal);
        Assert.Contains("needs a name", others[1].Why, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(service.CryptFolder, ".setup-*"));
    }

    [Fact]
    public void A_save_is_imported_into_its_slot_and_nothing_is_written_over_without_being_told()
    {
        string install = MakeSaves();
        var importer = new FakeImporter();
        var service = new SetupService(UserData, ManifestOf(install), new FakeMachine(), importer, "the tests");
        var first = service.ImportOriginals(install, [new("SP4"), new("sp0", "SP12"), new("MP1")], replace: false, cancel: TestContext.Current.CancellationToken).ToList();
        Assert.All(first, r => Assert.True(r.Imported, r.Why));
        Assert.Equal(Path.Combine(service.SavesFolder, "SP4.vsave"), first[0].Target);
        Assert.Equal("imported SP0", File.ReadAllText(Path.Combine(service.SavesFolder, "SP12.vsave")));
        Assert.True(File.Exists(Path.Combine(service.SavesFolder, "MP1.vsave")));
        Assert.Equal(Path.GetFullPath(install), importer.Calls[0].Data);                 // nothing imported yet: the install's own files

        File.WriteAllText(Path.Combine(service.SavesFolder, "SP4.vsave"), "the player's own");
        var refused = service.ImportOriginals(install, [new("SP4")], replace: false, cancel: TestContext.Current.CancellationToken).Single();
        Assert.True(refused.Refused);
        Assert.Contains("SP4.vsave is there already", refused.Why, StringComparison.Ordinal);
        Assert.Equal("the player's own", File.ReadAllText(Path.Combine(service.SavesFolder, "SP4.vsave")));
        Assert.Equal(3, importer.Calls.Count);                                          // refused before anything ran

        var replaced = service.ImportOriginals(install, [new("SP4")], replace: true, cancel: TestContext.Current.CancellationToken).Single();
        Assert.True(replaced.Imported);
        Assert.Equal("imported SP4", File.ReadAllText(Path.Combine(service.SavesFolder, "SP4.vsave")));
        Assert.Equal("the player's own", File.ReadAllText(Path.Combine(service.SavesFolder, "SP4.vsave.previous")));

        var others = service.ImportOriginals(install, [new("npf000001"), new("SP9"), new("SP4", "MP3"), new("SP4", "SP30")], replace: true, cancel: TestContext.Current.CancellationToken);
        Assert.All(others, r => Assert.True(r.Refused));
        Assert.Contains("shorter than the 108-byte header", others[0].Why, StringComparison.Ordinal);
        Assert.Contains("no such save", others[1].Why, StringComparison.Ordinal);
        Assert.Contains("not a slot for it", others[2].Why, StringComparison.Ordinal);
        Assert.Contains("not a slot for it", others[3].Why, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(service.SavesFolder, ".setup-*"));

        var failing = new SetupService(UserData, ManifestOf(install), new FakeMachine(), new FakeImporter(code: 2), "t");
        var failed = failing.ImportOriginals(install, [new("SP4", "SP5")], replace: false, cancel: TestContext.Current.CancellationToken).Single();
        Assert.False(failed.Imported || failed.Refused);
        Assert.False(File.Exists(Path.Combine(service.SavesFolder, "SP5.vsave")));
    }

    [Fact]
    public void OriginalPick_reads_a_slot_to_import_into()
    {
        Assert.Equal(new OriginalPick("SP4", "SP12"), OriginalPick.Parse("SP4=SP12"));
        Assert.Equal(new OriginalPick("SP4"), OriginalPick.Parse("SP4"));
    }

    // ---- FFmpeg ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_file_named_ffmpeg_that_is_not_one_is_passed_over()
    {
        string fake = Path.Combine(root, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        File.WriteAllText(fake, "not a program");
        Assert.NotEqual(fake, FFmpegSearch.Find(fake, installerFolders: [])?.FFmpeg);     // run before it is believed: passed over
        Assert.NotEqual(fake, FFmpegSearch.Find(root, installerFolders: [])?.FFmpeg);     // the folder named: the same
        Assert.Equal("winget install --exact --id Gyan.FFmpeg", FFmpegSearch.WingetLine);
    }

    // ---- verify and repair the imported game files ---------------------------------------------------------------------------------

    [Fact]
    public void Verify_finds_a_missing_a_changed_and_an_extra_file_and_repair_copies_them_again_from_the_copy_they_came_from()
    {
        string install = MakeInstall("install", scripts: false);
        var m = ManifestOf(install);
        string target = ImportedData.Folder(UserData);
        var cancel = TestContext.Current.CancellationToken;
        Assert.True(DataImport.Run(install, target, m, new FakeMachine(), "the tests", cancel: cancel).Accepted);
        Assert.Contains("nothing is imported yet", DataVerify.Run(Path.Combine(root, "nobody"), cancel: cancel).Refusal, StringComparison.Ordinal);

        var steps = new List<string>();
        var good = DataVerify.Run(UserData, new SyncProgress(p => steps.Add(p.Step)), cancel);
        Assert.True(good.AllGood);
        Assert.Equal((m.Files.Count, m.Bytes(DataManifest.Game)), (good.Files, good.Bytes));
        Assert.Equal([SetupProgress.Hashes], steps.Distinct());                       // every byte read

        // Damaged: one gone, one changed at its size, one extra over them.
        File.Delete(Path.Combine(target, "Chronicles", "Prague.nsc"));
        File.WriteAllText(Path.Combine(target, "Video", "intro.bik"), "FILM FILM FILM");
        Write(Path.Combine(target, "Materials", "by-hand.tga"), "put there by hand");
        var bad = DataVerify.Run(UserData, cancel: cancel);
        Assert.False(bad.AllGood);
        Assert.Equal(["Chronicles/Prague.nsc"], bad.Missing.Select(p => p.Path));
        Assert.Equal(["Video/intro.bik"], bad.Changed.Select(p => p.Path));
        Assert.Equal(["Materials/by-hand.tga"], bad.Extra);

        // The copy of the game changed too: refused, saying which copy to select and to import again; nothing copied.
        File.WriteAllText(Path.Combine(install, "Video", "intro.bik"), "FILM film film");
        var refused = DataVerify.Repair(bad, cancel: cancel);
        Assert.False(refused.Repaired);
        Assert.Contains("is not as the manifest has it either (Video/intro.bik: other bytes than the manifest's)", refused.Refusal, StringComparison.Ordinal);
        Assert.Contains("Import again", refused.Refusal, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(target, "Chronicles", "Prague.nsc")));

        File.WriteAllText(Path.Combine(install, "Video", "intro.bik"), "film film film");
        var repaired = DataVerify.Repair(bad, cancel: cancel);
        Assert.True(repaired.Repaired, repaired.Refusal);
        Assert.Equal(["Chronicles/Prague.nsc", "Video/intro.bik"], repaired.Copied.Order(StringComparer.Ordinal));
        Assert.Equal(1, repaired.SetAside);
        Assert.True(DataVerify.Run(UserData, cancel: cancel).AllGood);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(root, "user", ImportedData.ImportedFolderName, "set-aside"), "by-hand.tga", SearchOption.AllDirectories));

        // The copy of the game gone: refused, naming it.
        File.Delete(Path.Combine(target, "classes.zip"));
        Directory.Move(install, install + "-moved");
        var gone = DataVerify.Repair(DataVerify.Run(UserData, cancel: cancel), cancel: cancel);
        Assert.Contains($"the copy of the game the files were imported from ({Path.GetFullPath(install)}) is not there any more", gone.Refusal, StringComparison.Ordinal);
    }
}
