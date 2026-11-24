// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Launcher;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host.Tests;

/// <summary>
/// **The launcher and the user folder**, with fakes: the Play page's checklist (each item its mark, its line and the page that fixes it;
/// Play disabled until every item a start needs is ready, its reason the first that is not); a first start's choices (the usual place,
/// an existing one, portable; the last two disabled, saying why, when the Vitomir folder cannot be written), nothing written before
/// one is made and the launcher moving onto it; a bad ini named with its line and mended; the Folders page's Move… (done, the old folder
/// kept or removed, cancelled, refused while the game runs, refused with why); the launcher's log kept in memory until a folder is chosen.
/// </summary>
public sealed class LauncherUserFolderTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("vitomir-launcher-user-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    string Top => Path.Combine(root, "Vitomir");
    string Bin => Path.Combine(Top, "bin");
    UserFolderPlace Place => new(null, Bin, Path.Combine(root, "Local"));

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    sealed class NoMachine : ISetupMachine
    {
        public string? RegistryValue(string key, string name) => null;
        public IReadOnlyList<string> FixedDrives() => [];
        public string? Environment(string name) => null;
        public long FreeBytes(string folder) => long.MaxValue / 2;
    }

    sealed class NoImporter : ISaveImporter
    {
        public (int Code, IReadOnlyList<string> Lines) Import(string slotFolder, string target, string dataFolder, CancellationToken cancel) => (1, ["not here"]);
    }

    sealed class Shell : ILauncherShell
    {
        public string? OpenFolder(string folder) => null;
        public string? OpenFile(string file) => null;
        public string? OpenLink(string url) => null;
        public Task<int> RunVisible(string line) => Task.FromResult(0);
    }

    sealed class Game : IRunningGame
    {
        public TaskCompletionSource<int> End { get; } = new();
        public int Id => 7;
        public Task<int> Ended => End.Task;
        public IReadOnlyList<string> LastLines => [];
        public string LogPath => "game.log";
    }

    /// <summary>A stand-in install of the archives, classes.zip and a few loose files, and a manifest of it.</summary>
    (string Install, DataManifest Manifest) MakeInstall()
    {
        string dir = Path.Combine(root, "install");
        foreach (var a in DataManifest.Archives) Write(Path.Combine(dir, a), "archive " + a + new string('x', 64));
        Write(Path.Combine(dir, "classes.zip"), "classes");
        Write(Path.Combine(dir, "Chronicles", "Prague.nsc"), "chronicle");
        Write(Path.Combine(dir, "Sounds", "Dialog", "line0.mp3"), "a line");
        return (dir, DataManifest.Make(dir, "test-data", 1, "a test's", cancel: TestContext.Current.CancellationToken));
    }

    List<string> Log { get; } = [];
    List<string> LoggedTo { get; } = [];

    (Vitomir.Launcher.Launcher Launcher, List<Game> Games) Make(FolderPlaces? places = null, DataManifest? manifest = null)
    {
        var choice = UserFolderChoice.Resolve(Place);
        manifest ??= MakeInstall().Manifest;
        var service = new SetupService(choice.Folder, manifest, new NoMachine(), new NoImporter(), "the tests");
        var games = new List<Game>();
        var options = new LauncherOptions
        {
            Developer = true, UserData = choice.Folder, Folder = choice, Places = places ?? new FolderPlaces([], [], _ => true, FolderWrite.Test), Machine = new NoMachine(),
            ProgramFolder = Bin, GameProgram = Path.Combine(Bin, "vitomir-platform.exe"), GameDir = () => null,
            ModulesFolder = Path.Combine(AppContext.BaseDirectory, "Modules"), GamesFolder = Path.Combine(AppContext.BaseDirectory, "Games"),
            LogTo = folder => { lock (LoggedTo) LoggedTo.Add(folder); },
        };
        var launcher = new Vitomir.Launcher.Launcher(options, new SetupPage(service, new Shell(), _ => { }), _ => { var g = new Game(); games.Add(g); return g; },
            new Shell(), line => { lock (Log) Log.Add(line); });
        return (launcher, games);
    }

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

    /// <summary>Every control shown disabled says why, and the reason stands on the screen (the style guide's rule).</summary>
    static void ReasonsShown(Vitomir.Launcher.Launcher launcher, string state)
    {
        Drawn(launcher);
        var lines = launcher.Screen.Root.Walk().OfType<TextPiece>().Where(Seen).Select(t => t.Words).ToList();
        foreach (var w in launcher.Screen.Root.Walk().OfType<Worked>().Where(w => Seen(w) && !w.Enabled))
        {
            Assert.False(string.IsNullOrEmpty(w.WhyNot), $"{state}: {w} is disabled with no reason");
            Assert.True(lines.Any(l => l.Contains(w.WhyNot, StringComparison.Ordinal)), $"{state}: {w}'s reason \"{w.WhyNot}\" is not shown");
        }
    }

    static Button Button(Vitomir.Launcher.Launcher launcher, string id) => launcher.Screen.Root.Walk().OfType<Button>().First(b => b.Id == id);

    [Fact]
    public void A_first_start_asks_where_the_user_folder_goes_writes_nothing_before_and_moves_onto_the_one_chosen()
    {
        Directory.CreateDirectory(Bin);
        var (launcher, _) = Make();
        launcher.Start([]);
        Assert.Equal("play", launcher.Page);                                            // the choices are on the Play page
        var ready = launcher.Ready;
        Assert.Equal(["userfolder", "gamefiles", "modules", "program", "ffmpeg"], ready.Items.Select(i => i.Key));
        Assert.Equal("userfolder", ready.FirstStopping!.Key);
        Assert.Equal(ReadyState.Waiting, ready["gamefiles"]!.State);
        Assert.False(launcher.CanPlay);
        launcher.Fill();
        Assert.True(launcher.Data.Flag("askFolder"));
        Assert.Equal("Not ready: your user folder (below).", launcher.Data.Text("playWhy"));
        Drawn(launcher);
        Assert.Equal(launcher.Data.Text("playWhy"), Button(launcher, "play").WhyNot);
        var rows = launcher.Data.Items("ready");
        Assert.Equal(["failed", "waiting", "waiting", "ready"], rows.Take(4).Select(r => r.Text("mark")));      // FFmpeg's, last, as this machine has it
        Assert.Equal("choices", launcher.Ready["userfolder"]!.Fix);
        Assert.False(rows[0].Flag("hasFix"));                                            // its choices stand under the list instead
        foreach (var page in Vitomir.Launcher.Launcher.Pages)
        {
            launcher.Act("page", page);
            ReasonsShown(launcher, "no user folder yet, " + page);
        }
        launcher.Act("page", "setup");
        Drawn(launcher);
        Assert.Equal("Choose your user folder first, on the Play page.", Button(launcher, "check").WhyNot);
        launcher.Act("closeonstart", "on");
        Assert.False(launcher.Settings.CloseOnStart);                                   // nothing written before a folder is chosen
        Assert.False(Directory.Exists(Place.Usual));
        Assert.False(File.Exists(Place.IniPath));

        // Use the usual place: made, and the launcher's pages over it.
        launcher.Act("page", "play");
        launcher.Act("usefolderusual");
        Assert.Equal(Place.Usual, launcher.UserData);
        Assert.Equal(Place.Usual, launcher.Setup.Service.UserData);
        Assert.Equal(Place.Usual, launcher.Modules.Store.UserData);
        Assert.Equal([Place.Usual], LoggedTo);
        Assert.True(File.Exists(Path.Combine(Place.Usual, UserFolderMarks.MarkName)));
        Assert.Equal(ReadyState.Ready, launcher.Ready["userfolder"]!.State);
        Assert.Equal(ReadyState.Failed, launcher.Ready["gamefiles"]!.State);            // next: the game's files
        launcher.Fill();
        Assert.False(launcher.Data.Flag("askFolder"));
        Assert.Equal("Not ready: the original game's files (below).", launcher.Data.Text("playWhy"));
        Assert.Contains(launcher.Data.Items("ready"), r => r.Text("key") == "gamefiles" && r.Flag("hasFix") && r.Text("fixLabel") == "Go to Setup" && r.Text("fixPage") == "setup");
    }

    [Fact]
    public void Portable_and_an_existing_one_need_the_Vitomir_folder_writable_and_say_why_when_it_is_not()
    {
        Directory.CreateDirectory(Bin);
        var refusing = new FolderPlaces([], [], _ => true, f => UserFolderMarks.Under(f, Top) ? "it is not allowed to" : null);
        var (locked, _) = Make(refusing);
        locked.Start([]);
        Drawn(locked);
        Assert.False(Button(locked, "makePortable").Enabled);
        Assert.False(Button(locked, "useExisting").Enabled);
        Assert.True(Button(locked, "useUsual").Enabled);
        Assert.Contains("cannot be written to (it is not allowed to)", Button(locked, "makePortable").WhyNot, StringComparison.Ordinal);
        ReasonsShown(locked, "the Vitomir folder not writable");
        locked.Act("makeportable");
        Assert.False(File.Exists(Place.IniPath));

        var (launcher, _) = Make();
        launcher.Start([]);
        // An existing one: refused when it is not one of Vitomir's, said; taken when it is.
        string photos = Path.Combine(root, "Photos");
        Write(Path.Combine(photos, "a.jpg"), "a picture");
        launcher.Act("usefolderexisting", photos);
        Assert.Equal(Place.Usual, launcher.UserData);
        launcher.Fill();
        Assert.Contains(launcher.Data.Items("folderResult"), l => l.Flag("warn") && l.Text("line").Contains("does not look like a user folder", StringComparison.Ordinal));
        launcher.Act("makeportable");
        Assert.Equal(Place.Portable, launcher.UserData);
        Assert.Equal("UserData", VitomirIni.Read(Place.IniPath).User);
        Assert.True(launcher.UserFolder.Choice.Portable);
        launcher.Act("page", "folders");
        launcher.Fill();
        Assert.Equal(Place.Portable, launcher.Data.Text("ufPath"));
        Assert.Equal("Portable: inside the Vitomir folder.", launcher.Data.Text("ufHow"));
        Assert.Empty(launcher.Data.Items("ufIni"));                                 // vitomir.ini behind Details while all is well (D1)
        launcher.Act("ufdetails");
        launcher.Fill();
        Assert.Contains(launcher.Data.Items("ufIni"), l => l.Text("line").Trim() == "user = UserData");
    }

    [Fact]
    public void A_bad_ini_is_named_with_its_line_and_mended_from_the_Folders_page()
    {
        Directory.CreateDirectory(Bin);
        Write(Place.IniPath, "[folders]\nwhere = D:\\Vitomir-data\n");
        var (launcher, _) = Make();
        launcher.Start([]);
        var item = launcher.Ready["userfolder"]!;
        Assert.Equal(ReadyState.Failed, item.State);
        Assert.Equal("folders", item.Fix);
        Assert.Contains("vitomir.ini line 2 is not understood", item.Line, StringComparison.Ordinal);
        launcher.Act("page", "folders");
        launcher.Fill();
        Assert.Contains("vitomir.ini line 2 is not understood", launcher.Data.Text("ufProblem"), StringComparison.Ordinal);
        Assert.True(launcher.Data.Flag("ufChoicesHere"));
        Assert.False(launcher.Data.Flag("canMove"));
        ReasonsShown(launcher, "a bad ini, folders");
        launcher.Act("page", "play");
        Assert.Contains(launcher.Data.Items("ready"), r => r.Text("key") == "userfolder" && r.Text("fixLabel") == "Go to Folders");
        launcher.Act("usefolderusual");
        Assert.Null(launcher.UserFolder.Choice.Problem);
        Assert.Equal(ReadyState.Ready, launcher.Ready["userfolder"]!.State);
        Assert.True(File.Exists(Place.IniPath + ".previous"));
    }

    [Fact]
    public void Move_copies_the_user_folder_and_the_launcher_moves_onto_it_and_is_refused_while_the_game_runs()
    {
        Directory.CreateDirectory(Bin);
        var (install, manifest) = MakeInstall();
        var (launcher, games) = Make(manifest: manifest);
        launcher.Start([]);
        launcher.Act("usefolderusual");
        // Imported, then Play can: every item ready but FFmpeg's note.
        launcher.Setup.UseFolder(install);
        launcher.Setup.StartImport();
        launcher.Setup.Wait();
        launcher.Poll();
        Assert.True(launcher.CanPlay, string.Join("; ", launcher.Ready.Items.Select(i => i.Line)));
        Assert.True(launcher.Ready.Ready);
        launcher.Fill();
        Assert.Equal("Ready to play", launcher.Data.Text("readyTitle"));
        // All well folds to one line with Details (D2); an item that warns (FFmpeg not found, on a machine without it) opens it by itself.
        bool allWell = launcher.Ready.Items.All(i => i.State == ReadyState.Ready);
        Assert.Equal(allWell, launcher.Data.Flag("readyFolds"));
        Assert.Equal(!allWell, launcher.Data.Flag("readyOpen"));
        if (allWell)
        {
            Assert.Equal("Details", launcher.Data.Text("readyDetailsWord"));
            launcher.Act("readydetails");
            launcher.Fill();
            Assert.True(launcher.Data.Flag("readyOpen"));
            Assert.Equal("Hide details", launcher.Data.Text("readyDetailsWord"));
            launcher.Act("readydetails");
            launcher.Fill();
            Assert.False(launcher.Data.Flag("readyOpen"));
        }
        Write(Path.Combine(Place.Usual, "Modules", "classic", "Saves", "SP4.vsave"), "a save");

        // Refused while the game runs.
        launcher.Act("play");
        Assert.Single(games);
        launcher.Act("page", "folders");
        launcher.Fill();
        Assert.False(launcher.Data.Flag("canMove"));
        Assert.Equal("Unavailable while the game is running: close it first.", launcher.Data.Text("ufMoveWhy"));
        games[0].End.SetResult(0);
        launcher.Poll();

        // Move… to a folder that is not empty and not Vitomir's: refused in a notice.
        string photos = Path.Combine(root, "Photos");
        Write(Path.Combine(photos, "a.jpg"), "a picture");
        launcher.Act("moveuserfolder");
        Assert.Equal("move", launcher.Notice!.Kind);
        Assert.Equal(["Choose a folder\u2026", "Make portable", "Cancel"], launcher.Notice.Buttons.Select(b => b.Label));
        launcher.Act("notice", "c");
        launcher.Act("moveto", photos);
        Assert.Contains("is not empty and is not a user folder of Vitomir's", launcher.Notice!.Lines[0].Text, StringComparison.Ordinal);
        launcher.Act("notice", "a");

        // Moved, the old folder kept as asked.
        string to = Path.Combine(root, "D", "Vitomir data");
        launcher.Act("keepold", "on");
        launcher.Act("moveto", to);
        Assert.True(launcher.UserFolder.Moving);
        launcher.UserFolder.Wait();
        launcher.Poll();
        Assert.False(launcher.UserFolder.Moving);
        Assert.Equal(to, launcher.UserData);
        Assert.Equal(to, launcher.Setup.Service.UserData);
        Assert.Equal(to, VitomirIni.Read(Place.IniPath).User);
        Assert.True(Directory.Exists(Place.Usual));
        Assert.Equal("a save", File.ReadAllText(Path.Combine(to, "Modules", "classic", "Saves", "SP4.vsave")));
        Assert.True(launcher.CanPlay);                                                  // the imported files came along
        Assert.Contains(to, LoggedTo);
        launcher.Fill();
        Assert.Contains(launcher.Data.Items("moveResult"), l => l.Text("line").StartsWith($"Moved to {to}.", StringComparison.Ordinal) && l.Text("line").Contains("The old folder was kept.", StringComparison.Ordinal));

        // Cancelled at once: the folder in use stays; then portable, the old one removed.
        launcher.Act("keepold", "off");
        launcher.Act("moveto", "portable");
        launcher.Act("cancelmove");
        launcher.UserFolder.Wait();
        launcher.Poll();
        if (launcher.UserData == to)
        {
            // Cancelled before it was done (it nearly always is: Cancel came at once): said, and the folder in use is the same.
            Assert.StartsWith("Move cancelled", Assert.Single(launcher.UserFolder.Result).Text, StringComparison.Ordinal);
            Assert.Equal(to, VitomirIni.Read(Place.IniPath).User);
            launcher.Act("moveto", "portable");
            launcher.UserFolder.Wait();
            launcher.Poll();
        }
        Assert.Equal(Place.Portable, launcher.UserData);
        Assert.False(Directory.Exists(to));
        Assert.True(launcher.CanPlay);
    }

    [Fact]
    public void The_launchers_log_waits_in_memory_until_a_user_folder_is_chosen_and_follows_a_move()
    {
        using var log = new LauncherLog(null, echo: false);
        log.Say("launcher: a first start");
        Assert.Null(log.Path);
        string first = Path.Combine(root, "first", "Logs");
        log.MoveTo(first);
        log.Say("launcher: the user folder chosen");
        string second = Path.Combine(root, "second", "Logs");
        log.MoveTo(second);
        log.Say("launcher: moved");
        log.Dispose();
        var text = File.ReadAllText(Path.Combine(second, "launcher.log"));
        Assert.Contains("a first start", text, StringComparison.Ordinal);
        Assert.Contains("the user folder chosen", text, StringComparison.Ordinal);
        Assert.Contains("moved", text, StringComparison.Ordinal);
        Assert.DoesNotContain("moved", File.ReadAllText(Path.Combine(first, "launcher.log")), StringComparison.Ordinal);
    }
}
