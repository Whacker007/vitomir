// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.PlayerModules;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Tests;

/// <summary>
/// **The user folder**: <c>vitomir.ini</c>'s forms (none, empty, an empty <c>user</c>, a whole path, a relative one inside the Vitomir
/// folder, environment variables, quotes, every bad line), the precedence (<c>VITOMIR_USERDATA</c>, then the ini, then the usual place),
/// the top of the Vitomir folder from <c>bin</c>; the readiness check's every item failing and passing; the first start's choices; the
/// in-use lock; and the move (done and checked, the old folder kept or removed, cancelled, failed in the middle and carried on, each
/// refusal).
/// </summary>
public sealed class UserFolderTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-userfolder-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    string Top => Path.Combine(root, "Vitomir");
    string Bin => Path.Combine(Top, "bin");
    string Local => Path.Combine(root, "Local");

    UserFolderPlace Place(string? variable = null) => new(variable, Bin, Local);

    void Ini(string text)
    {
        Directory.CreateDirectory(Top);
        File.WriteAllText(Path.Combine(Top, VitomirIni.FileName), text);
    }

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    FolderPlaces Places(string? writeRefused = null, string? driveGone = null, params string[] synced) =>
        new([Path.Combine(root, "Program Files"), Path.Combine(root, "Program Files (x86)")], synced,
            drive => driveGone is null || !drive.Equals(driveGone, StringComparison.OrdinalIgnoreCase),
            folder => writeRefused is not null && UserFolderMarks.Under(folder, writeRefused) ? "it is not allowed to" : null);

    sealed class Room(long free) : ISetupMachine
    {
        public string? RegistryValue(string key, string name) => null;
        public IReadOnlyList<string> FixedDrives() => [];
        public string? Environment(string name) => null;
        public long FreeBytes(string folder) => free;
    }

    // ---- the ini ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_top_of_the_Vitomir_folder_is_bin_s_parent_or_the_program_s_own_folder()
    {
        Assert.Equal(Top, UserFolderPlace.TopOf(Bin));
        Assert.Equal(Top, UserFolderPlace.TopOf(Bin + Path.DirectorySeparatorChar));
        Assert.Equal(Top, UserFolderPlace.TopOf(Path.Combine(Top, "BIN")));
        string build = Path.Combine(root, "src", "Vitomir.Launcher", "bin", "Debug", "net10.0");
        Assert.Equal(build, UserFolderPlace.TopOf(build));                                   // a developer's build folder is its own top
        Assert.Equal(Path.Combine(Top, VitomirIni.FileName), Place().IniPath);
        Assert.Equal(Path.Combine(Top, "UserData"), Place().Portable);
        Assert.Equal(Path.Combine(Local, "Vitomir"), Place().Usual);
        Assert.Equal(Bin, Place().Bin);
        Assert.Null(new UserFolderPlace(null, build, Local).Bin);
    }

    [Fact]
    public void No_ini_an_empty_one_and_an_empty_user_are_the_usual_place()
    {
        var none = UserFolderChoice.Resolve(Place());
        Assert.Equal(UserFolderSource.Usual, none.Source);
        Assert.Equal(Place().Usual, none.Folder);
        Assert.False(none.Ini.Exists);
        Assert.Null(none.Problem);
        Assert.False(none.Chosen);                                                           // a first start: nothing there yet
        Directory.CreateDirectory(Place().Usual);
        Assert.False(UserFolderChoice.Resolve(Place()).Chosen);                              // there and empty: still a first start
        Write(Path.Combine(Place().Usual, "Logs", "platform-1.log"), "a line");
        var older = UserFolderChoice.Resolve(Place());
        Assert.True(older.OlderVitomir);                                                     // an older Vitomir's folder of the same name:
        Assert.False(older.Chosen);                                                          // never taken without a user folder's mark,
        var refused = UserFolderSetup.UseUsual(Place(), Places(), "the tests");               // nor made one, nor deleted
        Assert.Equal((false, UserFolderChoice.OlderVitomirWords + "."), (refused.Done, refused.Line));
        Assert.True(File.Exists(Path.Combine(Place().Usual, "Logs", "platform-1.log")));
        Assert.False(File.Exists(Path.Combine(Place().Usual, UserFolderMarks.MarkName)));
        Write(Path.Combine(ImportedData.Folder(Place().Usual), ImportedData.MarkerName), "{}");
        Assert.True(UserFolderChoice.Resolve(Place()).Chosen);                               // the import's marker: a user folder
        Directory.Delete(Place().Usual, true);
        Write(Path.Combine(Place().Usual, UserFolderMarks.MarkName), "{}");
        Assert.True(UserFolderChoice.Resolve(Place()).Chosen);                               // the launcher's mark: a user folder
        Directory.Delete(Place().Usual, true);

        foreach (var text in new[] { "", "; only a comment\n# and another\n", "[folders]\n", "[folders]\nuser =\n", "[Folders]\r\nUSER =   \r\n" })
        {
            Ini(text);
            var c = UserFolderChoice.Resolve(Place());
            Assert.True(c.Problem is null, $"{text}: {c.Problem}");
            Assert.Equal(UserFolderSource.Usual, c.Source);
            Assert.Equal(Place().Usual, c.Folder);
            Assert.True(c.Chosen);                                                           // an ini says so: the usual place, chosen
        }
    }

    [Fact]
    public void A_whole_path_a_relative_one_quotes_and_environment_variables_are_read()
    {
        string own = Path.Combine(root, "My data", "Vitomir");
        Ini($"; where the saves go\n[folders]\nuser = {own}\n");
        var whole = UserFolderChoice.Resolve(Place());
        Assert.Equal(UserFolderSource.Ini, whole.Source);
        Assert.Equal(own, whole.Folder);
        Assert.Equal(3, whole.Ini.UserLine);
        Assert.False(whole.Portable);
        Assert.Contains("line 3", whole.How, StringComparison.Ordinal);

        Ini("[folders]\nuser = UserData\n");
        var portable = UserFolderChoice.Resolve(Place());
        Assert.Equal(Path.Combine(Top, "UserData"), portable.Folder);
        Assert.True(portable.Portable);
        Assert.Contains("portable", portable.How, StringComparison.Ordinal);
        Ini("[folders]\nuser = Saves" + Path.DirectorySeparatorChar + "Mine\n");
        Assert.Equal(Path.Combine(Top, "Saves", "Mine"), UserFolderChoice.Resolve(Place()).Folder);

        Ini($"[folders]\nuser = \"{own}\" ; quoted, a comment after it\n");
        Assert.Equal(own, UserFolderChoice.Resolve(Place()).Folder);
        Ini($"[folders]\nuser = {own} ; no comment after an unquoted value\n");
        Assert.EndsWith("; no comment after an unquoted value", UserFolderChoice.Resolve(Place()).Folder, StringComparison.Ordinal);

        string variable = "VITOMIR_TEST_DATA_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(variable, Path.Combine(root, "expanded"));
        try
        {
            Ini($"[folders]\nuser = %{variable}%{Path.DirectorySeparatorChar}Vitomir\n");
            Assert.Equal(Path.Combine(root, "expanded", "Vitomir"), UserFolderChoice.Resolve(Place()).Folder);
        }
        finally { Environment.SetEnvironmentVariable(variable, null); }
        Ini($"[folders]\nuser = %{variable}%{Path.DirectorySeparatorChar}Vitomir\n");
        var unset = UserFolderChoice.Resolve(Place());
        Assert.Contains($"%{variable}% is not set on this machine", unset.Problem, StringComparison.Ordinal);
        Assert.Contains("vitomir.ini, line 2", unset.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[display]\nwidth = 3\n", 1, "[display] is not a section of vitomir.ini")]
    [InlineData("[folders]\ncolour = red\n", 2, "colour is not a setting of [folders]")]
    [InlineData("user = D:\\Vitomir-data\n", 1, "user is outside a section")]
    [InlineData("[folders]\nuser = a\nuser = b\n", 3, "user is set twice (lines 2 and 3)")]
    [InlineData("[folders]\njust some words\n", 2, "it is not a setting")]
    [InlineData("[folders]\nuser = \"D:\\Vitomir-data\n", 2, "its quote does not close")]
    [InlineData("[folders]\nuser = \"D:\\Vitomir-data\" more\n", 2, "words after the closing quote")]
    [InlineData("[folders\nuser = a\n", 1, "a section's name ends with ]")]
    public void A_bad_line_is_a_problem_with_its_line_never_a_fallback(string text, int line, string why)
    {
        Ini(text);
        var c = UserFolderChoice.Resolve(Place());
        var problem = Assert.Single(c.Problems);
        Assert.Equal(line, problem.Line);
        Assert.Contains(why, problem.Why, StringComparison.Ordinal);
        Assert.StartsWith($"vitomir.ini, line {line} (\"", c.Problem, StringComparison.Ordinal);
        // The readiness check refuses it, with its line, and sends the player to the Folders page.
        var item = Readiness.UserFolder(c, Places());
        Assert.Equal(ReadyState.Failed, item.State);
        Assert.StartsWith($"vitomir.ini line {line} is not understood (\"", item.Line, StringComparison.Ordinal);
        Assert.EndsWith("Correct it, or choose your user folder again on Folders.", item.Line, StringComparison.Ordinal);
        Assert.Equal("folders", item.Fix);
    }

    [Fact]
    public void A_path_that_is_a_file_is_a_problem()
    {
        string file = Path.Combine(root, "a-file.txt");
        File.WriteAllText(file, "not a folder");
        Ini($"[folders]\nuser = {file}\n");
        Assert.Contains("is a file, not a folder", UserFolderChoice.Resolve(Place()).Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void VITOMIR_USERDATA_beats_the_ini_which_beats_the_usual_place()
    {
        string mine = Path.Combine(root, "mine"), dev = Path.Combine(root, "dev");
        Ini($"[folders]\nuser = {mine}\n");
        Assert.Equal(mine, UserFolderChoice.Resolve(Place()).Folder);
        var variable = UserFolderChoice.Resolve(Place(dev));
        Assert.Equal(UserFolderSource.Variable, variable.Source);
        Assert.Equal(dev, variable.Folder);
        Assert.True(variable.Chosen);
        Assert.Contains("VITOMIR_USERDATA", variable.How, StringComparison.Ordinal);
        Ini("[nonsense]\n");
        Assert.Null(UserFolderChoice.Resolve(Place(dev)).Problem);                             // the variable's: the ini is not read for it
        File.Delete(Path.Combine(Top, VitomirIni.FileName));
        Assert.Equal(Place().Usual, UserFolderChoice.Resolve(Place()).Folder);
    }

    [Fact]
    public void The_ini_is_written_with_its_comments_kept()
    {
        string made = VitomirIni.WithUser(null, "UserData");
        Assert.StartsWith(";", made, StringComparison.Ordinal);
        var parsed = VitomirIni.Parse("x", made);
        Assert.Empty(parsed.Problems);
        Assert.Equal("UserData", parsed.User);

        string mine = "; mine\n[folders]\n# the folder\nuser = D:\\Vitomir-data\\Old\n";
        var replaced = VitomirIni.Parse("x", VitomirIni.WithUser(mine, @"D:\Vitomir-data\New data"));
        Assert.Equal(@"D:\Vitomir-data\New data", replaced.User);
        Assert.Equal(["; mine", "[folders]", "# the folder", @"user = D:\Vitomir-data\New data"], replaced.Lines);
        Assert.Equal(" padded ", VitomirIni.Parse("x", VitomirIni.WithUser(mine, " padded ")).User);   // quoted to keep its spaces
        Assert.Equal("", VitomirIni.Parse("x", VitomirIni.WithUser(mine, "")).User);
        var added = VitomirIni.Parse("x", VitomirIni.WithUser("; nothing yet\n", "UserData"));
        Assert.Empty(added.Problems);
        Assert.Equal("UserData", added.User);
    }

    // ---- the readiness check --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_user_folder_item_fails_and_passes_on_each_of_its_rules()
    {
        // Not chosen: a first start.
        var first = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places());
        Assert.Equal(ReadyState.Failed, first.State);
        Assert.Equal("Not chosen yet.", first.Line);
        Assert.Equal("choices", first.Fix);

        // The ini names a folder that is not there: its drive gone, or the folder.
        string away = Path.Combine(root, "away", "Vitomir-data");
        Ini($"[folders]\nuser = {away}\n");
        var gone = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places(driveGone: Path.GetPathRoot(away)));
        Assert.Equal($"{away} is not there: is its drive plugged in?", gone.Line);
        var missing = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places());
        Assert.Contains("is not there (vitomir.ini names it, line 2)", missing.Line, StringComparison.Ordinal);
        Assert.Equal(ReadyState.Failed, missing.State);

        // In Program Files; not writable; in a synced folder (a warning, not a refusal); and ready.
        string programs = Path.Combine(root, "Program Files", "Vitomir data");
        Directory.CreateDirectory(programs);
        Ini($"[folders]\nuser = {programs}\n");
        var pf = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places());
        Assert.Contains("is in Program Files", pf.Line, StringComparison.Ordinal);
        Assert.True(pf.Stops);
        Directory.CreateDirectory(away);
        Ini($"[folders]\nuser = {away}\n");
        var locked = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places(writeRefused: away));
        Assert.Contains("cannot be written to (it is not allowed to)", locked.Line, StringComparison.Ordinal);
        string onedrive = Path.Combine(root, "OneDrive - Home", "Vitomir");
        Directory.CreateDirectory(onedrive);
        Ini($"[folders]\nuser = {onedrive}\n");
        var synced = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places());
        Assert.Equal(ReadyState.Warning, synced.State);
        Assert.False(synced.Stops);
        Assert.Contains("is in OneDrive: syncing can make a save fail", synced.Line, StringComparison.Ordinal);
        string dropbox = Path.Combine(root, "Box", "Vitomir");
        Directory.CreateDirectory(dropbox);
        Ini($"[folders]\nuser = {dropbox}\n");
        Assert.Contains("is in Box", Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places(synced: Path.Combine(root, "Box"))).Line, StringComparison.Ordinal);
        Ini($"[folders]\nuser = {away}\n");
        var ready = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places());
        Assert.Equal(ReadyState.Ready, ready.State);
        Assert.StartsWith(away, ready.Line, StringComparison.Ordinal);
        // A relative (portable) folder that is not there yet is made on first use when the Vitomir folder can be written (a fresh
        // unzip with user = UserData); when it cannot, refused saying so. A missing absolute one keeps its "moved?" (above).
        Ini("[folders]\nuser = UserData\n");
        string portable = Path.Combine(Top, "UserData");
        Assert.True(UserFolderChoice.Resolve(Place()).Relative);
        var refusedMake = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places(writeRefused: Top));
        Assert.Equal(ReadyState.Failed, refusedMake.State);
        Assert.Equal($"{portable} cannot be created: the Vitomir folder {Top} cannot be written to (it is not allowed to). Choose your user folder again on Folders.", refusedMake.Line);
        Assert.False(Directory.Exists(portable));
        var made = Readiness.UserFolder(UserFolderChoice.Resolve(Place()), Places());
        Assert.Equal(ReadyState.Ready, made.State);
        Assert.True(Directory.Exists(portable));
        Ini($"[folders]\nuser = {away}\n");
        Assert.False(UserFolderChoice.Resolve(Place()).Relative);
        // The variable's folder may be made when first needed.
        var dev = Readiness.UserFolder(UserFolderChoice.Resolve(Place(Path.Combine(root, "not-yet"))), Places());
        Assert.Equal(ReadyState.Ready, dev.State);
        Assert.Contains("made when it is first needed", dev.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_game_files_item_fails_until_imported_whole_and_passes_then()
    {
        var none = Readiness.GameFiles(new ImportedState("x", null, [], []), null);
        Assert.Equal(ReadyState.Failed, none.State);
        Assert.Equal("Not imported yet.", none.Line);
        Assert.Equal("setup", none.Fix);
        var marker = new ImportedMarker { Manifest = "vtmr-1.1-data", ManifestVersion = 1, Store = "GOG", Files = 3133, Bytes = 1_258_199_186 };
        var damaged = Readiness.GameFiles(new ImportedState("x", marker, ["Levels.nob: missing"], []), null);
        Assert.Equal(ReadyState.Failed, damaged.State);
        Assert.Equal("Some imported files are missing or changed: on Setup, select Import again.", damaged.Line);
        var ready = Readiness.GameFiles(new ImportedState("x", marker, [], []), null);
        Assert.Equal(ReadyState.Ready, ready.State);
        Assert.Equal("Imported from your GOG copy.", ready.Line);
        Assert.Equal(ReadyState.Waiting, Readiness.GameFiles(root, false, null, userFolderReady: false).State);
        // A developer's VTMR_GAME_DIR naming an install.
        string install = Path.Combine(root, "install");
        Write(Path.Combine(install, "Resource.nob"), "an archive");
        var dev = Readiness.GameFiles(new ImportedState("x", null, [], []), install);
        Assert.Equal(ReadyState.Ready, dev.State);
        Assert.Contains("VTMR_GAME_DIR's (a developer's", dev.Line, StringComparison.Ordinal);
        // From the folder itself: nothing imported there.
        Assert.Equal(ReadyState.Failed, Readiness.GameFiles(Path.Combine(root, "user"), false, null).State);
    }

    [Fact]
    public void The_modules_item_fails_on_an_error_or_warnings_not_accepted_and_passes_otherwise()
    {
        static PlayerModuleProblem P(ProblemLevel level) => new(level, "a-pack", "x.tga", "what", "do this");
        Assert.Equal("None on.", Readiness.Modules(null).Line);
        Assert.Equal(ReadyState.Waiting, Readiness.Modules(null, checking: true).State);
        Assert.Equal(ReadyState.Waiting, Readiness.Modules(null, ready: false).State);
        Assert.Equal(ReadyState.Failed, Readiness.Modules(null, listProblem: "it does not read").State);
        var errors = Readiness.Modules(new ModuleStart([], [P(ProblemLevel.Error), P(ProblemLevel.Warning)], "c", false, true));
        Assert.Equal(ReadyState.Failed, errors.State);
        Assert.Equal("1 error: see Modules.", errors.Line);
        Assert.Equal("modules", errors.Fix);
        var warnings = Readiness.Modules(new ModuleStart([], [P(ProblemLevel.Warning), P(ProblemLevel.Warning)], "c", false, true));
        Assert.Equal(ReadyState.Failed, warnings.State);
        Assert.Equal("2 warnings to accept or fix.", warnings.Line);
        var accepted = Readiness.Modules(new ModuleStart([], [P(ProblemLevel.Warning)], "c", true, true));
        Assert.Equal(ReadyState.Ready, accepted.State);
        Assert.Contains("its warning accepted", accepted.Line, StringComparison.Ordinal);
        Assert.Equal(ReadyState.Ready, Readiness.Modules(new ModuleStart([], [P(ProblemLevel.Note)], "c", false, false)).State);
    }

    [Fact]
    public void Vitomir_unzipped_inside_the_original_games_folder_is_found()
    {
        string game = Path.Combine(Path.GetTempPath(), "vitomir-inside-" + Guid.NewGuid().ToString("N"), "steamapps", "common", "Vampire");
        try
        {
            Directory.CreateDirectory(Path.Combine(game, "Vitomir", "Modules"));
            File.WriteAllText(Path.Combine(game, "Resource.nob"), "");
            Assert.Equal(new DirectoryInfo(game).FullName, Readiness.InsideGameInstall(Path.Combine(game, "Vitomir")));
            Assert.Equal(new DirectoryInfo(game).FullName, Readiness.InsideGameInstall(game));
            Assert.Null(Readiness.InsideGameInstall(Path.GetDirectoryName(game)));      // beside it is fine
            var item = Readiness.Program(Path.Combine(game, "Vitomir", "Modules"), Path.Combine(game, "Vitomir", "Games"));
            Assert.Equal(ReadyState.Failed, item.State);                                // not whole comes first
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(game)))!, true); } catch (IOException) { } }
    }

    [Fact]
    public void Vitomirs_own_files_and_FFmpeg_items()
    {
        string modules = Path.Combine(Bin, "Modules"), games = Path.Combine(Bin, "Games");
        var bare = Readiness.Program(modules, games, Path.Combine(Bin, "vitomir-platform.exe"));
        Assert.Equal(ReadyState.Failed, bare.State);
        Assert.Contains("vitomir-platform.exe", bare.Line, StringComparison.Ordinal);
        Assert.Contains("the Classic module", bare.Line, StringComparison.Ordinal);
        Assert.Contains("unzip Vitomir's whole zip again", bare.Line, StringComparison.Ordinal);
        // The Classic module's manifest and its game list as the source tree has them, laid out as a package lays them out.
        LayOut();
        var whole = Readiness.Program(modules, games);
        Assert.True(whole.State == ReadyState.Ready, whole.Line);
        var none = Readiness.FFmpeg(null);
        Assert.Equal(ReadyState.Warning, none.State);
        Assert.False(none.Required);
        Assert.False(none.Stops);
        Assert.Contains("the movies will be skipped", none.Line, StringComparison.Ordinal);
        Assert.Equal(ReadyState.Ready, Readiness.FFmpeg(new FFmpegFound("ffmpeg", null, "7")).State);
        var report = new ReadinessReport([Readiness.FFmpeg(null), bare, Readiness.Modules(null)]);
        Assert.False(report.Ready);
        Assert.Same(bare, report.FirstStopping);
        Assert.True(new ReadinessReport([Readiness.FFmpeg(null), Readiness.Modules(null)]).Ready);
    }

    /// <summary>The Classic module's <c>module.json</c> and <c>Games\classic.json</c> from the source tree, laid out in the stand-in <c>bin</c>.</summary>
    void LayOut()
    {
        Write(Path.Combine(Bin, "Modules", "Classic", "module.json"), File.ReadAllText(Path.Combine(PlatformBoundaryTests.SourceRoot("Vitomir.Modules.Classic"), "module.json")));
        Write(Path.Combine(Bin, "Modules", "Classic", "bin", "Vitomir.Modules.Classic.dll"), "a stand-in for the module's assembly");
        Write(Path.Combine(Bin, "Games", "classic.json"), File.ReadAllText(Path.Combine(PlatformBoundaryTests.SourceRoot("Vitomir.Platform.Host"), "Games", "classic.json")));
    }

    [Fact]
    public void The_games_own_check_waits_on_the_user_folder_and_names_the_first_thing_that_stops_it()
    {
        LayOut();
        var first = Readiness.ForGame(UserFolderChoice.Resolve(Place()), Places(), null, Path.Combine(Bin, "Modules"), Path.Combine(Bin, "Games"));
        Assert.False(first.Ready);
        Assert.Equal("userfolder", first.FirstStopping!.Key);
        Assert.Equal(ReadyState.Waiting, first["gamefiles"]!.State);
        Assert.Equal(ReadyState.Waiting, first["modules"]!.State);
        Assert.False(Directory.Exists(Place().Usual));                                       // nothing made by the check
        var set = Readiness.ForGame(UserFolderChoice.Resolve(Place(Path.Combine(root, "user"))), Places(), null, Path.Combine(Bin, "Modules"), Path.Combine(Bin, "Games"));
        Assert.Equal("gamefiles", set.FirstStopping!.Key);
        Assert.Equal(ReadyState.Ready, set["program"]!.State);
    }

    // ---- the first start's choices --------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_starts_choices_make_the_usual_place_take_an_existing_one_or_make_it_portable()
    {
        Directory.CreateDirectory(Bin);
        var usual = UserFolderSetup.UseUsual(Place(), Places(), "the tests");
        Assert.True(usual.Done);
        Assert.True(File.Exists(Path.Combine(Place().Usual, UserFolderMarks.MarkName)));
        Assert.False(File.Exists(Place().IniPath));                                          // no ini for the usual place
        Assert.True(UserFolderChoice.Resolve(Place()).Chosen);

        // An existing one: refused when it does not look like a user folder, taken when it does.
        string other = Path.Combine(root, "Other");
        Write(Path.Combine(other, "holiday.jpg"), "a picture");
        var refused = UserFolderSetup.UseExisting(Place(), other, Places());
        Assert.False(refused.Done);
        Assert.Contains("does not look like a user folder of Vitomir's", refused.Line, StringComparison.Ordinal);
        Assert.False(File.Exists(Place().IniPath));
        string old = Path.Combine(root, "Old user folder");
        Write(Path.Combine(old, "Imported", "vtmr-1.1", "vitomir-imported.json"), "{}");
        var taken = UserFolderSetup.UseExisting(Place(), old, Places());
        Assert.True(taken.Done, taken.Line);
        var now = UserFolderChoice.Resolve(Place());
        Assert.Equal(old, now.Folder);
        Assert.Equal(UserFolderSource.Ini, now.Source);
        Assert.False(UserFolderSetup.UseExisting(Place(), old, Places(writeRefused: Top)).Done);   // the ini cannot be written

        // Portable: UserData inside the Vitomir folder, the ini relative; refused, saying why, when the Vitomir folder cannot be written.
        var cannot = UserFolderSetup.MakePortable(Place(), Places(writeRefused: Top), "the tests");
        Assert.False(cannot.Done);
        Assert.Contains("cannot be written to", cannot.Line, StringComparison.Ordinal);
        Assert.NotNull(UserFolderSetup.IniWhy(Place(), Places(writeRefused: Top)));
        var portable = UserFolderSetup.MakePortable(Place(), Places(), "the tests");
        Assert.True(portable.Done, portable.Line);
        Assert.Equal("UserData", VitomirIni.Read(Place().IniPath).User);
        Assert.Equal(Place().Portable, UserFolderChoice.Resolve(Place()).Folder);
        Assert.True(UserFolderChoice.Resolve(Place()).Portable);

        // A bad ini mended by the usual place: a new one written, the bad one kept as vitomir.ini.previous.
        Ini("; mine\n[folders]\nuser = \"broken\n");
        Assert.NotNull(UserFolderChoice.Resolve(Place()).Problem);
        Assert.True(UserFolderSetup.UseUsual(Place(), Places(), "the tests").Done);
        var mended = UserFolderChoice.Resolve(Place());
        Assert.Null(mended.Problem);
        Assert.Equal(Place().Usual, mended.Folder);
        Assert.Empty(mended.Ini.Problems);
        Assert.StartsWith("; mine", File.ReadAllText(Place().IniPath + ".previous"), StringComparison.Ordinal);   // the bad one kept beside it
        // In Program Files: the Vitomir folder's ini cannot be written there.
        var inPrograms = new UserFolderPlace(null, Path.Combine(root, "Program Files", "Vitomir", "bin"), Local);
        Assert.Contains("Program Files", UserFolderSetup.IniWhy(inPrograms, Places()), StringComparison.Ordinal);
    }

    // ---- the lock and the move ------------------------------------------------------------------------------------------------

    [Fact]
    public void Games_share_the_user_folder_and_a_move_holds_it_alone()
    {
        string folder = Path.Combine(root, "user");
        using (var one = UserFolderLock.HoldShared(folder, out bool m1))
        using (var two = UserFolderLock.HoldShared(folder, out bool m2))
        {
            Assert.NotNull(one);
            Assert.NotNull(two);                                                             // two games side by side
            Assert.False(m1 || m2);
            Assert.Null(UserFolderLock.TakeAlone(folder, out string? why));                  // a move refused while one runs
            Assert.Contains("the game is running", why, StringComparison.Ordinal);
        }
        using (var alone = UserFolderLock.TakeAlone(folder, out _))
        {
            Assert.NotNull(alone);
            Assert.Null(UserFolderLock.HoldShared(folder, out bool moving));                 // a game refused while a move copies
            Assert.True(moving);
        }
    }

    /// <summary>A user folder of the usual place with some of what a player's holds.</summary>
    string MakeUser()
    {
        string user = Place().Usual;
        Write(Path.Combine(user, "settings.json"), "{ \"a\": 1 }");
        Write(Path.Combine(user, "Logs", "launcher.log"), "a launcher's log");
        Write(Path.Combine(user, "Modules", "classic", "Saves", "SP4.vsave"), new string('s', 70_000));
        Write(Path.Combine(user, "Imported", "vtmr-1.1", "Levels.nob"), new string('l', 200_000));
        Directory.CreateDirectory(Path.Combine(user, "Modules", "classic", "Crypt"));          // an empty folder goes too
        Directory.CreateDirectory(Bin);
        return user;
    }

    static Dictionary<string, string> Tree(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is not (UserFolderMarks.MarkName or UserFolderLock.FileName))
            .ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllText);

    [Fact]
    public void A_move_copies_and_checks_every_file_writes_the_ini_and_removes_the_old_folder()
    {
        string user = MakeUser();
        var before = Tree(user);
        string to = Path.Combine(root, "D", "Vitomir-data");
        var plan = UserFolderMove.Plan(UserFolderChoice.Resolve(Place()), to, Places(), new Room(long.MaxValue / 2), out string? refused);
        Assert.Null(refused);
        Assert.Equal(4, plan!.Files);
        var steps = new List<SetupProgress>();
        string? switchedTo = null;
        var report = UserFolderMove.Run(plan, keepOld: false, "the tests", new Progress(steps), TestContext.Current.CancellationToken, switched: f => switchedTo = f);
        Assert.True(report.Done, report.Line);
        Assert.Equal(4, report.Copied);
        Assert.True(report.OldRemoved);
        Assert.False(Directory.Exists(user));
        Assert.Equal(before, Tree(to));
        Assert.True(Directory.Exists(Path.Combine(to, "Modules", "classic", "Crypt")));
        Assert.False(File.Exists(Path.Combine(to, UserFolderMove.ProgressName)));
        Assert.True(File.Exists(Path.Combine(to, UserFolderMarks.MarkName)));
        Assert.Equal(to, switchedTo);
        Assert.Equal(to, UserFolderChoice.Resolve(Place()).Folder);                          // the ini names it now
        Assert.Contains(steps, s => s.Step == SetupProgress.Copy && s.Item.EndsWith("Levels.nob", StringComparison.Ordinal));
        Assert.Equal($"Moved to {to}. The old folder was removed.", report.Line);
        Assert.Equal(4, report.Copied + report.Kept);

        // And on to portable, the old one kept as asked.
        var back = UserFolderMove.Plan(UserFolderChoice.Resolve(Place()), "portable", Places(), new Room(long.MaxValue / 2), out refused);
        Assert.Null(refused);
        Assert.True(back!.Portable);
        var kept = UserFolderMove.Run(back, keepOld: true, "the tests", cancel: TestContext.Current.CancellationToken);
        Assert.True(kept.Done);
        Assert.False(kept.OldRemoved);
        Assert.True(Directory.Exists(to));
        Assert.Equal(Place().Portable, UserFolderChoice.Resolve(Place()).Folder);
        Assert.Equal("UserData", VitomirIni.Read(Place().IniPath).User);
        Assert.Equal(before, Tree(Place().Portable));
    }

    sealed class Progress(List<SetupProgress> steps) : IProgress<SetupProgress>
    {
        public void Report(SetupProgress value) { lock (steps) steps.Add(value); }
    }

    [Fact]
    public void A_cancelled_move_leaves_the_old_folder_in_use_and_a_failed_one_carries_on_where_it_stopped()
    {
        string user = MakeUser();
        var before = Tree(user);
        string to = Path.Combine(root, "D", "Vitomir-data");
        var machine = new Room(long.MaxValue / 2);
        // Cancelled at the second file.
        using var stop = new CancellationTokenSource();
        int seen = 0;
        var plan = UserFolderMove.Plan(UserFolderChoice.Resolve(Place()), to, Places(), machine, out _)!;
        var cancelled = UserFolderMove.Run(plan, false, "the tests", cancel: stop.Token, beforeFile: _ => { if (++seen == 2) stop.Cancel(); });
        Assert.True(cancelled.Cancelled);
        Assert.Equal(user, UserFolderChoice.Resolve(Place()).Folder);                        // still the old one: no ini
        Assert.False(File.Exists(Place().IniPath));
        Assert.Equal(before, Tree(user));
        Assert.Equal("Move cancelled: your user folder is still the old one. Move to the same folder to continue.", cancelled.Line);

        // Failed in the middle (a full disk at the third file): the old one still in use; a move to the same folder then carries on.
        var again = UserFolderMove.Plan(UserFolderChoice.Resolve(Place()), to, Places(), machine, out string? refused)!;
        Assert.Null(refused);
        Assert.True(again.Resuming);
        int at = 0;
        var failed = UserFolderMove.Run(again, false, "the tests", cancel: TestContext.Current.CancellationToken, beforeFile: rel => { if (++at == 3) throw new IOException("There is not enough space on the disk."); });
        Assert.False(failed.Done);
        Assert.Contains("not enough space", failed.Failed, StringComparison.Ordinal);
        Assert.Equal(1, failed.Copied);                                                     // the second one; the first kept from before
        Assert.Equal(1, failed.Kept);
        Assert.Equal(user, UserFolderChoice.Resolve(Place()).Folder);
        var carried = UserFolderMove.Run(UserFolderMove.Plan(UserFolderChoice.Resolve(Place()), to, Places(), machine, out _)!, false, "the tests", cancel: TestContext.Current.CancellationToken);
        Assert.True(carried.Done, carried.Line);
        Assert.Equal(2, carried.Kept);                                                       // the two copied before, checked and kept
        Assert.Equal(2, carried.Copied);
        Assert.Equal(before, Tree(to));
        Assert.Equal(to, UserFolderChoice.Resolve(Place()).Folder);
    }

    [Fact]
    public void A_move_is_refused_where_it_must_not_go()
    {
        string user = MakeUser();
        var choice = UserFolderChoice.Resolve(Place());
        var machine = new Room(long.MaxValue / 2);
        string? Refused(string target, FolderPlaces? places = null, ISetupMachine? m = null, UserFolderChoice? from = null)
        {
            Assert.Null(UserFolderMove.Plan(from ?? choice, target, places ?? Places(), m ?? machine, out string? why));
            return why;
        }
        Assert.Contains("in Program Files", Refused(Path.Combine(root, "Program Files (x86)", "Data")), StringComparison.Ordinal);
        Assert.Contains("Vitomir's own bin folder", Refused(Path.Combine(Bin, "Data")), StringComparison.Ordinal);
        Assert.Contains("the Vitomir folder itself", Refused(Top), StringComparison.Ordinal);
        Assert.Contains("inside your user folder", Refused(Path.Combine(user, "Elsewhere")), StringComparison.Ordinal);
        Assert.Contains("is your user folder already", Refused(user), StringComparison.Ordinal);
        Assert.Contains("your user folder is inside", Refused(Local), StringComparison.Ordinal);
        string full = Path.Combine(root, "Photos");
        Write(Path.Combine(full, "a.jpg"), "a picture");
        Assert.Contains("is not empty and is not a user folder of Vitomir's", Refused(full), StringComparison.Ordinal);
        string another = Path.Combine(root, "Another");
        Write(Path.Combine(another, "settings.json"), "{}");
        Assert.Contains("another user folder of Vitomir's", Refused(another), StringComparison.Ordinal);
        Assert.Contains("not enough room", Refused(Path.Combine(root, "D", "x"), m: new Room(100_000)), StringComparison.Ordinal);
        Assert.Contains("VITOMIR_USERDATA names your user folder", Refused(Path.Combine(root, "D", "x"), from: UserFolderChoice.Resolve(Place(user))), StringComparison.Ordinal);
        Assert.Contains("Vitomir cannot be made portable", Refused("portable", Places(writeRefused: Top)), StringComparison.Ordinal);
        Assert.Contains("is not there", Refused(Path.Combine(root, "E", "x"), Places(driveGone: Path.GetPathRoot(root))), StringComparison.Ordinal);
        using (UserFolderLock.HoldShared(user, out _))
            Assert.Contains("the game is running", Refused(Path.Combine(root, "D", "x")), StringComparison.Ordinal);
        // A synced folder is a warning, not a refusal.
        var synced = UserFolderMove.Plan(choice, Path.Combine(root, "OneDrive", "Vitomir"), Places(), machine, out string? none);
        Assert.Null(none);
        Assert.Contains("a synced folder", Assert.Single(synced!.Warnings), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root, "OneDrive")));                      // nothing written by a plan
    }
}
