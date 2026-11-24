// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Render.Classic;

namespace Vitomir.Platform.Host;

/// <summary>
/// **A game whose module keeps its own time**: <c>--game &lt;id&gt;</c> naming a game one of
/// whose modules offers a frame loop (<see cref="IModule.Loop"/>; the Classic game, <c>Games\classic.json</c>) is run by that
/// loop, not by the platform's session and its fixed ticks. A loop that plays on the program's host (the Classic module's) is
/// handed the host and its frames run by the host's frame loop: in a window, as a still (<c>--headless &lt;file.png&gt;</c>), or
/// headless, <c>--headless --frames &lt;n&gt; --frame-ms &lt;ms&gt; --size &lt;w&gt;x&lt;h&gt;</c> (60 of 33 ms at 640x480 by default):
/// the same frame on the host made with no window, its picture drawn into a target of that size and never shown
/// (<c>SdlHost.Windowless</c>; the module's own headless copy of the frame, is retired). A loop that does not play on
/// the host is run headless by the platform's <see cref="HeadlessLoop"/> (none does now). The rest of the command line is the
/// module's (the Classic module reads the old program's <c>--level</c>, <c>--player</c>, <c>--new-game</c>, <c>--seed</c>,
/// <c>--digest</c>, <c>--digest-check</c> …). The module is given its own folder, its own user folder
/// (<c>&lt;user data&gt;\Modules\&lt;id&gt;\</c>) and the 1.1 install (<c>--oneone</c>, as for the enhanced game). The enhanced game, and any
/// game whose modules offer no loop, is not touched: this answers null and the program goes on as before.
/// </summary>
public static class LoopCommand
{
    /// <summary>Frames a headless loop runs when <c>--frames</c> is not given.</summary>
    public const int DefaultFrames = 60;

    /// <summary>Each frame's time when <c>--frame-ms</c> is not given: 33 ms, the old harness's settle step.</summary>
    public const double DefaultFrameMs = 33;

    /// <summary>The module's own user folder under the program's.</summary>
    public static string UserFolderOf(string moduleId) => Path.Combine(UserData.Folder, "Modules", moduleId);

    /// <summary>The game's window settings under the program's user folder: <c>Games\&lt;id&gt;\window.json</c>.</summary>
    public static string WindowSettingsOf(string gameId) => Vitomir.Platform.Windowing.GameWindowSettings.PathFor(UserData.Folder, gameId);

    /// <summary>The program's own switches, which 1.1's command line never sees: the game, the modules' folder, the install, the log file.</summary>
    static readonly string[] Ours = ["--game", "--modules", "--oneone", "--log-file"];

    /// <summary>The program's own switches with no value: the developer switch, which opens the module's undeclared ones and changes nothing in the game; the log window, the program's.</summary>
    static readonly string[] OursBare = [DeveloperSwitch.Flag, "--log-window"];

