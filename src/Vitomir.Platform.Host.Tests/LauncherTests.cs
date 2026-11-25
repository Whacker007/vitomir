// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Launcher;
using Vitomir.Platform.Interface;
using Vitomir.Platform.PlayerModules;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The launcher**, its working half with fakes: the check of the folder it runs from (each case:
/// Program Files either one by its known folder's path, a zip being browsed by Windows', 7-Zip's or WinRAR's temporary copy or the
/// zip's own shell path, a folder it cannot write to, one that is not there, a folder of the player's own); the Setup page's flow
/// **through <see cref="SetupService"/>** on a stand-in install (found, checked, imported with its progress, stopped and carried on,
/// 1.1's saves imported and asked about before one is replaced, FFmpeg's button running winget's line); the first page; Play starting
/// the game as a process (a fake), the launcher hidden while it runs and back after, a bad exit said with its log; the screen read
/// without a problem and each page drawn; the Modules page.
/// </summary>
public sealed class LauncherTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-launcher-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    string User => Path.Combine(root, "user");

    static string Rooted(params string[] parts) => Path.Combine([OperatingSystem.IsWindows() ? @"C:\" : "/c/", .. parts]);

    // ---- where it runs from -----------------------------------------------------------------------------------------------------

    static readonly LocationPlaces Places = new([Rooted("Program Files"), Rooted("Program Files (x86)")], [Rooted("Users", "player", "AppData", "Local", "Temp")]);

    static string? Writable(string folder) => null;

    [Theory]
    [InlineData("Program Files")]
    [InlineData("Program Files (x86)")]
    public void Program_Files_either_one_is_found_by_its_known_folders_path(string programs)
    {
        var problems = Location.Check(Rooted(programs, "Vitomir", "bin"), Places, Writable, isFile: _ => false);
        Assert.Contains(problems, p => p.Kind == LocationKind.ProgramFiles && p.Words.Contains(Rooted(programs), StringComparison.Ordinal));
        // By the known folder's path, not its name: one named so elsewhere is not it, and the folder itself is.
        Assert.DoesNotContain(Location.Check(Rooted("Games", programs, "Vitomir"), Places, Writable, _ => false), p => p.Kind == LocationKind.ProgramFiles);
        Assert.DoesNotContain(Location.Check(Rooted(programs + " extra", "Vitomir"), Places, Writable, _ => false), p => p.Kind == LocationKind.ProgramFiles);
        var other = Places with { ProgramFiles = [Rooted("Programme")] };   // a Windows in another language
        Assert.Contains(Location.Check(Rooted("Programme", "Vitomir"), other, Writable, _ => false), p => p.Kind == LocationKind.ProgramFiles);
        Assert.Equal(Location.Advice, Location.Words(problems)[^1]);
        Assert.Contains(@"C:\Games\Vitomir", Location.Advice, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Temp1_VitomirPlatform-1.0.zip")]     // Explorer's
    [InlineData("Temp2_VitomirPlatform (1).zip")]
    [InlineData("7zO4A2B3C1D")]                          // 7-Zip's
    [InlineData("Rar$EXa1234.5678")]                     // WinRAR's
    public void A_zip_being_browsed_is_found_by_the_temporary_copy_Windows_or_a_zip_program_makes(string made)
    {
        string temp = Places.Temp[0];
        var problems = Location.Check(Path.Combine(temp, made, "VitomirPlatform", "bin"), Places, _ => "never asked", _ => false);
        var zip = Assert.Single(problems, p => p.Kind == LocationKind.InsideZip);
        Assert.Contains(made, zip.Words, StringComparison.Ordinal);
        Assert.DoesNotContain(problems, p => p.Kind == LocationKind.NotWritable);   // a zip's copy: the zip is what to say
        // A folder of the temporary folder that is no zip's copy is not it.
        Assert.DoesNotContain(Location.Check(Path.Combine(temp, "SomethingElse", "bin"), Places, Writable, _ => false), p => p.Kind == LocationKind.InsideZip);
    }

    [Fact]
    public void A_zip_being_browsed_is_found_by_its_own_shell_path_when_the_zip_is_a_file()
    {
        string zip = Path.Combine(root, "Downloads", "VitomirPlatform-1.0.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        File.WriteAllText(zip, "a zip");
        var problems = Location.Check(Path.Combine(zip, "VitomirPlatform", "bin"), Places, Writable);
        Assert.Contains(problems, p => p.Kind == LocationKind.InsideZip && p.Words.Contains(zip, StringComparison.Ordinal));
        // A folder whose name ends in .zip is a folder of the player's.
        string folder = Path.Combine(root, "Games", "Vitomir.zip", "bin");
        Directory.CreateDirectory(folder);
        Assert.Empty(Location.Check(folder, Places, Location.WriteTest));
    }

    [Fact]
    public void A_folder_it_cannot_write_to_is_found_by_a_test_write_and_a_folder_of_the_players_own_is_all_well()
    {
        string own = Path.Combine(root, "Games", "Vitomir");
        Directory.CreateDirectory(own);
        Assert.Empty(Location.Check(own, Places, Location.WriteTest));
        Assert.Null(Location.WriteTest(own));
        Assert.Empty(Directory.EnumerateFileSystemEntries(own));                           // the test file deleted again

        var locked = Location.Check(own, Places, _ => "it is not allowed to");
        var problem = Assert.Single(locked);
        Assert.Equal(LocationKind.NotWritable, problem.Kind);
        Assert.Contains("cannot write in its folder", problem.Words, StringComparison.Ordinal);

        // A folder that is not there (a developer's --location): said, and nothing written there.
        var missing = Location.Check(Path.Combine(root, "nowhere"), Places, _ => throw new InvalidOperationException("not asked"));
        Assert.Equal(LocationKind.Missing, Assert.Single(missing).Kind);
        Assert.False(Directory.Exists(Path.Combine(root, "nowhere")));
        if (!OperatingSystem.IsWindows()) Assert.NotNull(Location.WriteTest("/proc"));     // a folder no one writes in: the real test says so
    }

    // ---- the Setup page, through SetupService ------------------------------------------------------------------------------------

    internal sealed class FakeMachine : ISetupMachine
    {
        /// <summary>GOG's record of a copy of the game, or none (then nothing is found).</summary>
        public string? Gog { get; init; }
        public string? RegistryValue(string key, string name) => Gog is not null && key == InstallFinder.GogKeys[0] && name == "path" ? Gog : null;
        public IReadOnlyList<string> FixedDrives() => [];
        public string? Environment(string name) => null;
        public long FreeBytes(string folder) => long.MaxValue / 2;
    }

    internal sealed class FakeImporter : ISaveImporter
    {
        public List<string> Calls { get; } = [];
        public (int Code, IReadOnlyList<string> Lines) Import(string slotFolder, string target, string dataFolder, CancellationToken cancel)
        {
            lock (Calls) Calls.Add(Path.GetFileName(slotFolder));
            File.WriteAllText(target, "imported " + Path.GetFileName(slotFolder));
            return (0, ["import: " + Path.GetFileName(slotFolder)]);
        }
    }

    internal sealed class FakeShell : ILauncherShell
    {
        public List<string> Opened { get; } = [];
        public List<string> Ran { get; } = [];
        public string? OpenFolder(string folder) { Opened.Add("folder " + folder); return null; }
        public string? OpenFile(string file) { Opened.Add("file " + file); return null; }
        public string? OpenLink(string url) { Opened.Add("link " + url); return null; }
        public Task<int> RunVisible(string line) { Ran.Add(line); return Task.FromResult(0); }
    }

    sealed class FakeGame(string log) : IRunningGame
    {
        public TaskCompletionSource<int> End { get; } = new();
        public int Id => 4242;
        public Task<int> Ended => End.Task;
        public IReadOnlyList<string> LastLines { get; set; } = [];
        public string LogPath => log;
    }

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>A stand-in install (as SetupTests makes them): the six archives, classes.zip, a chronicle, a film, sounds, 1.1's saves.</summary>
    string MakeInstall()
    {
        string dir = Path.Combine(root, "install");
        foreach (var a in DataManifest.Archives) Write(Path.Combine(dir, a), "archive " + a + new string('x', 64));
        Write(Path.Combine(dir, "classes.zip"), "classes");
        Write(Path.Combine(dir, "Chronicles", "Prague.nsc"), "chronicle");
        Write(Path.Combine(dir, "Video", "intro.bik"), "film film film");
        for (int i = 0; i < 6; i++) Write(Path.Combine(dir, "Sounds", "Dialog", $"line{i}.mp3"), "a line " + i);
        foreach (var (slot, said) in new[] { ("SP0", "Autosave"), ("SP4", "Monastery 1 (01:28:05)") })
        {
            var head = new byte[0x78 + 10];
            Encoding.ASCII.GetBytes(said).CopyTo(head, 0);
            Directory.CreateDirectory(Path.Combine(dir, "SaveGames", slot));
            File.WriteAllBytes(Path.Combine(dir, "SaveGames", slot, "!main.nsg"), head);
        }
        Write(Path.Combine(dir, "Crypt", "npf000001.npf"), "an actor");
        File.WriteAllBytes(Path.Combine(dir, "Crypt", "npf000002.npf"), new Vitomir.TestData.NpfBytes { Login = "npf000002" }.Build());   // Anna, a Brujah
        return dir;
    }

    /// <summary>The Setup page's lines, as launcher.log has them.</summary>
    List<string> PageLog { get; } = [];

    (Vitomir.Launcher.Launcher Launcher, FakeShell Shell, FakeImporter Importer, List<GameStart> Started, List<FakeGame> Games, string Install) Make(Func<string?>? gameDir = null, bool found = false, Func<string, FilmsPage>? films = null)
    {
        string install = MakeInstall();
        if (found) Write(Path.Combine(install, InstallFinder.GogInfo), "{}");     // a GOG copy, found by its record
        var manifest = DataManifest.Make(install, "test-data", 1, "a test's", cancel: TestContext.Current.CancellationToken);
        var shell = new FakeShell();
        var importer = new FakeImporter();
        var started = new List<GameStart>();
        var games = new List<FakeGame>();
        var options = new LauncherOptions
        {
            Developer = true, UserData = User, ProgramFolder = Path.Combine(root, "Vitomir"), GameProgram = Path.Combine(root, "Vitomir", "vitomir-platform.exe"),
            GameDir = gameDir ?? (() => null), ModulesFolder = Path.Combine(AppContext.BaseDirectory, "Modules"), NewFilms = films,
        };
        var service = new SetupService(User, manifest, new FakeMachine { Gog = found ? install : null }, importer, "the tests");
        var launcher = new Vitomir.Launcher.Launcher(options, new SetupPage(service, shell, line => { lock (PageLog) PageLog.Add(line); }), s =>
        {
            started.Add(s);
            var g = new FakeGame(s.LogPath);
            games.Add(g);
            return g;
        }, shell, _ => { });
        return (launcher, shell, importer, started, games, install);
    }

    [Fact]
    public void The_setup_page_finds_checks_imports_and_is_stopped_and_carried_on_through_the_SetupService()
    {
        var (launcher, _, _, _, _, install) = Make();
        launcher.Start([]);
        Assert.Equal("setup", launcher.Page);                                   // nothing imported: Setup first
        Assert.False(launcher.CanPlay);
        var page = launcher.Setup;
        Assert.Empty(page.Installs);                                            // the fake machine has no store's records

        // A folder typed that is not a copy of the game: said, nothing selected.
        page.Path = Path.Combine(root, "nothing");
        launcher.Act("usefolder");
        Assert.Contains("is not a folder", page.PathWhy, StringComparison.Ordinal);
        Directory.CreateDirectory(page.Path);
        launcher.Act("usefolder");
        Assert.Contains("has no Resource.nob", page.PathWhy, StringComparison.Ordinal);

        // Typed into the box, Enter: the folder used (--from's way), found and selected.
        launcher.Act("edit", "path");
        Assert.Equal("path", launcher.Editing);
        page.Path = "";
        launcher.Type(install + "x");
        launcher.Backspace();
        launcher.EndEdit(commit: true);
        Assert.Null(launcher.Editing);
        Assert.Null(page.PathWhy);
        Assert.Empty(page.Installs);                                            // nothing found: the folder typed is Another folder's
        Assert.True(page.Other);
        Assert.Equal(Path.GetFullPath(install), page.Chosen!.Folder);
        Assert.Equal(["SP0", "SP4", "npf000001", "npf000002"], page.Saves.Select(s => s.Id));

        // The check, by size, its result in words.
        launcher.Act("check");
        page.Wait();
        Assert.True(page.CheckReport!.GameWhole);
        Assert.StartsWith("All 15 game files are there (", page.Result[0].Text, StringComparison.Ordinal);
        Assert.Contains(page.Result, l => l.Warn && l.Text.Contains("not a GOG or Steam copy", StringComparison.Ordinal));

        // The import, stopped at its first file copied: nothing used, said so.
        var steps = new List<SetupProgress>();
        page.OnProgress = p =>
        {
            lock (steps) steps.Add(p);
            if (p.Step == SetupProgress.Copy) page.Cancel();
        };
        launcher.Act("import");
        Assert.True(page.Busy);
        page.Wait();
        Assert.False(page.Busy);
        Assert.Null(page.ImportReport);
        Assert.Equal("Import cancelled. Select Import to continue.", Assert.Single(page.Result).Text);
        Assert.False(page.Imported(developer: true));
        launcher.Recheck();
        Assert.False(launcher.CanPlay);

        // Carried on: what was copied kept, the rest copied, the marker written; Play now can.
        page.OnProgress = null;
        launcher.Act("import");
        page.Wait();
        var report = page.ImportReport!;
        Assert.True(report.Accepted);
        Assert.True(report.Kept >= 1, "the file copied before the stop is kept");
        Assert.Equal(15, report.Kept + report.Copied);
        Assert.Contains(steps, s => s.Step == SetupProgress.Copy && s.Total > 0);
        Assert.Contains(steps, s => s.Step == SetupProgress.Sizes);
        Assert.StartsWith("Imported 15 files (", page.Result[0].Text, StringComparison.Ordinal);
        Assert.EndsWith("The game is ready: go to Play.", page.Result[0].Text, StringComparison.Ordinal);
        Assert.All(page.Result.Skip(1), l => Assert.True(l.Detail, l.Text));     // the rest behind Details (D1), and in launcher.log
        Assert.True(page.Imported(developer: true));
        launcher.Poll();                                                        // the step's end seen: Play can now
        Assert.True(launcher.CanPlay);
        launcher.Fill();
        Assert.Equal("Import again", launcher.Data.Text("importWord"));
    }

    [Fact]
    public void The_setup_page_imports_the_saves_selected_and_asks_before_writing_over_one()
    {
        var (launcher, shell, importer, _, _, install) = Make();
        launcher.Start([]);
        launcher.Setup.UseFolder(install);
        var page = launcher.Setup;
        launcher.Fill();
        var rows = launcher.Data.Items("saves");
        Assert.Equal(4, rows.Count);
        Assert.Equal("", rows[1].Text("into"));                                 // where it goes: no more said (H3)
        Assert.False(rows[2].Flag("importable"));                               // a file 1.1 itself would not list: why said
        Assert.StartsWith("cannot be imported: it is shorter than the 108-byte header", rows[2].Text("into"), StringComparison.Ordinal);
        Assert.True(rows[3].Flag("importable"));                                // a character: to the Crypt under its name
        Assert.Equal("", rows[3].Text("into"));
        Assert.Contains("Anna (Brujah), 1,250 experience", rows[3].Text("desc"), StringComparison.Ordinal);

        // Selected by its checkbox (the row's own data), imported.
        rows[1].Set("selected", true);
        launcher.Handle(new UiAction("pick", "1", rows[1], null));
        Assert.Equal(["SP4"], page.Picked);
        launcher.Act("importsaves");
        page.Wait();
        launcher.Poll();
        Assert.Equal(["SP4"], importer.Calls);
        Assert.Equal("imported SP4", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP4.vsave")));
        Assert.Equal("Imported SP4.", Assert.Single(page.SaveResult).Text);
        launcher.Fill();
        Assert.Equal("already imported", launcher.Data.Items("saves")[1].Text("into"));
        Assert.Null(launcher.Notice);

        // Again over the save there now: refused before anything runs, and asked.
        File.WriteAllText(Path.Combine(page.Service.SavesFolder, "SP4.vsave"), "the player's own");
        page.Picked.Add("SP4");
        launcher.Act("importsaves");
        page.Wait();
        launcher.Poll();
        Assert.Single(importer.Calls);
        Assert.Equal("replace", launcher.Notice?.Kind);
        Assert.StartsWith("SP4 \"", launcher.Notice!.Lines[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(launcher.Notice!.Lines, l => l.Warn);                // nothing went wrong: no warning red (N1)
        Assert.Equal("This save is already imported. Replace it? The one there now is kept as a backup (.previous).", launcher.Notice!.Lines[^1].Text);
        Assert.Equal(["Replace", "Keep the existing one", "Cancel"], launcher.Notice!.Buttons.Select(b => b.Label));
        Assert.Equal("the player's own", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP4.vsave")));
        launcher.Act("play");                                                   // a notice up: the pages wait
        Assert.Equal("replace", launcher.Notice?.Kind);

        // Cancel (Escape): nothing written, the save still selected.
        launcher.Act("escape");
        Assert.Null(launcher.Notice);
        page.Wait();
        Assert.Equal("the player's own", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP4.vsave")));
        Assert.Equal(["SP4"], page.Picked);
        Assert.Contains(page.SaveResult, l => l.Text == "SP4 not imported.");

        // Asked again; Keep the existing one: nothing written, the save taken off the selection.
        launcher.Act("importsaves");
        page.Wait();
        launcher.Poll();
        launcher.Act("notice", "b");
        page.Wait();
        Assert.Equal("the player's own", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP4.vsave")));
        Assert.Empty(page.Picked);
        Assert.Contains(page.SaveResult, l => l.Text == "Kept the existing SP4.");

        // Asked again and replaced: the old one kept as .previous.
        page.Picked.Add("SP4");
        launcher.Act("importsaves");
        page.Wait();
        launcher.Poll();
        launcher.Act("notice", "a");
        page.Wait();
        Assert.Equal("imported SP4", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP4.vsave")));
        Assert.Equal("the player's own", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP4.vsave.previous")));

        // A character: into the Crypt; again, asked about the one kept by its name, and left.
        page.Picked.Add("npf000002");
        launcher.Act("importsaves");
        page.Wait();
        launcher.Poll();
        Assert.Equal("imported npf000002.npf", File.ReadAllText(Path.Combine(page.Service.CryptFolder, "Anna.vplayer")));
        page.Picked.Add("npf000002");
        launcher.Act("importsaves");
        page.Wait();
        launcher.Poll();
        Assert.Equal("Replace the existing character?", launcher.Notice?.Title);
        launcher.Act("notice", "b");
        page.Wait();
        // Each import in the launcher's log (a launcher.log once had no line for one): what, into which slot or file, replaced or left.
        lock (PageLog)
        {
            Assert.Contains(PageLog, l => l.StartsWith("setup page: SP4 \"Monastery 1 (01:28:05)\": imported into ", StringComparison.Ordinal) && l.EndsWith("SP4.vsave", StringComparison.Ordinal));
            Assert.Contains(PageLog, l => l.StartsWith("setup page: SP4 ", StringComparison.Ordinal) && l.Contains("replacing the one there (kept as SP4.vsave.previous)", StringComparison.Ordinal));
            Assert.Contains(PageLog, l => l.StartsWith("setup page: npf000002 ", StringComparison.Ordinal) && l.EndsWith("Anna.vplayer", StringComparison.Ordinal));
            Assert.Contains(PageLog, l => l.StartsWith("setup page: npf000002: not imported: ", StringComparison.Ordinal) && l.EndsWith("(the player kept the existing one)", StringComparison.Ordinal));
            Assert.Contains(PageLog, l => l.StartsWith("setup page: SP4: not imported: the player cancelled the replace notice", StringComparison.Ordinal));
        }

        // FFmpeg's button: winget's line run in a window the player sees (here, the fake shell), and looked for again after.
        launcher.Act("ffmpeg");
        page.Wait();
        Assert.Equal([FFmpegSearch.WingetLine], shell.Ran);
        Assert.Equal("winget install --exact --id Gyan.FFmpeg", FFmpegSearch.WingetLine);
    }

    // ---- the launcher: its first page, Play, its screen ------------------------------------------------------------------------

    [Fact]
    public void Play_starts_the_game_as_a_process_hides_while_it_runs_and_says_a_bad_exit_with_its_log()
    {
        var (launcher, shell, _, started, games, install) = Make();
        launcher.Start([]);
        launcher.Act("page", "play");
        launcher.Act("play");
        Assert.Empty(started);                                                  // nothing imported: Play cannot
        launcher.Fill();
        Assert.False(launcher.Data.Flag("canPlay"));                            // shown, disabled, its reason said
        Assert.True(launcher.Data.Flag("toSetup"));
        Assert.Equal("Not ready: the original game's files (below).", launcher.Data.Text("playWhy"));   // the item named; its row says what to do
        launcher.Screen.Layout(LauncherWindow.Metrics(1280, 800));
        var playButton = launcher.Screen.Root.Walk().OfType<Button>().Single(b => b.Id == "play");
        Assert.False(playButton.Enabled);
        Assert.Equal(launcher.Data.Text("playWhy"), playButton.WhyNot);

        launcher.Setup.UseFolder(install);
        launcher.Setup.StartImport();
        launcher.Setup.Wait();
        launcher.Poll();
        Assert.True(launcher.CanPlay);
        launcher.Fill();
        Assert.True(launcher.Data.Flag("canPlay"));
        Assert.DoesNotContain("It plays from", launcher.Data.Text("playFrom"), StringComparison.Ordinal);   // the imported game files go without saying
        Assert.Equal("The launcher hides while the game is running.", launcher.Data.Text("playNote"));
        Assert.False(launcher.Data.Flag("closeOnStart"));                       // off by default

        // Play: vitomir-platform --game classic (and the developer's switch), its lines kept in the user folder's logs; the launcher hidden.
        launcher.Act("play");
        var start = Assert.Single(started);
        Assert.Equal(Path.Combine(root, "Vitomir", "vitomir-platform.exe"), start.Program);
        Assert.Equal(["--game", "classic", "--developer"], start.Args);
        Assert.Equal(Path.Combine(User, "Logs"), Path.GetDirectoryName(start.LogPath));
        Assert.NotNull(launcher.Running);
        launcher.Act("play");
        Assert.Single(started);                                                 // one game at a time
        Assert.False(launcher.Poll());

        // It ends well: back, said.
        games[0].End.SetResult(0);
        Assert.True(launcher.Poll());
        Assert.Null(launcher.Running);
        Assert.Equal(0, launcher.LastExit);
        Assert.Null(launcher.Notice);
        launcher.Fill();
        Assert.Equal("", launcher.Data.Text("playResult"));                    // an end as expected says nothing more (E2)

        // It ends badly: said plainly, what it said last, its log and the logs folder a button away.
        launcher.Act("play");
        games[1].LastLines = ["game classic: --oneone /nowhere is not a 1.1 install"];
        games[1].End.SetResult(2);
        launcher.Poll();
        var notice = launcher.Notice!;
        Assert.Equal("ended", notice.Kind);
        Assert.Contains(notice.Lines, l => l.Warn && l.Text == "Its command line or its modules were refused.");
        Assert.Contains(notice.Lines, l => l.Text.Contains("is not a 1.1 install", StringComparison.Ordinal));
        Assert.DoesNotContain(notice.Lines, l => l.Text.Contains("exit code", StringComparison.Ordinal));   // the code in launcher.log, not the page (E1)
        launcher.Fill();
        Assert.Equal(launcher.Data.Text("playResult"), launcher.Data.Text("playResult").Split(" (")[0]);
        Assert.Equal(["Open log", "Open logs folder", "Close"], notice.Buttons.Select(b => b.Label));
        Assert.Equal((0, 2), (notice.Default, notice.CancelAt));
        launcher.Act("notice", "a");
        launcher.Act("notice", "b");
        Assert.Equal(["file " + started[1].LogPath, "folder " + Path.Combine(User, "Logs")], shell.Opened);
        launcher.Act("escape");                                                 // Escape is Close
        Assert.Null(launcher.Notice);
    }

    [Fact]
    public void The_screen_reads_without_a_problem_every_page_lays_out_and_the_folder_check_is_a_notice_first()
    {
        var (launcher, shell, _, _, _, _) = Make();
        Assert.Empty(launcher.Problems);
        var problems = Location.Check(Rooted("Program Files", "Vitomir"), Places, Writable, _ => false);
        launcher.Start(problems);
        Assert.Equal("location", launcher.Notice?.Kind);
        launcher.Fill();
        Assert.True(launcher.Data.Flag("notice"));
        Assert.False(launcher.Data.Flag("noNotice"));
        Assert.Equal("Continue anyway", launcher.Data.Text("noticeB"));
        var metrics = LauncherWindow.Metrics(1280, 800);
        var list = new DrawList();
        launcher.Screen.Layout(metrics);
        launcher.Screen.Draw(list);
        Assert.True(list.Count > 20);
        Assert.Contains(launcher.Notice!.Lines, l => l.Warn && l.Text.Contains("Program Files", StringComparison.Ordinal));
        launcher.Act("escape");                                                 // carry on
        Assert.Null(launcher.Notice);

        foreach (var page in Vitomir.Launcher.Launcher.Pages)
        {
            launcher.Act("page", page);
            Assert.Equal(page, launcher.Page);
            launcher.Fill();
            launcher.Screen.Layout(metrics);
            launcher.Screen.Draw(list);
            Assert.True(list.Count > 20, $"the {page} page drew {list.Count}");
            Assert.True(launcher.Screen.Worked.Count >= 6, $"the {page} page has {launcher.Screen.Worked.Count} pieces to work");
        }
        // The folders open in Explorer (the shell's).
        launcher.Act("page", "folders");
        Directory.CreateDirectory(Path.Combine(User, "Logs"));
        launcher.Act("open", "logs");
        Assert.Equal("folder " + Path.Combine(User, "Logs"), Assert.Single(shell.Opened));
        // The About page says where it runs from and what is wrong with it.
        Assert.Contains(launcher.About(), a => a.Warn && a.Value.Contains("Program Files", StringComparison.Ordinal));
        Assert.DoesNotContain(launcher.About(), a => a.Label is ".NET" or "System" or "Runs from" or "Your user folder" or "FFmpeg");
        launcher.Act("copydetails");                                            // .NET and the system: copied for a bug report
        Assert.Contains(".NET: ", launcher.TakeClipboard(), StringComparison.Ordinal);
        Assert.Null(launcher.TakeClipboard());
        launcher.Fill();
        Assert.Equal("Copied.", launcher.Data.Text("aboutResult"));
        launcher.Act("opensource");
        Assert.Equal("link " + Vitomir.Launcher.Launcher.SourceUrl, shell.Opened[^1]);
        // Quit.
        launcher.Act("quit");
        Assert.True(launcher.QuitAsked);
    }

    [Fact]
    public void The_details_for_a_bug_report_name_no_user()
    {
        // Posted in public: the player's own Windows folder (which holds their user name) becomes %USERPROFILE%, in any case,
        // at a line's end and before a space, while a folder that only begins the same is left alone. The paths are put
        // together here so that no Windows path is written in the source.
        static string P(params string[] parts) => string.Join('\\', parts);
        string profile = P("C:", "Users", "Anna");
        string details = $"Runs from: {P("C:", "Games", "Vitomir")}\r\n" +
                         $"User folder: {P(profile, "AppData", "Local", "Vitomir")} (the default)\r\n" +
                         $"FFmpeg: {P(profile.ToLowerInvariant(), "AppData", "ffmpeg.exe")} (8.0)\r\n" +
                         $"Its folder: {profile}\r\n" +
                         $"Other: {P("C:", "Users", "Annabel", "Saves")}\r\n";
        string shown = Vitomir.Launcher.Launcher.WithoutTheUser(details, profile + "\\");
        Assert.Contains($"User folder: {P("%USERPROFILE%", "AppData", "Local", "Vitomir")} (the default)", shown);
        Assert.Contains($"FFmpeg: {P("%USERPROFILE%", "AppData", "ffmpeg.exe")}", shown);
        Assert.Contains("Its folder: %USERPROFILE%" + "\r\n", shown);           // the whole folder, at the line's end
        Assert.Contains($"Other: {P("C:", "Users", "Annabel", "Saves")}", shown);
        Assert.Contains($"Runs from: {P("C:", "Games", "Vitomir")}", shown);
        Assert.Equal(details, Vitomir.Launcher.Launcher.WithoutTheUser(details, null));
        Assert.Equal(details, Vitomir.Launcher.Launcher.WithoutTheUser(details, "C:" + "\\"));
    }

    [Fact]
    public void Every_word_of_the_launcher_is_in_vitomirs_delivered_faces_at_the_themes_sizes()
    {
        var (launcher, _, _, _, _, _) = Make();
        launcher.Start([]);
        var metrics = LauncherWindow.Metrics(1280, 800);
        var list = new DrawList();
        var faces = new HashSet<FaceId>();
        foreach (var page in Vitomir.Launcher.Launcher.Pages)
        {
            launcher.Act("page", page);
            launcher.Fill();
            launcher.Screen.Layout(metrics);
            launcher.Screen.Draw(list);
            foreach (var c in list.Commands) if (c.Kind == DrawKind.Text) faces.Add(c.Face);
        }
        // Vitomir Text, Text SemiBold (a warning), Wedge SemiBold and Knife (the version): none of the code-drawn faces.
        Assert.Subset(new HashSet<FaceId> { FaceId.Text, FaceId.TextSemiBold, FaceId.WedgeSemiBold, FaceId.Knife }, faces);
        Assert.DoesNotContain(FaceId.Book, faces);
        Assert.DoesNotContain(FaceId.Uncial, faces);
        Assert.DoesNotContain(FaceId.Capitals, faces);
        // The kit gives a style its face and colour; its size is the theme's.
        foreach (var (name, style) in CourtKit.Court.Styles)
        {
            var theme = Theme.Styles[name switch { "number" or "rules" => "note", "caution" => "warn", _ => name }];
            Assert.Equal(theme.Em, style.Em);
            Assert.Equal(theme.LineHeight, style.LineHeight);
            Assert.NotNull(Letters.Whole(style.Lettering));
        }
    }

    [Fact]
    public void The_launcher_is_drawn_in_the_court_kit_in_the_kits_order()
    {
        var (launcher, _, _, _, _, _) = Make();
        launcher.Start([]);
        launcher.Act("page", "setup");
        launcher.Fill();
        var metrics = LauncherWindow.Metrics(1280, 800);
        var list = new DrawList();
        launcher.Screen.Layout(metrics);
        launcher.Screen.Draw(list);
        var look = CourtLook.Court;
        Assert.Same(look, launcher.Screen.Skin);
        // Every picture the look draws is sent with the list, once, mipmapped; each kit piece's picture is the size uikit.json says.
        var sent = new List<object>();
        list.Resources.Take(sent);
        var pictures = sent.OfType<DrawPicture>().ToList();
        Assert.Equal(look.Pictures.Count, pictures.Count);
        Assert.All(pictures, p => Assert.True(p.Mipmaps));
        var kit = CourtKit.Court;
        int Id(string name) => CourtLook.KitPieces.ToList().IndexOf(name) + 1;
        foreach (var name in CourtLook.KitPieces)
        {
            var piece = kit.Piece(name);
            var picture = Assert.Single(pictures, p => p.Id == Id(name));
            Assert.Equal((piece.Width, piece.Height), (picture.Width, picture.Height));
        }
        // The background first, over the whole window.
        var first = list.Commands.First(c => c.Kind is DrawKind.Picture or DrawKind.Text);
        Assert.Equal(DrawKind.Picture, first.Kind);
        var bg = list.Pictures[first.Item];
        Assert.Equal((0f, 0f, 1280f, 800f), (bg.X, bg.Y, bg.W, bg.H));
        // The page's frame: the four corner knots drawn last, at the page panel's corners, inside it.
        var page = metrics.ToPixels(launcher.Screen.Find("page")!.Rect);
        var last = list.Commands.TakeLast(4).ToList();
        Assert.All(last, c => Assert.Equal(DrawKind.Picture, c.Kind));
        var knots = last.Select(c => list.Pictures[c.Item]).ToList();
        Assert.All(knots, k => Assert.Equal(Id("corner_plate"), k.Picture));
        Assert.Equal(page.X, knots[0].X);
        Assert.Equal(page.Y, knots[0].Y);
        Assert.Equal(page.Right, knots[3].X + knots[3].W);
        Assert.Equal(page.Bottom, knots[3].Y + knots[3].H);
        // The page's name a picture whose 56-unit letter band sits in the middle of the 60-unit title band (the picture 63 units,
        // 3.5 of margin above and below the band); no words drawn for the title.
        var band = launcher.Screen.Find("pageBand")!.Rect;
        Assert.Equal(60f, band.Height);
        var pageName = list.Pictures.Single(q => q.Picture == look.PictureId("page:setup"));
        Assert.Equal(MathF.Round((band.Y + 61.5f) * metrics.Pixel) - MathF.Round((band.Y - 1.5f) * metrics.Pixel), pageName.H, 0);
        Assert.Equal(MathF.Round((band.Y - 1.5f) * metrics.Pixel), pageName.Y, 0);
        Assert.DoesNotContain("Setup: the game's files", list.Texts());
        Assert.Equal("Setup", launcher.Data.Text("pageTitle"));                // the page's name alone
        // Quit: the kit's quiet button, at the button style's size.
        var quit = launcher.Screen.Find("quit")!;
        var quitPx = metrics.ToPixels(quit.Rect);
        bool OnQuit(PictureQuad q) => q.X >= quitPx.X - 8 && q.Y >= quitPx.Y - 8 && q.X + q.W <= quitPx.Right + 8 && q.Y + q.H <= quitPx.Bottom + 8;
        Assert.Equal(9, list.Pictures.Count(q => q.Picture == Id("button_quiet_normal") && OnQuit(q)));     // its nine slices
        Assert.DoesNotContain(list.Pictures, q => q.Picture == Id("button_normal") && OnQuit(q));
        Assert.Contains(list.Commands, c => c.Kind == DrawKind.Text && c.Text == "Quit" && c.Colour == kit.Piece("button_quiet_normal").Text);
        Assert.Equal(CourtKit.Court.Styles["button"], ((Button)quit).Style);
        // The chosen tab: the nav's selected ground and its mark in the kit's colour; the words' colours the kit's.
        Assert.Contains(list.Pictures, q => q.Picture == Id("nav_selected"));
        Assert.Contains(list.Commands, c => c.Kind == DrawKind.Solid && c.Colour == kit.Colour("mark_selected"));
        Assert.Contains(list.Commands, c => c.Kind == DrawKind.Text && c.Text == "Setup" && c.Colour == kit.Piece("nav_selected").Text);
        Assert.Contains(list.Commands, c => c.Kind == DrawKind.Text && c.Text == "Play" && c.Colour == kit.Piece("nav_selected").TextUnselected);
    }

    [Fact]
    public void The_notice_stands_over_the_kits_scrim_with_its_title_band_friezes_and_action_band()
    {
        var (launcher, _, _, _, _, _) = Make();
        launcher.Start(Location.Check(Rooted("Program Files", "Vitomir"), Places, Writable, _ => false));
        launcher.Fill();
        var metrics = LauncherWindow.Metrics(1024, 680);
        var list = new DrawList();
        launcher.Screen.Layout(metrics);
        launcher.Screen.Draw(list);
        var kit = CourtKit.Court;
        var look = CourtLook.Court;
        Assert.Contains(list.Commands, c => c.Kind == DrawKind.Solid && c.Colour == kit.Colour("scrim") && c.Rect == new PixelRect(0, 0, 1024, 680));
        Assert.Contains(list.Commands, c => c.Kind == DrawKind.Solid && c.Colour == kit.Colour("band_dark"));
        Assert.True(list.Pictures.Count(q => q.Picture == look.PictureId("frieze")) >= 2);
        Assert.Contains(list.Pictures, q => q.Picture == look.PictureId("velvet"));
        Assert.Contains("Vitomir should not run from this folder", list.Texts());
        // Its sizes as the layout's: the notice 1240 units wide, its title band at least 60, its friezes 17.
        Assert.Equal(1240f, launcher.Screen.Find("noticePanel")!.Rect.Width);
        Assert.True(launcher.Screen.Find("noticeBand")!.Rect.Height >= 60f);
        Assert.Equal(17f, launcher.Screen.Find("noticeFrieze")!.Rect.Height);
    }

    [Fact]
    public void A_transparent_pixel_takes_its_nearest_neighbours_colour_and_stays_transparent()
    {
        // 3 x 1: a red opaque pixel, then two transparent black ones.
        byte[] rgba = [255, 0, 0, 255, 0, 0, 0, 0, 0, 0, 0, 0];
        CourtLook.Bleed(rgba, 3, 1);
        Assert.Equal([255, 0, 0, 255, 255, 0, 0, 0, 255, 0, 0, 0], rgba);
    }

    [Fact]
    public void The_launcher_lists_only_the_classic_game_and_never_names_the_enhanced_module_in_any_build()
    {
        // A developer's build has the Enhanced module beside the program; the launcher lists the Classic game only.
        string modulesFolder = Path.Combine(AppContext.BaseDirectory, "Modules");
        Assert.Contains(Directory.EnumerateDirectories(modulesFolder), d => Path.GetFileName(d).Equals("enhanced", StringComparison.OrdinalIgnoreCase));   // a developer's build has it
        var modules = Vitomir.Launcher.Launcher.FindModules(modulesFolder);
        var classic = Assert.Single(modules);
        Assert.Equal("classic", classic.Id);
        Assert.False(classic.Warn);
        Assert.Contains("Play starts it", classic.Status, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(classic.Version));
        Assert.Empty(Vitomir.Launcher.Launcher.FindModules(Path.Combine(root, "no modules")));
        // No word of any page, the notices or the log says it.
        var log = new List<string>();
        var (launcher, _, _, _, _, _) = Make();
        launcher.Start([]);
        foreach (var page in Vitomir.Launcher.Launcher.Pages)
        {
            launcher.Act("page", page);
            foreach (var w in Drawn(launcher).Concat(launcher.Screen.Root.Walk().OfType<TextPiece>().Select(t => t.Words)))
                Assert.False(w.Contains("nhanced", StringComparison.OrdinalIgnoreCase) || w.Contains("enhanced game", StringComparison.OrdinalIgnoreCase), $"the {page} page says: {w}");
        }
        foreach (var (label, value, _) in launcher.About()) Assert.DoesNotContain("nhanced", label + value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_check_words_tell_a_replacement_from_an_addition()
    {
        string install = MakeInstall();
        var manifest = DataManifest.Make(install, "test-data", 1, "a test's", cancel: TestContext.Current.CancellationToken);
        // A real archive for LMaterials.nob's entries, made before the manifest is.
        string install2 = Path.Combine(root, "install2");
        foreach (var f in Directory.EnumerateFiles(install, "*", SearchOption.AllDirectories))
            Write(Path.Combine(install2, Path.GetRelativePath(install, f)), File.ReadAllText(f));
        File.Delete(Path.Combine(install2, "LMaterials.nob"));
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(install2, "LMaterials.nob"), System.IO.Compression.ZipArchiveMode.Create))
            zip.CreateEntry("materials/General/ankhs.tga", System.IO.Compression.CompressionLevel.NoCompression);
        var m2 = DataManifest.Make(install2, "test-data", 1, "a test's", cancel: TestContext.Current.CancellationToken);
        Write(Path.Combine(install2, "Materials", "General", "ankhs.tga"), "a texture pack's");
        Write(Path.Combine(install2, "Materials", "SDK", "grid.tga"), "the SDK's");
        Write(Path.Combine(install2, "Materials", "SDK", "marker.tga"), "the SDK's");
        var words = SetupPage.CheckWords(DataCheck.Run(install2, m2, CheckDepth.Sizes, cancel: TestContext.Current.CancellationToken)).Select(l => l.Text).ToList();
        Assert.Contains("1 file from a mod in your copy is left out. Add mods on the Modules page.", words);
        Assert.Contains("    Left out: Materials/ (1)", words);                                        // behind Details
        Assert.Contains("    Left out, adding to the game only: 2 files (Materials/ (2))", words);
        Assert.All(words, w => Assert.True(w.All(c => c < 128), w));
        Assert.NotNull(manifest);
    }
    // ---- the style guide: controls, disabled states with their reasons, notices' keys, the words ------------------------------

    /// <summary>The launcher's screen laid out and drawn at 1280 x 800 as it is now; answers the drawn words.</summary>
    static List<string> Drawn(Vitomir.Launcher.Launcher launcher)
    {
        launcher.Fill();
        launcher.Screen.Layout(LauncherWindow.Metrics(1280, 800));
        var list = new DrawList();
        launcher.Screen.Draw(list);
        return [.. list.Texts()];
    }

    static bool Seen(Piece p)
    {
        for (var a = p; a is not null; a = a.Parent) if (!a.Shown) return false;
        return true;
    }

    /// <summary>Every control shown disabled says why, and the reason stands on the screen in a line of its own words.</summary>
    static void ReasonsShown(Vitomir.Launcher.Launcher launcher, string state)
    {
        Drawn(launcher);
        var lines = launcher.Screen.Root.Walk().OfType<TextPiece>().Where(Seen).Select(t => t.Words).ToHashSet(StringComparer.Ordinal);
        // The arrows at the module list's ends need no line (the style guide's E1: "trivially deduced"): their reason stays on them.
        foreach (var w in launcher.Screen.Root.Walk().OfType<Worked>().Where(w => Seen(w) && !w.Enabled && w.Id is not ("moduleUp" or "moduleDown")))
        {
            Assert.False(string.IsNullOrEmpty(w.WhyNot), $"{state}: {w} is disabled with no reason");
            // A row's own reason may be in the row's words ("cannot be imported: ..."); else a line of its own says it.
            Assert.True(lines.Contains(w.WhyNot) || lines.Any(l => l.Contains(w.WhyNot, StringComparison.Ordinal)), $"{state}: {w}'s reason \"{w.WhyNot}\" is not shown");
        }
    }

    [Fact]
    public void Every_control_shown_disabled_says_why_beside_it_in_every_state()
    {
        var (launcher, _, _, _, _, install) = Make();
        launcher.Start([]);
        foreach (var page in Vitomir.Launcher.Launcher.Pages)
        {
            launcher.Act("page", page);
            ReasonsShown(launcher, "nothing found, " + page);
        }
        launcher.Act("page", "setup");
        var setup = launcher.Setup;
        Drawn(launcher);
        Assert.False(((Button)launcher.Screen.Find("check")!).Enabled);           // no copy selected: Check files and Import say so
        Assert.Equal("Select a copy of the game first (step 1).", ((Button)launcher.Screen.Find("import")!).WhyNot);
        Assert.Equal("", launcher.Data.Text("savesWhy"));                         // nothing to select: no reason line for buttons not shown
        setup.UseFolder(install);
        ReasonsShown(launcher, "a copy selected");
        Drawn(launcher);
        Assert.False(((Button)launcher.Screen.Find("importSaves")!).Enabled);     // nothing selected yet
        Assert.Equal("Select the saves to import first.", ((Button)launcher.Screen.Find("importSaves")!).WhyNot);
        var unreadable = launcher.Screen.Find("saves")!.Children.OfType<Checkbox>().ElementAt(2);
        Assert.False(unreadable.Enabled);                                         // a save that cannot be imported: its row says why
        Assert.StartsWith("cannot be imported:", unreadable.WhyNot, StringComparison.Ordinal);

        // While a step runs: Check files and Import shown disabled with the reason, Cancel there; every other control the same.
        using var hold = new ManualResetEventSlim();
        setup.OnProgress = _ => hold.Wait(TimeSpan.FromSeconds(20));
        launcher.Act("import");
        Assert.True(setup.Busy);
        ReasonsShown(launcher, "importing");
        foreach (var id in new[] { "check", "import", "browse", "useFolder", "path" })
            Assert.Equal("Unavailable while Setup imports the files.", ((Worked)launcher.Screen.Find(id)!).WhyNot);
        Assert.True(launcher.Screen.Find("cancel")!.Shown);
        Assert.False(((Radio)launcher.Screen.Find("copyOther")!).Enabled);
        launcher.Act("page", "play");
        ReasonsShown(launcher, "importing, play");
        hold.Set();
        setup.Wait();
        setup.OnProgress = null;
        launcher.Poll();
        launcher.Act("page", "setup");
        ReasonsShown(launcher, "imported");

        // All selected: Select all disabled with its reason.
        launcher.Act("selectall");
        Drawn(launcher);
        Assert.Equal("All the saves that can be imported are selected.", ((Button)launcher.Screen.Find("selectAll")!).WhyNot);
        ReasonsShown(launcher, "all selected");
    }

    [Fact]
    public void The_copies_of_the_game_are_a_radio_group_and_the_saves_checkboxes_with_select_all_and_clear()
    {
        var (launcher, _, _, _, _, install) = Make(found: true);
        launcher.Start([]);
        launcher.Setup.UseFolder(install);
        Drawn(launcher);
        var copy = (Radio)launcher.Screen.Find("copy")!;
        Assert.True(copy.Checked);
        Assert.Equal("0", launcher.Data.Text("copySelected"));
        Assert.Equal("copy", copy.Group);
        // The whole row is the radio: the store's line, the folder and how it was found are its label.
        Assert.Contains(copy.Walk().OfType<TextPiece>(), t => t.Words == Path.GetFullPath(install));
        Assert.Same(copy, launcher.Screen.HitTest(copy.Rect.Right * launcher.Screen.Metrics.Pixel - 20f, (copy.Rect.Y + copy.Rect.Height * 0.5f) * launcher.Screen.Metrics.Pixel));

        // The saves: a checkbox each; Select all takes every one that can be imported, Clear selection none.
        var boxes = launcher.Screen.Find("saves")!.Children.OfType<Checkbox>().ToList();
        Assert.Equal(4, boxes.Count);
        launcher.Act("selectall");
        Assert.Equal(["SP0", "SP4", "npf000002"], launcher.Setup.Picked.Order(StringComparer.Ordinal));     // npf000001 cannot be
        Drawn(launcher);
        Assert.Equal([true, true, false, true], launcher.Screen.Find("saves")!.Children.OfType<Checkbox>().Select(b => b.Checked));
        launcher.Act("clearselection");
        Assert.Empty(launcher.Setup.Picked);
        // A checkbox clicked: the row's flag written and the selection follows it.
        Drawn(launcher);
        var sp4 = launcher.Screen.Find("saves")!.Children.OfType<Checkbox>().ElementAt(1);
        launcher.Screen.SetFocus(sp4);
        launcher.Screen.Key(UiKey.Space);
        launcher.TakeActions();
        Assert.Equal(["SP4"], launcher.Setup.Picked);
        Drawn(launcher);
        launcher.Screen.SetFocus(launcher.Screen.Find("saves")!.Children.OfType<Checkbox>().ElementAt(1));
        launcher.Screen.Key(UiKey.Space);
        launcher.TakeActions();
        Assert.Empty(launcher.Setup.Picked);
    }

    [Fact]
    public void A_notice_answers_Enter_with_its_default_button_and_Escape_with_its_cancel_button()
    {
        var (launcher, _, _, _, _, install) = Make();
        launcher.Start([]);
        launcher.Setup.UseFolder(install);
        var page = launcher.Setup;
        Directory.CreateDirectory(page.Service.SavesFolder);
        File.WriteAllText(Path.Combine(page.Service.SavesFolder, "SP0.vsave"), "the player's own");
        File.WriteAllText(Path.Combine(page.Service.SavesFolder, "SP4.vsave"), "the player's own too");
        page.Picked.Add("SP0");
        page.Picked.Add("SP4");
        launcher.Act("importsaves");
        page.Wait();
        launcher.Poll();
        var notice = launcher.Notice!;
        Assert.Equal("Replace the existing saves?", notice.Title);
        Assert.StartsWith("SP0 \"", notice.Lines[0].Text, StringComparison.Ordinal);
        Assert.Equal("These saves are already imported. Replace them? The ones there now are kept as backups (.previous).", notice.Lines[^1].Text);
        Assert.Equal(["Replace", "Keep the existing ones", "Cancel"], notice.Buttons.Select(b => b.Label));
        // Laid out: the default has the focus and is the default button; Escape works Cancel through the screen.
        Drawn(launcher);
        Assert.Equal("noticeA", launcher.TakeFocus());
        var screen = launcher.Screen;
        Assert.True(((Button)screen.Find("noticeA")!).IsDefault);
        Assert.False(((Button)screen.Find("noticeB")!).IsDefault);
        Assert.True(((Button)screen.Find("noticeC")!).IsCancel);
        screen.Key(UiKey.Escape);
        launcher.TakeActions();
        Assert.Null(launcher.Notice);
        Assert.Equal("the player's own", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP0.vsave")));
        // Again; Enter (from anywhere the default answers): Replace, the old ones kept as .previous.
        launcher.Act("importsaves");
        page.Wait();
        launcher.Poll();
        Drawn(launcher);
        screen.SetFocus(screen.Find(launcher.TakeFocus()!));
        screen.Key(UiKey.Enter);
        launcher.TakeActions();
        page.Wait();
        Assert.Equal("the player's own", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP0.vsave.previous")));
        Assert.Equal("imported SP0", File.ReadAllText(Path.Combine(page.Service.SavesFolder, "SP0.vsave")));
    }

    [Fact]
    public void No_word_the_player_sees_says_chosen_marked_or_picked_and_the_buttons_are_named_by_their_verbs()
    {
        var (launcher, _, _, started, games, install) = Make();
        var seen = new List<string>();
        launcher.Start(Location.Check(Rooted("Program Files", "Vitomir"), Places, Writable, _ => false));
        seen.AddRange(Drawn(launcher));
        launcher.Act("escape");
        foreach (var p in Vitomir.Launcher.Launcher.Pages) { launcher.Act("page", p); seen.AddRange(Drawn(launcher)); }
        launcher.Act("page", "setup");
        launcher.Setup.UseFolder(install);
        seen.AddRange(Drawn(launcher));
        launcher.Act("check");
        launcher.Setup.Wait();
        launcher.Poll();
        launcher.Act("import");
        launcher.Setup.Wait();
        launcher.Poll();
        launcher.Setup.Picked.Add("SP4");
        launcher.Act("importsaves");
        launcher.Setup.Wait();
        launcher.Poll();
        seen.AddRange(Drawn(launcher));
        File.WriteAllText(Path.Combine(launcher.Setup.Service.SavesFolder, "SP4.vsave"), "the player's own");
        launcher.Setup.Picked.Add("SP4");
        launcher.Act("importsaves");
        launcher.Setup.Wait();
        launcher.Poll();
        seen.AddRange(Drawn(launcher));                                          // the replace notice
        launcher.Act("notice", "b");
        launcher.Act("page", "play");
        launcher.Act("play");
        games[0].End.SetResult(3);
        launcher.Poll();
        seen.AddRange(Drawn(launcher));                                          // the game's bad end
        Assert.NotEmpty(started);
        foreach (var line in seen)
            foreach (var word in new[] { "chosen", "chose", "marked", "picked", "Stop", "Look again", "Use", "Leave" })
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(line, $@"\b{word}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) && !line.Contains("Use this folder", StringComparison.Ordinal),
                             $"\"{line}\" says \"{word}\"");
        // The buttons the table names, as named.
        foreach (var label in new[] { "Browse\u2026", "Use this folder", "Check files", "Import again", "Select all", "Clear selection", "Import selected", "Check again", "Quit" })
            Assert.Contains(label, seen);
    }

    // ---- the Modules page: the player's modules, the problems list, Play anyway with these ----------------------------------------

    /// <summary>A TGA of one colour, 24 bits, uncompressed.</summary>
    static byte[] Tga(int width, int height, byte red = 200)
    {
        var data = new byte[18 + width * height * 3];
        data[2] = 2;
        data[12] = (byte)width; data[13] = (byte)(width >> 8); data[14] = (byte)height; data[15] = (byte)(height >> 8);
        data[16] = 24;
        for (int i = 18; i < data.Length; i += 3) { data[i] = 10; data[i + 1] = 20; data[i + 2] = red; }
        return data;
    }

    static void Put(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    /// <summary>A stand-in base game a developer's VTMR_GAME_DIR names: one real archive with a wall's texture (the modules' checks read it).</summary>
    string MakeBase()
    {
        string dir = Path.Combine(root, "base");
        Directory.CreateDirectory(dir);
        using (var zip = System.IO.Compression.ZipFile.Open(Path.Combine(dir, "Resource.nob"), System.IO.Compression.ZipArchiveMode.Create))
        using (var e = zip.CreateEntry("materials/Prague/Wall.TGA", System.IO.Compression.CompressionLevel.NoCompression).Open())
            e.Write(Tga(64, 128));
        return dir;
    }

    /// <summary>A pack of 1.1-path files (a bare pack: no module.json), with what else it is given.</summary>
    string MakePack(string name, params (string Path, byte[] Data)[] files)
    {
        string dir = Path.Combine(root, "packs", name);
        foreach (var (path, data) in files) Put(Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar)), data);
        return dir;
    }

    /// <summary>The launcher with a developer's base game (so Play can start), on its Modules page.</summary>
    (Vitomir.Launcher.Launcher Launcher, FakeShell Shell, List<GameStart> Started, List<FakeGame> Games) MakeModules()
    {
        string base1 = MakeBase();
        var (launcher, shell, _, started, games, _) = Make(() => base1);
        launcher.Start([]);
        launcher.Act("page", "modules");
        return (launcher, shell, started, games);
    }

    /// <summary>The words of every text piece shown, laid out at 1280 x 800 (a wrapped line's whole words, not its drawn lines).</summary>
    static List<string> Said(Vitomir.Launcher.Launcher launcher)
    {
        Drawn(launcher);
        return [.. launcher.Screen.Root.Walk().OfType<TextPiece>().Where(Seen).Select(t => t.Words)];
    }

    static void Settle(Vitomir.Launcher.Launcher launcher)
    {
        launcher.Modules.Wait(TimeSpan.FromMinutes(2));
        launcher.Poll();
    }

    [Fact]
    public void The_modules_page_adds_a_pack_lists_refused_files_and_shows_the_classic_game_first_and_fixed()
    {
        var (launcher, _, _, _) = MakeModules();
        var words = Said(launcher);
        Assert.Contains("No modules yet. Select Add a module\u2026 to add a texture pack or a mod.", words);
        Assert.Contains("No modules are enabled.", words);
        Assert.Contains(Vitomir.Launcher.Launcher.GameName, words);                // the game's one name and its line (Q1)
        Assert.Contains(Vitomir.Launcher.Launcher.GameLine, words);
        Assert.False(Seen(launcher.Screen.Find("sortModules")!));                  // under two modules: not offered (Q3)
        var classic = (Checkbox)launcher.Screen.Find("classicOn")!;
        Assert.True(classic.Checked);
        Assert.False(classic.Enabled);                                            // first and fixed, and it says why
        Assert.Equal("Always on, always first.", classic.WhyNot);
        ReasonsShown(launcher, "no modules");

        // Add a module…: where the pack is asked first; each answer the window's picker; the picker's answer added.
        launcher.Act("addmodule");
        Assert.Equal(["Folder\u2026", "Zip or .nob\u2026", "Cancel"], launcher.Notice!.Buttons.Select(b => b.Label));
        launcher.Act("notice", "b");
        Assert.Equal(PickFor.ModuleFile, launcher.TakePick());
        Assert.Null(launcher.TakePick());
        string pack = MakePack("Sharp Walls", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 90)), ("Vampire.exe", [0x4d, 0x5a]), ("readme.txt", Encoding.ASCII.GetBytes("a pack")));
        string zip = Path.Combine(root, "packs", "Sharp Walls.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(pack, zip);
        launcher.Picked(zip);
        Assert.True(launcher.Modules.Importing || launcher.Modules.Rows.Count == 1);
        Settle(launcher);
        var row = Assert.Single(launcher.Modules.Rows);
        Assert.Equal(("sharp-walls", true), (row.Id, row.On));
        var result = launcher.Modules.Result.Select(l => l.Text).ToList();
        Assert.Equal("Added Sharp Walls 0.0.0: on, at the end of the list.", result[0]);
        Assert.Contains("1 file not used (program files Vitomir does not run).", result);   // not an error: never imported
        Assert.Contains(launcher.Modules.Result, l => l.Detail && l.Text.StartsWith("    Vampire.exe: ", StringComparison.Ordinal));   // behind Details
        Assert.Contains("Kept aside, never read by the game: 1 document (readme.txt), in its Documents folder.", result);
        words = Said(launcher);
        Assert.Contains("Sharp Walls", words);                                    // its name, its version, its kinds as tags, in their columns
        Assert.Contains("0.0.0", words);
        Assert.Contains("look", words);
        Assert.Equal(["Name", "Version", "Kind"], launcher.Screen.Find("moduleHeader")!.Walk().OfType<TextPiece>().Select(t => t.Words));
        Assert.Contains("No problems.", words);
        // Its details closed at first; its chevron or its name opens them under its row: where it came from, its files by kind, what it
        // was made for, the documents, and the files not used folded to one line (not red: they were never imported).
        Assert.Empty(launcher.Modules.Open);
        Assert.Equal("closed", launcher.Data.Items("modules")[0].Text("chevron"));
        Assert.DoesNotContain("Made for: 1.1's models.", words);
        var toggle = (Checkbox)launcher.Screen.Find("moduleOpen")!;
        launcher.Screen.SetFocus(toggle);
        launcher.Screen.Key(UiKey.Enter);                                         // Enter on the focused row opens it
        launcher.TakeActions();
        Assert.Contains("sharp-walls", launcher.Modules.Open);
        words = Said(launcher);
        Assert.Equal("open", launcher.Data.Items("modules")[0].Text("chevron"));
        Assert.True(Seen(launcher.Screen.Find("moduleDetailsPanel")!));
        Assert.Contains(words, w => w.StartsWith("Look: 1 file,", StringComparison.Ordinal) && w.Contains("(materials/ 1)", StringComparison.Ordinal));
        Assert.Contains("Made for: 1.1's models.", words);
        Assert.Contains(words, w => w.StartsWith("Documents kept aside", StringComparison.Ordinal) && w.Contains("readme.txt", StringComparison.Ordinal));
        Assert.Contains("1 file not used (program files Vitomir does not run)", words);
        Assert.DoesNotContain(words, w => w.StartsWith("Vampire.exe", StringComparison.Ordinal));
        var notUsed = launcher.Screen.Find("moduleDetailsPanel")!.Walk().OfType<TextPiece>().Single(t => t.Words.EndsWith("does not run)", StringComparison.Ordinal));
        Assert.Equal(CourtKit.Court.Styles["body"], notUsed.Style);
        launcher.Act("notusedopen", "sharp-walls");
        words = Said(launcher);
        Assert.Contains("They were never imported: the module's folder holds only its data, so there is nothing to clean.", words);
        Assert.Contains(words, w => w.StartsWith("Vampire.exe: ", StringComparison.Ordinal));
        Assert.Equal("Remove module\u2026", ((Button)launcher.Screen.Find("moduleRemove")!).Text);
        ReasonsShown(launcher, "one module");
        launcher.Screen.SetFocus(launcher.Screen.Find("moduleOpen")!);
        launcher.Screen.Key(UiKey.Space);                                         // Space closes it again
        launcher.TakeActions();
        Assert.Empty(launcher.Modules.Open);
        Assert.Equal("The only module cannot move up or down.", ((Button)launcher.Screen.Find("moduleUp")!).WhyNot);
        Assert.False(Seen(launcher.Screen.Find("sortModules")!));
    }

    [Fact]
    public void Two_modules_with_a_warning_refuse_play_until_play_anyway_with_these_and_any_change_asks_again()
    {
        var (launcher, _, started, games) = MakeModules();
        launcher.Act("addfrom", MakePack("first", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 1))));
        Settle(launcher);
        launcher.Act("addfrom", MakePack("second", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 2))));
        Settle(launcher);
        Assert.Equal(["first", "second"], launcher.Modules.Rows.Select(r => r.Id));
        var start = launcher.Modules.Start!;
        var overlap = Assert.Single(start.Warnings);
        Assert.Equal("overlap", overlap.Kind);
        Assert.Equal(("Warning: second: first and second both give 1 file: second's is used, as it loads later.", "Files: materials/Prague/Wall.TGA", "To do: to use first's, put it after second."),
                     ModulesPage.Words(overlap));
        // By the names the list shows (one term per idea).
        Assert.Equal("Warning: Second Pack: First Pack and Second Pack both give 1 file: Second Pack's is used, as it loads later.",
                     ModulesPage.Words(overlap, new Dictionary<string, string> { ["first"] = "First Pack", ["second"] = "Second Pack" }).Head);
        var words = Said(launcher);
        Assert.Contains("1 warning: the game may look wrong.", words);
        Assert.True(Seen(launcher.Screen.Find("playAnyway")!));
        // The warning's head in the caution style (the kit's amber, text_caution), an error's would be in the warning red.
        var head = launcher.Screen.Find("problemList")!.Walk().OfType<TextPiece>().First(t => t.Words.StartsWith("Warning:", StringComparison.Ordinal) && Seen(t));
        Assert.Equal(CourtKit.Court.Styles["caution"], head.Style);

        // Play: shown disabled, a one-line note with Go to Modules (the list on Modules only), Play anyway beside Play (Q2).
        launcher.Act("page", "play");
        words = Said(launcher);
        var play = launcher.Screen.Root.Walk().OfType<Button>().Single(b => b.Id == "play");
        Assert.False(play.Enabled);
        Assert.Equal("1 warning in your modules.", play.WhyNot);
        Assert.DoesNotContain(words, w => w.StartsWith("Warning: second:", StringComparison.Ordinal));
        Assert.True(Seen(launcher.Screen.Find("playAnywayHere")!));
        Assert.True(Seen(launcher.Screen.Find("toModules")!));
        ReasonsShown(launcher, "a warning, play");
        launcher.Act("play");
        Assert.Empty(started);
        launcher.Act("playanyway");
        Assert.Single(started);                                                   // accepted, and played
        Assert.True(launcher.Modules.Start!.Accepted);
        games[0].End.SetResult(0);
        launcher.Poll();

        // Accepted for this combination: Play plays; a change (an order changed, by the row's button) asks again.
        launcher.Act("page", "modules");
        launcher.Act("moduleup", "second");
        Assert.Equal(["second", "first"], launcher.Modules.Rows.Select(r => r.Id));
        Settle(launcher);
        Assert.False(launcher.Modules.Start!.Accepted);
        Assert.NotNull(launcher.Modules.PlayBlocked());
        launcher.Act("moduledown", "second");
        Settle(launcher);
        Assert.True(launcher.Modules.Start!.Accepted);                            // that combination again: kept
        Assert.Null(launcher.Modules.PlayBlocked());

        // A module's checkbox turned off (on the screen): no warning left; on again: the accepted combination.
        Drawn(launcher);
        var box = launcher.Screen.Find("moduleRows")!.Walk().OfType<Checkbox>().First(c => c.Id == "moduleOn");
        launcher.Screen.SetFocus(box);
        launcher.Screen.Key(UiKey.Space);
        launcher.TakeActions();
        Assert.False(launcher.Modules.Rows[0].On);
        Settle(launcher);
        Assert.Empty(launcher.Modules.Start!.Warnings);
        launcher.Act("moduletoggle", "first");
        Settle(launcher);
        Assert.True(launcher.Modules.Start!.Accepted);

        // Remove: asked first, Cancel the default; Remove goes.
        launcher.Act("moduleremove", "first");
        var notice = launcher.Notice!;
        Assert.Equal("Remove first?", notice.Title);
        Assert.Equal("Its imported copy is deleted from your user folder. The original pack on disk is not touched, so you can add it again.", Assert.Single(notice.Lines).Text);
        Assert.Equal(["Remove", "Cancel"], notice.Buttons.Select(b => b.Label));
        Assert.Equal((1, 1), (notice.Default, notice.CancelAt));
        launcher.Act("escape");
        Assert.Equal(2, launcher.Modules.Rows.Count);
        launcher.Act("moduleremove", "first");
        launcher.Act("notice", "a");
        Assert.Equal(["second"], launcher.Modules.Rows.Select(r => r.Id));
        Assert.False(Directory.Exists(Path.Combine(User, "PlayerModules", "first")));
    }

    [Fact]
    public void An_error_refuses_play_with_what_to_do_sort_by_dependencies_mends_the_order_and_a_start_refused_shows_the_problems()
    {
        var (launcher, _, started, games) = MakeModules();
        string Authored(string id, string extra)
        {
            string dir = Path.Combine(root, "packs", id);
            Write(Path.Combine(dir, "module.json"), $"{{ \"id\": \"{id}\", \"name\": \"{id}\", \"version\": \"1.0.0\", \"game\": \"classic\"{extra} }}");
            Put(Path.Combine(dir, "Data", "materials", id + ".tga"), Tga(8, 8));
            return dir;
        }
        launcher.Act("addfrom", Authored("user", ", \"dependencies\": { \"lib\": \"1.0.0\" }, \"author\": \"someone\""));
        Settle(launcher);
        var error = Assert.Single(launcher.Modules.Start!.Errors);
        Assert.Equal(("Error: user needs the module lib (1.0.0 or a later 1.x), which is not imported.", "", "To do: add lib, or turn user off."), ModulesPage.Words(error));
        launcher.Act("moduleopen", "user");
        var words = Said(launcher);
        Assert.Contains("1 error: the game does not start until the error is fixed.", words);
        Assert.Contains("Needs lib 1.0.0 or a later 1.x.", words);              // in the details
        Assert.Contains(words, w => w.StartsWith("Id user, version 1.0.0, by someone", StringComparison.Ordinal));
        Assert.Contains("needs lib", words);                                      // in its row, in the warning colour, with the problem's line
        var needs = launcher.Screen.Find("moduleRows")!.Walk().OfType<TextPiece>().Single(t => t.Words == "needs lib" && Seen(t));
        Assert.Equal(CourtKit.Court.Styles["warn"], needs.Style);
        Assert.Contains("Error: user needs the module lib (1.0.0 or a later 1.x), which is not imported.", words);
        Assert.False(Seen(launcher.Screen.Find("playAnyway")!));                  // an error is not accepted
        var head = launcher.Screen.Find("problemList")!.Walk().OfType<TextPiece>().First(t => t.Words.StartsWith("Error:", StringComparison.Ordinal) && Seen(t));
        Assert.Equal(CourtKit.Court.Styles["warn"], head.Style);
        Assert.NotNull(launcher.Modules.Accept());
        launcher.Act("page", "play");
        Drawn(launcher);
        Assert.Equal("1 error in your modules.", (launcher.Screen.Root.Walk().OfType<Button>().Single(b => b.Id == "play")).WhyNot);
        Assert.True(Seen(launcher.Screen.Find("toModules")!));
        launcher.Act("play");
        Assert.Empty(started);

        // The dependency added after it: an order the start moves (a note), which Sort by dependencies writes into the list.
        launcher.Act("page", "modules");
        launcher.Act("addfrom", Authored("lib", ""));
        Settle(launcher);
        Assert.Empty(launcher.Modules.Start!.Errors);
        Assert.Contains(launcher.Modules.Start!.Problems, p => p is { Level: ProblemLevel.Note, Kind: "order" });
        launcher.Act("sortmodules");
        Assert.Equal(["lib", "user"], launcher.Modules.Rows.Select(r => r.Id));
        Assert.Equal("Sorted by dependencies: user moved after the modules it needs or loads after.", launcher.Modules.Result[0].Text);
        launcher.Act("sortmodules");
        Assert.Equal("Already in order: each module is after the modules it needs or loads after.", launcher.Modules.Result[0].Text);
        Settle(launcher);
        Assert.Null(launcher.Modules.PlayBlocked());

        // A start the game refused for its modules (the list changed behind the launcher's back): the problems, not the general notice.
        launcher.Act("page", "play");
        launcher.Act("play");
        games[0].End.SetResult(7);
        launcher.Poll();
        Assert.Equal("modules", launcher.Notice!.Kind);
        Assert.Equal("The enabled modules have errors", launcher.Notice.Title);
        Assert.Equal(["Go to Modules", "Close"], launcher.Notice.Buttons.Select(b => b.Label));
        launcher.Act("notice", "a");
        Assert.Equal("modules", launcher.Page);
        Settle(launcher);                                                         // checked again: nothing wrong here, so Play may try
        launcher.Act("play");
        games[1].End.SetResult(8);
        launcher.Poll();
        Assert.Equal("The modules' warnings are not accepted", launcher.Notice!.Title);
    }

    [Fact]
    public void While_a_module_is_imported_every_control_of_the_page_says_why_it_waits_and_cancel_stops_it()
    {
        var (launcher, _, _, _) = MakeModules();
        launcher.Act("addfrom", MakePack("one", ("materials/Prague/Wall.TGA", Tga(64, 128))));
        Settle(launcher);
        using var hold = new ManualResetEventSlim();
        launcher.Modules.OnProgress = _ => hold.Wait(TimeSpan.FromSeconds(20));
        launcher.Act("addfrom", MakePack("big", [.. Enumerable.Range(0, 4).Select(i => ($"materials/Prague/w{i}.tga", Tga(16, 16)))]));
        Assert.True(launcher.Modules.Importing);
        ReasonsShown(launcher, "importing");
        Assert.False(Seen(launcher.Screen.Find("sortModules")!));                 // one module: Sort by dependencies not offered
        foreach (var id in new[] { "addModule", "moduleUp", "moduleDown", "moduleRemove", "moduleOn", "moduleVerify" })
            Assert.Equal("Unavailable while a module is being imported.", ((Worked)launcher.Screen.Find(id)!).WhyNot);
        Assert.True(Seen(launcher.Screen.Find("cancelAdd")!));
        launcher.Act("page", "play");
        ReasonsShown(launcher, "importing, play");
        launcher.Act("page", "modules");
        launcher.Act("canceladd");
        hold.Set();
        Settle(launcher);
        launcher.Modules.OnProgress = null;
        Assert.Equal("Adding big cancelled. Add it again to carry on.", launcher.Modules.Result[0].Text);
        ReasonsShown(launcher, "cancelled");
    }

    // ---- the owner's tweaks: closing on start, Another folder, Verify files, the list's order, Verify of a module, a blanked film -------

    [Fact]
    public void Close_the_launcher_when_the_game_starts_is_off_by_default_kept_in_the_settings_and_when_on_starts_the_game_alone_and_quits()
    {
        string base1 = MakeBase();
        var (launcher, _, _, started, _, _) = Make(() => base1);
        launcher.Start([]);
        launcher.Act("page", "play");
        Assert.False(launcher.Settings.CloseOnStart);
        Drawn(launcher);
        var box = (Checkbox)launcher.Screen.Find("closeOnStart")!;
        Assert.False(box.Checked);
        Assert.Equal("Close the launcher when the game starts", box.Text);
        launcher.Screen.SetFocus(box);
        launcher.Screen.Key(UiKey.Space);                                         // the checkbox worked as the player works it
        launcher.TakeActions();
        Assert.True(launcher.Settings.CloseOnStart);
        Assert.Contains("\"closeOnStart\": true", File.ReadAllText(LauncherSettings.PathIn(User)), StringComparison.Ordinal);
        launcher.Fill();
        Assert.Equal("", launcher.Data.Text("playNote"));                      // the label says it (H2)

        // Remembered: a launcher started again has it on.
        var (again, _, _, startedAgain, _, _) = Make(() => base1);
        Assert.True(again.Settings.CloseOnStart);

        // Play: the game started on its own (its output not read), and the launcher quits; nothing runs for it to wait for.
        launcher.Act("play");
        var start = Assert.Single(started);
        Assert.True(start.Detached);
        Assert.True(launcher.QuitAsked);
        Assert.Null(launcher.Running);

        // With a module error, Play stays refused as before: the check runs before the start, not after it (exit code 7 cannot come back).
        again.Start([]);
        again.Act("page", "modules");
        string dir = Path.Combine(root, "packs", "needy");
        Write(Path.Combine(dir, "module.json"), "{ \"id\": \"needy\", \"name\": \"needy\", \"version\": \"1.0.0\", \"game\": \"classic\", \"dependencies\": { \"lib\": \"1.0.0\" } }");
        Put(Path.Combine(dir, "Data", "materials", "needy.tga"), Tga(8, 8));
        again.Act("addfrom", dir);
        Settle(again);
        again.Act("page", "play");
        again.Act("play");
        Assert.Empty(startedAgain);
        Assert.False(again.QuitAsked);
        again.Data.Set("closeOnStart", false);                                    // the checkbox turned off again (it writes its own flag)
        again.Act("closeonstart");
        Assert.False(again.Settings.CloseOnStart);
    }

    [Fact]
    public void Skip_the_opening_films_is_the_game_s_own_qol_switch_written_in_place_and_read_back()
    {
        string base1 = MakeBase();
        var (launcher, _, _, _, _, _) = Make(() => base1);
        launcher.Start([]);
        launcher.Act("page", "play");
        Drawn(launcher);
        var box = (Checkbox)launcher.Screen.Find("skipOpening")!;
        Assert.False(box.Checked);                                                // off by default: nothing written yet
        Assert.False(File.Exists(launcher.GameSettingsPath));

        // A player's own file, with a comment and another section, keeps both.
        Write(launcher.GameSettingsPath, "; mine\n[video]\ngamma = 1.2\n");
        launcher.Screen.SetFocus(box);
        launcher.Screen.Key(UiKey.Space);
        launcher.TakeActions();
        string text = File.ReadAllText(launcher.GameSettingsPath);
        Assert.Equal("; mine\n[video]\ngamma = 1.2\n\n[qol]\nskipopening = on\n", text);
        Assert.True(launcher.SkipOpening);
        launcher.Fill();
        Assert.True(launcher.Data.Flag("skipOpening"));

        // Written by hand as the game reads it (1), the box shows it; turned off, the same line changes.
        Write(launcher.GameSettingsPath, "[qol]\nskipopening=1\nskipstoryfilms = on\n");
        var (again, _, _, _, _, _) = Make(() => base1);
        again.Start([]);
        again.Act("page", "play");
        again.Fill();
        Assert.True(again.Data.Flag("skipOpening"));
        again.Data.Set("skipOpening", false);
        again.Act("skipopening");
        Assert.Equal("[qol]\nskipopening=off\nskipstoryfilms = on\n", File.ReadAllText(again.GameSettingsPath));
        Assert.False(again.SkipOpening);
    }

    [Fact]
    public void Another_folder_is_a_radio_of_the_copies_group_selected_by_typing_and_cleared_by_selecting_a_copy_found()
    {
        var (launcher, _, _, _, _, install) = Make(found: true);
        launcher.Start([]);
        launcher.Act("page", "setup");
        var page = launcher.Setup;
        Assert.Equal(Path.GetFullPath(install), Assert.Single(page.Installs).Folder);
        Assert.False(page.Other);
        Drawn(launcher);
        var other = (Radio)launcher.Screen.Find("copyOther")!;
        var copy = (Radio)launcher.Screen.Find("copy")!;
        Assert.Equal((copy.Group, true, false), (other.Group, copy.Checked, other.Checked));
        Assert.Equal("0", launcher.Data.Text("copySelected"));
        // The box, Browse… and Use this folder are in the radio's label: one of the choices, not a part of the copy above it.
        foreach (var id in new[] { "path", "browse", "useFolder", "pathHint" })
            Assert.Contains(other.Walk(), p => p.Id == id);
        Assert.Contains(other.Walk().OfType<TextPiece>(), t => t.Words == "Another folder:");
        Assert.DoesNotContain(copy.Walk(), p => p.Id == "path");
        launcher.Screen.SetFocus(copy);
        launcher.Screen.Key(UiKey.Down);                                          // the arrows step through the group: to Another folder
        launcher.TakeActions();
        Assert.True(page.Other);
        Assert.Null(page.Chosen);                                                 // nothing used yet: the steps wait for it
        launcher.Fill();
        Assert.Equal("other", launcher.Data.Text("copySelected"));
        Assert.Equal("Select a copy of the game first (step 1).", launcher.Data.Text("startWhy"));

        // A copy found selected again: the box cleared. Typing in the box selects Another folder.
        launcher.Act("choose", "0");
        Assert.False(page.Other);
        launcher.Act("edit", "path");
        launcher.Type(Path.Combine(root, "elsewhere"));
        Assert.True(page.Other);
        Assert.Null(page.Chosen);
        launcher.Fill();
        Assert.Equal("Select Use this folder first (step 1).", launcher.Data.Text("startWhy"));
        launcher.EndEdit(commit: false, putBack: true);                           // Escape: the box as it was
        Assert.Equal("", page.Path);
        launcher.Act("choose", "0");
        Assert.Equal("", page.Path);

        // Another copy of the game, not found by the stores: used from the box, Another folder selected with it.
        string second = Path.Combine(root, "second-copy");
        foreach (var f in Directory.EnumerateFiles(install, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".info", StringComparison.Ordinal)))
            Write(Path.Combine(second, Path.GetRelativePath(install, f)), File.ReadAllText(f));
        launcher.Act("edit", "path");
        launcher.Type(second);
        launcher.EndEdit(commit: true);
        Assert.True(page.Other);
        Assert.Equal(Path.GetFullPath(second), page.Chosen!.Folder);
        Assert.Single(page.Installs);                                             // it is no radio row of its own
        // The copy found typed into the box: its own radio selected, the box cleared.
        page.Typed(install);
        launcher.Act("usefolder");
        Assert.False(page.Other);
        Assert.Equal(("", Path.GetFullPath(install)), (page.Path, page.Chosen!.Folder));
        // Selecting Another folder by its radio with a folder used before: it is the copy again only once used.
        launcher.Act("chooseother");
        Assert.True(page.Other);
        Assert.Null(page.Chosen);
    }

    [Fact]
    public void Verify_files_reports_all_good_then_a_damaged_and_a_missing_file_and_repair_copies_them_again()
    {
        var (launcher, _, _, _, _, install) = Make();
        launcher.Start([]);
        var page = launcher.Setup;
        launcher.Fill();
        Assert.False(launcher.Data.Flag("canVerify"));
        Assert.False(launcher.Data.Flag("imported"));                             // not offered before anything is imported (Q3)
        page.UseFolder(install);
        page.StartImport();
        page.Wait();
        launcher.Poll();
        launcher.Fill();
        Assert.True(launcher.Data.Flag("canVerify"));
        Assert.True(launcher.Data.Flag("imported"));

        var steps = new List<SetupProgress>();
        page.OnProgress = steps.Add;
        launcher.Act("verify");
        Assert.True(page.Busy);
        page.Wait();
        page.OnProgress = null;
        launcher.Poll();
        Assert.Matches("^All [0-9]+ files are as they should be\\.$", page.Result[0].Text);
        Assert.Contains(steps, p => p.Step == SetupProgress.Hashes);              // its progress: every file read
        launcher.Fill();
        Assert.False(launcher.Data.Flag("offerRepair"));                          // nothing to repair: Repair is not offered

        string data = page.Service.DataFolder;
        File.WriteAllText(Path.Combine(data, "Video", "intro.bik"), "FILM FILM FILM");     // damaged, at its size
        File.Delete(Path.Combine(data, "Chronicles", "Prague.nsc"));                         // missing
        launcher.Act("verify");
        page.Wait();
        launcher.Poll();
        var words = page.Result.Select(l => l.Text).ToList();
        Assert.Equal("2 files are missing or changed: select Repair.", words[0]);
        Assert.Contains("    Missing: Chronicles/Prague.nsc", words);
        Assert.Contains("    Changed: Video/intro.bik", words);
        Assert.All(page.Result.Skip(1), l => Assert.True(l.Detail, l.Text));     // each file behind Details
        launcher.Fill();
        Assert.True(launcher.Data.Flag("resultHasDetails"));
        Assert.Single(launcher.Data.Items("result"));
        launcher.Act("setupdetails");
        launcher.Fill();
        Assert.Equal(3, launcher.Data.Items("result").Count);
        Assert.Equal("Hide details", launcher.Data.Text("resultDetailsWord"));
        launcher.Act("setupdetails");
        Assert.Contains(PageLog, l => l.Contains("Missing: Chronicles/Prague.nsc", StringComparison.OrdinalIgnoreCase) || l.Contains("missing: Chronicles/Prague.nsc", StringComparison.Ordinal));
        launcher.Fill();
        Assert.True(launcher.Data.Flag("offerRepair"));
        ReasonsShown(launcher, "verified, damaged");

        launcher.Act("repair");
        page.Wait();
        launcher.Poll();
        Assert.Equal("Repaired: 2 files copied again.", page.Result[0].Text);
        Assert.Equal("film film film", File.ReadAllText(Path.Combine(data, "Video", "intro.bik")));
        launcher.Act("verify");
        page.Wait();
        launcher.Poll();
        Assert.Matches("^All [0-9]+ files are as they should be\\.$", page.Result[0].Text);

        // The copy of the game gone: Repair says which copy to select and to import again.
        File.Delete(Path.Combine(data, "classes.zip"));
        Directory.Move(install, install + "-gone");
        launcher.Act("verify");
        page.Wait();
        launcher.Act("repair");
        page.Wait();
        launcher.Poll();
        Assert.True(page.Result[0].Warn);
        Assert.Contains("is not there any more: select your copy of the game in step 1 and select Import again", page.Result[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_modules_list_is_ordered_by_dragging_a_rows_handle_by_its_arrows_and_by_Alt_up_and_down()
    {
        var (launcher, _, _, _) = MakeModules();
        foreach (var name in new[] { "alpha", "beta", "gamma" })
        {
            launcher.Act("addfrom", MakePack(name, ($"materials/Prague/{name}.tga", Tga(8, 8))));
            Settle(launcher);
        }
        Assert.Equal(["alpha", "beta", "gamma"], launcher.Modules.Rows.Select(r => r.Id));
        Drawn(launcher);
        var list = (ListPiece)launcher.Screen.Find("moduleRows")!;
        Assert.Equal("modulemove", list.Reorder);
        // Compact rows: one line each, its handle, its checkbox and name, its version, its kinds as tags, and its arrows.
        var row = list.Children[0];
        Assert.Single(row.Walk().OfType<DragHandle>());
        Assert.Equal(["Move up", "Move down"], row.Walk().OfType<Button>().Where(b => b.Icon is not null).Select(b => b.Text));
        Assert.Equal(["up", "down"], row.Walk().OfType<Button>().Where(b => b.Icon is not null).Select(b => b.Icon));
        Assert.True(row.Rect.Height < 70f, $"a row is {row.Rect.Height} units high");

        // Dragged: alpha's handle pressed, moved down past gamma's middle, dropped there.
        launcher.Screen.SetFocus(list.Children[0].Walk().OfType<Checkbox>().First());   // the row in sight
        Drawn(launcher);
        float u = launcher.Screen.Metrics.Pixel;
        var grip = list.Children[0].Walk().OfType<DragHandle>().Single().Rect;
        launcher.Screen.PointerMove((grip.X + grip.Width / 2) * u, (grip.Y + grip.Height / 2) * u);
        launcher.Screen.PointerDown();
        var gamma = list.Children[2].Rect;
        launcher.Screen.PointerMove((grip.X + grip.Width / 2) * u, (gamma.Y + gamma.Height * 0.8f) * u);
        Assert.Equal((0, 2), (launcher.Screen.Drag!.From, launcher.Screen.Drag.To));
        var drawn = new DrawList();
        launcher.Screen.Draw(drawn);                                              // the drop line drawn in the kit's colour
        launcher.Screen.PointerUp();
        launcher.TakeActions();
        Assert.Equal(["beta", "gamma", "alpha"], launcher.Modules.Rows.Select(r => r.Id));
        Settle(launcher);

        // Alt+Up on the focused row's checkbox: a place up, the focus with it.
        Drawn(launcher);
        list = (ListPiece)launcher.Screen.Find("moduleRows")!;
        launcher.Screen.SetFocus(list.Children[2].Walk().OfType<Checkbox>().First());
        launcher.Screen.Key(UiKey.AltUp);
        launcher.TakeActions();
        Assert.Equal(["beta", "alpha", "gamma"], launcher.Modules.Rows.Select(r => r.Id));
        Drawn(launcher);
        Assert.Same(list.Children[1].Walk().OfType<Checkbox>().First(), launcher.Screen.Focus);
        // The arrows do the same.
        launcher.Act("moduledown", "beta");
        Assert.Equal(["alpha", "beta", "gamma"], launcher.Modules.Rows.Select(r => r.Id));
        var words = Said(launcher);
        Assert.DoesNotContain("The first module cannot move up. The last module cannot move down.", words);   // trivially deduced (E1)
        Assert.DoesNotContain(words, w => w.StartsWith("Drag a module by its handle", StringComparison.Ordinal));  // the docs say it (H4)
        // Two rows' details open at once, each under its own row; the others stay closed.
        launcher.Act("moduleopen", "gamma");
        launcher.Act("moduleopen", "alpha");
        Drawn(launcher);
        list = (ListPiece)launcher.Screen.Find("moduleRows")!;
        Assert.Equal([true, false, true], list.Children.Select(c => Seen(c.Walk().First(p => p.Id == "moduleDetailsPanel"))));
        Settle(launcher);
    }

    [Fact]
    public void Verify_of_a_module_finds_a_changed_file_and_the_module_is_an_error_until_it_is_added_again()
    {
        var (launcher, _, _, _) = MakeModules();
        string pack = MakePack("walls", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 3)));
        launcher.Act("addfrom", pack);
        Settle(launcher);
        launcher.Act("moduleopen", "walls");
        launcher.Act("moduleverify", "walls");
        Settle(launcher);
        Assert.Equal("Verified: its file is as imported.", launcher.Modules.VerifyResult!.Value.Lines[0].Text);
        Assert.Contains(launcher.Modules.VerifyResult!.Value.Lines[0].Text, Said(launcher));

        File.WriteAllBytes(Path.Combine(User, "PlayerModules", "walls", "Data", "materials", "Prague", "Wall.TGA"), Tga(64, 128, red: 4));
        launcher.Act("moduleverify", "walls");
        Settle(launcher);
        var lines = launcher.Modules.VerifyResult!.Value.Lines;
        Assert.True(lines.All(l => l.Warn));
        Assert.Equal("1 of its files is not as imported: materials/Prague/Wall.TGA: changed since the import.", lines[0].Text);
        Assert.Equal("Remove it and add it again from its pack.", lines[1].Text);
        var error = Assert.Single(launcher.Modules.Start!.Errors);
        Assert.Contains("(found by Verify): remove the module and add it again", error.Text, StringComparison.Ordinal);
        Assert.NotNull(launcher.Modules.PlayBlocked());
        launcher.Act("addfrom", pack);
        Settle(launcher);
        Assert.Empty(launcher.Modules.Start!.Errors);
    }

    [Fact]
    public void A_packs_blanked_film_is_a_note_and_the_game_plays_with_it_on()
    {
        var (launcher, _, _, _) = MakeModules();
        launcher.Act("addfrom", MakePack("Blank Films", ("Video/Logo.bik", Encoding.ASCII.GetBytes("#Dummy.bik")), ("Video/Trailer.bik", Encoding.ASCII.GetBytes("#Dummy.bik"))));
        Settle(launcher);
        var start = launcher.Modules.Start!;
        Assert.Empty(start.Errors);
        Assert.Null(launcher.Modules.PlayBlocked());
        var words = Said(launcher);
        Assert.Contains("Note: Blank Films: it is not a film: it takes 1.1's film out, so the game skips it.", words);   // by its name
        Assert.Contains("Files: Video/Logo.bik", words);
        Assert.DoesNotContain(words, w => w.Contains("do not read", StringComparison.Ordinal));
    }

    [Fact]
    public void A_module_of_the_same_id_from_another_folder_asks_replace_and_cancel_changes_nothing_and_replace_keeps_its_place()
    {
        var (launcher, _, _, _) = MakeModules();
        launcher.Act("addfrom", MakePack("first", ("materials/Prague/first.tga", Tga(8, 8))));
        Settle(launcher);
        string old = MakePack("Texturemod", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 1)), ("materials/Prague/old.tga", Tga(8, 8)));
        launcher.Act("addfrom", old);
        Settle(launcher);
        launcher.Act("addfrom", MakePack("last", ("materials/Prague/last.tga", Tga(8, 8))));
        Settle(launcher);
        launcher.Act("moduletoggle", "texturemod");
        Settle(launcher);
        string moved = Path.Combine(root, "moved", "Texturemod");
        Put(Path.Combine(moved, "materials", "Prague", "Wall.TGA"), Tga(64, 128, red: 2));
        string record = Path.Combine(User, "PlayerModules", "texturemod", "vitomir-module.json");
        string before = File.ReadAllText(record);

        // Add a module… on the other folder: the question, in the page's confirm style (Cancel the default), not the command line's refusal.
        launcher.Act("addfrom", moved);
        Settle(launcher);
        var notice = launcher.Notice!;
        Assert.Equal("Replace Texturemod?", notice.Title);
        Assert.Equal($"The Texturemod in the list came from another folder ({old}). Replacing it takes this folder's files and keeps its place in the load order and whether it is enabled.",
                     Assert.Single(notice.Lines).Text);
        Assert.Equal(["Replace", "Cancel"], notice.Buttons.Select(b => b.Label));
        Assert.Equal((1, 1), (notice.Default, notice.CancelAt));
        Assert.DoesNotContain(Said(launcher), w => w.Contains("--replace", StringComparison.Ordinal) || w.Contains("module-remove", StringComparison.Ordinal));

        // Cancel: nothing changes, and nothing is said.
        launcher.Act("escape");
        Settle(launcher);
        Assert.Null(launcher.Notice);
        Assert.Null(launcher.Modules.ReplaceAsked);
        Assert.Empty(launcher.Modules.Result);
        Assert.Equal(before, File.ReadAllText(record));
        Assert.Equal(["first", "texturemod", "last"], launcher.Modules.Rows.Select(r => r.Id));

        // Replace: this folder's files, in its place, still off; what changed in one line.
        launcher.Act("addfrom", moved);
        Settle(launcher);
        Assert.Equal("Replace Texturemod?", launcher.Notice!.Title);
        launcher.Act("notice", "a");
        Settle(launcher);
        Assert.Null(launcher.Notice);
        Assert.Equal([("first", true), ("texturemod", false), ("last", true)], launcher.Modules.Rows.Select(r => (r.Id, r.On)));
        var replaced = launcher.Modules.Rows[1].Module!;
        Assert.Equal(Path.GetFullPath(moved), replaced.Record.Source);
        Assert.Equal(["materials/Prague/Wall.TGA"], replaced.Record.Files.Select(f => f.Path));
        Assert.False(File.Exists(Path.Combine(replaced.DataFolder, "materials", "Prague", "old.tga")));
        Assert.True(File.Exists(Path.Combine(old, "materials", "Prague", "old.tga")));             // the old pack is never touched
        Assert.Equal("Texturemod replaced: 1 file (1 changed, 1 no longer in it).", launcher.Modules.Result[0].Text);
        Assert.Contains("Texturemod replaced: 1 file (1 changed, 1 no longer in it).", Said(launcher));
    }

    [Fact]
    public void Update_takes_in_an_edited_file_drops_one_put_in_by_hand_and_is_disabled_with_why_when_its_folder_is_gone()
    {
        var (launcher, _, _, _) = MakeModules();
        string pack = MakePack("Texturemod", ("materials/Prague/Wall.TGA", Tga(64, 128, red: 1)), ("materials/Prague/shop.tga", Tga(8, 8)));
        launcher.Act("addfrom", pack);
        Settle(launcher);
        launcher.Act("addfrom", MakePack("other", ("materials/Prague/other.tga", Tga(8, 8))));
        Settle(launcher);
        launcher.Act("moduleup", "other");
        Settle(launcher);
        string data = Path.Combine(User, "PlayerModules", "texturemod", "Data");
        Put(Path.Combine(data, "misc", "game.ntt"), Encoding.ASCII.GetBytes("by hand"));
        Put(Path.Combine(data, "misc", "Templates", "darkAges.not"), Encoding.ASCII.GetBytes("by hand"));
        launcher.Act("moduletoggle", "texturemod");                                // any change checks again
        launcher.Act("moduletoggle", "texturemod");
        Settle(launcher);

        // One error for the module, not one a file, in plain words, with what to do.
        var error = Assert.Single(launcher.Modules.Start!.Errors);
        Assert.Equal("added", error.Kind);
        var words = Said(launcher);
        Assert.Contains("Error: Texturemod has files Vitomir did not add.", words);
        Assert.Contains("These files were put into Vitomir's copy of the module by hand: misc/Templates/darkAges.not, misc/game.ntt. "
                        + "Vitomir plays only what it added, so it can check a module is the same on every machine.", words);
        Assert.Contains("To do: put the files in the module's own folder and select Update, or turn the module off.", words);
        Assert.DoesNotContain(words, w => w.Contains("put there after the import", StringComparison.Ordinal));

        // Update in its details: its folder read again, the edit taken in, the hand's files taken out, its place and on kept.
        Put(Path.Combine(pack, "materials", "Prague", "Wall.TGA"), Tga(64, 128, red: 2));
        launcher.Act("moduleopen", "texturemod");
        Drawn(launcher);
        var update = (Button)launcher.Screen.Find("moduleRows")!.Walk().First(p => p.Id == "moduleUpdate" && Seen(p));
        Assert.Equal("Update", update.Text);
        Assert.True(update.Enabled);
        launcher.Act("moduleupdate", "texturemod");
        Settle(launcher);
        Assert.Equal([("other", true), ("texturemod", true)], launcher.Modules.Rows.Select(r => (r.Id, r.On)));
        var m = launcher.Modules.Rows[1].Module!;
        Assert.Equal(Vitomir.Platform.PlayerModules.PlayerModule.Hash(Tga(64, 128, red: 2)), m.Record.Files.Single(f => f.Path == "materials/Prague/Wall.TGA").Sha256);
        Assert.False(File.Exists(Path.Combine(data, "misc", "game.ntt")));
        Assert.Empty(launcher.Modules.Start!.Errors);
        var result = launcher.Modules.Result.Select(l => l.Text).ToList();
        Assert.Equal("Texturemod updated: 2 files (1 changed).", result[0]);
        Assert.Equal("Taken out of Vitomir's copy, as they are not in its folder: misc/Templates/darkAges.not, misc/game.ntt.", result[1]);

        // Its folder moved away: Update disabled, with the reason in one line.
        Directory.Move(pack, pack + " (moved)");
        launcher.Act("moduleopen", "texturemod");
        launcher.Act("moduleopen", "texturemod");
        Drawn(launcher);
        update = (Button)launcher.Screen.Find("moduleRows")!.Walk().First(p => p.Id == "moduleUpdate" && Seen(p));
        Assert.False(update.Enabled);
        Assert.Equal($"Its folder, {Path.GetFullPath(pack)}, is not there any more: use Add a module\u2026 with its new folder.", update.WhyNot);
        ReasonsShown(launcher, "update, its folder gone");
        launcher.Act("moduleupdate", "texturemod");
        Assert.False(launcher.Modules.Importing);
    }

    // ---- the Films page (ours): improved films made on this PC, set film by film ---------------------------------------------------

    /// <summary>The Films page over stand-ins: tools already there, a game's films, and programs that write what the real ones would.</summary>
    FilmsPage StandInFilms(string folder, FilmPrograms programs)
    {
        string game = Path.Combine(root, "films-game");
        foreach (var f in Vitomir.Platform.Films.FilmCatalog.All) Write(Path.Combine(game, "video", f.File), "BIKi");
        var tools = new Vitomir.Platform.Films.FilmTools(folder, [new("realesrgan", "Real-ESRGAN", "", "", 45_000_000, "realesrgan", ["realesrgan"], "BSD 3-Clause", ""),
                                                                 new("rife", "RIFE", "", "", 431_000_000, "rife", ["rife"], "MIT", "")]);
        foreach (var p in tools.Packs) Write(Path.Combine(tools.FolderOf(p.Id), p.Program), "program");
        return new FilmsPage(folder, () => new FFmpegFound("ffmpeg", "ffprobe", "a test's"), () => game, _ => { }, tools, programs.Run);
    }

    [Fact]
    public void The_movies_page_reads_the_disk_once_not_every_frame()
    {
        int looks = 0;
        string game = Path.Combine(root, "films-game-once");
        Write(Path.Combine(game, "video", "intro.bik"), "BIKi");
        var (launcher, _, _, _, _, _) = Make(films: folder => new FilmsPage(folder, () => null, () => { Interlocked.Increment(ref looks); return game; }, _ => { }));
        launcher.Start([]);
        launcher.Act("page", "films");
        for (int i = 0; i < 200; i++) { launcher.Poll(); launcher.Fill(); }
        Assert.InRange(looks, 1, 2);                                              // once for the page opened, not once a frame
        launcher.Act("page", "play");
        for (int i = 0; i < 200; i++) { launcher.Poll(); launcher.Fill(); }
        Assert.InRange(looks, 1, 2);                                              // nor on another page
        Assert.Equal("Movies", PageTitleOf(launcher, "films"));
    }

    static string PageTitleOf(Vitomir.Launcher.Launcher launcher, string page)
    {
        launcher.Act("page", page);
        launcher.Fill();
        return launcher.Data.Text("pageTitle");
    }

    [Fact]
    public void The_films_page_makes_what_is_set_asks_before_every_removal_and_cancel_and_keeps_the_other_version()
    {
        var programs = new FilmPrograms();
        var (launcher, _, _, _, _, _) = Make(films: folder => StandInFilms(folder, programs));
        launcher.Start([]);
        launcher.Act("page", "films");
        var words = Drawn(launcher);
        Assert.Contains(words, w => w.Contains("Upscaling takes a long time", StringComparison.Ordinal));
        Assert.Contains(words, w => w.Contains("gaming above all", StringComparison.Ordinal));
        Assert.Equal(11, launcher.Data.Items("films").Count);
        ReasonsShown(launcher, "films, nothing set");

        // The opening set to Best: not finished until made; the start asks first, with the warnings.
        launcher.Act("filmchoice", "intro:best");
        launcher.Fill();
        var intro = launcher.Data.Items("films").Single(i => i.Text("stem") == "intro");
        Assert.Equal("best", intro.Text("choice"));
        Assert.StartsWith("Not finished", intro.Text("bestText"), StringComparison.Ordinal);
        launcher.Act("filmstart");
        Assert.Equal("Upscale this movie?", launcher.Notice!.Title);
        Assert.Contains(launcher.Notice.Lines, l => l.Text.Contains("slows the work down", StringComparison.Ordinal));
        launcher.Act("notice", "a");
        launcher.Films.Maker.Wait(TimeSpan.FromSeconds(30));
        launcher.Poll();
        launcher.Fill();
        intro = launcher.Data.Items("films").Single(i => i.Text("stem") == "intro");
        Assert.StartsWith("Made", intro.Text("bestText"), StringComparison.Ordinal);
        Assert.True(intro.Flag("bestMade"));

        // Fast made too; set back to Best, Fast stays.
        launcher.Act("filmchoice", "intro:fast");
        launcher.Act("filmstart");
        launcher.Act("notice", "a");
        launcher.Films.Maker.Wait(TimeSpan.FromSeconds(30));
        launcher.Poll();
        launcher.Act("filmchoice", "intro:best");
        Assert.True(launcher.Films.Versions.Ready(Vitomir.Platform.Films.FilmVersion.Fast, "intro.bik"));

        // A removal asks, names what it frees, and Cancel keeps it.
        launcher.Act("filmremove", "intro:best");
        Assert.Equal("Remove the Best version of New game opening?", launcher.Notice!.Title);
        Assert.Contains(launcher.Notice.Lines, l => l.Text.Contains("play the original again", StringComparison.Ordinal));
        launcher.Act("escape");
        Assert.True(launcher.Films.Versions.Ready(Vitomir.Platform.Films.FilmVersion.Best, "intro.bik"));
        launcher.Act("filmremove", "intro:best");
        launcher.Act("notice", "a");
        Assert.False(launcher.Films.Versions.Ready(Vitomir.Platform.Films.FilmVersion.Best, "intro.bik"));
        Assert.Equal(Vitomir.Platform.Films.FilmVersion.Original, launcher.Films.Versions.Choice("intro.bik"));

        // Cancelling the work: asked, the film in progress named and thrown away; Play while working asks too.
        programs.Hold = "trailer";
        launcher.Act("filmall", "fast");
        launcher.Act("filmstart");
        launcher.Act("notice", "a");
        Assert.True(programs.Holding.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        launcher.Poll();
        ReasonsShown(launcher, "films, working");
        launcher.Act("page", "play");
        launcher.Play();
        Assert.Equal("Cancel upscaling and play?", launcher.Notice!.Title);
        launcher.Act("escape");
        launcher.Act("page", "films");
        launcher.Act("filmstop");
        Assert.Equal("Cancel upscaling?", launcher.Notice!.Title);
        Assert.Contains(launcher.Notice.Lines, l => l.Warn && l.Text.Contains("discarded", StringComparison.Ordinal));
        launcher.Act("notice", "a");
        launcher.Films.Maker.Wait(TimeSpan.FromSeconds(30));
        launcher.Poll();
        launcher.Fill();
        var trailer = launcher.Data.Items("films").Single(i => i.Text("stem") == "trailer");
        Assert.StartsWith("Not finished", trailer.Text("fastText"), StringComparison.Ordinal);

        // Every film removed, then the tools: each asked first.
        launcher.Act("filmremoveall");
        Assert.Equal("Remove all upscaled movies?", launcher.Notice!.Title);
        launcher.Act("notice", "a");
        Assert.Empty(launcher.Films.Versions.Made());
        launcher.Act("filmremovetools");
        Assert.Equal("Remove the upscaling tools?", launcher.Notice!.Title);
        launcher.Act("notice", "a");
        Assert.False(Directory.Exists(launcher.Films.Tools.Folder));
        ReasonsShown(launcher, "films, no tools");
    }

    /// <summary>FFmpeg, ffprobe, Real-ESRGAN and RIFE as the film maker calls them, each writing what the real one would.</summary>
    public sealed class FilmPrograms
    {
        readonly Dictionary<string, double> lengths = new(StringComparer.Ordinal);
        public string? Hold;
        public ManualResetEventSlim Holding { get; } = new();

        public async Task<Vitomir.Platform.Films.FilmRun> Run(string program, IReadOnlyList<string> args, CancellationToken token)
        {
            var list = args.ToList();
            string? Arg(string name) => list.IndexOf(name) is var i and >= 0 && i + 1 < list.Count ? list[i + 1] : null;
            string last = list[^1];
            static void Frames(string folder, int n)
            {
                Directory.CreateDirectory(folder);
                for (int i = 1; i <= n; i++) File.WriteAllText(Path.Combine(folder, $"{i:00000000}.png"), "png");
            }
            switch (program)
            {
                case "ffprobe":
                    double length = last.EndsWith(".mp4", StringComparison.Ordinal) ? lengths.GetValueOrDefault(last) : 2.0;
                    return new(0, $"r_frame_rate=15/1\nheight=346\nduration={length.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n");
                case "ffmpeg" when list.Contains("-framerate"):
                    int made = Directory.GetFiles(Path.GetDirectoryName(Arg("-i")!)!, "*.png").Length;
                    File.WriteAllText(last, "mp4");
                    lengths[last] = made / 30.0;
                    return new(0, "");
                case "ffmpeg" when last.EndsWith("%08d.png", StringComparison.Ordinal):
                    Frames(Path.GetDirectoryName(last)!, 30);
                    return new(0, "");
                case "ffmpeg":
                    if (last.EndsWith(".m4a", StringComparison.Ordinal)) File.WriteAllText(last, "sound");
                    return new(0, "");
                case var p when p.EndsWith("realesrgan", StringComparison.Ordinal):
                    if (Hold is { } stem && list.Any(a => a.Contains(stem + "-", StringComparison.Ordinal)))
                    {
                        Holding.Set();
                        await Task.Delay(Timeout.Infinite, token);
                    }
                    Frames(Arg("-o")!, Directory.GetFiles(Arg("-i")!, "*.png").Length);
                    return new(0, "");
                case var p when p.EndsWith("rife", StringComparison.Ordinal):
                    Frames(Arg("-o")!, int.Parse(Arg("-n")!, System.Globalization.CultureInfo.InvariantCulture));
                    return new(0, "");
            }
            return new(1, "unknown");
        }
    }
}
