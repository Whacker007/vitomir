// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The launcher's working half: its pages' data and its actions, knowing no SDL (LauncherWindow.cs draws it).
using System.Globalization;
using System.Runtime.InteropServices;
using Vitomir.Platform.Host;
using Vitomir.Platform.Interface;
using Vitomir.Platform.Modules;
using Vitomir.Platform.PlayerModules;
using Vitomir.Platform.Setup;

namespace Vitomir.Launcher;

/// <summary>How the launcher was started: a developer's switches, the game's extra words, its folders.</summary>
public sealed record LauncherOptions
{
    /// <summary><c>--developer</c>: the game started with it too, and a developer's <c>--manifest</c> and <c>--location</c> taken.</summary>
    public bool Developer { get; init; }
    /// <summary>The words after <c>--</c> on its command line, given to the game after <c>--game classic</c> (a developer's: a still's switches).</summary>
    public IReadOnlyList<string> GameArgs { get; init; } = [];
    /// <summary>The platform's user data (<c>%LOCALAPPDATA%\Vitomir</c>, or <c>VITOMIR_USERDATA</c>).</summary>
    public string UserData { get; init; } = Platform.Host.UserData.Folder;
    /// <summary>The folder the launcher runs from (checked at its start; the game's program is looked for beside it).</summary>
    public string ProgramFolder { get; init; } = AppContext.BaseDirectory;
    /// <summary>The game's program (<c>vitomir-platform.exe</c> beside the launcher).</summary>
    public string GameProgram { get; init; } = SetupCommand.ProgramPath();
    /// <summary>A developer's install named by <c>VTMR_GAME_DIR</c> (the start's rule: played from when nothing is imported), or null.</summary>
    public Func<string?> GameDir { get; init; } = () => Environment.GetEnvironmentVariable("VTMR_GAME_DIR");
    /// <summary>The folder the modules are found in (the program's <c>Modules\</c>).</summary>
    public string ModulesFolder { get; init; } = ModuleLoader.ProgramModules;
    /// <summary>The folder of the games' lists (the program's <c>Games\</c>).</summary>
    public string GamesFolder { get; init; } = ModuleLoader.ProgramGames;
    /// <summary>
    /// The user folder's choice (<see cref="UserFolderChoice"/>: <c>VITOMIR_USERDATA</c>, <c>vitomir.ini</c>, the usual place), or null: then
    /// <see cref="UserData"/> as given (a test's), taken as <c>VITOMIR_USERDATA</c>'s would be.
    /// </summary>
    public UserFolderChoice? Folder { get; init; }
    /// <summary>The machine's places the user folder is checked against (Program Files, synced folders, the drives, the write test).</summary>
    public FolderPlaces Places { get; init; } = FolderPlaces.ThisMachine();
    /// <summary>The machine (the free room on a drive, for a move).</summary>
    public ISetupMachine Machine { get; init; } = ThisMachine.Instance;
    /// <summary>The Setup page for another user folder (the launcher's own makes its save importer for that folder), or null: the same page's, its importer kept.</summary>
    public Func<string, SetupPage>? NewSetup { get; init; }

    /// <summary>The Films page over a user folder (a test's, with stand-in tools; else the machine's).</summary>
    public Func<string, FilmsPage>? NewFilms { get; init; }
    /// <summary>The Desktop and Start menu shortcuts' hand (Windows': the shell's ShellLink), or null: no Shortcuts section (a test gives a fake).</summary>
    public IShortcutWriter? ShortcutWriter { get; init; } = Shortcuts.ThisMachine();
    /// <summary>Told the new user folder once it is the one in use (the launcher's log goes there), or null.</summary>
    public Action<string>? LogTo { get; init; }
}

/// <summary>
/// A notice over the pages: a title, its lines, up to three buttons. <see cref="Default"/> is the button Enter works (it has the focus
/// when the notice comes up), <see cref="Cancel"/> the one Escape works (the last when not given).
/// </summary>
public sealed record Notice(string Kind, string Title, IReadOnlyList<PageLine> Lines, IReadOnlyList<(string Label, Action Do)> Buttons, int Default = 0, int? Cancel = null)
{
    /// <summary>The button Escape works.</summary>
    public int CancelAt => Cancel ?? Buttons.Count - 1;
}

/// <summary>A module the launcher found, as its Modules page says it.</summary>
public sealed record ModuleRow(string Id, string Name, string Version, string What, string Status, bool Warn);

/// <summary>What the window's picker is asked for: a folder or a file, for whom, and where it starts.</summary>
public enum PickFor { SetupFolder, ModuleFolder, ModuleFile, UserFolderExisting, MoveTarget }

/// <summary>
/// **The launcher**, its working half: the pages (<see cref="Pages"/>: Play, Setup, Modules, Folders,
/// About) as data of the launcher's screen (<c>Screens/launcher.json</c>, <see cref="Fill"/>), the screen's actions answered
/// (<see cref="Handle"/>), the running steps' ends and the game's taken (<see cref="Poll"/>). It knows nothing of SDL: the window
/// (<see cref="LauncherWindow"/>) or a still's steps (<see cref="LauncherSteps"/>) drive it, and the tests drive it with fakes.
/// </summary>
public sealed partial class Launcher
{
    /// <summary>The pages, in the order the left side lists them.</summary>
    public static readonly string[] Pages = ["play", "setup", "modules", "films", "folders", "about"];

    /// <summary>The one module a player plays in release 1.</summary>
    public const string Game = "classic";

    LauncherOptions options;
    readonly Func<GameStart, IRunningGame> startGame;
    readonly ILauncherShell shell;
    readonly Action<string> say;
    readonly Dictionary<string, string> listed = new(StringComparer.Ordinal);
    IReadOnlyList<LocationProblem> location = [];
    string? editWas;
    bool canPlay;
    /// <summary>The Play page's checklist opened by **Details** while all is ready (D2: all well is one line).</summary>
    bool readyDetails;
    /// <summary>The details of Setup's result (a check's, an import's, a verification's) shown (D1).</summary>
    bool setupDetails;
    /// <summary>The details of the Modules page's result (an add's refused files) shown (D1).</summary>
    bool moduleDetails;
    /// <summary>The Folders page's vitomir.ini shown (D1: shown by itself when it has a problem).</summary>
    bool ufDetails;
    int ends;
    string playFrom = "", playWhy = "", playResult = "", openWhy = "", aboutResult = "";
    string? clipboard;
    ReadyItem folderItem = Readiness.Modules(null), filesItem = Readiness.Modules(null), programItem = Readiness.Modules(null);

    public Launcher(LauncherOptions options, SetupPage setup, Func<GameStart, IRunningGame> startGame, ILauncherShell shell, Action<string> say)
    {
        this.options = options;
        this.startGame = startGame;
        this.shell = shell;
        this.say = say;
        Setup = setup;
        TextBox.Register();
        ReadyMark.Register();
        // The launcher's words in its kit's faces and colours (Vitomir Text, Wedge, Knife), at the theme's sizes.
        var loader = new ScreenLoader(say) { Styles = CourtKit.Court.Styles };
        using var s = typeof(Launcher).Assembly.GetManifestResourceStream("Launcher/launcher.json")
                      ?? throw new InvalidOperationException("the launcher's screen (Launcher/launcher.json) is not in vitomir-launcher");
        using var reader = new StreamReader(s);
        Screen = loader.Parse(reader.ReadToEnd(), "launcher", Data);
        Screen.Skin = CourtLook.Court;
        Screen.DropLineColour = CourtKit.Court.Colour("mark_selected");
        Problems = loader.Problems;
        ProgramModules = FindModules(options.ModulesFolder);
        Modules = new ModulesPage(new PlayerModuleStore(options.UserData, Game), BaseFolder, say);
        Films = NewFilms(options.UserData);
        Settings = LauncherSettings.Read(options.UserData);
        UserFolder = new UserFolderPage(options.Folder ?? UserFolderChoice.Resolve(new UserFolderPlace(options.UserData, options.ProgramFolder, "")),
                                        options.Places, options.Machine, PlatformVersion.Text, say);
    }

    /// <summary>The user folder: where it is, the first start's choices, the move.</summary>
    public UserFolderPage UserFolder { get; }

    // ---- the Setup page's shortcuts ----------------------------------------------------------------------------------------

    /// <summary>The Desktop and Start menu shortcuts of this Vitomir folder, or null where there are none to make (not Windows).</summary>
    public Shortcuts? Shortcuts => shortcuts ??= options.ShortcutWriter is { } w ? new Shortcuts(w, TopFolder) : null;
    Shortcuts? shortcuts;

    /// <summary>Each place's shortcut as last found on disk (at the start, and after each switch or Update).</summary>
    readonly Dictionary<ShortcutPlace, ShortcutStatus> shortcutFound = [];

    /// <summary>What the last switch or Update did.</summary>
    string shortcutLine = "";

    /// <summary>The shortcuts looked at on disk again.</summary>
    void LookAtShortcuts()
    {
        if (Shortcuts is not { } s) return;
        foreach (var place in new[] { ShortcutPlace.Desktop, ShortcutPlace.StartMenu })
        {
            shortcutFound[place] = s.Status(place);
            say($"launcher: shortcut {Shortcuts.Word(place)}: {shortcutFound[place].State.ToString().ToLowerInvariant()}{(shortcutFound[place].PointsAt is { } t ? $" ({t})" : "")}");
        }
    }

    /// <summary>A switch turned (its flag written: on or off) or a row's Update: done at once, and what is on disk read back.</summary>
    void Shortcut(string word, bool? on)
    {
        if (Shortcuts is not { } s || Shortcuts.Parse(word) is not { } place) return;
        var (status, line) = on is { } want ? s.Set(place, want) : s.Update(place);
        shortcutFound[place] = status;
        shortcutLine = line;
        say($"launcher: shortcut {Shortcuts.Word(place)}: {line}");
    }

    void FillShortcuts()
    {
        var d = Data;
        d.Set("hasShortcuts", Shortcuts is not null);
        foreach (var place in new[] { ShortcutPlace.Desktop, ShortcutPlace.StartMenu })
        {
            string key = place == ShortcutPlace.Desktop ? "shortcutDesktop" : "shortcutStart";
            var found = shortcutFound.GetValueOrDefault(place) ?? new ShortcutStatus(place, ShortcutState.NoFolder, null, null);
            d.Set(key, found.State is ShortcutState.On or ShortcutState.Moved);
            d.Set(key + "Can", found.State is not (ShortcutState.NoFolder or ShortcutState.NotOurs));
            d.Set(key + "Why", found.State is ShortcutState.NoFolder or ShortcutState.NotOurs ? Shortcuts.Note(found) : "");
            d.Set(key + "Note", Shortcuts.Note(found));
            d.Set(key + "Moved", found.State == ShortcutState.Moved);
        }
        d.Set("shortcutLine", shortcutLine);
    }

