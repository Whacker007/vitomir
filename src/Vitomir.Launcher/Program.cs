// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// vitomir-launcher.exe: its command line, its log, and its window or its stills.
using System.Globalization;
using Vitomir.Platform.Host;
using Vitomir.Platform.Render.Windowing;
using Vitomir.Platform.Setup;

namespace Vitomir.Launcher;

/// <summary>
/// **vitomir-launcher**: with no switch, the launcher's window. A developer's switches (with <c>--developer</c>, which the game is
/// then started with too): <c>--manifest &lt;file&gt;</c> (the setup checks and imports against another manifest, as
/// <c>--setup</c>'s), <c>--location &lt;folder&gt;</c> (the start's check made of another folder, as if it ran from there),
/// <c>--headless --steps "&lt;steps&gt;" [--size WxH]</c> (no window: <see cref="LauncherSteps"/>), and <c>-- &lt;words&gt;</c> (the
/// game's command line after <c>--game classic</c>). Its lines go to <c>&lt;user data&gt;\Logs\launcher.log</c> (the one before kept as
/// <c>launcher.previous.log</c>) and, for a developer's headless run, the terminal.
/// </summary>
public static class Program
{
    public const string Usage = """
        vitomir-launcher: Vitomir's launcher: Play, Setup, Modules, Folders, About.
          (no switch)                     the launcher's window
          --developer                     a developer's run: the game is started with --developer too, and these are taken:
            --manifest <file>             the setup's check and import against another manifest (as --setup --manifest)
            --location <folder>           the start's check of its folder made of <folder> instead
            --headless --steps "<steps>"  no window: steps separated by ';' (or @file), stills among them (--size WxH, 1280x800;
                                          --scale 1.5 lays them out as on a display scaled 150 %)
            -- <words>                    the game's command line after --game classic --developer
        """;

    [STAThread]
    public static int Main(string[] args)
    {
        int dashes = Array.IndexOf(args, "--");
        string[] own = dashes < 0 ? args : args[..dashes], game = dashes < 0 ? [] : args[(dashes + 1)..];
        var cli = new Cli(own);
        bool headless = cli.Flag("--headless");
        if (headless || cli.Flag("--help")) TextConsole.Open(TextConsole.Wanted.Terminal);
        if (cli.Flag("--help") || cli.Flag("-h")) { Console.WriteLine(Usage); return 0; }
        // The user folder as chosen (VITOMIR_USERDATA, vitomir.ini, the usual place). On a first start (none chosen yet) or with a bad ini
        // the log waits in memory: nothing is written in a folder the player has not chosen; it goes there once one is (LauncherOptions.LogTo).
        var folder = UserData.Choice;
        bool ready = folder.Chosen && folder.Problem is null;
        using var log = new LauncherLog(ready ? Path.Combine(folder.Folder, "Logs") : null, echo: headless);
        try { return Run(cli, game, headless, folder, log); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Say($"launcher: stopped on an unhandled error: {e}");
            throw;
        }
    }