    /// <summary>
    /// **1.1's command line, from the program's**: the program's own switches and their values taken out, and the 1.1
    /// install named as 1.1's controller names it (<c>--game &lt;folder&gt;</c>), when the program found one.
    /// </summary>
    public static string[] OneOneArgs(IReadOnlyList<string> args, string? install)
    {
        var kept = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            if (Ours.Contains(args[i])) { i++; continue; }
            if (OursBare.Contains(args[i])) continue;
            kept.Add(args[i]);
        }
        if (install is not null) kept.InsertRange(0, ["--game", install]);
        return [.. kept];
    }

    /// <summary>
    /// **A headless run's 1.1 command line**: <c>--headless</c> taken out (to 1.1's controller it names a still's picture), and
    /// the frames' count, their time and the picture's size given as this program's headless loop had them by default
    /// (<see cref="DefaultFrames"/> of <see cref="DefaultFrameMs"/> ms, 640x480) when the line does not say.
    /// </summary>
    public static string[] HeadlessArgs(IReadOnlyList<string> oneOne)
    {
        var made = oneOne.Where(a => a != "--headless").ToList();
        if (!made.Contains("--frames")) made.AddRange(["--frames", DefaultFrames.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (!made.Contains("--frame-ms")) made.AddRange(["--frame-ms", DefaultFrameMs.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (!made.Contains("--size")) made.AddRange(["--size", $"{HeadlessLoop.PictureWidth}x{HeadlessLoop.PictureHeight}"]);
        return [.. made];
    }

    /// <summary>Runs the game's module loop and answers the exit code, or null when the game asked for has none (the program goes on).</summary>
    public static int? Run(Cli cli, PlatformSettings settings, HostLog log)
    {
        if (ModulesCommand.GameChoice(cli) is not { } chosen || chosen == ModuleLoader.DefaultGame) return null;
        var game = ModuleLoader.FindGame(chosen, ModulesCommand.GameFolders);
        // A switch no module of the game declares is refused before anything else is asked, the base game's files among them: what
        // the start refuses for its words alone needs no install (the same refusal as below, from the manifests read off the disk).
        if (!cli.Flag(DeveloperSwitch.Flag))
        {
            var declared = ModuleChecker.Find(ModulesCommand.ModuleFolders(cli.Arg("--modules")), [])
                                        .Where(m => game.Modules.Contains(m.Id)).SelectMany(m => m.Switches).ToList();
            var early = DeveloperSwitch.Undeclared(declared, OneOneArgs(cli.Args, null));
            if (declared.Count > 0 && early.Count > 0)
            {
                string refusal = DeveloperSwitch.Refusal(game.Id, early);
                log.Say(refusal);
                Program.ShowError("Vitomir Platform", refusal, cli);
                return 2;
            }
        }
        // The setup, item 8: the base game's files are the imported data (--setup), or --oneone's (a developer's override), or
        // VTMR_GAME_DIR's (a developer's); with none, the start says plainly that the setup is needed and how, and ends (exit code 3).
        var data = SetupCommand.Resolve(cli, UserData.Folder);
        if (data.Refusal is { } notSetUp)
        {
            log.Say($"game {game.Id}: {notSetUp}");
            Program.ShowError("Vitomir Platform", notSetUp, cli);
            return data.Code;
        }
        log.Say($"game {game.Id}: {data.Line}");
        // The user folder held, shared, for the game's run (games may run side by side): a move of it is refused meanwhile, and a game
        // does not start while one copies it.
        using var held = Vitomir.Platform.Setup.UserFolderLock.HoldShared(UserData.Folder, out bool moving);
        if (moving)
        {
            string words = $"Your user folder ({UserData.Folder}) is being moved: the game starts once the move has ended.";
            log.Say($"game {game.Id}: not started: {words}");
            Program.ShowError("Vitomir Platform", words, cli);
            return SetupCommand.Exit.NotTheData;
        }
        using var install = GameInstall.TryOpen(data.Folder);
        // The improved films the player made and chose on the launcher's Films page (ours): this user folder's, played in place of 1.1's.
        Vitomir.Platform.Films.FilmVersions.Active = new Vitomir.Platform.Films.FilmVersions(UserData.Folder);
        // The player's modules: the game's list resolved and checked before anything runs. Errors refuse the start (exit code 7) and
        // warnings not accepted for this combination do too (8), every problem said; the modules on are handed to the game as layers.
        var (playerModules, modulesRefused, modulesWords) = SetupCommand.StartModules(game.Id, install, log.Say);
        if (modulesRefused is { } refusedCode)
        {
            Program.ShowError("Vitomir Platform", modulesWords, cli);
            return refusedCode;
        }
        var loaded = ModuleLoader.Load(new ModuleLoadOptions
        {
            ModuleFolders = ModulesCommand.ModuleFolders(cli.Arg("--modules")),
            Game = game,
            OneOne = install,
            Switches = ModulesCommand.Switches(cli),
            Say = log.Say,
        });
        if (loaded.Loop is not { } kept)
        {
            log.Say($"game {game.Id}: no module keeps its own time; it is played in the platform's world");
            return null;
        }
        var (module, loop) = kept;
        // The a comparison with 1.1's question 6: the players' switches are the ones the module's manifest declares; any
        // other (the harness's, a developer's) only with --developer, and without it the start is refused in one line.
        var undeclared = DeveloperSwitch.Undeclared(module.Manifest.Switches, OneOneArgs(cli.Args, null));
        if (undeclared.Count > 0 && !cli.Flag(DeveloperSwitch.Flag))
        {
            string refusal = DeveloperSwitch.Refusal(game.Id, undeclared);
            log.Say(refusal);
            Program.ShowError("Vitomir Platform", refusal, cli);
            loop.Dispose();
            return 2;
        }
        if (cli.Flag(DeveloperSwitch.Flag))
            log.Say($"game {game.Id}: {DeveloperSwitch.Flag}: the module's every switch is taken, its manifest's and the developer's"
                  + (undeclared.Count > 0 ? $" ({string.Join(' ', undeclared)})" : ""));
        // A game that plays in a window (the Classic module's) is played on this program's host, in a window; and so is a
        // still (`--headless <file.png>`, 1.1's command line: no window, the picture written). the pushed frame — the host
        // is handed to the module's loop at its start (LoopContext.Host) and this program's frame loop runs its frames
        // (SdlHost.Frames: the host's waits, the input gathered, each frame timed and handed in).
        bool still = cli.Arg("--headless") is { } png && png.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        // `--headless` with no picture is the same frame on the same host made with no window
        // (SdlHost.Windowless), its frames' count, time and picture's size handed to 1.1's command line; the module's own headless
        // copy of the frame is gone. A loop that does not play on the host (none now) is still the platform's HeadlessLoop's.
        bool windowless = cli.Flag("--headless") && !still;
        if (!cli.Flag("--headless") || still || loop.Windowed)
        {
            if (!loop.Windowed)
            {
                log.Say($"game {game.Id}: module {module.Manifest.Id}'s loop runs headless only (--headless --frames <n> --frame-ms <ms>)");
                loop.Dispose();
                return 2;
            }
            var oneOne = OneOneArgs(cli.Args, install?.Root);
            if (windowless) oneOne = HeadlessArgs(oneOne);
            oneOne = [.. oneOne, .. playerModules.SelectMany(folder => new[] { "--player-module", folder })];
            log.Say(windowless
                ? $"game {game.Id}: module {module.Manifest.Id} keeps its own time; headless: its frames on this program's host with no window; "
                  + $"1.1 install {install?.Root ?? "not found"}; its command line: {SecretSwitches.Line(oneOne)}"
                : $"game {game.Id}: module {module.Manifest.Id} plays in a window on this program's host; 1.1 install {install?.Root ?? "not found"}; "
                  + $"its command line: {SecretSwitches.Line(oneOne)}");                 // --password's value never written
            // The window's keys are the platform's, kept per game; a first start says so.
            var window = new Vitomir.Platform.Windowing.GameWindowSettings(WindowSettingsOf(game.Id), log.Say);
            if (windowless) { }                                                          // no window: nothing of the window's to say
            else if (!window.Kept)
                log.Say($"window: no window settings kept for the game {game.Id} yet ({window.Path}): a first start "
                      + "(borderless full screen at the desktop's own size, unless --size, --window, --fullscreen, --borderless, "
                      + "a harness run or a replay says otherwise)");
            else log.Say($"window: the game {game.Id}'s window settings from {window.Path}");
            var host = new ClassicHost(log.Say, window, windowless: windowless);
            // The module's own folders, as its headless loop has them (its user folder, its cache, its folder read only);
            // And this program's host, which its loop plays on.
            var played = new LoopContext
            {
                ModuleFolder = module.Manifest.Folder,
                UserFolder = UserFolderOf(module.Manifest.Id),
                Install = install,
                Args = oneOne,
                Switches = module.Switches,
                Say = log.Say,
                Host = host,
                Headless = windowless,
            };
            int code;
            long hostFrames = 0;
            try
            {
                loop.Start(played);
                code = host.Frames(frame => { hostFrames++; return loop.Frame(frame); }).ExitCode;
                host.Close();                                            // the GPU, the window's place, the window and SDL
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                log.Say($"game {game.Id}: module {module.Manifest.Id}'s game failed: {e.GetType().Name}: {e.Message}");
                log.Say(e.StackTrace ?? "");
                code = 1;
            }
            loop.Dispose();
            if (windowless) log.Say($"game {game.Id}: headless, {hostFrames} frames of the host run, exit code {code}");
            log.Say($"game {game.Id}: ended, exit code {code}");
            return code;
        }
        int frames = cli.Int("--frames", DefaultFrames);
        var (width, height) = cli.Size("--size") ?? (HeadlessLoop.PictureWidth, HeadlessLoop.PictureHeight);
        double ms = cli.Float("--frame-ms", (float)DefaultFrameMs);
        var context = new LoopContext
        {
            ModuleFolder = module.Manifest.Folder,
            UserFolder = UserFolderOf(module.Manifest.Id),
            Install = install,
            Args = cli.Args,
            Switches = module.Switches,
            Headless = true,
            Say = log.Say,
        };
        log.Say($"game {game.Id}: module {module.Manifest.Id} keeps its own time; {frames} frames of {ms} ms, headless, a picture of {width}x{height}; 1.1 install {install?.Root ?? "not found"}");
        HeadlessLoop.Ran ran;
        try { ran = HeadlessLoop.Run(loop, context, HeadlessLoop.Fixed(frames, ms), width, height); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Say($"game {game.Id}: module {module.Manifest.Id}'s loop failed: {e.GetType().Name}: {e.Message}");
            log.Say(e.StackTrace ?? "");
            return 1;
        }
        log.Say($"game {game.Id}: {ran.Frames} frames run, {ran.Last.Verdict}{(ran.Last.Reason.Length > 0 ? $" ({ran.Last.Reason})" : "")}");
        return ran.Last.Verdict == LoopVerdict.Quit ? ran.Last.ExitCode : 0;
    }
}