    /// <summary>The user folder the launcher uses now.</summary>
    public string UserData => options.UserData;

    /// <summary>
    /// **The launcher moved onto another user folder** (a first start's choice made, a move done): the Setup page, the Modules page and the
    /// launcher's settings made again over it, its log moved there (<see cref="LauncherOptions.LogTo"/>), and everything looked at again.
    /// </summary>
    void Rebind(string folder)
    {
        say($"launcher: the user folder is now {folder}");
        options = options with { UserData = folder };
        Setup = options.NewSetup is { } make ? make(folder) : Setup.For(Setup.Service.With(folder));
        Modules = new ModulesPage(new PlayerModuleStore(folder, Game), BaseFolder, say);
        Films.Shutdown();
        Films = NewFilms(folder);
        skipOpening = null;
        Settings = LauncherSettings.Read(folder);
        options.LogTo?.Invoke(folder);
        ends = 0;
        Setup.Refresh();
        Modules.Refresh();
        Recheck();
    }

    /// <summary>The launcher's own settings (the user folder's <c>Launcher\settings.json</c>).</summary>
    public LauncherSettings Settings { get; private set; }

    /// <summary>
    /// The base game's files the modules' checks read, as the game's start takes them: the imported data when it is ready, else a
    /// developer's <c>VTMR_GAME_DIR</c> install; or null.
    /// </summary>
    string? BaseFolder()
    {
        var state = Setup.Service.Imported(options.Developer);
        if (state.Ready) return state.Folder;
        return options.GameDir() is { Length: > 0 } named && Platform.Assets.GameInstall.Locate(named) is { } dev ? Path.GetFullPath(dev) : null;
    }

    /// <summary>The launcher's screen.</summary>
    public Screen Screen { get; }

    /// <summary>Its data (<see cref="Fill"/> writes it).</summary>
    public ScreenData Data { get; } = new();

    /// <summary>What the screen's file got wrong (nothing, the tests hold).</summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>The Setup page.</summary>
    public SetupPage Setup { get; private set; }

    /// <summary>The program's own modules found beside it (the Classic game's row of the Modules page).</summary>
    public IReadOnlyList<ModuleRow> ProgramModules { get; }

    /// <summary>The Modules page: the player's modules and the problems list.</summary>
    public ModulesPage Modules { get; private set; }

    /// <summary>The page shown.</summary>
    public string Page { get; private set; } = "play";

    /// <summary>The notice up, or null (its default button takes the focus).</summary>
    public Notice? Notice
    {
        get => notice;
        private set
        {
            notice = value;
            focus = value is null ? "tab" + char.ToUpperInvariant(Page[0]) + Page[1..] : "notice" + (char)('A' + value.Default);
        }
    }
    Notice? notice;

    /// <summary>The game while it runs (the launcher is hidden), or null.</summary>
    public IRunningGame? Running { get; private set; }

    /// <summary>The last game's exit code, or null.</summary>
    public int? LastExit { get; private set; }

    /// <summary>Whether Quit was asked.</summary>
    public bool QuitAsked { get; private set; }

    /// <summary>
    /// What the window's picker is asked for (a folder for the Setup page, a folder or a zip or <c>.nob</c> for the Modules page; the window
    /// shows Windows' dialog and answers <see cref="Picked"/>), or null; taken once.
    /// </summary>
    public PickFor? TakePick()
    {
        var p = pick;
        pick = null;
        if (p is not null) picking = p;
        return p;
    }
    PickFor? pick, picking;

    /// <summary>
    /// The piece to give the keyboard's focus to once the screen is laid out (the page's own tab when a page is chosen, the notice's first
    /// button when one comes up), taken once; null when none is asked for.
    /// </summary>
    public string? TakeFocus()
    {
        string? f = focus;
        focus = null;
        return f;
    }
    string? focus;

    /// <summary>The field being typed in, or null.</summary>
    public string? Editing { get; private set; }

    /// <summary>Whether Play can start the game now.</summary>
    public bool CanPlay => canPlay;

    /// <summary>
    /// **The readiness check as the Play page lists it** (<see cref="Readiness"/>): the user folder, the game's files, the modules on, Vitomir's
    /// own files and FFmpeg; each waits while one before it that it needs is not ready.
    /// </summary>
    public ReadinessReport Ready
    {
        get
        {
            bool before = folderItem.Stops || filesItem.Stops;
            var modules = before ? Readiness.Modules(null, ready: false)
                : Readiness.Modules(Modules.Checking ? null : Modules.Start, checking: Modules.Checking || Modules.Importing, listProblem: Modules.ListProblem);
            return new([folderItem, filesItem, modules, programItem, Readiness.FFmpeg(Setup.FFmpeg)]);
        }
    }

    /// <summary>Why the user folder holds back what would write in it (Setup's steps, a module added, the settings), or null when it is ready.</summary>
    string? FolderBlock => !folderItem.Stops ? null
        : folderItem.Fix == "choices" ? "Choose your user folder first, on the Play page." : "Fix your user folder first, on the Folders page.";

    /// <summary>Why Move… cannot be selected now, or null.</summary>
    string? MoveBlock =>
        UserFolder.Moving ? "Unavailable while the user folder is being moved."
        : Running is not null ? "Unavailable while the game is running: close it first."
        : Setup.Busy ? "Unavailable while Setup " + BusyWords(Setup.BusyWith) + "."
        : Modules.Importing || Modules.Checking || Modules.Verifying is not null ? "Unavailable while the modules are being imported or checked."
        : UserFolder.IniWhy is { } why ? $"Unavailable: {char.ToUpperInvariant(why[0])}{why[1..]}."
        : folderItem.Stops && folderItem.Fix == "choices" ? "There is nothing to move yet: choose your user folder first, on the Play page."
        : UserFolder.Choice.Problem is not null ? "Choose your user folder again first (below)."
        : null;

    /// <summary>The check of the folder the launcher runs from (<see cref="Start"/>).</summary>
    public IReadOnlyList<LocationProblem> LocationProblems => location;

    /// <summary>
    /// **The launcher's start**: the check of the folder it runs from (a notice when anything is wrong, with what to do), the Setup
    /// page's first look, whether the game can be played, and the first page: **Setup when nothing is imported** (nor a developer's
    /// <c>VTMR_GAME_DIR</c> set), else Play.
    /// </summary>
    public void Start(IReadOnlyList<LocationProblem> problems)
    {
        location = problems;
        Setup.Refresh();
        Modules.Refresh();
        LookAtShortcuts();
        Recheck();
        Page = canPlay || folderItem.Stops || programItem.Stops ? "play" : "setup";
        focus = "tab" + char.ToUpperInvariant(Page[0]) + Page[1..];
        say($"launcher: {PlatformVersion.Text}, from {options.ProgramFolder}; user folder {options.UserData}; first page {Page}"
            + (problems.Count == 0 ? "; its folder: all well" : $"; its folder: {string.Join(", ", problems.Select(p => p.Kind))}"));
        if (problems.Count > 0)
            Notice = new("location", "Vitomir should not run from this folder",
                [.. Location.Words(problems).Select(w => new PageLine(w, w != Location.Advice))],
                [("Quit", () => QuitAsked = true), ("Continue anyway", () => Notice = null)], Default: 0, Cancel: 1);
    }

    /// <summary>
    /// **Whether the game can be played**, and the words for it: the start's own rule (<see cref="SetupCommand.Resolve"/> without a
    /// developer's <c>--oneone</c>): the imported data when it is ready, else a developer's <c>VTMR_GAME_DIR</c>
    /// (<see cref="LauncherOptions.GameDir"/>) when it names an install, else not, with why.
    /// </summary>
    public void Recheck()
    {
        UserFolder.Reload();
        folderItem = UserFolder.Item;
        programItem = Readiness.Program(options.ModulesFolder, options.GamesFolder);
        var state = Setup.Service.Imported(options.Developer);
        string? dev = state.Ready ? null : options.GameDir() is { Length: > 0 } named ? Platform.Assets.GameInstall.Locate(named) : null;
        filesItem = folderItem.Stops ? Readiness.GameFiles(options.UserData, options.Developer, null, userFolderReady: false) : Readiness.GameFiles(state, options.GameDir());
        bool filesReady = state.Ready || dev is not null;
        canPlay = filesReady && !folderItem.Stops && !programItem.Stops;
        // The imported game files go without saying (the Setup page says them); a developer's other source or manifest is said.
        if (state.Ready) playFrom = string.Join(" ", state.Notes.Where(n => n.Length > 0).Select(n => char.ToUpperInvariant(n[0]) + SetupWords.Plain(n[1..]) + "."));
        else if (dev is not null) playFrom = $"It plays from {Path.GetFullPath(dev)}, VTMR_GAME_DIR's (a developer's: nothing is imported).";
        else playFrom = "";
        // Play's reason names the item only; its row in the checklist says what to do (H2).
        playWhy = canPlay ? ""
            : folderItem.Stops ? NotReady(folderItem.Name)
            : programItem.Stops ? NotReady(programItem.Name)
            : NotReady(filesItem.Name);
    }

    static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Play's reason: the checklist's item named, pointing at its row (<c>Not ready: your user folder (below).</c>).</summary>
    static string NotReady(string item) =>
        $"Not ready: {(item.StartsWith("The ", StringComparison.Ordinal) || item.StartsWith("Your ", StringComparison.Ordinal) ? char.ToLowerInvariant(item[0]) + item[1..] : item)} (below).";

    /// <summary>The Play page's one line for the modules' problems (the list is on Modules): <c>2 warnings in your modules.</c></summary>
    static string ModulesNote(ModuleStart start)
    {
        int errors = start.Errors.Count(), warnings = start.Warnings.Count();
        string Count(int n, string one) => $"{N(n)} {one}{(n == 1 ? "" : "s")}";
        return (errors > 0 && warnings > 0 ? $"{Count(errors, "error")} and {Count(warnings, "warning")}" : errors > 0 ? Count(errors, "error") : Count(warnings, "warning"))
            + " in your modules.";
    }

    /// <summary>How the user folder was chosen, in one short line (F1, F11): the usual place, portable, named by the ini or the variable.</summary>
    static string HowShort(UserFolderChoice choice) => choice.Source switch
    {
        UserFolderSource.Variable => $"Named by {UserFolderPlace.VariableName} (a developer's).",
        UserFolderSource.Ini when choice.Portable => "Portable: inside the Vitomir folder.",
        UserFolderSource.Ini => $"Named by {VitomirIni.FileName}.",
        _ => "The usual place.",
    };

    /// <summary>The Classic game's one name, wherever the launcher names it (Whacker007, Q1, 2026-11-17).</summary>
    public const string GameName = "Vampire: The Masquerade \u2013 Redemption";

    /// <summary>The quiet line under the game's name where it is the thing played (the Play page, its row on Modules).</summary>
    public const string GameLine = "The original 2000 game (version 1.1), played on Vitomir";