    static int Run(Cli cli, string[] game, bool headless, UserFolderChoice folder, LauncherLog log)
    {
        Action<string> say = log.Say;
        bool developer = cli.Flag("--developer");
        foreach (var only in new[] { "--manifest", "--location", "--steps", "--scale" })
            if (cli.Flag(only) && !developer) { say($"launcher: {only} is a developer's (with --developer)"); return 2; }
        if (game.Length > 0 && !developer) { say("launcher: the game's words after -- are a developer's (with --developer)"); return 2; }
        DataManifest manifest = DataManifest.Builtin;
        if (cli.Arg("--manifest") is { } other)
        {
            try { manifest = DataManifest.Read(other); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { say($"launcher: the manifest {other} does not read: {e.Message}"); return 2; }
        }
        string program = SetupCommand.ProgramPath();
        SetupPage MakeSetup(string userData) =>
            new(new SetupService(userData, manifest, ThisMachine.Instance, new ProgramSaveImporter(program, userData), PlatformVersion.Text), WindowsShell.Instance, say);
        var options = new LauncherOptions
        {
            Developer = developer, GameArgs = game, Folder = folder, UserData = folder.Folder, GameProgram = program,
            NewSetup = MakeSetup, LogTo = userData => log.MoveTo(Path.Combine(userData, "Logs")),
        };
        var service = new SetupService(options.UserData, manifest, ThisMachine.Instance, new ProgramSaveImporter(options.GameProgram, options.UserData), PlatformVersion.Text);
        var launcher = new Launcher(options, new SetupPage(service, WindowsShell.Instance, say), ProcessGame.Start, WindowsShell.Instance, say);
        foreach (var p in launcher.Problems) say($"launcher: {p}");
        string where = cli.Arg("--location") ?? options.ProgramFolder;
        var problems = Location.Check(where, LocationPlaces.ThisMachine(), Location.WriteTest);
        if (cli.Arg("--location") is not null) say($"launcher: the start's check made of {where} (--location)");
        foreach (var p in problems) say($"launcher: its folder: {p.Kind}: {p.Words}");
        if (cli.Arg("--manifest") is not null) say($"launcher: the setup's manifest {manifest} ({cli.Arg("--manifest")}, a developer's)");
        launcher.Start(problems);
        if (!headless) return LauncherWindow.Run(launcher, say);
        if (cli.Arg("--steps") is not { } steps) { say("launcher: --headless wants --steps \"<steps>\""); return 2; }
        var size = cli.Size("--size") ?? LauncherWindow.Size;
        float scale = cli.Arg("--scale") is { } sc && float.TryParse(sc, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 1f ? f : 1f;
        return LauncherSteps.Run(launcher, LauncherSteps.Parse(steps), size, say, scale);
    }
}

/// <summary>The launcher's log: <c>launcher.log</c> in the logs folder, each line stamped; the one before kept as <c>launcher.previous.log</c>.</summary>
public sealed class LauncherLog : IDisposable
{
    StreamWriter? file;
    readonly bool echo;
    readonly object gate = new();
    readonly DateTime start = DateTime.UtcNow;

    /// <summary>The log in <paramref name="folder"/> (the one there before kept as <c>launcher.previous.log</c>), or, with none, kept in memory until <see cref="MoveTo"/> names one.</summary>
    public LauncherLog(string? folder, bool echo)
    {
        this.echo = echo;
        if (folder is not null) Open(folder, fresh: true);
    }

    readonly List<string> waiting = [];

    void Open(string folder, bool fresh)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string path = System.IO.Path.Combine(folder, "launcher.log");
            if (fresh && File.Exists(path)) File.Copy(path, System.IO.Path.Combine(folder, "launcher.previous.log"), overwrite: true);
            file = new StreamWriter(path, append: !fresh) { AutoFlush = true };
            Path = path;
            foreach (var l in waiting) file.WriteLine(l);
            waiting.Clear();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { file = null; }
    }

    /// <summary>
    /// **The log moved to <paramref name="folder"/>** (the user folder's <c>Logs</c>, once it is chosen or moved): the file there is closed
    /// and its whole text taken along when the new folder holds a copy of it (a move's), then the lines go on there; the lines kept in memory
    /// on a first start are written first.
    /// </summary>
    public void MoveTo(string folder)
    {
        lock (gate)
        {
            string? was = Path;
            file?.Dispose();
            file = null;
            string to = System.IO.Path.Combine(folder, "launcher.log");
            try
            {
                if (was is not null && File.Exists(was) && !System.IO.Path.GetFullPath(was).Equals(System.IO.Path.GetFullPath(to), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(folder);
                    File.Copy(was, to, overwrite: true);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            Open(folder, fresh: was is null);
        }
    }

    /// <summary>The log file, or null while it waits for a user folder (or could not be made).</summary>
    public string? Path { get; private set; }

    public void Say(string line)
    {
        if (echo) Console.WriteLine(line);
        string stamped = $"{(DateTime.UtcNow - start).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture),8} {line}";
        lock (gate)
        {
            if (file is not null) file.WriteLine(stamped);
            else if (Path is null && waiting.Count < 10_000) waiting.Add(stamped);
        }
    }

    public void Dispose()
    {
        lock (gate) file?.Dispose();
    }
}
