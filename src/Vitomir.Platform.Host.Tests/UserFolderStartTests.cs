// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The game started on its own, and the user folder from the command line**: a start with no command line before Vitomir is set up
/// shows the box (behind <see cref="IStartDialog"/>, so its words are seen here) naming what is not ready, Open the launcher starting it,
/// exit code 3, nothing written in a user folder not chosen; ready, it goes on with no box; the launcher found at the top of the Vitomir
/// folder or beside the program; a run with a command line refused, with no box, when its user folder cannot be used; and
/// <c>--setup move-user-folder</c> moving it, refusing with why, keeping the old folder with <c>--keep-old</c>.
/// </summary>
public sealed class UserFolderStartTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-user-start-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    sealed class FakeDialog(bool open) : IStartDialog
    {
        public List<string> Shown { get; } = [];
        public bool AskOpenLauncher(string title, string text, bool launcherFound)
        {
            Shown.Add($"{title}|{launcherFound}|{text}");
            return open;
        }
    }

    static FolderPlaces Places(string? writeRefused = null) =>
        new([], [], _ => true, folder => writeRefused is not null && UserFolderMarks.Under(folder, writeRefused) ? "it is not allowed to" : null);

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void A_start_with_no_command_line_before_setup_shows_the_box_naming_what_is_not_ready()
    {
        var place = new UserFolderPlace(null, Path.Combine(root, "Vitomir", "bin"), Path.Combine(root, "Local"));
        var report = Readiness.ForGame(UserFolderChoice.Resolve(place), Places(), null, Modules.ModuleLoader.ProgramModules, Modules.ModuleLoader.ProgramGames);
        var dialog = new FakeDialog(open: true);
        var opened = new List<string>();
        var said = new List<string>();
        string launcher = Path.Combine(root, "Vitomir", "vitomir-launcher.exe");
        int? code = BareStart.Run(report, dialog, launcher, l => { opened.Add(l); return null; }, said.Add);
        Assert.Equal(SetupCommand.Exit.NotTheData, code);
        var shown = Assert.Single(dialog.Shown);
        Assert.StartsWith("Vitomir|True|Vitomir isn't set up yet: open the launcher to set it up.", shown, StringComparison.Ordinal);
        Assert.Contains("Your user folder: Not chosen yet.", shown, StringComparison.Ordinal);
        Assert.Equal([launcher], opened);
        Assert.Contains(said, l => l.StartsWith("start: Your user folder: NOT READY", StringComparison.Ordinal));
        Assert.Contains(said, l => l == $"start: the launcher started ({launcher})");
        Assert.False(Directory.Exists(place.Usual));                                        // nothing written in a folder not chosen

        // Close: nothing started. No launcher beside it: the box says so and offers Close alone.
        var closed = new FakeDialog(open: false);
        Assert.Equal(3, BareStart.Run(report, closed, launcher, l => { opened.Add(l); return null; }, _ => { }));
        Assert.Single(opened);
        var alone = new FakeDialog(open: true);
        Assert.Equal(3, BareStart.Run(report, alone, null, l => throw new InvalidOperationException("not asked"), _ => { }));
        Assert.Contains("|False|", alone.Shown[0], StringComparison.Ordinal);
        Assert.Contains("vitomir-launcher.exe is not beside this program", alone.Shown[0], StringComparison.Ordinal);

        // The user folder set, the game's files not imported: that is named.
        var set = Readiness.ForGame(UserFolderChoice.Resolve(place with { Variable = Path.Combine(root, "user") }), Places(), null, Modules.ModuleLoader.ProgramModules, Modules.ModuleLoader.ProgramGames);
        var named = new FakeDialog(open: false);
        BareStart.Run(set, named, launcher, _ => null, _ => { });
        Assert.Contains("The original game's files: Not imported yet.", named.Shown[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Your user folder", named.Shown[0], StringComparison.Ordinal);

        // Ready: no box, the game goes on.
        var ready = new ReadinessReport([Readiness.Modules(null), Readiness.FFmpeg(null)]);
        var none = new FakeDialog(open: true);
        Assert.Null(BareStart.Run(ready, none, launcher, _ => null, _ => { }));
        Assert.Empty(none.Shown);
    }

    [Fact]
    public void The_launcher_is_found_at_the_top_of_the_Vitomir_folder_or_beside_the_program()
    {
        string name = OperatingSystem.IsWindows() ? "vitomir-launcher.exe" : "vitomir-launcher";
        string bin = Path.Combine(root, "Vitomir", "bin");
        Directory.CreateDirectory(bin);
        Assert.Null(BareStart.LauncherPath(bin));
        Write(Path.Combine(bin, name), "beside");
        Assert.Equal(Path.Combine(bin, name), BareStart.LauncherPath(bin));
        Write(Path.Combine(root, "Vitomir", name), "at the top");
        Assert.Equal(Path.Combine(root, "Vitomir", name), BareStart.LauncherPath(bin));     // the package's, first
    }

    /// <summary>
    /// A fresh unzip whose vitomir.ini says <c>user = UserData</c> (portable), its folder not made yet: the
    /// bare start makes it (the Vitomir folder can be written) and names what is really not ready, the game's files, not a "moved" folder.
    /// When the Vitomir folder cannot be written, it says that; an absolute folder that is missing keeps its "moved?".
    /// </summary>
    [Fact]
    public void A_portable_user_folder_not_made_yet_is_made_on_first_use_and_the_box_names_the_game_files()
    {
        var place = new UserFolderPlace(null, Path.Combine(root, "Vitomir", "bin"), Path.Combine(root, "Local"));
        Write(place.IniPath, "[folders]\nuser = UserData\n");
        string portable = Path.Combine(root, "Vitomir", "UserData");
        var refused = Readiness.ForGame(UserFolderChoice.Resolve(place), Places(writeRefused: Path.Combine(root, "Vitomir")), null, Modules.ModuleLoader.ProgramModules, Modules.ModuleLoader.ProgramGames);
        var box = new FakeDialog(open: false);
        BareStart.Run(refused, box, null, _ => null, _ => { });
        Assert.Contains($"Your user folder: {portable} cannot be created: the Vitomir folder", box.Shown[0], StringComparison.Ordinal);
        Assert.False(Directory.Exists(portable));

        var report = Readiness.ForGame(UserFolderChoice.Resolve(place), Places(), null, Modules.ModuleLoader.ProgramModules, Modules.ModuleLoader.ProgramGames);
        Assert.True(Directory.Exists(portable));
        var shown = new FakeDialog(open: false);
        Assert.Equal(3, BareStart.Run(report, shown, null, _ => null, _ => { }));
        Assert.DoesNotContain("Your user folder", shown.Shown[0], StringComparison.Ordinal);
        Assert.DoesNotContain("moved", shown.Shown[0], StringComparison.Ordinal);
        Assert.Contains("The original game's files: Not imported yet.", shown.Shown[0], StringComparison.Ordinal);
        Assert.Null(BareStart.CommandLineRefusal(UserFolderChoice.Resolve(place), Places()));

        // An absolute folder that is missing: moved, or its drive unplugged; not made.
        string gone = Path.Combine(root, "gone");
        Write(place.IniPath, $"[folders]\nuser = {gone}\n");
        Assert.Contains("was it moved, renamed or deleted?", BareStart.CommandLineRefusal(UserFolderChoice.Resolve(place), Places()), StringComparison.Ordinal);
        Assert.False(Directory.Exists(gone));
    }

    /// <summary>
    /// The box's words as Windows' TaskDialog shows them (SDL's box cut "Open the launcher" to "pen the launche" on Windows): the
    /// main line its first line, the detail the items; the dialog for this machine (the TaskDialog itself is seen on Windows only).
    /// </summary>
    [Fact]
    public void The_box_splits_into_the_task_dialog_s_main_line_and_detail()
    {
        string text = BareStart.Lead + "\n\nThe original game's files: Not imported yet.\nThe modules on: After the original game's files.";
        var (main, detail) = BareStart.Split(text);
        Assert.Equal("Vitomir isn't set up yet: open the launcher to set it up.", main);
        Assert.Equal("The original game's files: Not imported yet.\nThe modules on: After the original game's files.", detail);
        Assert.Equal((BareStart.Lead, ""), BareStart.Split(BareStart.Lead));
        Assert.Equal("Open the launcher", BareStart.OpenWord);
        Assert.Equal("Close", BareStart.CloseWord);
        Assert.Contains("Microsoft.Windows.Common-Controls\" version=\"6.0.0.0\"", WindowsStartDialog.Manifest, StringComparison.Ordinal);
        Assert.IsType(OperatingSystem.IsWindows() ? typeof(WindowsStartDialog) : typeof(SdlStartDialog), WindowsStartDialog.ForThisMachine);
    }

    [Fact]
    public void A_run_with_a_command_line_is_refused_when_its_user_folder_cannot_be_used_but_not_on_a_first_start()
    {
        var place = new UserFolderPlace(null, Path.Combine(root, "Vitomir", "bin"), Path.Combine(root, "Local"));
        Assert.Null(BareStart.CommandLineRefusal(UserFolderChoice.Resolve(place), Places()));   // no user folder yet: the usual place, as before
        Write(place.IniPath, "[folders]\nuser = \"broken\n");
        Assert.Contains("vitomir.ini line 2 is not understood", BareStart.CommandLineRefusal(UserFolderChoice.Resolve(place), Places()), StringComparison.Ordinal);
        Write(place.IniPath, $"[folders]\nuser = {Path.Combine(root, "gone")}\n");
        Assert.Contains("is not there", BareStart.CommandLineRefusal(UserFolderChoice.Resolve(place), Places()), StringComparison.Ordinal);
        Directory.CreateDirectory(Path.Combine(root, "gone"));
        Assert.Null(BareStart.CommandLineRefusal(UserFolderChoice.Resolve(place), Places()));
    }

    /// <summary>The program, a process with no command line at all, <c>VITOMIR_USERDATA</c> the test's own and no <c>VTMR_GAME_DIR</c>.</summary>
    [Fact]
    public async Task The_program_with_no_command_line_and_nothing_imported_says_so_and_ends_with_exit_code_3()
    {
        string user = Path.Combine(root, "user");
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform"))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = root };
        start.Environment["VITOMIR_USERDATA"] = user;
        start.Environment.Remove("VTMR_GAME_DIR");
        start.Environment["SDL_AUDIODRIVER"] = "dummy";
        using var p = Process.Start(start)!;
        var err = p.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        string output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(120_000)) { p.Kill(entireProcessTree: true); Assert.Fail("the program did not end"); }
        Assert.Equal(3, p.ExitCode);
        Assert.Contains(BareStart.Lead, output, StringComparison.Ordinal);                  // a console's: the box's words printed
        Assert.Contains("The original game's files: Not imported yet.", output, StringComparison.Ordinal);
        Assert.Contains("start: The original game's files: NOT READY", File.ReadAllText(Directory.GetFiles(Path.Combine(user, "Logs"), "platform-*.log").Single()), StringComparison.Ordinal);
        Assert.Equal("", (await err).Trim());
    }

    [Fact]
    public void The_setup_moves_the_user_folder_refuses_with_why_and_keeps_the_old_one_when_asked()
    {
        string bin = Path.Combine(root, "Vitomir", "bin");
        Directory.CreateDirectory(bin);
        var place = new UserFolderPlace(null, bin, Path.Combine(root, "Local"));
        Write(Path.Combine(place.Usual, "settings.json"), "{ \"mine\": true }");
        Write(Path.Combine(place.Usual, "Modules", "classic", "Saves", "SP4.vsave"), new string('s', 5000));
        var lines = new List<string>();
        int Move(params string[] words) => SetupCommand.Run(new Cli(["--setup", "move-user-folder", .. words]), lines.Add, where: place, places: Places(),
            cancel: TestContext.Current.CancellationToken);

        Assert.Equal(SetupCommand.Exit.Usage, Move());
        Assert.Equal(SetupCommand.Exit.NotTheData, Move(Path.Combine(place.Usual, "inside")));
        Assert.Contains(lines, l => l.Contains("not moved:", StringComparison.Ordinal) && l.Contains("inside your user folder", StringComparison.Ordinal));
        lines.Clear();
        string to = Path.Combine(root, "D", "Vitomir data");
        Assert.Equal(SetupCommand.Exit.Done, Move(to, "--keep-old"));
        Assert.Contains(lines, l => l.StartsWith($"setup: move-user-folder: Moved to {to}.", StringComparison.Ordinal));     // the settings and a save (the in-use lock never copied)
        Assert.Contains(lines, l => l.Contains("The old folder was kept.", StringComparison.Ordinal));
        Assert.True(Directory.Exists(place.Usual));
        Assert.Equal(to, UserFolderChoice.Resolve(place).Folder);
        Assert.Equal("{ \"mine\": true }", File.ReadAllText(Path.Combine(to, "settings.json")));
        Assert.All(lines, l => Assert.True(l.All(c => c < 128), l));                           // the setup's plain ASCII
        lines.Clear();
        Assert.Equal(SetupCommand.Exit.Done, Move("portable"));
        Assert.Equal(place.Portable, UserFolderChoice.Resolve(place).Folder);
        Assert.False(Directory.Exists(to));                                                    // removed this time
        Assert.Contains(lines, l => l.Contains("The old folder was removed.", StringComparison.Ordinal));
    }
}