    /// <summary>Where Vitomir's source and its updates are (About's link, opened in the browser).</summary>
    public const string SourceUrl = "https://github.com/Whacker007/vitomir";

    /// <summary>About's Licence, first: who owns Vitomir and its licence (the README and the zip's README say the same).</summary>
    public const string Licence = "The Vitomir platform and all of its original content are copyright \u00a9 2026 Whacker007, under the "
                                + "Vitomir Licence (LICENSE.txt in the Vitomir folder): free to play, change and share for any noncommercial "
                                + "purpose, never to be sold, and published only from its official page above. The fonts and third-party "
                                + "components below have their own licences.";

    /// <summary>About's Licence, then: the original game it needs, which the player must own and install.</summary>
    public const string LicenceGame = "Vitomir needs the original game's assets from " + GameName + ", and comes with none of them. You are "
                                    + "responsible for owning and installing a legitimate copy of the original game (GOG or Steam, version 1.1) "
                                    + "for Vitomir to read. Nothing of the original game may ever be shared with Vitomir or anything "
                                    + "made from it.";

    /// <summary>About's Licence, last: whose the game's names and properties are (not this project's).</summary>
    public const string LicenceRights = "Vampire: The Masquerade, " + GameName + ", White Wolf, World of Darkness and all related names, "
                                      + "characters and properties are trademarks or copyrights of their respective owners, not of this project. "
                                      + "Vitomir is an unofficial fan project, not affiliated with or endorsed by them; their names are used only "
                                      + "to say which game it runs.";

    /// <summary>The licence of the fonts the launcher and the game draw their words in.</summary>
    public const string FontsLicence = "SIL Open Font License 1.1";

    /// <summary>The file in the Vitomir folder that lists the third-party components and their licences.</summary>
    public const string ThirdPartyFile = "THIRD_PARTY.txt";

    /// <summary>The Vitomir folder's top (the folder of <c>vitomir-launcher.exe</c> and <c>vitomir.ini</c>; its programs are in its <c>bin</c>).</summary>
    string TopFolder => UserFolderPlace.TopOf(options.ProgramFolder);

    /// <summary>
    /// The words <b>Copy details for a bug report</b> put on the clipboard (the window takes them, <see cref="TakeClipboard"/>), or null;
    /// taken once.
    /// </summary>
    public string? TakeClipboard()
    {
        string? c = clipboard;
        clipboard = null;
        return c;
    }

    // ---- the actions ----------------------------------------------------------------------------------------------------------

    /// <summary>An action by name (the scripted steps', and a test's).</summary>
    public void Act(string name, string argument = "") => Handle(new UiAction(name, argument, Data, null));

    /// <summary>The screen's actions since the last call, answered.</summary>
    public void TakeActions()
    {
        foreach (var a in Screen.TakeActions()) Handle(a);
    }

    /// <summary>**One of the screen's actions**, answered.</summary>
    public void Handle(UiAction a)
    {
        if (a.Name != "edit" && Editing is not null) EndEdit(commit: false);
        if (Notice is not null && a.Name is not ("notice" or "escape" or "quit")) return;
        switch (a.Name)
        {
            case "page" when Pages.Contains(a.Argument):
                // Each page opens at its top (G3): the pages share one scrolling area.
                if (Page != a.Argument && Screen.Find("pageArea") is Scroll area) area.Offset = 0f;
                Page = a.Argument;
                focus = "tab" + char.ToUpperInvariant(Page[0]) + Page[1..];
                openWhy = "";
                if (Page == "play") { Recheck(); skipOpening = null; }
                if (Page == "films") Films.Opened();
                break;
            case "quit":
                QuitAsked = true;
                break;
            case "escape":
                if (Notice is { } n && n.CancelAt >= 0 && n.CancelAt < n.Buttons.Count) n.Buttons[n.CancelAt].Do();
                break;
            case "notice":
                if (Notice is { } up && "abc".IndexOf(a.Argument, StringComparison.Ordinal) is var i and >= 0 && i < up.Buttons.Count) up.Buttons[i].Do();
                break;
            case "play":
                Play();
                break;
            case var film when film.StartsWith("film", StringComparison.Ordinal) && HandleFilms(a):
                break;
            case "setupdetails":
                setupDetails = a.Argument switch { "on" => true, "off" => false, _ => !setupDetails };
                break;
            case "moduledetails" when a.Argument.Length == 0 || a.Argument is "on" or "off":
                moduleDetails = a.Argument switch { "on" => true, "off" => false, _ => !moduleDetails };
                break;
            case "ufdetails":
                ufDetails = a.Argument switch { "on" => true, "off" => false, _ => !ufDetails };
                break;
            case "readydetails":
                readyDetails = a.Argument switch { "on" => true, "off" => false, _ => !readyDetails };
                break;
            case "choose" when int.TryParse(a.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index):
                Setup.Choose(index);
                break;
            case "edit":
                BeginEdit(a.Argument);
                break;
            case "usefolder":
                Setup.UseFolder();
                break;
            case "chooseother":
                Setup.ChooseOther();
                break;
            case "closeonstart":
                // The checkbox wrote its own "closeOnStart": kept in the launcher's settings (a developer's step names on or off).
                SetCloseOnStart(a.Argument switch { "on" => true, "off" => false, _ => a.Scope.Flag("closeOnStart") });
                break;
            case "skipopening":
                SetSkipOpening(a.Argument switch { "on" => true, "off" => false, _ => a.Scope.Flag("skipOpening") });
                break;
            case "verify":
                Setup.StartVerify();
                break;
            case "repair":
                Setup.StartRepair();
                break;
            case "browse":
                pick = PickFor.SetupFolder;
                break;
            case "moduleon" when a.Argument.Length > 0:
                // The row's checkbox wrote its own "on": the module follows it.
                Modules.Toggle(a.Argument, a.Scope.Flag("on"));
                break;
            case "moduletoggle" when Modules.Rows.FirstOrDefault(r => r.Id == a.Argument) is { } toggled:
                // A developer's step (the stills'): the module's checkbox worked, as a click would.
                Modules.Toggle(toggled.Id, !toggled.On);
                break;
            case "moduleup":
                Modules.Move(a.Argument, -1);
                break;
            case "moduledown":
                Modules.Move(a.Argument, 1);
                break;
            case "moduleopen" when Modules.Rows.Any(r => r.Id == a.Argument):
                // A row's chevron or name (its mark-less checkbox wrote its own "open"), or a developer's step (turned over): its details
                // opened or closed under it; several may be open.
                Turn(Modules.Open, a.Argument, a.Source is not null ? a.Scope.Flag("open") : !Modules.Open.Contains(a.Argument));
                break;
            case "notusedopen" when Modules.Rows.Any(r => r.Id == a.Argument):
                // A module's folded line of files not used, opened or closed (as moduleopen).
                Turn(Modules.NotUsedOpen, a.Argument, a.Source is not null ? a.Scope.Flag("notUsedOpen") : !Modules.NotUsedOpen.Contains(a.Argument));
                break;
            case "modulemove" when a.Argument.Split(' ') is [var from, var to]
                                   && int.TryParse(from, NumberStyles.Integer, CultureInfo.InvariantCulture, out int movedFrom) && int.TryParse(to, NumberStyles.Integer, CultureInfo.InvariantCulture, out int movedTo):
                // A row dragged and dropped, or moved by Alt+Up or Alt+Down: from its place to the one it was dropped at.
                Modules.MoveTo(movedFrom, movedTo);
                break;
            case "moduleverify" when a.Argument.Length > 0:
                Modules.StartVerify(a.Argument);
                break;
            case "moduleupdate" when a.Argument.Length > 0 && !Modules.Importing && FolderBlock is null:
                // A module's Update: added again from where it came from, keeping its place and whether it is on.
                Modules.StartUpdate(a.Argument);
                break;
            case "moduleremove" when Modules.Rows.FirstOrDefault(r => r.Id == a.Argument) is { } removing:
                AskRemove(removing);
                break;
            case "addmodule":
                AskAdd();
                break;
            case "addfrom" when a.Argument.Length > 0:
                // A developer's step (the stills'): the pack named, as the picker would answer.
                Modules.StartAdd(a.Argument);
                break;
            case "canceladd":
                Modules.Cancel();
                break;
            case "sortmodules":
                Modules.Sort();
                break;
            case "playanyway":
                PlayAnyway();
                break;
            case "check":
                Setup.StartCheck();
                break;
            case "import":
                Setup.StartImport();
                break;
            case "cancel":
                Setup.Cancel();
                break;
            case "pick" when int.TryParse(a.Argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n2) && n2 >= 0 && n2 < Setup.Saves.Count:
                {
                    // The row's checkbox wrote its own "selected" (the row's item has it): the selection follows it.
                    string id = Setup.Saves[n2].Id;
                    if (a.Scope.Flag("selected") && Setup.Saves[n2].ImportsAs is not null) Setup.Picked.Add(id); else Setup.Picked.Remove(id);
                    break;
                }
            case "selectall":
                Setup.SelectAll();
                break;
            case "clearselection":
                Setup.ClearSelection();
                break;
            case "importsaves":
                Setup.StartSaves();
                break;
            case "ffmpeg":
                Setup.InstallFFmpeg();
                break;
            case "shortcut":
                // The switch wrote its own flag (a developer's step may name on or off after the place: "desktop on").
                {
                    var parts = a.Argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) break;
                    string key = Shortcuts.Parse(parts[0]) == ShortcutPlace.Desktop ? "shortcutDesktop" : "shortcutStart";
                    Shortcut(parts[0], parts.Length > 1 ? parts[1] == "on" : a.Scope.Flag(key));
                }
                break;
            case "shortcutupdate":
                Shortcut(a.Argument, null);
                break;
            case "ffmpeglook":
                Setup.LookForFFmpeg();
                break;
            case "usefolderusual" when FolderChoiceBlock is null:
                Chosen(UserFolder.UseUsual());
                break;
            case "usefolderexisting" when FolderChoiceBlock is null && UserFolder.IniWhy is null:
                if (a.Argument.Length > 0) Chosen(UserFolder.UseExisting(a.Argument));   // a developer's step: the folder as the picker would answer
                else pick = PickFor.UserFolderExisting;
                break;
            case "makeportable" when FolderChoiceBlock is null && UserFolder.IniWhy is null:
                Chosen(UserFolder.MakePortable());
                break;
            case "moveuserfolder" when MoveBlock is null:
                AskMove();
                break;
            case "moveto" when MoveBlock is null && a.Argument.Length > 0:
                // A developer's step (the stills'): the folder as the picker would answer, or portable.
                MoveTo(a.Argument);
                break;
            case "keepold":
                UserFolder.KeepOld = a.Argument switch { "on" => true, "off" => false, _ => a.Scope.Flag("keepOld") };
                break;
            case "cancelmove":
                UserFolder.CancelMove();
                break;
            case "open":
                openWhy = AllFolders().FirstOrDefault(f => f.Key == a.Argument && (!folderItem.Stops || f.Key == "program")) is { Path.Length: > 0 } f ? shell.OpenFolder(f.Path) ?? "" : "";
                break;
            case "opensource":
                aboutResult = shell.OpenLink(SourceUrl) is { } noLink ? $"Could not open it: {noLink}." : "";
                break;
            case "openthirdparty":
                aboutResult = shell.OpenFile(Path.Combine(TopFolder, ThirdPartyFile)) is { } noFile ? $"Could not open it: {noFile}." : "";
                break;
            case "copydetails":
                clipboard = BugReport();
                aboutResult = "Copied.";
                say("launcher: the details for a bug report copied to the clipboard");
                break;
        }
    }

    static void Turn(HashSet<string> set, string id, bool on)
    {
        if (on) set.Add(id); else set.Remove(id);
    }

    /// <summary>
    /// The picker's answer (or null: none selected): for the Setup page, put in the folder box and used; for the Modules page, the pack
    /// added.
    /// </summary>
    public void Picked(string? path)
    {
        var whose = picking ?? PickFor.SetupFolder;
        picking = null;
        if (path is null) return;
        if (whose == PickFor.SetupFolder)
        {
            Setup.Path = path;
            Setup.UseFolder();
        }
        else if (whose == PickFor.UserFolderExisting) Chosen(UserFolder.UseExisting(path));
        else if (whose == PickFor.MoveTarget) MoveTo(path);
        else Modules.StartAdd(path);
    }

    /// <summary>
    /// **Add a module…**: a notice asking where the pack is (a folder, or a zip or <c>.nob</c> file), each answer the window's picker; the
    /// import itself runs on the Modules page (<see cref="ModulesPage.StartAdd"/>).
    /// </summary>
    void AskAdd()
    {
        if (Modules.Importing || FolderBlock is not null) return;
        Notice = new("add", "Add a module",
            [new("Add it from the folder you unpacked it into, or from its zip or .nob.")],
            [("Folder…", () => { Notice = null; pick = PickFor.ModuleFolder; }),
             ("Zip or .nob…", () => { Notice = null; pick = PickFor.ModuleFile; }),
             ("Cancel", () => Notice = null)], Default: 0, Cancel: 2);
    }

    /// <summary>**Remove module…**: asked first, naming what goes (the module's imported copy, in the user folder) and what stays (the original pack).</summary>
    void AskRemove(ModuleView row)
    {
        if (Modules.Importing) return;
        string name = row.Module?.Manifest.Name ?? row.Id;
        Notice = new("remove", $"Remove {name}?",
            [new("Its imported copy is deleted from your user folder. The original pack on disk is not touched, so you can add it again.")],
            [("Remove", () =>
              {
                  Notice = null;
                  if (Modules.Remove(row.Id) is { } why)
                      Notice = new("failed", $"{name} could not be removed", [new(char.ToUpperInvariant(why[0]) + why[1..] + ".", true)], [("Close", () => Notice = null)], Default: 0, Cancel: 0);
              }),
             ("Cancel", () => Notice = null)], Default: 1, Cancel: 1);
    }

    /// <summary>
    /// **Replace X?**: a pack added whose module id is in the list already, from another folder or file: replacing it takes this pack's files
    /// and keeps the module's place in the load order and whether it is on. **Replace**, or **Cancel** (the default: the old copy's files
    /// go, and its own folder may be gone), which changes nothing.
    /// </summary>
    void AskReplaceModule(ReplaceQuestion q)
    {
        string these = Directory.Exists(q.Source) ? "this folder's files" : "the files of this " + (q.Source.EndsWith(".nob", StringComparison.OrdinalIgnoreCase) ? ".nob" : "zip");
        Notice = new("replacemodule", $"Replace {q.Name}?",
            [new($"The {q.Name} in the list came from another {(Directory.Exists(q.Source) ? "folder" : "place")} ({SetupWords.Plain(q.From)}). "
                 + $"Replacing it takes {these} and keeps its place in the load order and whether it is enabled.")],
            [("Replace", () => { Notice = null; Modules.StartReplace(); }),
             ("Cancel", () => { Notice = null; Modules.ForgetReplace(); })], Default: 1, Cancel: 1);
    }

    /// <summary>Why a first start's choices cannot be made now (a move runs), or null.</summary>
    string? FolderChoiceBlock => UserFolder.Moving ? "Unavailable while the user folder is being moved." : Setup.Busy ? "Unavailable while Setup " + BusyWords(Setup.BusyWith) + "." : null;

    /// <summary>A first start's choice answered: when it made a user folder, the launcher moves onto it.</summary>
    void Chosen(UserFolderAnswer answer)
    {
        if (answer.Done && answer.Folder is { } folder) Rebind(folder);
        else Recheck();
    }

    /// <summary>
    /// **Move…**: a notice saying what a move does and asking where to: **Choose a folder…** (Windows' folder dialog), **Make portable**
    /// (<c>UserData</c> inside the Vitomir folder), **Cancel**.
    /// </summary>
    void AskMove()
    {
        // What a refusal would say is said when it happens (F7); Keep the old folder is chosen here, where the move is (F2, Q5).
        Notice = new("move", "Move your user folder",
            [new("Your user folder is copied to the folder you choose, checked, then used."),
             new("Choose an empty or new folder.")],
            [("Choose a folder\u2026", () => { Notice = null; pick = PickFor.MoveTarget; }),
             ("Make portable", () => { Notice = null; MoveTo("portable"); }),
             ("Cancel", () => Notice = null)], Default: 0, Cancel: 2);
    }

    /// <summary>The move to <paramref name="target"/> (a folder, or <c>portable</c>) checked; refused with why in a notice, a synced folder asked about first, else started.</summary>
    void MoveTo(string target)
    {
        if (MoveBlock is { } blocked) { playResult = blocked; return; }
        if (UserFolder.Plan(target, out string? refused) is not { } plan)
        {
            Notice = new("failed", "The user folder cannot be moved there", [new(char.ToUpperInvariant(refused![0]) + refused[1..] + ".", true)], [("Close", () => Notice = null)], Default: 0, Cancel: 0);
            return;
        }
        if (plan.Warnings.Count > 0)
        {
            Notice = new("movewarn", "Move it into a synced folder?",
                [.. plan.Warnings.Select(w => new PageLine(char.ToUpperInvariant(w[0]) + w[1..] + ".", Caution: true))],
                [("Move anyway", () => { Notice = null; StartMove(plan); }), ("Cancel", () => Notice = null)], Default: 1, Cancel: 1);
            return;
        }
        StartMove(plan);
    }

    void StartMove(UserFolderMovePlan plan)
    {
        Page = "folders";
        UserFolder.StartMove(plan, folder => options.LogTo?.Invoke(folder));
    }

    /// <summary>The Play page's checkbox: the launcher closes when the game starts (on) or hides and comes back (off), kept in its settings.</summary>
    void SetCloseOnStart(bool on)
    {
        if (FolderBlock is { } blocked) { playResult = blocked; return; }
        Settings = Settings with { CloseOnStart = on };
        if (Settings.Write(options.UserData) is { } why) playResult = $"The setting could not be saved: {SetupWords.Plain(why)}.";
        say($"launcher: Close the launcher when the game starts: {(on ? "on" : "off")}");
    }

    /// <summary>
    /// The game's own settings file (the Classic module's <c>vitomir.ini</c> in the user folder), where the
    /// Play page's "Skip the opening films" is kept: the same <c>[qol] skipopening</c> the game reads and a
    /// player may write by hand, so the launcher keeps no copy of its own.
    /// </summary>
    public string GameSettingsPath => Path.Combine(options.UserData, "Modules", Game, "vitomir.ini");

    /// <summary>
    /// Whether the game's settings have the opening films skipped (<c>[qol] skipopening</c>): read once and kept (the screen is filled
    /// every frame, and a read of the disk each time slowed the launcher), read again when the Play page is opened (the game or a
    /// player's hand may have changed it).
    /// </summary>
    public bool SkipOpening
    {
        get
        {
            if (skipOpening is { } kept) return kept;
            try { skipOpening = File.Exists(GameSettingsPath) && Vitomir.Formats.IniText.On(Vitomir.Formats.IniText.Value(File.ReadAllText(GameSettingsPath), "qol", "skipopening")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { skipOpening = false; }
            return skipOpening.Value;
        }
    }

    bool? skipOpening;

    /// <summary>The Play page's "Skip the opening films": one line of the game's settings changed in place, every other line kept.</summary>
    void SetSkipOpening(bool on)
    {
        if (FolderBlock is { } blocked) { playResult = blocked; return; }
        try
        {
            string path = GameSettingsPath, text = File.Exists(path) ? File.ReadAllText(path) : "";
            string updated = Vitomir.Formats.IniText.Apply(text, "qol", [new KeyValuePair<string, string>("skipopening", on ? "on" : "off")]);
            if (updated != text)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, updated);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            playResult = $"The setting could not be saved: {SetupWords.Plain(e.Message)}.";
        }
        skipOpening = null;
        say($"launcher: Skip the opening movies: {(on ? "on" : "off")}");
    }

    /// <summary>**Play anyway with these**: the warnings of the combination on accepted (asked again when anything changes), then Play.</summary>
    void PlayAnyway()
    {
        if (Modules.Accept() is { } why)
        {
            playResult = $"Not accepted: {why}.";
            return;
        }
        Page = "play";
        Play();
    }

    // ---- typing ---------------------------------------------------------------------------------------------------------------

    void BeginEdit(string field)
    {
        if (field != "path") return;
        Editing = field;
        editWas = Setup.Path;
    }

    /// <summary>Text typed into the box being typed in (which selects the Another folder radio).</summary>
    public void Type(string text)
    {
        if (Editing != "path" || text.Length == 0) return;
        Setup.Typed(Setup.Path + text.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal));
    }

    /// <summary>Backspace in the box (a word at a time with <paramref name="word"/>).</summary>
    public void Backspace(bool word = false)
    {
        if (Editing != "path" || Setup.Path.Length == 0) return;
        if (!word) { Setup.Typed(Setup.Path[..^1]); return; }
        int cut = Setup.Path.TrimEnd('\\', '/', ' ').LastIndexOfAny(['\\', '/', ' ']);
        Setup.Typed(cut < 0 ? "" : Setup.Path[..(cut + 1)]);
    }

    /// <summary>The box's typing ended: Enter (<paramref name="commit"/>: its action, the folder used), else Escape (put back as it was) or elsewhere (kept).</summary>
    public void EndEdit(bool commit, bool putBack = false)
    {
        if (Editing is null) return;
        Editing = null;
        if (putBack && editWas is not null && editWas != Setup.Path) Setup.Typed(editWas);
        editWas = null;
        if (commit) Setup.UseFolder();
    }

    // ---- the game ---------------------------------------------------------------------------------------------------------------

    /// <summary>The game's command line: <c>--game classic</c>, <c>--developer</c> with the launcher's, then the words after its <c>--</c>.</summary>
    public IReadOnlyList<string> GameArgs => ["--game", Game, .. options.Developer ? ["--developer"] : Array.Empty<string>(), .. options.GameArgs];

    /// <summary>
    /// **Play**: the game started as a process of its own (<see cref="ProcessGame"/>: <c>vitomir-platform --game classic</c>, a window
    /// or full screen as its own settings say), its lines kept in <c>&lt;user data&gt;\Logs\game-&lt;when&gt;.log</c>; the launcher hides
    /// while it runs (<see cref="Running"/>) and comes back when it ends (<see cref="Poll"/>), or, with **Close the launcher when the game
    /// starts** on (<see cref="LauncherSettings.CloseOnStart"/>), it starts the game on its own and quits. Nothing when it cannot be played:
    /// the modules' check runs before the start, so modules with errors or warnings not accepted refuse Play either way.
    /// </summary>
    public void Play()
    {
        if (Films.Working && AskStopFilmsForPlay()) return;
        Recheck();
        if (!canPlay || Running is not null || Setup.Busy || UserFolder.Moving || Modules.PlayBlocked() is not null) return;
        string log = Path.Combine(options.UserData, "Logs", $"game-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.log");
        bool closing = Settings.CloseOnStart;
        var start = new GameStart(options.GameProgram, GameArgs, log, Detached: closing);
        try
        {
            var game = startGame(start);
            playResult = "";
            if (closing)
            {
                say($"launcher: started {options.GameProgram} {string.Join(' ', start.Args)} (process {game.Id}) on its own, and closes (Close the launcher when the game starts is on; the game keeps its own log)");
                QuitAsked = true;
                return;
            }
            Running = game;
            say($"launcher: started {options.GameProgram} {string.Join(' ', start.Args)} (process {Running.Id}); its lines in {log}");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            say($"launcher: the game could not be started: {e.Message}");
            Notice = new("failed", "The game could not be started",
                [new($"{options.GameProgram} could not be started: {e.Message}", true), new("It should be beside vitomir-launcher.exe: unzip Vitomir's whole zip again into a folder of your own.")],
                [("Close", () => Notice = null)], Default: 0, Cancel: 0);
        }
    }

    /// <summary>**The steps' and the game's ends taken** (once a frame on the launcher's thread). Answers whether anything changed.</summary>
    public bool Poll()
    {
        bool changed = Setup.Poll();
        if (Setup.Ends != ends)
        {
            // A step ended (taken here, or by a scripted step's wait): whether the game can be played now, and a save to ask about; the
            // modules checked again over the base game's files as they are now.
            ends = Setup.Ends;
            Recheck();
            Modules.Recheck();
            changed = true;
        }
        changed |= Modules.Poll();
        changed |= Films.Poll();
        if (UserFolder.Poll() is { } moved)
        {
            // A move's end: done, the launcher moves onto the new folder (its log already there); else the old one stays in use.
            if (moved.Done && moved.Folder is { } folder) Rebind(folder);
            else Recheck();
            changed = true;
        }
        if (Setup.AskReplace.Count > 0 && Notice is null && !Setup.Busy)
        {
            AskReplace();
            changed = true;
        }
        if (Modules.ReplaceAsked is { } replacing && Notice is null && !Modules.Importing)
        {
            AskReplaceModule(replacing);
            changed = true;
        }
        if (Running is { Ended.IsCompleted: true } game)
        {
            Running = null;
            Ended(game, game.Ended.Result);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// **The replace notice**: the saves (or characters) selected whose slot or name is taken already, listed, and the question; Replace
    /// (the default: each one there kept as <c>.previous</c>), Keep the existing ones (those not imported, the rest done), or Cancel
    /// (nothing written for them, still selected).
    /// </summary>
    void AskReplace()
    {
        var asked = Setup.AskReplace;
        bool characters = asked.All(a => a.Item.Kind == OriginalKind.Character), saves = asked.All(a => a.Item.Kind != OriginalKind.Character);
        bool one = asked.Count == 1;
        string what = saves ? (one ? "save" : "saves") : characters ? (one ? "character" : "characters") : (one ? "file" : "files");
        // Each one named plainly (not in the warning red: nothing went wrong), no folder path (N1).
        var lines = new List<PageLine>();
        lines.AddRange(asked.Select(a => new PageLine($"{a.Item.Id} \"{SetupWords.Plain(a.Item.Description)}\"")));
        lines.Add(new(one ? $"This {what} is already imported. Replace it? The one there now is kept as a backup (.previous)."
                          : $"These {what} are already imported. Replace them? The ones there now are kept as backups (.previous)."));
        string title = saves ? (one ? "Replace the existing save?" : "Replace the existing saves?")
            : characters ? (one ? "Replace the existing character?" : "Replace the existing characters?") : (one ? "Replace the existing file?" : "Replace the existing files?");
        Notice = new("replace", title, lines,
            [("Replace", () => { Notice = null; Setup.Replace(true); }),
             (one ? "Keep the existing one" : "Keep the existing ones", () => { Notice = null; Setup.Replace(false); }),
             ("Cancel", () => { Notice = null; Setup.CancelReplace(); })], Default: 0, Cancel: 2);
    }

    void Ended(IRunningGame game, int code)
    {
        LastExit = code;
        Recheck();
        string when = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
        say($"launcher: the game ended at {when}, exit code {code}; its lines in {game.LogPath}");
        if (code == 0)
        {
            // An end as expected says nothing more (E2): the launcher back is the news.
            playResult = "";
            return;
        }
        // No exit code on the page (E1): it is in the log.
        playResult = $"The game ended with an error at {when}.";
        if (code is 7 or 8)
        {
            // The start refused the modules on: their problems listed again (the start's own check) instead of the general notice.
            playResult = code == 7 ? $"The game did not start at {when}: the enabled modules have errors." : $"The game did not start at {when}: the modules' warnings are not accepted.";
            Modules.Refresh();
            Notice = new("modules", code == 7 ? "The enabled modules have errors" : "The modules' warnings are not accepted",
                [new(code == 7 ? "Fix them, or turn those modules off." : "Accept them with Play anyway, or change the modules.")],
                [("Go to Modules", () => { Notice = null; Page = "modules"; focus = "tabModules"; }), ("Close", () => Notice = null)], Default: 0, Cancel: 1);
            return;
        }
        // What it said last, with Open log (its exit code and the log's path are in launcher.log: E1).
        string meaning = code switch
        {
            2 => "Its command line or its modules were refused.",
            3 => "The game's files are not set up.",
            _ => "",
        };
        var lines = new List<PageLine>();
        if (meaning.Length > 0) lines.Add(new(meaning, true));
        var last = game.LastLines.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(6).ToList();
        if (last.Count > 0)
        {
            lines.Add(new("What it said last:"));
            lines.AddRange(last.Select(l => new PageLine("    " + SetupWords.Plain(l.Length > 300 ? l[..300] + "..." : l))));
        }
        else lines.Add(new("It wrote nothing before it ended: see its log."));
        string logs = Path.Combine(options.UserData, "Logs");
        Notice = new("ended", "The game ended with an error", lines,
            [("Open log", () => Opened(shell.OpenFile(game.LogPath))), ("Open logs folder", () => Opened(shell.OpenFolder(logs))), ("Close", () => Notice = null)], Default: 0, Cancel: 2);
    }

    /// <summary>What opening a log or a folder from a notice answered: said in the notice when it could not.</summary>
    void Opened(string? why)
    {
        openWhy = why ?? "";
        if (why is not null && Notice is { } n)
            Notice = n with { Lines = [.. n.Lines.Where(l => !l.Text.StartsWith("Could not open", StringComparison.Ordinal)), new($"Could not open it: {why}.", true)] };
    }

    // ---- the pages' data --------------------------------------------------------------------------------------------------------

    /// <summary>The folders the Folders page opens: key, what it is, the folder, a note.</summary>
    public IReadOnlyList<(string Key, string Label, string Path, string Note)> Folders() => folderItem.Stops
        // A user folder not set (a first start, a bad ini): none of its folders is named, only Vitomir's own. The user folder itself is
        // the panel's, with its own Open (F5: said once).
        ? [.. AllFolders().Where(f => f.Key == "program")]
        : [.. AllFolders().Where(f => f.Key != "user")];

    IReadOnlyList<(string Key, string Label, string Path, string Note)> AllFolders() =>
    [
        ("user", "Your user folder", options.UserData, "Settings, logs, saves and the original game's files (imported)."),
        ("logs", "The logs", Path.Combine(options.UserData, "Logs"), "launcher.log and each game's log."),
        ("saves", "Your saves", Setup.Service.SavesFolder, "SP0 is the autosave."),
        ("imported", "The original game's files (imported)", Setup.Service.DataFolder, "Setup's copy of them, from your copy of the game."),
        ("program", "Vitomir's own folder", TopFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "vitomir-launcher.exe, vitomir.ini when there is one, and its programs in bin."),
    ];

    /// <summary>**The screen's data** from the launcher's state (each frame; a list is made again only when what it shows changed).</summary>
    public void Fill()
    {
        var d = Data;
        d.Set("version", Vitomir.Platform.ReleaseVersion.Shown(PlatformVersion.Text));
        d.Set("page", Page);
        foreach (var p in Pages)
        {
            string name = char.ToUpperInvariant(p[0]) + p[1..];
            d.Set("on" + name, Page == p);
            d.Set("mark" + name, Page == p ? "chosenEdge" : "none");
        }
        d.Set("pageTitle", Page switch
        {
            "play" => "Play",
            "setup" => "Setup",
            "modules" => "Modules",
            "films" => "Movies",
            "folders" => "Folders",
            _ => "About",
        });
        d.Set("whereWarn", location.Count > 0);
        d.Set("whereLine", location.Count > 0 ? "This folder is not one Vitomir works from: see About." : "");
        d.Set(TextBox.Editing, Editing ?? "");

        // Play: the button always there, disabled with its reason when the game cannot start now.
        var s = Setup;
        bool busy = s.Busy;
        string? modulesBlock = Modules.PlayBlocked();
        bool playNow = canPlay && Running is null && !busy && !UserFolder.Moving && modulesBlock is null;
        var start = Modules.Checking ? null : Modules.Start;
        bool problems = start is { } st && st.Problems.Any(p => p.Level != ProblemLevel.Note);
        d.Set("playGame", GameName + (start is { Order.Count: > 0 } on ? $", with {N(on.Order.Count)} {(on.Order.Count == 1 ? "module" : "modules")}" : ""));
        d.Set("gameLine", GameLine);
        d.Set("canPlay", playNow);
        d.Set("toSetup", !canPlay && !folderItem.Stops && !programItem.Stops);
        d.Set("playFrom", canPlay ? playFrom : "");
        // The modules' problems: a one-line note with Go to Modules (the list is on Modules only), Play anyway beside Play (Q2).
        bool modulesNote = canPlay && problems && modulesBlock is not null && !Modules.Importing && !Modules.Checking;
        d.Set("playWhy", playNow ? "" : canPlay && UserFolder.Moving ? "Unavailable while the user folder is being moved." : canPlay && busy ? "Unavailable while Setup " + BusyWords(s.BusyWith) + "."
            : canPlay && Running is not null ? "Unavailable while the game is running." : canPlay ? modulesNote ? "" : modulesBlock ?? "" : playWhy);
        d.Set("problemsOnPlay", modulesNote);
        d.Set("playModulesNote", start is null ? "" : ModulesNote(start));
        d.Set("playButtonWhy", modulesNote ? d.Text("playModulesNote") : d.Text("playWhy"));
        d.Set("acceptOnPlay", canPlay && Modules.CanAccept && !busy);
        d.Set("modulesOnlyOnPlay", canPlay && start is { } se && se.Errors.Any());
        d.Set("playResult", playResult);
        d.Set("closeOnStart", Settings.CloseOnStart);
        d.Set("playNote", Settings.CloseOnStart ? "" : "The launcher hides while the game is running.");
        d.Set("canCloseOnStart", FolderBlock is null);
        d.Set("skipOpening", SkipOpening);
        d.Set("closeOnStartWhy", FolderBlock is null ? "" : folderItem.Fix == "choices" ? "Choose your user folder first (above)." : FolderBlock);
        FillReady(modulesNote);

        // Setup
        string busyWhy = busy ? "Unavailable while Setup " + BusyWords(s.BusyWith) + "." : "";
        d.Set("notBusy", !busy);
        d.Set("busyWhy", busyWhy);
        d.Set("setupIntro", "Import the original game's files from your GOG or Steam copy (1.26 GB). Your copy is only read.");
        int selectedCopy = -1;
        for (int i = 0; i < s.Installs.Count; i++) if (!s.Other && s.Chosen is not null && s.Installs[i].Folder == s.Chosen.Folder) selectedCopy = i;
        d.Set("copySelected", s.Other ? "other" : selectedCopy.ToString(CultureInfo.InvariantCulture));
        d.Set("otherFill", s.Other ? "chosen" : "band");
        d.Set("otherEdge", s.Other ? "chosenEdge" : "none");
        Rows("installs", string.Join("|", s.Installs.Select(c => c.Folder)) + "#" + s.Chosen?.Folder, add =>
        {
            for (int i = 0; i < s.Installs.Count; i++)
            {
                var c = s.Installs[i];
                bool selected = i == selectedCopy;
                add().Set("n", i.ToString(CultureInfo.InvariantCulture)).Set("folder", c.Folder)
                     .Set("store", $"{c.StoreName} copy")
                     .Set("how", c.Supported ? "" : "This is not a GOG or Steam copy, so it is not supported.")
                     .Set("rowFill", selected ? "chosen" : "band").Set("rowEdge", selected ? "chosenEdge" : "none");
            }
        });
        // An empty list, not an error (Y1, L5); gone once a folder of the player's own is used.
        d.Set("installsNone", s.Installs.Count == 0 && !(s.Other && s.Chosen is not null) ? "No GOG or Steam copy found: type its folder below." : "");
        d.Set("path", s.Path);
        d.Set("pathHint", "The folder that holds Resource.nob.");
        d.Set("pathWhy", s.PathWhy ?? "");
        d.Set("copyWhy", busyWhy);
        string? folderBlock = FolderBlock;
        d.Set("canStart", s.Chosen is not null && !busy && folderBlock is null);
        d.Set("startWhy", busy ? busyWhy : folderBlock is not null ? folderBlock : s.Chosen is null ? (s.Other && s.Path.Trim().Length > 0 ? "Select Use this folder first (step 1)." : "Select a copy of the game first (step 1).") : "");
        bool imported = s.HasImport;
        // Verify files is offered once something is imported (Q3: a command that cannot apply is hidden); while a step runs, its reason
        // is said once, under the first row of buttons (G9).
        d.Set("imported", imported);
        d.Set("canVerify", imported && !busy);
        d.Set("verifyWhy", "");
        d.Set("canRepair", s.CanRepair && !busy);
        d.Set("repairWhy", "");
        d.Set("offerRepair", s.CanRepair);
        d.Set("importWord", s.Imported(options.Developer) ? "Import again" : "Import");
        d.Set("busy", busy);
        d.Set("canCancel", busy && s.BusyWith is "check" or "import" or "verify" or "repair");
        var progress = s.Progress;
        d.Set("progress", progress?.Share ?? 0f);
        // A short -ing phrase and how far (E4).
        d.Set("progressText", !busy ? "" : s.BusyWith switch
        {
            "check" => "Checking\u2026",
            "import" => "Importing\u2026",
            "verify" => "Verifying\u2026",
            "repair" => "Repairing\u2026",
            "saves" => "Importing the saves\u2026",
            "winget" => "winget is running in its own window\u2026",
            _ => "Working\u2026",
        } + (progress is { } at && s.BusyWith != "winget" ? $" {(int)(at.Share * 100)}% ({SetupWords.Plain(at.Item)})" : ""));
        Lines("result", s.Result, setupDetails);
        bool resultDetails = s.Result.Any(l => l.Detail);
        d.Set("resultHasDetails", resultDetails);
        d.Set("resultDetailsWord", setupDetails ? "Hide details" : "Details");
        Rows("saves", string.Join("|", s.Saves.Select(i => i.Id + ":" + s.Picked.Contains(i.Id) + ":" + s.Into(i))) + "#" + busy, add =>
        {
            for (int i = 0; i < s.Saves.Count; i++)
            {
                var item = s.Saves[i];
                string kind = item.Kind switch { OriginalKind.Save => "", OriginalKind.NetworkSave => " (network game)", _ => " (character)" };
                bool importable = item.ImportsAs is not null;
                string when = item.WrittenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), into = s.Into(item);
                add().Set("n", i.ToString(CultureInfo.InvariantCulture)).Set("id", item.Id)
                     .Set("when", when).Set("sub", into.Length > 0 ? $"{when}, {into}" : when)
                     .Set("desc", "\"" + SetupWords.Plain(item.Description) + "\"" + kind).Set("into", s.Into(item))
                     .Set("selected", s.Picked.Contains(item.Id))
                     .Set("importable", importable).Set("canPick", importable && !busy)
                     .Set("pickWhy", !importable ? s.Into(item) : busyWhy);
            }
        });
        d.Set("savesNone", s.Chosen is null ? "Select a copy of the game to list its saves."
            : s.Saves.Count == 0 ? "This copy has no saves or characters." : "");
        int importables = s.Saves.Count(i => i.ImportsAs is not null);
        bool none = s.Picked.Count == 0, all = importables > 0 && s.Saves.Where(i => i.ImportsAs is not null).All(i => s.Picked.Contains(i.Id));
        d.Set("hasSaves", importables > 0);
        d.Set("canSelectAll", !busy && !all);
        d.Set("canClear", !busy && !none);
        d.Set("canImportSaves", !busy && !none && folderBlock is null);
        string selectAllWhy = busy ? busyWhy : all ? "All the saves that can be imported are selected." : "";
        // No reason line right after an import that worked (E2): the result says what happened.
        string selectFirst = busy ? busyWhy : none && !s.SaveResult.Any(l => !l.Warn) ? "Select the saves to import first." : "";
        d.Set("selectAllWhy", selectAllWhy);
        d.Set("clearWhy", selectFirst);
        d.Set("importSavesWhy", selectFirst.Length == 0 && folderBlock is not null ? folderBlock : selectFirst);
        // The buttons' reasons, under them; none while there is nothing to select (the buttons are not shown then).
        d.Set("savesWhy", importables == 0 ? "" : string.Join(" ", new[] { selectAllWhy, d.Text("importSavesWhy") }.Where(w => w.Length > 0).Distinct()));
        Lines("saveResult", s.SaveResult);
        d.Set("noFFmpeg", s.FFmpeg is null);
        d.Set("ffmpegWhy", s.FFmpeg is null ? busyWhy : "");
        // Its path and version are on About (A3).
        d.Set("ffmpegLine", s.FFmpeg is not null ? "FFmpeg found: the movies will play." : "FFmpeg is not found: the movies will be skipped.");
        d.Set("ffmpegNote", (s.FFmpegNote.Length > 0 ? s.FFmpegNote + " " : "") + "Any FFmpeg on your PATH works too.");
        FillShortcuts();

        // Modules: the Classic game first and fixed, the player's below in load order, the details of one, the problems list.
        FillModules(busy);
        FillFilms();

        // Folders
        var folders = Folders();
        Rows("folders", string.Join("|", folders.Select(f => f.Path + Directory.Exists(f.Path))), add =>
        {
            foreach (var f in folders)
            {
                bool there = Directory.Exists(f.Path);
                add().Set("key", f.Key).Set("label", f.Label).Set("path", f.Path).Set("there", there)
                     .Set("note", there ? f.Note : f.Note + " Not there yet.");
            }
        });
        FillUserFolder();
        d.Set("openWhy", openWhy);

        // About
        var about = About();
        Rows("about", string.Join("|", about.Select(a => a.Value)), add =>
        {
            foreach (var (label, value, warn) in about) add().Set("label", label).Set("value", value).Set("plain", !warn).Set("warn", warn);
        });
        d.Set("sourceUrl", SourceUrl);
        d.Set("licence", Licence);
        d.Set("licenceGame", LicenceGame);
        d.Set("licenceRights", LicenceRights);
        d.Set("fontsLicence", FontsLicence);
        d.Set("thirdPartyFile", ThirdPartyFile);
        d.Set("aboutResult", aboutResult);
        d.Set("aboutResultWarn", aboutResult.StartsWith("Could not", StringComparison.Ordinal));
        d.Set("aboutResultPlain", aboutResult.Length > 0 && !aboutResult.StartsWith("Could not", StringComparison.Ordinal));

        // The notice
        d.Set("notice", Notice is not null);
        d.Set("noticeKeepOld", Notice is { Kind: "move" });
        d.Set("noNotice", Notice is null);
        d.Set("noticeTitle", Notice?.Title ?? "");
        Lines("noticeLines", Notice?.Lines ?? []);
        for (int i = 0; i < 3; i++)
        {
            string key = "notice" + (char)('A' + i);
            d.Set(key, Notice is { } n && i < n.Buttons.Count ? n.Buttons[i].Label : "");
            d.Set(key + "Default", Notice is { } nd && i < nd.Buttons.Count && nd.Default == i);
            d.Set(key + "Cancel", Notice is { } nc && i < nc.Buttons.Count && nc.CancelAt == i);
        }
    }

    /// <summary>
    /// **The Play page's checklist**: each item of <see cref="Ready"/> a row (its mark, ✓ ✗ ! or …, drawn: the fonts have no such letters;
    /// its name; its line in the style of its weight; a button to the page that fixes it while it is not ready), and under the user folder's
    /// row, on a first start, its three choices (Use the usual place, Use an existing one…, Make portable; the last two disabled, saying
    /// why, when the Vitomir folder cannot be written).
    /// </summary>
    void FillReady(bool modulesNote = false)
    {
        var d = Data;
        var report = Ready;
        // D2: all ready folds to one line, Details opens it; a failing or warning item opens it by itself.
        bool allWell = report.Ready && report.Items.All(i => i.State == ReadyState.Ready);
        d.Set("readyMark", report.Ready ? allWell ? "ready" : "warning" : "failed");
        d.Set("readyTitle", report.Ready ? "Ready to play" : "Not ready yet");
        d.Set("readyFolds", allWell);
        d.Set("readyOpen", !allWell || readyDetails);
        d.Set("readyDetailsWord", readyDetails ? "Hide details" : "Details");
        Rows("ready", string.Join("|", report.Items.Select(i => $"{i.Key}:{i.State}:{i.Line}")) + "#" + modulesNote, add =>
        {
            foreach (var item in report.Items)
            {
                bool fixable = item.State is ReadyState.Failed or ReadyState.Warning && item.Fix is "setup" or "modules" or "folders" && !(item.Key == "ffmpeg" && Page == "setup")
                               && !(item.Key == "modules" && modulesNote);   // Go to Modules once, beside the note (H2)
                string label = item.Fix switch { "setup" => "Go to Setup", "modules" => "Go to Modules", "folders" => "Go to Folders", _ => "" };
                add().Set("key", item.Key).Set("mark", item.State.ToString().ToLowerInvariant()).Set("name", item.Name).Set("line", item.Line)
                     .Set("lineReady", item.State == ReadyState.Ready).Set("lineFailed", item.State == ReadyState.Failed)
                     .Set("lineWarning", item.State == ReadyState.Warning).Set("lineWaiting", item.State == ReadyState.Waiting)
                     .Set("hasFix", fixable).Set("fixLabel", label).Set("fixPage", item.Fix);
            }
        });
        bool ask = folderItem.Stops && folderItem.Fix == "choices";
        d.Set("askFolder", ask);
        FillChoices();
    }

    /// <summary>The first start's three choices' data (the Play page's, and the Folders page's when the user folder has a problem).</summary>
    void FillChoices()
    {
        var d = Data;
        var place = UserFolder.Choice.Place;
        string? block = FolderChoiceBlock, iniWhy = UserFolder.IniWhy;
        // An older Vitomir's folder in the usual place is never taken (nor deleted): Use the usual place waits until it is renamed or deleted.
        string? usualBlock = block ?? (UserFolder.Choice.OlderVitomir ? UserFolderChoice.OlderVitomirWords + "." : null);
        d.Set("canUseUsual", usualBlock is null);
        d.Set("useUsualWhy", usualBlock ?? "");
        string iniBlock = block ?? (iniWhy is null ? "" : $"Unavailable: {char.ToUpperInvariant(iniWhy[0])}{iniWhy[1..]}.");
        d.Set("canUseIni", iniBlock.Length == 0);
        d.Set("useIniWhy", iniBlock);
        d.Set("choicesWhy", string.Join(" ", new[] { usualBlock ?? "", iniBlock }.Where(w => w.Length > 0).Distinct()));
        d.Set("choicesIntro", "Where should Vitomir keep your saves, settings and the original game's files (about 1.3 GB)?");
        d.Set("choiceUsualNote", $"{place.Usual} (recommended).");
        d.Set("choiceExistingNote", "One from another copy of Vitomir, or one you moved.");
        d.Set("choicePortableNote", $"{place.Portable}: everything stays in the Vitomir folder (for a USB drive or another PC).");
        // A choice that worked shows in the checklist's row; only one that failed is said here (H2, E2).
        Lines("folderResult", [.. UserFolder.Result.Where(l => l.Warn)]);
    }

    /// <summary>
    /// **The Folders page's user folder**: where it is and how it was chosen, <c>vitomir.ini</c>'s place and what it says, its problem or
    /// warning, **Move…** (disabled, saying why, while it cannot be) with **Keep the old folder**, the move's progress and **Cancel**, and the
    /// choices again when the user folder has a problem.
    /// </summary>
    void FillUserFolder()
    {
        var d = Data;
        var u = UserFolder;
        var item = folderItem;
        bool set = u.Choice.Problem is null && u.Choice.Chosen;
        d.Set("ufPath", set ? u.Choice.Folder : "Not set");
        d.Set("ufThere", set && Directory.Exists(u.Choice.Folder));
        d.Set("ufHow", set ? HowShort(u.Choice) : "");
        // vitomir.ini's text: shown by itself when it has a problem (its bad line in red), else behind Details (D1).
        bool iniProblem = u.Choice.Problems.Count > 0 || u.Choice.Ini.Problems.Count > 0;
        d.Set("ufHasDetails", !iniProblem);
        d.Set("ufDetailsWord", ufDetails ? "Hide details" : "Details");
        Lines("ufIni", iniProblem || ufDetails ? u.IniLines() : []);
        d.Set("ufProblem", item.State is ReadyState.Failed ? item.Line : "");
        d.Set("ufWarning", item.State is ReadyState.Warning ? item.Line : "");
        d.Set("ufChoicesHere", item.Stops && item.Fix == "folders");
        string? block = MoveBlock;
        d.Set("canMove", block is null);
        d.Set("ufMoveWhy", block ?? "");
        d.Set("keepOld", u.KeepOld);
        bool moving = u.Moving;
        d.Set("ufMoving", moving);
        d.Set("canCancelMove", moving);
        var progress = u.Progress;
        d.Set("moveProgress", progress is { Total: > 0 } pr ? (float)pr.Done / pr.Total : 0f);
        d.Set("moveProgressText", !moving ? "" : $"Moving to {u.MovingTo}\u2026" + (progress is { Total: > 0 } at ? $" {(int)(100.0 * at.Done / at.Total)}%" : ""));
        d.Set("keepOldNote", u.KeepOld ? "Delete it yourself once you are sure." : "The old folder is removed once the new one is checked.");
        Lines("moveResult", u.Result);
    }

    /// <summary>The Modules page's data: its buttons and their reasons, the import's progress, the rows, the details, the problems list.</summary>
    void FillModules(bool setupBusy)
    {
        var d = Data;
        var m = Modules;
        bool importing = m.Importing, checking = m.Checking;
        var rows = m.Rows;
        d.Set("modulesIntro", "Modules load over the game in this order: when two provide the same file, the lower one wins.");
        string importingWhy = importing ? "Unavailable while a module is being imported." : "";
        d.Set("canAddModule", !importing && FolderBlock is null);
        d.Set("addWhy", importing ? importingWhy : FolderBlock ?? "");
        int players = rows.Count;
        // Offered with two modules or more (Q3: a command that cannot apply is hidden).
        d.Set("offerSort", players >= 2);
        d.Set("canSort", !importing && players >= 2);
        string sortWhy = importing && players >= 2 ? importingWhy : "";
        d.Set("sortWhy", sortWhy);
        d.Set("modulesWhy", string.Join(" ", new[] { d.Text("addWhy"), sortWhy }.Where(w => w.Length > 0).Distinct()));
        d.Set("moduleBusy", importing);
        var progress = m.Progress;
        d.Set("moduleProgress", progress?.Share ?? 0f);
        d.Set("moduleProgressText", !importing ? "" : $"Importing {m.AddingName}\u2026" + (progress is { } at ? $" {(int)(at.Share * 100)}%" : ""));
        Lines("moduleResult", m.Result, moduleDetails);
        d.Set("moduleResultHasDetails", m.Result.Any(l => l.Detail));
        d.Set("moduleResultDetailsWord", moduleDetails ? "Hide details" : "Details");

        var classic = ProgramModules.FirstOrDefault(r => r.Id == Game);
        d.Set("classicOn", true);
        d.Set("classicToggle", false);
        // The game's one name and its line (Q1); no version on its row (About has Vitomir's).
        d.Set("classicName", GameName);
        d.Set("classicWhy", "Always on, always first.");
        d.Set("classicLine", "Always on, always first.");

        // The list in columns (a checkbox, the handle and the arrows, the name with what it needs under it, the version, the kinds as tags,
        // the chevron), in load order; each row's details open under it, one or several, all closed at first. A dependency missing or
        // off is said in the row, in the warning colour, with the problem's own line in the checks.
        var current = m.Checking ? null : m.Start;
        var names = rows.ToDictionary(r => r.Id, r => r.Module?.Manifest.Name ?? r.Id, StringComparer.Ordinal);
        string verifying = m.Verifying ?? "";
        bool verifyingNow = verifying.Length > 0;
        string verifyingWhy = verifyingNow ? $"Unavailable while {names.GetValueOrDefault(verifying, verifying)} is being verified." : "";
        string signature = string.Join("|", rows.Select(r => $"{r.Id}:{r.On}:{r.Module?.Record.ContentHash}:{r.Module?.Record.ManifestHash}:{r.Broken}"
                                                             + $":{(r.Module is { } u && Modules.Open.Contains(r.Id) ? ModulesPage.UpdateBlocked(u) : "")}"))
                           + $"#{FolderBlock}"
                           + $"#{string.Join(",", m.Open.Order(StringComparer.Ordinal))}#{string.Join(",", m.NotUsedOpen.Order(StringComparer.Ordinal))}"
                           + $"#{importing}#{m.Ends}#{verifying}#{(current is null ? "" : string.Join(";", current.Problems.Where(p => p.Kind == "dependency").Select(p => p.Line)))}";
        Rows("modules", signature, add =>
        {
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var man = r.Module?.Manifest;
                bool first = i == 0, last = i == rows.Count - 1, open = m.Open.Contains(r.Id);
                string upWhy = importing ? importingWhy : first && last ? OnlyWhy : first ? FirstWhy : "";
                string downWhy = importing ? importingWhy : first && last ? OnlyWhy : last ? LastWhy : "";
                var dependency = current?.Problems.FirstOrDefault(p => p.Module == r.Id && p.Kind == "dependency");
                string needs = man is null || man.Dependencies.Count == 0 ? ""
                    : "needs " + string.Join(", ", man.Dependencies.Select(dep => names.GetValueOrDefault(dep.Id, dep.Id)));
                // A dependency's problem is said once, in the checks (H2): the row's red line shows it.
                string warn = r.Broken is { } why ? $"Not usable: {why}." : "";
                string verifyWhy = importing ? importingWhy : verifyingWhy.Length > 0 ? verifyingWhy : r.Module is null ? "It was not fully imported: remove it, or add it again." : "";
                string removeWhy = importing ? importingWhy : verifyingWhy;
                string? updateBlocked = r.Module is { } source ? ModulesPage.UpdateBlocked(source) : null;
                string updateWhy = importing ? importingWhy : verifyingWhy.Length > 0 ? verifyingWhy : FolderBlock ?? updateBlocked ?? "";
                var notUsed = r.Module is { } withFiles ? ModulesPage.NotUsed(withFiles) : [];
                bool notUsedOpen = m.NotUsedOpen.Contains(r.Id);
                var item = add().Set("id", r.Id).Set("on", r.On)
                     .Set("name", man?.Name ?? r.Id)
                     .Set("version", man is null ? "" : man.Version.ToString())
                     .Set("needs", needs).Set("needsPlain", needs.Length > 0 && dependency is null).Set("needsWarn", needs.Length > 0 && dependency is not null)
                     .Set("rowWarn", warn).Set("hasRowWarn", warn.Length > 0)
                     .Set("canToggle", !importing).Set("toggleWhy", importingWhy)
                     .Set("canUp", !importing && !first).Set("upWhy", upWhy)
                     .Set("canDown", !importing && !last).Set("downWhy", downWhy)
                     .Set("open", open).Set("chevron", open ? "open" : "closed")
                     .Set("rowFill", i % 2 == 0 ? "bandAlt" : "band")
                     .Set("canVerifyModule", r.Module is not null && !importing && !verifyingNow).Set("verifyModuleWhy", verifyWhy)
                     .Set("canRemoveModule", !importing && !verifyingNow).Set("removeModuleWhy", removeWhy)
                     .Set("offerUpdate", r.Module is not null)
                     .Set("canUpdateModule", r.Module is not null && updateWhy.Length == 0).Set("updateModuleWhy", updateWhy)
                     .Set("detailsWhy", string.Join(" ", new[] { r.Module is null ? "" : updateWhy, verifyWhy, removeWhy }.Where(w => w.Length > 0).Distinct()))
                     .Set("hasNotUsed", notUsed.Count > 0).Set("notUsedOpen", notUsedOpen)
                     .Set("notUsedLine", ModulesPage.NotUsedLine(notUsed.Count)).Set("notUsedChevron", notUsedOpen ? "open" : "closed")
                     .Set("verifyingLine", verifying == r.Id ? $"Verifying {names.GetValueOrDefault(verifying, verifying)}\u2026" : "");
                if (r.Module is { } mod)
                {
                    foreach (var kind in PackKindWords.List(mod.Kinds)) item.Add("tags").Set("tag", kind).Set("plainTag", true).Set("rulesTag", false);
                    if (mod.Rules) item.Add("tags").Set("tag", "rules").Set("plainTag", false).Set("rulesTag", true);
                }
                if (!open) continue;
                foreach (var l in ModulesPage.Details(r)) Line(item.Add("details"), l);
                if (notUsedOpen && r.Module is { } listed)
                    foreach (var l in ModulesPage.NotUsedLines(listed)) Line(item.Add("notUsed"), l);
                if (m.VerifyResult is { } vr && vr.Id == r.Id)
                    foreach (var l in vr.Lines) Line(item.Add("verify"), l);
            }
        });
        d.Set("modulesNone", rows.Count == 0 ? "No modules yet. Select Add a module\u2026 to add a texture pack or a mod." : "");

        // The problems list: errors first, then warnings, then notes; live, the start's own check after every change.
        var start = m.Start;
        var problems = checking || start is null ? [] : start.Problems.OrderBy(p => p.Level).ToList();
        int errors = problems.Count(p => p.Level == ProblemLevel.Error), warnings = problems.Count(p => p.Level == ProblemLevel.Warning);
        string Count(int n, string one) => $"{N(n)} {one}{(n == 1 ? "" : "s")}";
        d.Set("problemsLine", checking ? "Checking the enabled modules\u2026"
            : m.ListProblem is { } bad ? SetupWords.Plain(bad) + "."
            : start is null || start.Order.Count == 0 && problems.Count == 0 ? "No modules are enabled."
            : errors > 0 ? (warnings > 0 ? $"{Count(errors, "error")}, {Count(warnings, "warning")}" : Count(errors, "error"))
                           + $": the game does not start until {(errors == 1 ? "the error is" : "the errors are")} fixed."
            : warnings > 0 ? start.Accepted ? $"{Count(warnings, "warning")}, accepted." : $"{Count(warnings, "warning")}: the game may look wrong."
            : "No problems.");
        Rows("problems", string.Join("\n", problems.Select(p => p.Line)) + "#" + checking, add =>
        {
            foreach (var p in problems)
            {
                var (head, files, todo) = ModulesPage.Words(p, names);
                add().Set("head", head).Set("files", files).Set("todo", todo).Set("hasFiles", files.Length > 0).Set("hasTodo", todo.Length > 0)
                     .Set("error", p.Level == ProblemLevel.Error).Set("warning", p.Level == ProblemLevel.Warning).Set("note", p.Level == ProblemLevel.Note);
            }
        });
        d.Set("canAccept", m.CanAccept && !setupBusy);
        d.Set("acceptNote", "Asked again when the modules change.");
    }

    /// <summary>The reasons the arrows of the list's ends are disabled (said in a line under the list).</summary>
    const string FirstWhy = "The first module cannot move up.", LastWhy = "The last module cannot move down.", OnlyWhy = "The only module cannot move up or down.";

    /// <summary>What a running step is doing, in words after "Unavailable while Setup …".</summary>
    static string BusyWords(string busy) => busy switch
    {
        "check" => "checks the files",
        "import" => "imports the files",
        "verify" => "verifies the files",
        "repair" => "repairs the files",
        "saves" => "imports the saves",
        "winget" => "waits for winget",
        _ => "is working",
    };

    /// <summary>
    /// The About page's list: the version, what is wrong with the folder it runs from (its check), a developer's switches. The folders are
    /// on Folders (the game's program on Play's checklist); the source, the licences and **Copy details for a bug report**
    /// (<see cref="BugReport"/>) follow the list on the page.
    /// </summary>
    public IReadOnlyList<(string Label, string Value, bool Warn)> About()
    {
        var list = new List<(string, string, bool)> { ("Version", Vitomir.Platform.ReleaseVersion.Shown(PlatformVersion.Text), false) };
        foreach (var w in Location.Words(location)) list.Add(("", w, true));
        if (options.Developer) list.Add(("Developer", "--developer: the game is started with it too" + (options.GameArgs.Count > 0 ? $", and with {string.Join(' ', options.GameArgs)}" : ""), true));
        return list;
    }

    /// <summary>
    /// **The details for a bug report** (About's <b>Copy details for a bug report</b>): Vitomir's version, the system, .NET, where it runs
    /// from, the user folder and how it was chosen, <c>vitomir.ini</c>'s state, and FFmpeg's, one per line. A report is posted in
    /// public, so the player's own Windows folder (which holds their user name) is written <c>%USERPROFILE%</c> throughout
    /// (<see cref="WithoutTheUser"/>).
    /// </summary>
    public string BugReport() => WithoutTheUser(Details(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// <paramref name="text"/> with every path in the player's own Windows folder (<paramref name="profile"/>, any case) written
    /// from <c>%USERPROFILE%</c>, so no user name is in it; a folder whose name merely begins the same (Anna's and Annabel's)
    /// is left alone.
    /// </summary>
    internal static string WithoutTheUser(string text, string? profile)
    {
        string root = (profile ?? "").TrimEnd('\\', '/');
        if (root.Length < 3) return text;                                     // none, or a drive's root
        // Not followed by more of a folder's name: a separator, a space, the line's end or punctuation.
        return System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(root) + @"(?![\w.-])",
            "%USERPROFILE%", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    string Details()
    {
        var lines = new List<string>
        {
            $"Vitomir: {PlatformVersion.Text}",
            $"System: {RuntimeInformation.OSDescription.Trim()}",
            $".NET: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()})",
            $"Runs from: {options.ProgramFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}",
            $"User folder: {(UserFolder.Choice.Problem is { } bad ? $"not set: {bad}" : $"{options.UserData} ({UserFolder.Choice.How})")}",
        };
        lines.AddRange(UserFolder.IniLines().Select(l => (l.Warn ? "  ! " : "  ") + l.Text.Trim()));
        lines.Add($"FFmpeg: {(Setup.FFmpeg is { } ff ? $"{ff.FFmpeg} ({SetupWords.Plain(ff.Version)})" : "not found")}");
        foreach (var w in Location.Words(location)) lines.Add("Its folder: " + w);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    void Rows(string list, string signature, Action<Func<ScreenData>> fill)
    {
        if (listed.TryGetValue(list, out var was) && was == signature) return;
        listed[list] = signature;
        Data.Clear(list);
        fill(() => Data.Add(list));
    }

    /// <summary>A list of lines; a line of the details (<see cref="PageLine.Detail"/>) shown only while they are open (D1).</summary>
    void Lines(string list, IReadOnlyList<PageLine> lines, bool details = false) =>
        Rows(list, string.Join("\n", lines.Select(l => (l.Warn ? "!" : " ") + (l.Detail ? "d" : " ") + (l.Caution ? "c" : " ") + l.Text)) + "#" + details, add =>
        {
            foreach (var l in lines.Where(l => !l.Detail || details)) Line(add(), l);
        });

    /// <summary>One line's data as <see cref="Lines"/> sets it (a row's own list of lines: a module's details).</summary>
    static void Line(ScreenData item, PageLine l) =>
        item.Set("line", l.Text).Set("plain", !l.Warn && !l.Detail && !l.Caution).Set("warn", l.Warn && !l.Detail).Set("detail", l.Detail)
            .Set("caution", l.Caution && !l.Warn && !l.Detail);

    /// <summary>The Classic game's module in <paramref name="folder"/> (a folder with its <c>module.json</c>), as the Modules page says it; no other.</summary>
    public static IReadOnlyList<ModuleRow> FindModules(string folder)
    {
        var rows = new List<ModuleRow>();
        if (!Directory.Exists(folder)) return rows;
        foreach (var dir in Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            var problems = new List<string>();
            if (ModuleManifest.Read(dir, problems) is not { } m)
            {
                if (File.Exists(Path.Combine(dir, ModuleManifest.FileName)) && Path.GetFileName(dir).Equals(Game, StringComparison.OrdinalIgnoreCase))
                    rows.Add(new(Path.GetFileName(dir), Path.GetFileName(dir), "", "", "Its module.json cannot be read: " + string.Join("; ", problems.Take(2)), true));
                continue;
            }
            // The launcher lists the Classic game and the player's modules only: another module beside the program (a developer's
            // build has more) is not the player's to play from here, and is not named.
            if (m.Id != Game) continue;
            rows.Add(new(m.Id, m.Name, $"{m.Version} ({m.Kind.ToString().ToLowerInvariant()})", FirstSentence(m.Description), "The game this release plays: Play starts it.", false));
        }
        return rows;
    }

    static string FirstSentence(string text)
    {
        int at = text.IndexOf(". ", StringComparison.Ordinal);
        return SetupWords.Plain(at > 0 ? text[..(at + 1)] : text);
    }
}
