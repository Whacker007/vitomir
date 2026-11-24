// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Diagnostics;
using System.Globalization;
using SDL;
using Vitomir.Platform.Control;
using Vitomir.Platform.Cameras;
using Vitomir.Platform.Core;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Powers;
using Vitomir.Platform.Presentation;
using Vitomir.Platform.Render;
using Vitomir.Platform.Render.Windowing;
using Vitomir.Platform.Setup;
using static SDL.SDL3;

namespace Vitomir.Platform.Host;

/// <summary>
/// <c>vitomir-platform</c>: the window, the main loop, the
/// command line. The loop steps the simulation by whole ticks of 1/120 s as the clock allows (at most a
/// quarter of a second's worth a frame, so a stall never runs away), each tick given the actions gathered
/// for it, and draws the picture between the last two ticks.
/// </summary>
/// <summary>
/// The program's entry for another build of it: <c>vitomir-platform-cli.exe</c>, the same program for a console,
/// which a console waits for and which shows the log as it goes (src/Vitomir.Platform.Cli).
/// </summary>
public static class Entry
{
    /// <summary>The runs whose errors go to the log and the exit code only, never a box on the screen: the headless ones
    /// (a still, a screen, a module's loop: <c>--headless</c> alone), the checks and dumps of tapes, and the
    /// harness's surveys, drives, arenas and scenarios. A box would wait for a click and hold a test.</summary>
    public static bool ErrorsStayInTheLog(Cli cli) =>
        cli.Flag("--headless") || cli.Arg("--headless-screen") is not null
        || cli.Arg("--replay-check") is not null || cli.Arg("--replay-dump") is not null
        || cli.Arg("--snagcheck") is not null || cli.Arg("--record-drive") is not null || cli.Arg("--arena") is not null
        || cli.Arg("--aim-survey") is not null || cli.Arg("--arc-survey") is not null || cli.Arg("--camera-survey") is not null
        || cli.Arg("--scenario") is not null || cli.Flag("--describe") || cli.Arg("--trace-event") is not null;

    public static int Run(string[] args) => Program.Run(args);
}

internal static unsafe class Program
{
    static int Main(string[] args) => Run(args);

    internal static int Run(string[] args)
    {
        // The log window's viewer (a game's log window, the same program started with --log-viewer) before anything
        // else: no log, no settings, no archive, no user folder touched.
        if (LogViewer.Asked(args)) return LogViewer.Run(args);
        var cli = new Cli(args);
        // The console first thing (Windowing.TextRuns): a run whose whole job is text the terminal it was started
        // from, the dedicated host a console window of its own, a game in a window nothing at all (its lines in the log).
        TextConsole.Open(TextRuns.WantedFor(args, platform: true));
        try
        {
            if (cli.Flag("--help") || cli.Flag("-h")) { Console.WriteLine(Cli.Usage); return 0; }
            // A start with no command line (double-clicked, the launcher not run): the readiness check, and a box sending the player to
            // the launcher when anything a start needs is not ready (BareStart). With a command line, a user folder that cannot be used
            // (vitomir.ini with a problem, its folder or drive not there, Program Files, not writable) refuses the run before anything is
            // read or written there, exit code 3: never a quiet fallback to another folder.
            if (args.Length == 0 && Bare() is { } bare) return bare;
            if (args.Length > 0 && UserFolderRefusal() is { } refusal)
            {
                Console.Error.WriteLine($"vitomir-platform: {refusal}");
                return SetupCommand.Exit.NotTheData;
            }
            // --describe/--check with --json write one JSON document to stdout, so the log's lines go to stderr.
            var stdout = Console.Out;
            if (cli.Flag("--json") && (cli.Flag("--describe") || cli.Flag("--check") || cli.Arg("--trace-event") is not null)) Console.SetOut(Console.Error);   // --trace-event --json
            try { return RunLogged(cli, args, stdout); }
            finally { Console.SetOut(stdout); }
        }
        finally
        {
            // The dedicated host's own window held open on its last line until a key; nothing for any other run.
            TextConsole.Ending();
        }
    }

    /// <summary>
    /// **A start with no command line**: null when the readiness check finds everything a start needs ready (the game goes on), else exit
    /// code 3 after the box (<see cref="BareStart"/>; Windows' message box for a game in a window, the lines printed for a console). Its
    /// lines go to a log of its own in the user folder when that folder is ready, else nowhere (nothing is written in a folder not chosen).
    /// </summary>
    static int? Bare()
    {
        var report = BareStart.Check();
        if (report.Ready) return null;
        using var log = report["userfolder"] is { Stops: false } ? new HostLog(null) : null;
        IStartDialog dialog = TextConsole.Chosen == TextConsole.Choice.Nothing ? WindowsStartDialog.ForThisMachine : new PrintedStartDialog();
        return BareStart.Run(report, dialog, BareStart.LauncherPath(AppContext.BaseDirectory), BareStart.Open, line => { if (log is null) Console.WriteLine(line); else log.Say(line); });
    }

    /// <summary>The box's words printed (a console's start with no command line): nothing waits for an answer.</summary>
    sealed class PrintedStartDialog : IStartDialog
    {
        public bool AskOpenLauncher(string title, string text, bool launcherFound)
        {
            Console.WriteLine(text);
            return false;
        }
    }

    /// <summary>Why this program's user folder cannot be used for a run with a command line, or null (<see cref="BareStart.CommandLineRefusal"/>).</summary>
    static string? UserFolderRefusal() => BareStart.CommandLineRefusal(UserData.Choice, FolderPlaces.ThisMachine());

    static int RunLogged(Cli cli, string[] args, TextWriter stdout)
    {
        using var log = new HostLog(cli.Arg("--log-file"));
        log.Say($"{PlatformVersion.Text} ({RuntimeInformationLine()})");
        if (TextConsole.Said is { } consoleSaid) log.Say(consoleSaid);
        // Streamer mode: the platform's one setting for every game and module, kept in its user data; --streamer
        // turns it on for this run (a players' switch, declared in each game's manifest).
        log.Say(Vitomir.Platform.Presentation.StreamerMode.Start(UserData.Folder, args, log.Say));
        string? moduleLogs = ModulesCommand.GameChoice(cli) is { } chosenGame && chosenGame != ModuleLoader.DefaultGame
            ? Path.Combine(LoopCommand.UserFolderOf(chosenGame), "Logs") : null;
        if (TextConsole.Lines is { } lines)
        {
            // A game in a window: what it prints, the module's lines among them, into this log; a crash's box naming it.
            lines.AlreadyLogged = () => HostLog.Saying;
            lines.Log = log.Printed;
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var error = e.ExceptionObject as Exception;
                log.Say($"error: the program stopped on an unhandled error: {error}");
                FailureBox.Show("Vitomir Platform", 1, [$"Vitomir stopped on an unhandled error: {error?.GetType().Name}: {error?.Message}"], log.LogPath, moduleLogs);
            };
        }
        // A viewer of its own: the log window, a developer's (the program's own switch, never the module's): the
        // lines of this run so far, then live.
        if (cli.Flag("--log-window")) log.Say(TextConsole.OpenLogWindow($"Vitomir log: {ModulesCommand.GameChoice(cli) ?? ModuleLoader.DefaultGame}"));
        int code = RunGame(cli, args, stdout, log);
        // A game in a window that ended with an error: no terminal to see it in, so a message box naming the logs.
        FailureBox.Show("Vitomir Platform", code, null, log.LogPath, moduleLogs);
        return code;
    }

    static int RunGame(Cli cli, string[] args, TextWriter stdout, HostLog log)
    {
        // The setup and import tool's working half (--setup), before anything of a game is read.
        if (SetupCommand.Asked(cli))
        {
            using var stop = new CancellationTokenSource();
            ConsoleCancelEventHandler stopped = (_, e) => { e.Cancel = true; stop.Cancel(); };
            Console.CancelKeyPress += stopped;
            try
            {
                int setup = SetupCommand.Run(cli, log.Say, cancel: stop.Token);
                log.Say($"setup: exit code {setup}");
                return setup;
            }
            finally { Console.CancelKeyPress -= stopped; }
        }
        // A tool's switch is vitomir-tools', refused in one line with exit code 2 before anything is read, as an
        // undeclared switch is — with --developer too, which took it and went on into the game. Under the platform's own game the two
        // it reads itself (--place, --gait) are its own (ToolSwitches.AlsoThePlatforms).
        bool moduleLoop = ModulesCommand.GameChoice(cli) is { } named && named != ModuleLoader.DefaultGame;
        if (ToolSwitches.First(args, moduleLoop) is { } tool)
        {
            log.Say(ToolSwitches.Refusal(tool));
            return 2;
        }
        var settings = PlatformSettings.Load(UserData.SettingsPath, log.Say);
        try
        {
            if (cli.Flag("--bind") || cli.Flag("--bindings")) return SdlKeys.BindCommand(args, settings, UserData.SettingsPath, log.Say);
            if (cli.Flag("--check-modules")) return ModulesCommand.Check(cli, settings, log);
            // A Classic tape (`--game classic --replay-check <tape>`) is checked by the module's own controller, not the platform's session.
            if (cli.Arg("--replay-check") is not null && ModulesCommand.GameChoice(cli) is { } loopGame && loopGame != ModuleLoader.DefaultGame
                && LoopCommand.Run(cli, settings, log) is { } loopedCheck) return loopedCheck;
            if (cli.Arg("--replay-check") is { } check) return ReplayCheck(cli, settings, log, check);
            if (cli.Arg("--replay-dump") is { } dumpTape) return ReplayDumpCommand.Run(cli, settings, log, dumpTape);
            if (cli.Arg("--snagcheck") is { } snag) return SnagCheckCommand.Run(cli, settings, log, snag);
            // ---- the scenario's --check and the game's --describe (with --json, the log goes to stderr: stdout is one JSON document)
            if (cli.Arg("--scenario") is { } scenarioToCheck && cli.Flag("--check")) return Scenario.ScenarioCheckCommand.Run(cli, settings, log, scenarioToCheck, stdout);
            if (cli.Flag("--describe")) return Scenario.ScenarioDescribe.Run(cli, settings, log, stdout);
            // ---- end
            if (cli.Arg("--arena") is { } arena) return Arena.ArenaCommand.Run(cli, settings, log, arena);
            if (cli.Arg("--scenario") is { } scenario && !cli.Flag("--check")) return Scenario.ScenarioCommand.Run(cli, settings, log, scenario);   // run a scenario (--check is the format work's)
            if (cli.Arg("--trace-event") is { } traceEvent) return Scenario.ScenarioTraceEvent.Run(traceEvent, cli.Arg("--out"), null, cli.Flag("--json"), log.Say, stdout);   // an event's chain
            if (cli.Arg("--aim-survey") is { } survey) return AimSurveyCommand.Run(cli, settings, log, survey);
            if (cli.Arg("--arc-survey") is { } arcs) return ArcSurveyCommand.Run(cli, settings, log, arcs);
            if (cli.Arg("--camera-survey") is { } cameraSurvey) return CameraSurveyCommand.Run(cli, settings, log, cameraSurvey);
            if (cli.Arg("--record-drive") is { } drive) return RecordDrive(cli, settings, log, drive);
            if (LoopCommand.Run(cli, settings, log) is { } looped) return looped;   // a game whose module keeps its own time (the Classic game)
            if (cli.Arg("--headless-screen") is { } screenName) return ScreenStill(cli, settings, log, screenName);
            if (cli.Arg("--headless") is { } png) return Still(cli, settings, log, png);
            return Play(cli, settings, log);
        }
        catch (ModuleRefusedException refused)
        {
            log.Say($"The rules module {refused.ModuleId} refused to start (fail closed):");
            foreach (var r in refused.Reasons) log.Say($"  - {r}");
            if (refused.Hint.Length > 0) log.Say(refused.Hint);
            ShowError("Vitomir Platform", $"The rules module {refused.ModuleId} refused to start (fail closed).\n\n"
                + string.Join("\n", refused.Reasons.Take(12).Select(r => "- " + r))
                + (refused.Reasons.Count > 12 ? $"\n... and {refused.Reasons.Count - 12} more (the log has them all)" : "")
                + (refused.Hint.Length > 0 ? "\n\n" + refused.Hint : ""), cli);
            return 2;
        }
        catch (ModuleCheckException check)
        {
            // The modules themselves were refused: a manifest, a dependency, an id, an override.
            log.Say("The game's modules were refused (fail closed):");
            foreach (var p in check.Problems) log.Say($"  - {p}");
            ShowError("Vitomir Platform", "The game's modules were refused (fail closed).\n\n"
                + string.Join("\n", check.Problems.Take(12).Select(p => "- " + p))
                + (check.Problems.Count > 12 ? $"\n... and {check.Problems.Count - 12} more (the log has them all)" : ""), cli);
            return 2;
        }
        catch (SessionException e)
        {
            log.Say($"error: {e.Message}");
            ShowError("Vitomir Platform", e.Message, cli);
            return 1;
        }
    }

    static string RuntimeInformationLine() =>
        $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription.Trim()}, .NET {Environment.Version}";

    internal static void ShowError(string title, string text, Cli cli)
    {
        if (Entry.ErrorsStayInTheLog(cli)) return;
        FailureBox.Mark();
        SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, title, text, null);
    }


    /// <summary>The level to play: <c>--level</c>, else the one played last.</summary>
    internal static string LevelFolder(Cli cli, PlatformSettings settings)
    {
        var level = cli.Arg("--level") ?? settings.LastLevel;
        if (string.IsNullOrWhiteSpace(level))
            throw new SessionException("No level given. Start with --level <folder of a level package>, for example --level C:\\Games\\Vitomir\\levels\\eastgate.l2");
        level = Path.GetFullPath(level.TrimEnd('/', '\\'));
        if (!File.Exists(Path.Combine(level, "level.json"))) throw new SessionException($"{level} is not a level package (it has no level.json).");
        return level;
    }

    /// <summary>
    /// The 1.1 install for the characters and the <c>oneone:</c> source: <c>--oneone</c>, else <c>--game</c> when it
    /// names a folder (as earlier, when <c>--game</c> was the install), else the one used last, else
    /// <c>VTMR_GAME_DIR</c>, else <c>C:\Games\VTMR</c>.
    /// </summary>
    internal static string? GameFolder(Cli cli, PlatformSettings settings)
    {
        foreach (var candidate in new[] { cli.Arg("--oneone"), cli.Arg("--game"), settings.GameFolder, Environment.GetEnvironmentVariable("VTMR_GAME_DIR"), @"C:\Games\VTMR" })
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        return null;
    }

    /// <summary>How the player's blow's direction is chosen — <c>--attack-direction</c>, else the setting <c>attackDirection</c> (the keys by default).</summary>
    internal static Control.AttackDirectionInput AttackDirectionOf(Cli cli, PlatformSettings settings)
    {
        if (cli.Arg("--attack-direction") is { } given)
            return Control.AttackDirectionInput.TryParse(given, out var m) ? new Control.AttackDirectionInput { Mode = m }
                : throw new SessionException($"--attack-direction is automatic, mouse or keys, not '{given}'");
        return new Control.AttackDirectionInput { Mode = Control.AttackDirectionInput.TryParse(settings.AttackDirection, out var s) ? s : Control.AttackDirectionMode.Keys };
    }

    /// <summary><c>--combat-tempo &lt;x&gt;</c>, the global combat tempo over the rules module's (0.25 to 4), or null.</summary>
    internal static float? CombatTempoOf(Cli cli)
    {
        if (cli.Arg("--combat-tempo") is not { } given) return null;
        return SessionOptions.TempoOf(given)
            ?? throw new SessionException($"--combat-tempo is a number from {Vitomir.Platform.Combat.CombatTempoOverride.Least} to {Vitomir.Platform.Combat.CombatTempoOverride.Most} (1 is 1.1's own timing), not '{given}'");
    }

    internal static SessionOptions Options(Cli cli, PlatformSettings settings, HostLog log) => new()
    {
        Level = LevelFolder(cli, settings),
        Game = GameFolder(cli, settings),
        GameList = ModulesCommand.GameChoice(cli),
        ModulesFolder = cli.Arg("--modules"),
        Switches = ModulesCommand.Switches(cli),
        Start = cli.Arg("--start") ?? "0",
        People = cli.Int("--people", 6),
        Seed = (ulong)cli.Int("--seed", 1),
        Facing = settings.ThirdPersonFacing,
        AlwaysRun = settings.AlwaysRun,
        // The legs' turn (the setting legTurn, --no-leg-turn), speed by direction (--no-direction-pace),
        // double tap to dodge (the settings doubleTapDodge, off by default, and doubleTapSeconds).
        LegTurn = settings.LegTurn && !cli.Flag("--no-leg-turn"),
        PersonTurn = settings.PersonTurnFor(cli),                           // a person's turn as a chain (--turn-…, --turn-chain-off)
        CrouchKneel = settings.CrouchKneel || cli.Flag("--crouch-kneel"),   // the kneel (off by default)
        PersonFeet = settings.PersonFeetFor(cli),                           // planted feet, the heel lift, the bend rule (--feet-off …)
        PersonGround = settings.PersonGroundFor(cli),                       // stairs and slopes (--stair-line-off, --slope-clips-off …)
        StairsDrawLastTick = settings.StairsDrawLastTick || cli.Flag("--stairs-draw-last-tick"),   // the body drawn between ticks as before
        CameraHardWalls = settings.CameraHardWalls || cli.Flag("--camera-hard-walls"),           // the third-person camera at walls as before
        SkirtSplit = settings.SkirtSplit || cli.Flag("--skirt-split"),                   // a skirt hung from the thighs as before
        PersonSwing = settings.PersonSwingFor(cli),                                   // the swing bones' simulator (--swing-off, --swing-near, --swing-far)
        PersonStarts = settings.PersonStartsFor(cli),                                 // the starts and stops (--start-clips-off, --crowd-start-clips-off, --stop-nearest)
        PersonAir = settings.PersonAirFor(cli),                                       // the jumps, the fall and the landings (--air-clips-off, --jump-at-once)
        PersonOver = settings.PersonOverFor(cli),                                     // over and onto things, off edges (--over-clips-off)
        PersonLedge = settings.PersonLedgeFor(cli),                                   // the ledge on clips (--ledge-clips-off)
        PersonPrelanding = settings.PersonPrelandingFor(cli),                         // the prelanding (--prelanding-off)
        PersonHands = settings.PersonHandsFor(cli),                                   // the hands on the real top (--hands-on-tops-off); the run's own vault (--auto-vault-off) in PersonOver
        PersonCommit = settings.PersonCommitFor(cli),                                 // the commit point in the air (--commit-off)
        PersonMoves = settings.PersonMovesFor(cli),                                   // tier 1's moves (--moves-off)
        PersonHang = settings.PersonHangFor(cli),                                     // the hang's corners and leaps (--hang-moves-off)
        PersonWall = settings.PersonWallFor(cli),                                     // the walls (--walls-off); --walls-no-flow
        PersonStealth = settings.PersonStealthFor(cli),                               // squeezing, stealth, cover, ducking (--stealth-off)
        PersonGaits = settings.PersonGaitsFor(cli),                                   // the player's own gaits (--player-gaits-off)
        PersonPresent = settings.PersonPresentFor(cli),                               // the lean, the look, the chain led by the way (presentation)
        PersonSprintFrom = settings.SprintFromFor(cli),                     // the people's sprint clip (--sprint-from, --sprint-clip-off)
        PersonCrouchHeight = settings.PersonCrouchHeightFor(cli),           // the new bodies' crouched capsule (--person-crouch-height)
        DirectionPace = !cli.Flag("--no-direction-pace"),
        CombatTempo = CombatTempoOf(cli),   // --combat-tempo
        // The third-person blow's pitch from the view's (the default), or from the reticle's point (the old way).
        ThirdPersonAim = new Control.ThirdPersonAimSettings { FromReticle = settings.ThirdPersonAimFromReticle || cli.Flag("--aim-from-reticle") },
        AttackDirection = AttackDirectionOf(cli, settings),   // the setting attackDirection, --attack-direction
        DoubleTapDodge = settings.DoubleTapDodge ? PlatformSettings.TapWindow(settings.DoubleTapSeconds) : 0f,
        BodyProfile = cli.Arg("--profile"),
        Place = cli.Arg("--place"),
        AutoMasquerade = settings.MasqueradeAutoSwitch,
        RecoverSeconds = !settings.PlayerRecovers || cli.Flag("--no-recovery") ? 0f
            : settings.PlayerRecoverSeconds is >= 0.5f and <= 600f ? settings.PlayerRecoverSeconds : SessionOptions.DefaultRecoverSeconds,
        TargetTest = cli.Arg("--target-test"),
        Witnesses = Witnesses(cli),
        Foes = Foes(cli),
        Night = Night(cli),
        Weapon = cli.Arg("--weapon"),
        PlayAs = cli.Arg("--play-as"),          // a developer's option for pictures
        StitchedAsQuick = StitchedAsQuick(cli), // the stitched blows played as the quick ones
        // The earlier build's camera to compare, else the player's two camera settings (null when the defaults).
        Cameras = cli.Flag("--camera-rigid") ? CameraSettings.Rigid : CameraSettings.Of(settings.FirstPersonActionSway, settings.ThirdPersonFollow, settings.ThirdPersonMeleeFraming || cli.Flag("--melee-framing")),
        NpcActions = cli.Arg("--npc-actions"),
        Sheets = Sheets(cli),
        Learn = Learn(cli),
        Armor = Armor(cli),
        CombatTrace = cli.Arg("--combat-trace"),
        EventTrace = cli.Arg("--event-trace"),
        Protected = cli.Arg("--protected"),
        PlayerBow = cli.Flag("--no-bow") ? null : SessionOptions.DefaultPlayerBow,         // the sandbox's player carries a bow (F swaps)
        PlayerArrows = Math.Clamp(cli.Int("--bow-arrows", SessionOptions.DefaultPlayerArrows), 0, 1000),
        PlayerBowAim = new Control.PlayerBowSettings                                                      // the player's bow's rules
        {
            NoAim = cli.Flag("--" + Control.PlayerBowSettings.NoAimSwitch),
            Solve = settings.BowAimSolve || cli.Flag("--" + Control.PlayerBowSettings.SolveOnSwitch),   // off by default
        },
        Body = BodyOf(cli, settings),                                                     // --body, the setting playerBody
        StartStopHalfLife = StartStopHalfLifeOf(cli),                                     // --start-stop-half-life, the knob
        ModelsFolder = PeopleBodies.FindFolder(cli.Arg("--models"), settings.ModelsFolder),   // --models, the setting modelsFolder, ...
        TownsfolkBody = TownsfolkBodyOf(cli, settings),                                   // the setting townsfolkBodies, --townsfolk-oneone
        CrowdBuilt = TownsfolkBodies.ChooseBuilt(cli.Flag("--crowd-built"), settings.TownsfolkBodies),   // The earlier build's run-time crowd
        Say = log.Say,
    };

    // ---- ----
    /// <summary>
    /// The player's body — <c>--body models|woman|oneone</c>, else <c>oneone</c> with <c>--play-as</c> or
    /// for a tool measuring the player's blows (<paramref name="combat"/>: the surveys), else the setting <c>playerBody</c> (models by
    /// default). A word that is no body stops the start (the setting's is said and taken as models).
    /// </summary>
    /// <summary>
    /// A development control, to judge the feel: <c>--start-stop-half-life &lt;s&gt;</c>, the player's half-life for the run
    /// (0 to 1 s; 0 the ramp), or null when not given. Anything else stops the start.
    /// </summary>
    internal static float? StartStopHalfLifeOf(Cli cli)
    {
        if (cli.Arg("--start-stop-half-life") is not { } given) return null;
        return float.TryParse(given, NumberStyles.Float, CultureInfo.InvariantCulture, out float h) && h >= 0f && h <= Bodies.StartStopSprings.LongestOverride
            ? h : throw new SessionException($"--start-stop-half-life is seconds from 0 (the ramp) to {Bodies.StartStopSprings.LongestOverride}, not '{given}'");
    }

    /// <summary>
    /// The townsfolk's bodies — 1.1's (null) with <c>--townsfolk-oneone</c> or the setting <c>townsfolkBodies</c> <c>oneone</c>,
    /// else the models repository's crowd (<c>models</c>, the default; any other word in the setting is taken as models).
    /// </summary>
    internal static string? TownsfolkBodyOf(Cli cli, PlatformSettings settings) =>
        TownsfolkBodies.Choose(cli.Flag("--townsfolk-oneone"), settings.TownsfolkBodies);   // the literal flag, so the docs' check finds it

    internal static string BodyOf(Cli cli, PlatformSettings settings, bool combat = false) =>
        PeopleBodies.Choose(cli.Arg("--body"), playAs: cli.Arg("--play-as") is not null, combat, settings.PlayerBody);
    // ---- end

    /// <summary>Every <c>--sheet who=file</c> given, in order; a malformed one stops the start.</summary>
    static SheetRequest[] Sheets(Cli cli)
    {
        var list = new List<SheetRequest>();
        for (int i = 0; i + 1 < cli.Args.Length; i++)
            if (cli.Args[i] == "--sheet")
            {
                if (!SheetRequest.TryParse(cli.Args[i + 1], out var r, out string error)) throw new SessionException(error);
                list.Add(r);
            }
        return [.. list];
    }

    /// <summary>Every <c>--armor who=piece[,piece]</c> given, in order; a malformed one stops the start.</summary>
    static ArmorRequest[] Armor(Cli cli)
    {
        var list = new List<ArmorRequest>();
        for (int i = 0; i + 1 < cli.Args.Length; i++)
            if (cli.Args[i] == "--armor")
            {
                if (!ArmorRequest.TryParse(cli.Args[i + 1], out var r, out string error)) throw new SessionException(error);
                list.Add(r);
            }
        return [.. list];
    }

    /// <summary>Every <c>--learn [who@]power=rank[:options]</c> given, in order; a malformed one stops the start.</summary>
    static LearnRequest[] Learn(Cli cli)
    {
        var list = new List<LearnRequest>();
        for (int i = 0; i + 1 < cli.Args.Length; i++)
            if (cli.Args[i] == "--learn")
            {
                if (!LearnRequest.TryParse(cli.Args[i + 1], out var r, out string error)) throw new SessionException(error);
                list.Add(r);
            }
        return [.. list];
    }

    /// <summary>Every <c>--foe</c> given, in order.</summary>
    static string[] Foes(Cli cli)
    {
        var list = new List<string>();
        for (int i = 0; i + 1 < cli.Args.Length; i++)
            if (cli.Args[i] == "--foe") list.Add(cli.Args[i + 1]);
        return [.. list];
    }

    /// <summary>Every <c>--witness</c> given, in order.</summary>
    static string[] Witnesses(Cli cli)
    {
        var list = new List<string>();
        for (int i = 0; i + 1 < cli.Args.Length; i++)
            if (cli.Args[i] == "--witness") list.Add(cli.Args[i + 1]);
        return [.. list];
    }

    /// <summary><c>--stitched-as-quick overhead|thrust|both</c> (null when not given).</summary>
    internal static string? StitchedAsQuick(Cli cli) => cli.Arg("--stitched-as-quick") switch
    {
        null => null,
        "overhead" or "thrust" or "both" => cli.Arg("--stitched-as-quick"),
        var s => throw new SessionException($"--stitched-as-quick is overhead, thrust or both, not '{s}'"),
    };

    /// <summary>
    /// The view play starts in — <c>--view first|right|left</c>, else the setting <c>lastView</c> — as
    /// <see cref="SessionOptions.StartView"/> (null for the right shoulder, so a tape's head is as before). A setting that names no view
    /// is said once and taken as the right shoulder.
    /// </summary>
    static string? StartView(Cli cli, PlatformSettings settings, HostLog log)
    {
        string view = cli.Arg("--view") ?? settings.LastView;
        if (view is not ("first" or "right" or "left"))
        {
            if (cli.Arg("--view") is not null) throw new SessionException($"--view is first, right or left, not '{view}'");
            log.Say($"settings: lastView '{view}' is no view (first, right or left): over the right shoulder");
            return null;
        }
        return view == "right" ? null : view;
    }

    /// <summary><c>--time day|night</c> (night when not given).</summary>
    static bool Night(Cli cli) => cli.Arg("--time") switch
    {
        null or "night" => true,
        "day" => false,
        var t => throw new SessionException($"--time is day or night, not '{t}'"),
    };

    // ---- playing in a window ----

    static int Play(Cli cli, PlatformSettings settings, HostLog log)
    {
        Tape? replay = cli.Arg("--replay") is { } replayPath ? Tape.Read(replayPath) : null;
        var options = Options(cli, settings, log);
        if (replay is not null) options = options.From(replay.Header);
        else options = options with { StartView = StartView(cli, settings, log) };   // the view he left the last session in
        options = options with { DecodePeopleImages = true,   // a window draws the people's maps (headless runs do not decode them)
                                 PeopleTextureLimit = settings.CharacterTextureLimitFor(cli.Arg("--character-texture-limit")) };   // kept at the renderer's cap
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO)) throw new SessionException($"SDL could not start its video: {SDL_GetError()}");
        SDL_Window* window = null;
        PlatformRenderer? renderer = null;
        Session? session = null;
        try
        {
            session = Session.Create(options);
            if (replay is not null) foreach (var d in session.Modules.Differences(replay.Header.Modules, out _)) log.Say($"replay: {d}");
            window = OpenWindow(cli, settings, log);
            var picture = PictureSize(cli, settings);
            renderer = PlatformRenderer.Create((nint)window, new RendererOptions(settings.Driver, settings.Quality, settings.VSync, picture.Item1, picture.Item2, Session.LookFor(options.Night),
                                                                                 settings.CharacterTextureLimitFor(cli.Arg("--character-texture-limit")),
                                                                                 settings.PeopleSkinningFor(cli.Flag("--skin-every-frame"))));
            log.Say($"renderer: {renderer.Driver}, picture {renderer.PixelSize.Width}x{renderer.PixelSize.Height}");
            renderer.CharacterTextures = session.Texture;
            renderer.CursorArt = session.CursorArt;
            renderer.LoadLevel(options.Level);
            settings.LastLevel = options.Level;
            if (options.Game is not null) settings.GameFolder = options.Game;
            if (options.ModelsFolder is not null) settings.ModelsFolder = options.ModelsFolder;   // the models folder used, kept as the install's is

            var input = new InputCollector(settings, log.Say);
            session.Orbit.Settings = settings.OrbitCameraFor();   // the orbit camera's numbers (the settings' orbit*)
            var recording = cli.Arg("--record") is not null ? new Tape { Header = session.TapeHeader(PlatformVersion.Text) } : null;
            // The pause menu and the Controls page. The view's two settings are the simulation's own, recorded in
            // a tape's head: they change at once when nothing is recorded or replayed, else from the next start.
            bool taped = recording is not null || replay is not null;
            var menus = new Menus(settings, log.Say)
            {
                Table = input.Table,
                // The camera's three and the attack direction are the simulation's too (a tape's head carries them):
                // they change from the next start.
                ViewNote = taped
                    ? "While a recording is made or played, always run, the facing and double tap change from the next start. The action sway, the follow and melee framing always do. The attack direction changes at once, from the next blow (a recording keeps the change)."
                    : "The action sway, the follow and melee framing change from the next start. The attack direction changes at once, from the next blow.",
            };
            // The attack direction set on the Controls page takes effect from the next tick (a play-test set Keys mid-run
            // and kept getting blows from the right: an earlier build read the setting once at the start). The change rides on the next live
            // frame (ActionFrame.AttackMode), so a recording carries it at its tick; during a replay it waits for the tape's end.
            Control.AttackDirectionMode? attackModeChange = null;
            // The stand-in HUD (the quick slots, Health and Blood, the masquerade's mode), shown over play unless the
            // setting showHud is off or --hide-hud hides it for this run; the action hud toggles it.
            var hud = new Interface.Hud(new Interface.ScreenLoader(log.Say));
            hud.View.SetKeys(input.Table, SdlKeys.Name);
            bool showHud = settings.ShowHud && !cli.Flag("--hide-hud");
            menus.Accepted = values =>
            {
                Menus.Store(values, settings);
                settings.Save(UserData.SettingsPath);
                menus.Table = input.Apply(settings);
                hud.View.SetKeys(input.Table, SdlKeys.Name);     // the slots show their keys as bound now
                if (!taped) session.SetControlOptions(settings.ThirdPersonFacing, settings.AlwaysRun, settings.DoubleTapDodge ? PlatformSettings.TapWindow(settings.DoubleTapSeconds) : 0f);
                attackModeChange = Control.AttackDirectionInput.ChangeTo(session.AttackDirectionNow, settings.AttackDirection);   // from the next tick
                View(session, settings);    // presentation only, so at once whether recording or not
                log.Say($"controls: saved to {UserData.SettingsPath}; {ViewLine(session)}");
            };
            // The reach highlight: the setting, or --show-reach for this run; the action reach toggles it.
            session.ShowReach = settings.ShowReach || cli.Flag("--show-reach");
            session.ShowAwareness = cli.Flag("--show-awareness");     // the developer's awareness overlay
            session.ShowHitVolumes = cli.Flag("--show-hit-volumes");  // the developer's hit volumes overlay
            session.ShowSwingTrail = cli.Flag("--show-swing");         // the developer's swing trail
            View(session, settings);
            if (renderer.PixelSize is (> 0, > 0) px) session.PictureAspect = px.Item1 / (float)px.Item2;     // for the start line's field across
            // The walk read back from the log wants these two at the start.
            log.Say($"pace: always run {(settings.AlwaysRun ? "on" : "off")}{(taped ? " (a recording keeps its own)" : "")}; reach: the highlight {(session.ShowReach ? "shown" : "hidden")}");
            log.Say(ViewLine(session));
            int replayAt = 0;
            var view = new FrameView();
            var cap = FrameCapFor(cli, settings, log, window);   // the frame cap (presentation only), or null
            var clock = Stopwatch.StartNew();
            double last = 0, accumulator = 0;
            double fpsWindow = 0; int fpsFrames = 0; double fps = 0;
            bool captured = false, cursorHidden = false;
            while (true)
            {
                WindowKeeper.Tick();
                double now = clock.Elapsed.TotalSeconds;
                double frame = Math.Min(now - last, 0.25);
                last = now;
                accumulator += frame;
                fpsWindow += frame; fpsFrames++;
                if (fpsWindow >= 0.5) { fps = fpsFrames / fpsWindow; fpsWindow = 0; fpsFrames = 0; }

                input.BeginFrame();
                int ww = 0, wh = 0;
                SDL_GetWindowSize(window, &ww, &wh);
                // The pointer's place is taken on the picture (fitted in the window), with the picture's shape; and
                // while the pointer is free the click's key works the click, not attack.
                {
                    float density = SDL_GetWindowPixelDensity(window) is var d && d > 0f ? d : 1f;
                    var r = renderer.PictureRect;
                    var (ppw, pph) = renderer.PixelSize;
                    input.SetPicture(r.X / density, r.Y / density, r.Width / density, r.Height / density, pph > 0 ? ppw / (float)pph : 0f);
                    if (pph > 0) session.PictureAspect = ppw / (float)pph;    // a field across keeps its width on this picture
                    input.PointerFree = session.PointerFree;
                    input.OrbitAllowed = !menus.Open && session.ThirdPerson && !session.PointerFree;   // the orbit camera, third person only
                }
                SDL_Event e;
                bool wasOpen = menus.Open;
                while (SDL_PollEvent(&e))
                {
                    if (!menus.Open || IsWindowEvent(e)) input.Take(e, captured, ww, wh);
                    else menus.Take(e, (x, y) => ToPicture(window, renderer, x, y));
                }
                if (input.QuitAsked) break;
                {
                    // The camera to watch from — its key held swings it with the mouse, the wheel zooms, a double tap puts it back.
                    var (orbitLook, wheel) = input.TakeOrbit();
                    session.Orbit.Step(input.OrbitAllowed, input.OrbitHeld, input.Pressed(Cameras.OrbitCamera.Action), orbitLook, wheel, session.ViewPitch, now, (float)frame);
                }
                if (!menus.Open && input.Pressed("hitVolumes"))
                {
                    session.ShowHitVolumes = !session.ShowHitVolumes;
                    log.Say($"hit volumes: the overlay {(session.ShowHitVolumes ? "shown" : "hidden")}");
                }
                if (!menus.Open && input.Pressed("swingTrail"))
                {
                    session.ShowSwingTrail = !session.ShowSwingTrail;
                    log.Say($"swing trail: the overlay {(session.ShowSwingTrail ? "shown" : "hidden")}");
                }
                if (!menus.Open && input.Pressed("swingArc"))     // the swing's arc, kept in the settings
                {
                    session.ShowSwingArc = settings.ShowSwingArc = !session.ShowSwingArc;
                    log.Say($"swing arc: {(session.ShowSwingArc ? "shown" : "hidden")} at {session.SwingArcOpacity * 100f:0} %");
                }
                if (!menus.Open && input.Pressed("showImpact"))   // the bow's point of impact (a developer's aid, kept in the settings)
                {
                    session.ShowBowImpact = settings.ShowBowImpact = !session.ShowBowImpact;
                    log.Say($"bow: the point of impact {(session.ShowBowImpact ? "shown" : "hidden")}");
                }
                if (!menus.Open && input.Pressed("showPath"))     // the bow's path (a developer's aid, kept in the settings)
                {
                    session.ShowBowPath = settings.ShowBowPath = !session.ShowBowPath;
                    log.Say($"bow: the arrow's path {(session.ShowBowPath ? "shown" : "hidden")}");
                }
                if (!menus.Open && input.Pressed("combatLog"))    // the combat log's window, kept in the settings
                {
                    session.ShowCombatLog = settings.ShowCombatLog = !session.ShowCombatLog;
                    log.Say($"combat log: the window {(session.ShowCombatLog ? "shown" : "hidden")}");
                }
                if (!menus.Open && input.Pressed("awareness"))
                {
                    session.ShowAwareness = !session.ShowAwareness;
                    log.Say($"perception: the awareness overlay {(session.ShowAwareness ? "shown" : "hidden")}");
                }
                if (!menus.Open && input.Pressed("hud"))
                {
                    showHud = settings.ShowHud = !showHud;
                    log.Say($"hud: {(showHud ? "shown" : "hidden")}");
                }
                if (!menus.Open && input.Pressed("reach"))
                {
                    session.ShowReach = settings.ShowReach = !session.ShowReach;
                    log.Say($"reach: the highlight {(session.ShowReach ? "shown" : "hidden")}");
                }
                if (!menus.Open && input.Pressed("menu"))
                {
                    menus.OpenPause();
                    input.ReleaseAll();
                }
                var request = menus.Update();
                if (request == MenuRequest.Quit) break;
                if (request == MenuRequest.Resume) { menus.Close(); input.ReleaseAll(); }
                // The simulation waits while a menu is open: no ticks, so a recording has none for that time.
                if (menus.Open) accumulator = 0;
                if (menus.Open != wasOpen && menus.Open)
                {
                    SDL_SetWindowRelativeMouseMode(window, false);
                    captured = false;
                    SDL_ShowCursor();
                    var r = renderer.PictureRect;
                    float density = SDL_GetWindowPixelDensity(window) is var d && d > 0f ? d : 1f;
                    SDL_WarpMouseInWindow(window, (r.X + r.Width * 0.5f) / density, (r.Y + r.Height * 0.5f) / density);
                }

                while (accumulator >= World.TickSeconds)
                {
                    ActionFrame actions;
                    if (replay is not null)
                    {
                        if (replayAt >= replay.Frames.Count) { log.Say($"replay: the tape's {replay.Frames.Count} ticks are played"); replay = null; actions = ActionFrame.Idle; }
                        else actions = replay.Frames[replayAt++];
                    }
                    else
                    {
                        actions = input.NextTick();
                        // The pointer's place on the picture into the simulation's pick field (first person's own field of view).
                        actions = actions with { Pointer = FirstPersonView.ToPick(actions.Pointer, session.PickScale) };
                        if (attackModeChange is { } changed) { actions = actions with { AttackMode = changed }; attackModeChange = null; }
                    }
                    session.Step(actions);
                    if (recording is not null)
                    {
                        recording.Frames.Add(actions);
                        if (session.Tick % Tape.DigestEvery == 0) recording.Digests[session.Tick] = session.Digest();
                    }
                    accumulator -= World.TickSeconds;
                }

                // The pointer is captured while the mouse steers the view, and given back when it is freed or the window loses focus.
                bool wantCaptured = !menus.Open && !session.PointerFree && (SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
                if (wantCaptured != captured)
                {
                    SDL_SetWindowRelativeMouseMode(window, wantCaptured);
                    captured = wantCaptured;
                    // A pointer freed starts where the reticle was, the picture's centre.
                    if (!captured && !menus.Open && session.PointerFree)
                    {
                        var r = renderer.PictureRect;
                        float density = SDL_GetWindowPixelDensity(window) is var d && d > 0f ? d : 1f;
                        SDL_WarpMouseInWindow(window, (r.X + r.Width * 0.5f) / density, (r.Y + r.Height * 0.5f) / density);
                        input.CentrePointer();
                    }
                }
                // The freed pointer is 1.1's cursor drawn in the picture, so the system's is hidden over the window then.
                bool hideCursor = !menus.Open && !captured && session.PointerFree && (SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
                if (hideCursor != cursorHidden)
                {
                    if (hideCursor) SDL_HideCursor(); else SDL_ShowCursor();
                    cursorHidden = hideCursor;
                }

                // With a frame cap, wait for the picture's time before it is drawn, and draw it as of then: the time waited goes into the
                // accumulator now (its ticks are stepped at the next frame, as any frame's are), so the simulation keeps its pace.
                if (cap is not null)
                {
                    cap.Wait();
                    double waited = clock.Elapsed.TotalSeconds;
                    accumulator += waited - last;
                    last = waited;
                }
                session.Fill(view, (float)(accumulator / World.TickSeconds));
                view.StatusLine = $"{PlatformVersion.Text}   {fps:0} fps   {session.Status}";
                // The cursor is drawn where the mouse is now (the verb is the last tick's), at the interface's scale.
                view.InterfaceScale = settings.InterfaceScale;
                if (view.Cursor is { } cursor) view.Cursor = cursor with { Place = input.Pointer };
                // A menu covers the picture's own lines (the sandbox's banner is in the menu's veil, under its panel).
                if (menus.Open) { view.ShowReticle = false; view.Banner = null; view.StatusLine = null; view.Cursor = null; }
                var (pw, ph) = renderer.PixelSize;
                // The HUD over play, never over the pause menu or a page (the menu's list is drawn instead).
                renderer.Draw(view, menus.Open ? menus.Draw(pw, ph) : showHud ? HudFor(session, hud, pw, ph, settings.InterfaceScale) : null);

                if (input.Pressed("screenshot"))
                {
                    var shot = Path.Combine(UserData.Ensure(UserData.Screenshots), $"platform-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.png");
                    renderer.SaveScreenshot(shot);
                    log.Say($"screenshot: {shot}");
                }
            }
            if (recording is not null && cli.Arg("--record") is { } path)
            {
                recording.Digests[session.Tick] = session.Digest();
                recording.Write(path);
                log.Say($"record: {recording.Frames.Count} ticks to {Path.GetFullPath(path)}");
            }
            if (replay is null && session.PlayerView is { } leftIn) settings.LastView = leftIn;   // the next session starts there
            settings.Save(UserData.SettingsPath);
            return 0;
        }
        finally
        {
            renderer?.Dispose();
            session?.Dispose();
            if (window != null) SDL_DestroyWindow(window);
            SDL_Quit();
        }
    }

    /// <summary>
    /// The run's frame cap (<see cref="PlatformSettings.FrameCapFor"/>) as an even fraction of the refresh of the display the window is on
    /// (SDL's mode of it; unknown: the number as given), said once; null when there is none.
    /// </summary>
    static FrameCap? FrameCapFor(Cli cli, PlatformSettings settings, HostLog log, SDL_Window* window)
    {
        float asked = settings.FrameCapFor(cli);
        if (asked <= 0f) return null;
        float refresh = 0f;
        var mode = SDL_GetCurrentDisplayMode(SDL_GetDisplayForWindow(window));
        if (mode != null) refresh = mode->refresh_rate;
        double fps = FrameCap.Effective(asked, refresh);
        log.Say(string.Create(CultureInfo.InvariantCulture,
            $"frame cap: {fps:0.##} pictures a second ({asked:0.##} asked; {(refresh > 0f ? $"the display at {refresh:0.##} Hz" : "the display's refresh not known")}; vsync {(settings.VSync ? "on" : "off")}; the simulation's tick untouched)"));
        return new FrameCap(fps);
    }

    /// <summary>Events that are the window's and not the player's input: closing, and the focus going.</summary>
    static bool IsWindowEvent(in SDL_Event e) => (SDL_EventType)e.type is SDL_EventType.SDL_EVENT_QUIT
        or SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED or SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST;

    /// <summary>A point in the window's coordinates as a point in the picture's pixels (the picture fitted in the window, centred).</summary>
    static (float X, float Y) ToPicture(SDL_Window* window, PlatformRenderer renderer, float x, float y)
    {
        float density = SDL_GetWindowPixelDensity(window) is var d && d > 0f ? d : 1f;
        var r = renderer.PictureRect;
        var (pw, ph) = renderer.PixelSize;
        if (r.Width <= 0 || r.Height <= 0) return (x, y);
        return ((x * density - r.X) * pw / r.Width, (y * density - r.Y) * ph / r.Height);
    }

    /// <summary>
    /// The window: <c>centered</c>, the default — no title bar, the
    /// settings' size (2560x1440) at the centre of the primary display's whole bounds, above the task bar while
    /// it has the focus (<see cref="WindowKeeper"/>); <c>borderless</c> over the whole primary display (the
    /// picture fitted inside it, centred); or <c>windowed</c>, with a title bar. <c>--centered WxH</c> or
    /// <c>--windowed WxH</c> chooses for one run (<see cref="WindowChoice"/>). Shown before the renderer takes it.
    /// </summary>
    static SDL_Window* OpenWindow(Cli cli, PlatformSettings settings, HostLog log)
    {
        var (mode, (ww, wh)) = WindowChoice(cli, settings, log);
        SDL_Window* w;
        if (mode == WindowMode.Centered) w = WindowKeeper.OpenCentred(PlatformVersion.Text, ww, wh, log.Say);
        else if (mode == WindowMode.Borderless)
        {
            var display = SDL_GetPrimaryDisplay();
            SDL_Rect bounds;
            if (!SDL_GetDisplayBounds(display, &bounds)) bounds = new SDL_Rect { x = 0, y = 0, w = 1920, h = 1080 };
            w = SDL_CreateWindow(PlatformVersion.Text, bounds.w, bounds.h, SDL_WindowFlags.SDL_WINDOW_BORDERLESS);
            if (w != null)
            {
                SDL_SetWindowPosition(w, bounds.x, bounds.y);
                WindowKeeper.Keep(w, log.Say);
            }
            log.Say($"window: borderless over the display, {bounds.w}x{bounds.h} at {bounds.x},{bounds.y}");
        }
        else
        {
            w = SDL_CreateWindow(PlatformVersion.Text, ww, wh, SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
            log.Say($"window: {ww}x{wh}, with a title bar");
        }
        if (w == null) throw new SessionException($"The window could not be made: {SDL_GetError()}");
        Vitomir.Platform.Render.Windowing.WindowIcon.Set(w);
        SDL_ShowWindow(w);
        SDL_RaiseWindow(w);
        SDL_SyncWindow(w);
        return w;
    }

    /// <summary>The window's mode and size for this run: <c>--centered WxH</c>, else <c>--windowed WxH</c>, else the settings'.</summary>
    internal static (WindowMode Mode, (int W, int H) Size) WindowChoice(Cli cli, PlatformSettings settings, HostLog? log = null)
    {
        foreach (var flag in new[] { "--centered", "--windowed" })
            if (cli.Arg(flag) is { } given && cli.Size(flag) is null) log?.Say($"window: {flag} {given} is not a size (WxH); passed over");
        if (cli.Size("--centered") is { } centred) return (WindowMode.Centered, centred);
        if (cli.Size("--windowed") is { } windowed) return (WindowMode.Windowed, windowed);
        return (settings.Window, (settings.WindowWidth, settings.WindowHeight));
    }

    /// <summary>
    /// The size the world is drawn at: <c>--picture</c>; else a centred window's own size; else the settings'
    /// picture; (0, 0) for the window's own.
    /// </summary>
    internal static (int W, int H) PictureSize(Cli cli, PlatformSettings settings) =>
        cli.Size("--picture") ?? (WindowChoice(cli, settings).Mode == WindowMode.Centered ? (0, 0)
            : settings.PictureWidth > 0 && settings.PictureHeight > 0 ? (settings.PictureWidth, settings.PictureHeight) : (0, 0));

    // ---- a still, headless ----

    /// <summary><c>--orbit yaw,pitch[,boom][@s]</c> read: the swing and tilt (radians), the boom (m; NaN the default's), the tick it is put on.</summary>
    static (float Yaw, float Pitch, float Boom, int At)? OrbitArg(string text)
    {
        var at = text.Split('@');
        var parts = at[0].Split(',');
        static float Num(string[] p, int i, float none) =>
            i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : none;
        float when = at.Length > 1 && float.TryParse(at[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float w) ? w : 0f;
        return (Num(parts, 0, 0f) * DetMath.DegToRad, Num(parts, 1, 0f) * DetMath.DegToRad, Num(parts, 2, float.NaN), (int)MathF.Round(when * World.TicksPerSecond));
    }

    static int Still(Cli cli, PlatformSettings settings, HostLog log, string png)
    {
        var options = Options(cli, settings, log) with { DecodePeopleImages = true,   // a still draws the people, so their maps are decoded
                                                         PeopleTextureLimit = settings.CharacterTextureLimitFor(cli.Arg("--character-texture-limit")) };   // kept at the renderer's cap
        var size = cli.Size("--picture") ?? (1280, 720);
        var making = Stopwatch.StartNew();   // what the people's textures cost, said at the end
        using var session = Session.Create(options);
        double madeSeconds = making.Elapsed.TotalSeconds;
        int ticks = (int)MathF.Round(cli.Float("--seconds", 1f) * World.TicksPerSecond);
        bool drive = cli.Flag("--drive"), first = cli.Flag("--first-person"), leftShoulder = cli.Flag("--left-shoulder");
        var lookAt = Point(cli.Arg("--look-at"));
        var walkTo = Point(cli.Arg("--walk-to")?.Split('@')[0]);
        // --walk-to x,y@s[,sprint]: the walk begins at that second (before it, the player stands), at a sprint if asked.
        var walkWhen = cli.Arg("--walk-to") is { } wt && wt.Contains('@') ? wt[(wt.IndexOf('@') + 1)..].Split(',') : [];
        int walkFrom = walkWhen.Length > 0 && float.TryParse(walkWhen[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float wf) ? (int)MathF.Round(wf * World.TicksPerSecond) : 0;
        bool walkSprint = walkWhen.Length > 1 && walkWhen[1] == "sprint";
        float aside = session.PlacedAside;     // --place: the session put the player there before the first tick
        var traverse = Traversing.Of(cli, aside).WithFighting(cli).WithDodges(cli);    // --attack-at, --block-at; --dodge-at
        // --arc-freeze <s>: the swing's arc predicted on that tick and kept (with --show-swing, against the blow swung).
        int arcFreeze = cli.Arg("--arc-freeze") is not null ? (int)MathF.Round(cli.Float("--arc-freeze", 0f) * World.TicksPerSecond) : -1;
        View(session, settings, cli);
        session.PictureAspect = size.Item1 / (float)size.Item2;    // the field across, on this picture
        // --orbit yaw,pitch[,boom][@s]: the orbit camera (Right Alt's) swung round the player by so many degrees (its look turned left positive, as
        // the mouse turns: -90 puts it on his left side) and tilted (up positive), at that boom (m), from that second (default the start), kept as he turns.
        var orbitPut = cli.Arg("--orbit") is { } op ? OrbitArg(op) : null;
        log.Say(ViewLine(session));
        // --look-pitch degrees (up positive): the view pitched so on the first tick, to look down at the feet or up.
        float pitch = cli.Float("--look-pitch", 0f) * DetMath.DegToRad;
        // --free-pointer x,y (0 to 1 across and down the picture): the pointer freed there after the first tick.
        var freePointer = Point(cli.Arg("--free-pointer"));
        var ladders = new List<string>();      // what the observers came to, for the run's summary
        // --turn-view deg[@s[:over s]]: the view turned by that much from that second (default 0.5 s), over
        // that long (default 0.2 s), as a mouse would; for the standing body's turn in place.
        var (turnBy, turnFrom, turnTicks) = TurnView(cli.Arg("--turn-view"));
        float orbit = cli.Float("--picture-turn", 0f) * DetMath.DegToRad;
        int orbitFrom = cli.Arg("--picture-turn-from") is not null ? (int)MathF.Round(cli.Float("--picture-turn-from", 0f) * World.TicksPerSecond) : -1;
        // --slot-at s: the quick slot 1 pressed at that second (Celerity, when learned), for pictures at speed.
        int slotAt = cli.Float("--slot-at", -1f) is >= 0f and var sa ? (int)MathF.Round(sa * World.TicksPerSecond) : -1;
        // --swap-at s[,s]: swapWeapon pressed then (the sword and the bow); --stills-from s [--stills-fps n]: a picture every
        // 1/n s from that second to the end, <png>-f0001.png on (for a film of the free yard, made with ffmpeg), the renderer made once.
        var swaps = new HashSet<int>();
        foreach (var part in (cli.Arg("--swap-at") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float swapAt)) swaps.Add((int)MathF.Round(swapAt * World.TicksPerSecond));
        int stillsFrom = cli.Arg("--stills-from") is not null ? (int)MathF.Round(cli.Float("--stills-from", 0f) * World.TicksPerSecond) : -1;
        int stillsEvery = Math.Max(1, (int)MathF.Round(World.TicksPerSecond / Math.Clamp(cli.Float("--stills-fps", 30f), 1f, 120f)));
        PlatformRenderer? stillRenderer = null;
        int stillCount = 0;
        PlatformRenderer Renderer()
        {
            if (stillRenderer is not null) return stillRenderer;
            stillRenderer = PlatformRenderer.CreateHeadless(size.Item1, size.Item2, new RendererOptions(settings.Driver, settings.Quality, false, Look: Session.LookFor(options.Night),
                                                                                         CharacterTextureLimit: settings.CharacterTextureLimitFor(cli.Arg("--character-texture-limit")),
                                                                                         PeopleSkinning: settings.PeopleSkinningFor(cli.Flag("--skin-every-frame"))));
            stillRenderer.CharacterTextures = session.Texture;
            stillRenderer.CursorArt = session.CursorArt;
            stillRenderer.LoadLevel(options.Level);
            return stillRenderer;
        }
        for (int i = 0; i < ticks; i++)
        {
            var frame = drive ? ScriptedDrive(i)
                      : walkTo is { } to && i >= walkFrom ? Sprinting(Toward(session, to, walk: true), walkSprint)
                      : lookAt is { } at && i == 0 ? Toward(session, at, walk: false)
                      : ActionFrame.Idle;
            // Switch view cycles (first, right shoulder, left shoulder): from the start's right shoulder, two presses.
            if (first && i is 0 or 2) frame = frame with { Held = frame.Held | Buttons.SwitchView };
            if (leftShoulder && !first && i == 0) frame = frame with { Held = frame.Held | Buttons.SwitchView };   // over the left shoulder
            if (pitch != 0f && i == 0) frame = frame with { Look = frame.Look + new System.Numerics.Vector2(0f, pitch) };
            if (turnTicks > 0 && i >= turnFrom && i < turnFrom + turnTicks) frame = frame with { Look = frame.Look + new System.Numerics.Vector2(turnBy / turnTicks, 0f) };
            // --picture-turn deg: on the last tick, free look held and the camera swung round the player by that
            // much (its orbit; the body keeps its facing), to see the legs from the side or the front.
            // --picture-turn-from s: the swing made at that second and free look held from then on (a film from the side).
            if (orbit != 0f && (orbitFrom < 0 ? i == ticks - 1 : i >= orbitFrom))
                frame = frame with { Held = frame.Held | Buttons.FreeLook, Look = frame.Look + new System.Numerics.Vector2(orbitFrom < 0 || i == orbitFrom ? orbit : 0f, 0f) };
            if (freePointer is { } place && i > 0)
                frame = frame with { Held = frame.Held | Buttons.FreePointer, Pointer = FirstPersonView.ToPick(place, session.PickScale), Aspect = size.Item1 / (float)size.Item2 };
            if (orbitPut is { } put && i == put.At) session.Orbit.Put(put.Yaw, put.Pitch, put.Boom);   // --orbit
            if (i == slotAt) frame = frame with { Held = frame.Held | Buttons.Slot1 };
            if (swaps.Contains(i)) frame = frame with { Held = frame.Held | Buttons.SwapWeapon };
            session.Step(traverse.Apply(frame, i));
            if (stillsFrom >= 0 && i >= stillsFrom && (i - stillsFrom) % stillsEvery == 0)   // --stills-from
            {
                var r = Renderer();
                var still = new FrameView { InterfaceScale = settings.InterfaceScale };
                session.ShowSwingTrail = cli.Flag("--show-swing");
                session.Fill(still, 1f);
                still.StatusLine = string.Create(CultureInfo.InvariantCulture, $"{PlatformVersion.Text}   {(i + 1) / (float)World.TicksPerSecond:0.00} s   {session.Status}");
                for (int k = 0; k < Math.Max(1, r.StillFrames); k++) r.Draw(still, null);
                r.SaveScreenshot(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(png)) ?? ".", $"{Path.GetFileNameWithoutExtension(png)}-f{++stillCount:0000}.png"));
            }
            if (arcFreeze == i) session.FreezeSwingArc();   // --arc-freeze s, the arc kept as predicted then
            foreach (var c in session.World.Events<Perception.AwarenessChanged>().ThisTick)
                ladders.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"{session.NameOf(c.Observer.Packed) ?? $"e{c.Observer.Index}"} {c.To.ToString().ToLowerInvariant()} at {c.Tick / (float)World.TicksPerSecond:0.00} s"));
        }
        if (ladders.Count > 0) log.Say($"perception: in this run {string.Join(", ", ladders)}");
        if (stillCount > 0) log.Say($"headless: {stillCount} stills every {stillsEvery} ticks from {stillsFrom / (float)World.TicksPerSecond:0.00} s (--stills-from)");
        var drawing = Stopwatch.StartNew();
        using var renderer = Renderer();   // made once (the stills' too)
        var view = new FrameView { InterfaceScale = settings.InterfaceScale };
        session.ShowReach = settings.ShowReach || cli.Flag("--show-reach");
        session.ShowAwareness = cli.Flag("--show-awareness");
        session.ShowHitVolumes = cli.Flag("--show-hit-volumes");
        session.ShowCues = cli.Flag("--show-cues") || cli.Flag("--show-hud");   // the flurry's cue only when asked
        session.ShowSwingTrail = cli.Flag("--show-swing");                      // the swing's trail
        session.Fill(view, 1f);
        FramePerson(cli, session, view, log);   // --frame-person, and where everyone is
        if (session.ShowSwingArc && session.SwingArcOpacity > 0f)
            log.Say($"swing arc: {session.SwingArcs.Blow.ToString().ToLowerInvariant()}, {view.SwingArc.Count} quads at {session.SwingArcOpacity * 100f:0} %{(session.SwingArcs.Meets.IsNone ? "" : $", meeting {session.NameOf(session.SwingArcs.Meets.Packed)} ({(session.SwingArcs.MeetsShield ? "shield" : Combat.BodyRegions.Name(session.SwingArcs.MeetsRegion))})")}");
        if (freePointer is not null) log.Say($"pointer: freed at {freePointer.Value.X:0.###},{freePointer.Value.Y:0.###}; {session.PointerLine}");
        if (session.ShowReach)
        {
            // How long the marks take to make (a second time, so that what is timed is the work, not its first compiling).
            var made = Stopwatch.StartNew();
            Presentation.ReachHighlight.For(session.World, session.Player, []);
            log.Say($"reach: {view.Reach.Count} marks round the player, made in {made.Elapsed.TotalMilliseconds:0.0} ms");
        }
        view.StatusLine = $"{PlatformVersion.Text}   {session.Status}";
        // A --headless picture has no HUD unless --show-hud asks for it (the levels' check pictures stay as they were);
        // --hud-sample shows every slot's state and both bars from a sample in place of the views, for the HUD's own pictures
        // before a module writes them.
        Interface.DrawList? hudList = null;
        if (cli.Flag("--show-hud") || cli.Flag("--hud-sample"))
        {
            var hud = new Interface.Hud(new Interface.ScreenLoader(log.Say));
            hud.View.SetKeys(SdlKeys.Table(settings.Bindings), SdlKeys.Name);
            Interface.HudView.Read(session.World, session.Player, hud.View);
            if (cli.Flag("--hud-sample")) HudSample(hud.View);
            hudList = hud.Draw(size.Item1, size.Item2, settings.InterfaceScale);
        }
        for (int i = 0; i < Math.Max(1, renderer.StillFrames); i++) renderer.Draw(view, hudList);
        double drawnSeconds = drawing.Elapsed.TotalSeconds;
        if (cli.Int("--frame-cost", 0) is > 0 and var costFrames) FrameCost(cli, session, settings, size, costFrames, log);
        renderer.SaveScreenshot(png);
        // The cost, for a
        // comparison of --character-texture-limit 2048 and 4096.
        using (var self = Process.GetCurrentProcess())
            log.Say(string.Create(CultureInfo.InvariantCulture,
                $"headless: characters' textures {renderer.CharacterTextureBytes / 1048576.0:0.0} MB on the GPU at {renderer.CharacterTextureLimit}; the session made in {madeSeconds:0.00} s, "
                + $"the renderer and the picture in {drawnSeconds:0.00} s; managed heap {GC.GetTotalMemory(false) / 1048576.0:0} MB ({GC.GetTotalMemory(true) / 1048576.0:0} MB after a full collection), "
                + $"working set {self.WorkingSet64 / 1048576.0:0} MB (peak {self.PeakWorkingSet64 / 1048576.0:0} MB)"));
        log.Say($"headless: {Path.GetFullPath(png)} after {ticks} ticks; the player: {session.Status}{(hudList is null ? "" : "; the HUD drawn" + (cli.Flag("--hud-sample") ? " from its sample" : ""))}");
        return 0;
    }

    /// <summary>
    /// <c>--frame-cost &lt;n&gt;</c> with <c>--headless</c>: after the run, <paramref name="frames"/> more ticks, each stepped and
    /// its picture made, and the characters' work on the CPU done as the renderer does it (<see cref="Render.Characters.CharacterSkinningMeasure"/>)
    /// — first with every person skinned every frame, then as the settings say (<see cref="Render.Characters.PeopleSkinning"/>) —
    /// and what each part took said: the tick, the picture's making (the poses), the characters' skinning; how many people were skinned,
    /// drawn again and left out, and the vertices made (for the upload). The pictures are not drawn (the cloud's software GPU runs out of
    /// memory mappings after a few dozen). Views: the run's own (third person behind the player at the start, unless placed), then with
    /// <c>--frame-cost-turn</c> the view turned round by that many degrees a frame. A developer's measure; the run's world goes on through it.
    /// </summary>
    static void FrameCost(Cli cli, Session session, PlatformSettings settings, (int W, int H) size, int frames, HostLog log)
    {
        var wanted = settings.PeopleSkinningFor(cli.Flag("--skin-every-frame"));
        float turn = cli.Float("--frame-cost-turn", 0f) * DetMath.DegToRad;
        using var measure = new Render.Characters.CharacterSkinningMeasure();
        // A first pass not counted: the helpers' trial and the first sight of each model (the faster way kept after 40 skinnings).
        foreach (var (label, skinning, counted) in new[] { ("warming", Render.Characters.PeopleSkinning.EveryFrame, false),
                                                           ("every person every frame", Render.Characters.PeopleSkinning.EveryFrame, true), ("the settings' skinning", wanted, true) })
        {
            measure.Skinning = skinning;
            var tick = new List<double>(); var fill = new List<double>(); var skin = new List<double>();
            long skinned = 0, reused = 0, culled = 0, verts = 0;
            var frameView = new FrameView { InterfaceScale = settings.InterfaceScale };
            for (int i = 0; i < (counted ? frames : Math.Min(frames, 60)); i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                session.Step(turn != 0f ? ActionFrame.Idle with { Held = Buttons.FreeLook, Look = new System.Numerics.Vector2(turn, 0f) } : ActionFrame.Idle);
                long t1 = Stopwatch.GetTimestamp();
                session.Fill(frameView, 1f);
                long t2 = Stopwatch.GetTimestamp();
                var c = measure.Frame(frameView.Characters, frameView.View, size.W / (float)size.H);
                tick.Add(Stopwatch.GetElapsedTime(t0, t1).TotalMilliseconds);
                fill.Add(Stopwatch.GetElapsedTime(t1, t2).TotalMilliseconds);
                skin.Add(c.SkinMilliseconds);
                (skinned, reused, culled, verts) = (skinned + c.Skinned, reused + c.Reused, culled + c.Culled, verts + c.Vertices);
            }
            if (!counted) continue;
            static string Of(List<double> v)
            {
                var o = v.Order().ToArray();
                return string.Create(CultureInfo.InvariantCulture, $"median {o[o.Length / 2]:0.00} mean {o.Average():0.00} p95 {o[Math.Min(o.Length - 1, (int)(o.Length * 0.95))]:0.00} ms");
            }
            log.Say(string.Create(CultureInfo.InvariantCulture,
                $"frame cost, {label}, {frames} frames, {session.PeoplePlaces().Count + 1} characters: the tick {Of(tick)}; the picture made (poses) {Of(fill)}; "
                + $"the characters skinned {Of(skin)} (helpers {(measure.HelpersFaster is { } h ? h ? "the faster" : "the slower: one thread" : "untried")}); "
                + $"a frame {skinned / (double)frames:0.0} people skinned, {reused / (double)frames:0.0} drawn again, {culled / (double)frames:0.0} out of view, {verts / (double)frames:N0} vertices"));
        }
    }

    /// <summary>
    /// A headless still's log of where every character but the player is (its name, entity, place, facing, kind), and with
    /// <c>--frame-person &lt;who&gt;[,metres[,degrees[,height[,at]]]]</c> the picture's view put on one of them — who a shown name (the first
    /// of that name) or <c>e&lt;index&gt;</c>, or <c>player</c>; from that far (3 m) at that angle from its facing (0: from the front), the eye that high (1.5 m),
    /// looking at that height on it (0.9 m: its middle; 1.6 its face). A developer's aid for pictures of the townsfolk; presentation only.
    /// </summary>
    static void FramePerson(Cli cli, Session session, FrameView view, HostLog log)
    {
        var places = session.PeoplePlaces();
        foreach (var p in places)
            log.Say(string.Create(CultureInfo.InvariantCulture, $"headless: {p.Name} e{p.Index} ({p.Kind}) at {p.Feet.X:0.00} {p.Feet.Y:0.00} {p.Feet.Z:0.00} facing {p.Yaw * DetMath.RadToDeg:0}°"));
        if (cli.Arg("--frame-person") is not { } text) return;
        var parts = text.Split(',');
        var found = places.Where(p => p.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase) || parts[0].Equals($"e{p.Index}", StringComparison.OrdinalIgnoreCase)).ToList();
        // The player too, by the word player (a side view of the player's body for the stills of stairs and slopes).
        if (parts[0].Equals("player", StringComparison.OrdinalIgnoreCase) && session.World.Alive(session.Player) && session.World.Has<Bodies.Body>(session.Player))
        {
            ref var pb = ref session.World.Get<Bodies.Body>(session.Player);
            found = [("player", session.Player.Index, pb.Feet, pb.Yaw, "")];
        }
        if (found.Count == 0) { log.Say($"headless: --frame-person: nobody is '{parts[0]}'"); return; }
        float Part(int i, float fallback) => parts.Length > i && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        var who = found[0];
        float far = Part(1, 3f), angle = who.Yaw + Part(2, 0f) * DetMath.DegToRad, high = Part(3, 1.5f);
        var at = who.Feet + new System.Numerics.Vector3(0f, 0f, Part(4, 0.9f));
        var eye = who.Feet + new System.Numerics.Vector3(MathF.Cos(angle) * far, MathF.Sin(angle) * far, high);
        var d = at - eye;
        view.View = new ViewPoint(eye, MathF.Atan2(d.Y, d.X), MathF.Atan2(d.Z, MathF.Sqrt(d.X * d.X + d.Y * d.Y)), 45f);
        log.Say(string.Create(CultureInfo.InvariantCulture, $"headless: the view on {who.Name} e{who.Index}, {far:0.#} m off at {Part(2, 0f):0}° from its facing"));
    }

    /// <summary>The HUD's draw list for this picture: the player's views read into it, then drawn (anew only when what it shows changed).</summary>
    static Interface.DrawList HudFor(Session session, Interface.Hud hud, int width, int height, float scale)
    {
        Interface.HudView.Read(session.World, session.Player, hud.View);
        return hud.Draw(width, height, scale);
    }

    /// <summary>
    /// A sample in place of the views (<c>--hud-sample</c>, a developer's option for pictures of the stand-in HUD before
    /// a module writes the views): each state once — ready, live, recast, short of Blood, unusable — then empty slots;
    /// Health and Blood part full (Health 20 of 100, Injured). The masquerade's mode stays the player's own.
    /// </summary>
    static void HudSample(Interface.HudView v)
    {
        v.Slots[0] = new SlotView { Power = 0, State = SlotState.Ready, Cost = 30 };
        v.Slots[1] = new SlotView { Power = 1, State = SlotState.Live, SecondsLeft = 6.4f, SecondsTotal = 10f, Cost = 30 };
        v.Slots[2] = new SlotView { Power = 2, State = SlotState.Recast, RecastLeft = 2.5f, Cost = 10 };
        v.Slots[3] = new SlotView { Power = 3, State = SlotState.Short, Cost = 40 };
        v.Slots[4] = new SlotView { Power = 4, State = SlotState.Unusable, Cost = 30 };
        string[] labels = ["Celerity", "Potence", "Dominate", "Obfuscate", "Celerity 6"];
        for (int i = 0; i < labels.Length; i++) v.Labels[i] = labels[i];
        v.HasVitals = true;
        v.Vitals = new Vitals { Health = 20f, HealthMax = 100f, Blood = 70f, BloodMax = 100f, HasBlood = true, Band = Combat.HealthBand.Injured };   // Health on the 1-1000 scale, its band
    }

    /// <summary>
    /// The first-person picture's settings into the session: the head motion (per cent to a share) and the field of
    /// view up the picture or across it, or <c>--head-motion</c>, <c>--fov</c> and <c>--fov-axis vertical|horizontal</c> for
    /// this run (<c>--fov</c> is in the axis's degrees). Presentation only.
    /// </summary>
    static void View(Session session, PlatformSettings settings, Cli? cli = null)
    {
        session.HeadMotion = Math.Clamp((cli?.Float("--head-motion", settings.HeadMotion) ?? settings.HeadMotion) / 100f, 0f, 1f);
        // The swing's arc — in play the settings' (shown, 25 %); a --headless picture only with --swing-arc <0..100>.
        session.SwingArcOpacity = Math.Clamp((cli is null ? settings.SwingArcOpacity : cli.Float("--swing-arc", 0f)) / 100f, 0f, 1f);
        session.ShowSwingArc = cli is null ? settings.ShowSwingArc : session.SwingArcOpacity > 0f;
        // The arc in the body's frame (or carried on, the old way); the reticle faint in melee (a headless picture
        // full unless --melee-reticle says); the combat log's window (a headless picture only with --show-combat-log).
        session.SwingArcs.Settings = session.SwingArcs.Settings with { CarryOn = settings.SwingArcCarryOn || cli?.Flag("--arc-carry-on") == true };
        session.MeleeReticleOpacity = Math.Clamp((cli is null ? settings.MeleeReticleOpacity : cli.Float("--melee-reticle", 100f)) / 100f, 0f, 1f);
        session.BowZoom = Math.Clamp((cli?.Float("--bow-zoom", settings.BowZoomPercent) ?? settings.BowZoomPercent) / 100f, 0f, 0.5f);   // presentation only
        session.ShowBowImpact = cli is null ? settings.ShowBowImpact : cli.Flag("--show-impact");   // the bow's aids (a headless picture only when asked)
        session.ShowBowPath = cli is null ? settings.ShowBowPath : cli.Flag("--show-path");
        session.ShowCombatLog = cli is null ? settings.ShowCombatLog : cli.Flag("--show-combat-log");
        session.CombatLog.Settings = session.CombatLog.Settings with
        {
            Corner = settings.CombatLogCorner,
            Rows = Math.Clamp(settings.CombatLogLines, CombatLogSettings.LeastRows, CombatLogSettings.MostRows),
        };
        var axis =cli?.Arg("--fov-axis") is { } named && Enum.TryParse<FieldOfViewAxis>(named, ignoreCase: true, out var given) ? given : settings.FieldOfViewAxis;
        session.FieldOfViewAxis = axis;
        session.FieldOfView = axis == FieldOfViewAxis.Horizontal
            ? FirstPersonView.HorizontalField(cli?.Float("--fov", settings.FieldOfViewHorizontal) ?? settings.FieldOfViewHorizontal)
            : FirstPersonView.Field(cli?.Float("--fov", settings.FieldOfView) ?? settings.FieldOfView);
    }

    /// <summary>
    /// The first-person view's settings as the log says them, so a walk read back from the log knows them: the
    /// head motion, and the field of view with its axis ("80° vertical", or "112° across" with the vertical drawn now).
    /// </summary>
    static string ViewLine(Session session) => string.Create(CultureInfo.InvariantCulture,
        $"view: first person's head motion {session.HeadMotion * 100f:0} %, field of view {session.FieldOfView:0}° ") +
        (session.FieldOfViewAxis == FieldOfViewAxis.Horizontal
            ? string.Create(CultureInfo.InvariantCulture, $"across ({session.DrawnFieldOfView:0.#}° vertical at {session.PictureAspect:0.###}:1)")
            : "vertical") + CameraWords(session.World.Service<CameraSettings>()) +
        string.Create(CultureInfo.InvariantCulture, $"; the swing's arc {(session.ShowSwingArc && session.SwingArcOpacity > 0f ? $"shown at {session.SwingArcOpacity * 100f:0} %" : "hidden")}");

    /// <summary>The camera's steadiness for the view's line: an action's sway (cm) and the third-person follow (s).</summary>
    static string CameraWords(CameraSettings c) => string.Create(CultureInfo.InvariantCulture,
        $"; an action's sway {(c.ActionAllowance < 0f ? "the head's whole (the rigid camera's)" : $"{c.ActionAllowance * 100f:0.#} cm")}, third person's follow {c.ThirdPersonFollowSeconds:0.###} s{(c.MeleeFraming ? ", melee framing on" : "")}");

    /// <summary><c>--forward</c>, <c>--jump-at</c>, <c>--crouch-at</c> and <c>--use-at</c>: the buttons laid over a headless still's actions.</summary>
    readonly record struct Traversing(bool Forward, Buttons Pace, int JumpFrom, int JumpTo, int CrouchFrom, System.Numerics.Vector2 Way, int UseAt = -1)
    {
        /// <summary>The buttons asked for; "forward" is along the placed facing, <paramref name="aside"/> from the view.</summary>
        public static Traversing Of(Cli cli, float aside)
        {
            bool forward = cli.Flag("--forward");
            var pace = cli.Arg("--forward") switch { "walk" => Buttons.Walk, "sprint" => Buttons.Sprint, _ => Buttons.None };
            int from = -1, to = -1, crouch = -1;
            if (cli.Arg("--jump-at") is { } j)
            {
                var parts = j.Split(':');
                if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float at))
                {
                    float held = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float h) ? h : 0f;
                    from = (int)MathF.Round(at * World.TicksPerSecond);
                    to = from + Math.Max(1, (int)MathF.Round(held * World.TicksPerSecond));
                }
            }
            if (cli.Float("--crouch-at", -1f) is >= 0f and var c) crouch = (int)MathF.Round(c * World.TicksPerSecond);
            var (sin, cos) = DetMath.SinCos(aside);
            int use = cli.Float("--use-at", -1f) is >= 0f and var u ? (int)MathF.Round(u * World.TicksPerSecond) : -1;
            return new Traversing(forward, pace, from, to, crouch, new System.Numerics.Vector2(-sin, cos), use);
        }

        /// <summary><c>--attack-at</c> and <c>--block-at</c>: the ticks attack and block are held, as (from, to) spans.</summary>
        public IReadOnlyList<(int From, int To)> Attacks { get; init; } = [];
        public (int From, int To) Block { get; init; } = (-1, -1);

        /// <summary>Reads <c>--attack-at s[:held],...</c> and <c>--block-at s[:held]</c> into a traversal's presses.</summary>
        public Traversing WithFighting(Cli cli)
        {
            static (int, int)? Span(string part, int fallback)
            {
                var bits = part.Split(':');
                if (!float.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float at)) return null;
                int from = (int)MathF.Round(at * World.TicksPerSecond);
                int to = bits.Length > 1 && float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float h)
                    ? from + Math.Max(1, (int)MathF.Round(h * World.TicksPerSecond)) : fallback < 0 ? from + 1 : fallback;
                return (from, to);
            }
            var attacks = new List<(int, int)>();
            foreach (var part in (cli.Arg("--attack-at") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (Span(part, -1) is { } s) attacks.Add(s);
            var block = cli.Arg("--block-at") is { } b && Span(b, int.MaxValue) is { } bs ? bs : (-1, -1);
            // --aim-at s[:held],...: aim (the bow's aim mode) held over those spans (to the end when no length is given).
            var aims = new List<(int, int)>();
            foreach (var part in (cli.Arg("--aim-at") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (Span(part, int.MaxValue) is { } s) aims.Add(s);
            // --attack-key left|right|forward|back: that move key held on each attack press's first tick (the keys'
            // mode chooses the blow's direction by it: A left, D right, W overhead, S a thrust), for the arc's pictures by direction.
            System.Numerics.Vector2? key = cli.Arg("--attack-key") switch
            {
                "left" => new System.Numerics.Vector2(-1, 0), "right" => new System.Numerics.Vector2(1, 0),
                "forward" => new System.Numerics.Vector2(0, 1), "back" => new System.Numerics.Vector2(0, -1), _ => null,
            };
            return this with { Attacks = attacks, Block = block, AttackKey = key, Aims = aims };
        }

        /// <summary>The spans aim is held (<c>--aim-at</c>).</summary>
        public IReadOnlyList<(int From, int To)> Aims { get; init; } = [];

        /// <summary>The move key held on each attack press's first tick (<c>--attack-key</c>), or none.</summary>
        public System.Numerics.Vector2? AttackKey { get; init; }

        /// <summary><c>--dodge-at s[:way][,...]</c>: the ticks Dodge is pressed and the move keys held with each (left, right, back, forward; none: back).</summary>
        public IReadOnlyList<(int At, System.Numerics.Vector2 Keys)> Dodges { get; init; } = [];

        /// <summary>Reads <c>--dodge-at</c> into a traversal's presses.</summary>
        public Traversing WithDodges(Cli cli)
        {
            var dodges = new List<(int, System.Numerics.Vector2)>();
            foreach (var part in (cli.Arg("--dodge-at") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = part.Split(':');
                if (!float.TryParse(bits[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float at)) continue;
                var keys = (bits.Length > 1 ? bits[1] : "") switch
                {
                    "left" => new System.Numerics.Vector2(-1, 0),
                    "right" => new System.Numerics.Vector2(1, 0),
                    "forward" => new System.Numerics.Vector2(0, 1),
                    "back" => new System.Numerics.Vector2(0, -1),
                    _ => System.Numerics.Vector2.Zero,
                };
                dodges.Add(((int)MathF.Round(at * World.TicksPerSecond), keys));
            }
            return this with { Dodges = dodges };
        }

        public ActionFrame Apply(ActionFrame frame, int tick)
        {
            foreach (var (at, keys) in Dodges)
                if (tick == at) frame = frame with { Held = frame.Held | Buttons.Dodge, Move = keys };   // a press with its keys
            foreach (var (from, to) in Attacks)
                if (tick >= from && tick < to) frame = frame with { Held = frame.Held | Buttons.Attack };
            if (Block.From >= 0 && tick >= Block.From && tick < Block.To) frame = frame with { Held = frame.Held | Buttons.Block };
            foreach (var (from, to) in Aims)
                if (tick >= from && tick < to) frame = frame with { Held = frame.Held | Buttons.Aim };
            if (Forward) frame = frame with { Move = Way, Held = frame.Held | Pace };
            if (tick >= JumpFrom && tick < JumpTo) frame = frame with { Held = frame.Held | Buttons.Jump };
            if (CrouchFrom >= 0 && tick >= CrouchFrom) frame = frame with { Held = frame.Held | Buttons.Crouch };
            if (UseAt >= 0 && tick == UseAt) frame = frame with { Held = frame.Held | Buttons.Use };   // a press of use (a ladder)
            if (AttackKey is { } key) foreach (var (from, _) in Attacks) if (tick == from) frame = frame with { Move = key };
            return frame;
        }
    }

    /// <summary><c>--turn-view deg[@s[:over s]]</c>: the turn (radians), the tick it starts and how many ticks it takes; none when not given.</summary>
    static (float By, int From, int Ticks) TurnView(string? text)
    {
        if (text is null) return (0f, 0, 0);
        var at = text.Split('@');
        if (!float.TryParse(at[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float deg)) return (0f, 0, 0);
        var when = at.Length > 1 ? at[1].Split(':') : [];
        float from = when.Length > 0 && float.TryParse(when[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0.5f;
        float over = when.Length > 1 && float.TryParse(when[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float o) ? o : 0.2f;
        return (deg * DetMath.DegToRad, (int)MathF.Round(from * World.TicksPerSecond), Math.Max(1, (int)MathF.Round(over * World.TicksPerSecond)));
    }

    /// <summary>A point of the level given as <c>x,y</c> (metres), or null.</summary>
    static System.Numerics.Vector2? Point(string? text)
    {
        var parts = text?.Split(',') ?? [];
        return parts.Length >= 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
               && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ? new(x, y) : null;
    }

    /// <summary>
    /// <c>--look-at</c> and <c>--walk-to</c>: the actions that turn the view to <paramref name="target"/>
    /// and, with <paramref name="walk"/>, run forward towards it until the feet are within 0.3 m of it.
    /// </summary>
    /// <summary><paramref name="frame"/> with sprint held when <paramref name="sprint"/> (<c>--walk-to x, y@s, sprint</c>).</summary>
    static ActionFrame Sprinting(ActionFrame frame, bool sprint) => sprint ? frame with { Held = frame.Held | Buttons.Sprint } : frame;

    static ActionFrame Toward(Session session, System.Numerics.Vector2 target, bool walk)
    {
        var world = session.World;
        if (!world.Alive(session.Player)) return ActionFrame.Idle;
        var feet = world.Get<Bodies.Body>(session.Player).Feet;
        var to = target - new System.Numerics.Vector2(feet.X, feet.Y);
        float turn = DetMath.WrapAngle(DetMath.Atan2(to.Y, to.X) - world.Get<CameraRig>(session.Player).Yaw);
        bool go = walk && to.Length() > 0.3f;
        return new ActionFrame(go ? new System.Numerics.Vector2(0, 1) : default, new System.Numerics.Vector2(turn, 0f), Buttons.None, new System.Numerics.Vector2(0.5f, 0.5f));
    }

    // ---- a screen of the interface, headless ----

    /// <summary>
    /// <c>--headless-screen pause|controls &lt;png&gt;</c>: one of the menus drawn to a picture for review, at
    /// <c>--picture</c> (default 2560x1440), over the level after <c>--seconds</c> when <c>--level</c> is given (else
    /// over black). <c>--ui-wait action.slot</c> leaves that key cell waiting; with <c>--ui-press name</c> the name is
    /// then pressed into it (a key moved from another action shows so); <c>--ui-focus id</c> puts the focus on a piece.
    /// </summary>
    static int ScreenStill(Cli cli, PlatformSettings settings, HostLog log, string name)
    {
        int at = Array.IndexOf(cli.Args, "--headless-screen");
        var png = at + 2 < cli.Args.Length && !cli.Args[at + 2].StartsWith("--", StringComparison.Ordinal) ? cli.Args[at + 2] : null;
        if (png is null || name is not ("pause" or "controls"))
        {
            log.Say("--headless-screen wants pause|controls <png>, for example --headless-screen controls controls.png");
            return 2;
        }
        var size = cli.Size("--picture") ?? (2560, 1440);
        Session? session = null;
        var view = new FrameView();
        if (cli.Arg("--level") is not null)
        {
            session = Session.Create(Options(cli, settings, log));
            int ticks = (int)MathF.Round(cli.Float("--seconds", 1f) * World.TicksPerSecond);
            for (int i = 0; i < ticks; i++) session.Step(ActionFrame.Idle);
            session.Fill(view, 1f);
        }
        view.ShowReticle = false;
        view.Banner = null;
        view.StatusLine = null;
        if (cli.Float("--ui-scale", 0f) is var scale && scale > 0f) settings.InterfaceScale = scale;
        var menus = new Menus(settings, log.Say);
        menus.OpenPause();
        if (name == "controls") menus.OpenControls();
        if (cli.Arg("--ui-wait") is { } wait && menus.Page is { } page)
        {
            var dot = wait.LastIndexOf('.');
            page.Wait(dot > 0 ? wait[..dot] : wait, dot > 0 && wait[(dot + 1)..] == "1" ? 1 : 0);
            if (page.Waiting is null) log.Say($"--ui-wait: no action \"{wait}\"");
            if (cli.Arg("--ui-press") is { } press)
            {
                if (BindingNames.Parse(press, SdlKeys.Code) is { } source) page.Press(source);
                else log.Say($"--ui-press: \"{press}\" names no key, button or wheel");
            }
        }
        var top = menus.Top!;
        top.Layout(new Interface.UiMetrics(size.Item1, size.Item2, settings.InterfaceScale));
        if (menus.Page?.Waiting is { } waiting) top.SetFocus(menus.Page.Cell(waiting.Action, waiting.Slot));
        if (cli.Arg("--ui-focus") is { } focus)
        {
            if (top.Find(focus) is { } piece) top.SetFocus(piece);
            else log.Say($"--ui-focus: no piece \"{focus}\" on {name}");
        }
        using var renderer = PlatformRenderer.CreateHeadless(size.Item1, size.Item2, new RendererOptions(settings.Driver, settings.Quality, false, Look: Session.LookFor(session?.Night ?? false),
                                                                                           CharacterTextureLimit: settings.CharacterTextureLimitFor(cli.Arg("--character-texture-limit")),
                                                                                         PeopleSkinning: settings.PeopleSkinningFor(cli.Flag("--skin-every-frame"))));
        if (session is not null)
        {
            renderer.CharacterTextures = session.Texture;
            renderer.LoadLevel(Options(cli, settings, log).Level);
        }
        var list = menus.Draw(size.Item1, size.Item2);
        for (int i = 0; i < Math.Max(1, renderer.StillFrames); i++) renderer.Draw(view, list);
        renderer.SaveScreenshot(png);
        session?.Dispose();
        log.Say($"headless-screen: {name} to {Path.GetFullPath(png)} ({size.Item1}x{size.Item2}, {list?.Count ?? 0} drawn)");
        return 0;
    }

    // ---- a scripted drive, recorded (for checking that replays agree across machines) ----

    /// <summary>
    /// <c>--record-drive &lt;tape&gt;</c>: a drive by script, headless — running, turning, jumping, sprinting,
    /// walking, strafing, switching view — recorded with its digests, so the same tape replayed on another
    /// machine (<c>--replay-check</c>) shows whether the simulation gives the same bits there.
    /// </summary>
    static int RecordDrive(Cli cli, PlatformSettings settings, HostLog log, string path)
    {
        var options = Options(cli, settings, log);
        using var session = Session.Create(options);
        var tape = new Tape { Header = session.TapeHeader(PlatformVersion.Text) };
        int ticks = (int)MathF.Round(cli.Float("--seconds", 30f) * World.TicksPerSecond);
        // --show-reach: the highlight made after every tick, as a window would show it (its digests are the same without).
        session.ShowReach = cli.Flag("--show-reach");
        var view = session.ShowReach ? new FrameView() : null;
        int marks = 0;
        // --pointer lays the freed pointer over the drive's last second of each dozen: swept across, clicked.
        bool pointer = cli.Flag("--pointer");
        // --toggles lays presses of the toggled actions over the drive (ToggleDrive), and counts the ticks latched.
        bool toggles = cli.Flag("--toggles");
        // --fights lays attacks and blocks over the drive (FightDrive), and counts what came of them.
        bool fights = cli.Flag("--fights");
        // --slots lays presses of the quick slots over the drive (SlotDrive), and counts what the module made of them.
        bool slots = cli.Flag("--slots");
        int slotPresses = 0, powersUsed = 0, powersRefused = 0;
        int begun = 0, strikes = 0, blocks = 0, refusals = 0;
        int crouchLatched = 0, walkLatched = 0, pointerLatched = 0, latchFlips = 0;
        var latchedWas = Buttons.None;
        int changes = 0, talks = 0, picked = 0, refused = 0;
        for (int t = 0; t < ticks; t++)
        {
            var frame = ScriptedDrive(t);
            if (pointer) frame = PointerDrive(t, frame);
            if (toggles) frame = ToggleDrive(t, frame);
            if (fights) frame = FightDrive(t, frame);
            if (slots) frame = SlotDrive(t, frame);
            if (QuickSlotButtons.Lowest(ButtonEdges.Pressed(frame.Held, tape.Frames.Count > 0 ? tape.Frames[^1].Held : Buttons.None)) != 0) slotPresses++;
            session.Step(frame);
            if (session.World.TryService<EventQueue<PowerUsed>>(out var used)) powersUsed += used.ThisTick.Count;
            if (session.World.TryService<EventQueue<PowerRefused>>(out var refusedPowers)) powersRefused += refusedPowers.ThisTick.Count;
            if (toggles && session.World.Alive(session.Player) && session.World.Has<Controlled>(session.Player))
            {
                var latched = session.World.Get<Controlled>(session.Player).Latched;
                if ((latched & Buttons.Crouch) != 0) crouchLatched++;
                if ((latched & Buttons.Walk) != 0) walkLatched++;
                if ((latched & Buttons.FreePointer) != 0) pointerLatched++;
                latchFlips += System.Numerics.BitOperations.PopCount((uint)(latched ^ latchedWas));
                latchedWas = latched;
            }
            var w = session.World;
            changes += w.Events<Cameras.PointerVerbChanged>().ThisTick.Count;
            talks += w.Events<Cameras.TalkRequested>().ThisTick.Count;
            picked += w.Events<Cameras.TargetPicked>().ThisTick.Count;
            refused += w.Events<Cameras.TargetRefused>().ThisTick.Count;
            begun += w.Events<AttackBegun>().ThisTick.Count;
            strikes += w.Events<StrikeMoment>().ThisTick.Count;
            blocks += w.Events<BlockRaised>().ThisTick.Count;
            refusals += w.Events<AttackRefused>().ThisTick.Count;
            if (view is not null) { session.Fill(view, 1f); marks += view.Reach.Count; }
            tape.Frames.Add(frame);
            if (session.Tick % Tape.DigestEvery == 0) tape.Digests[session.Tick] = session.Digest();
        }
        tape.Digests[session.Tick] = session.Digest();
        tape.Write(path);
        log.Say($"record-drive: {ticks} ticks to {Path.GetFullPath(path)}; the player ended at {session.Status}"
                + (view is not null ? $"; the reach highlight made as a window makes it ({marks} marks shown over the ticks)" : "")
                + $"; the pointer: {changes} verb changes, {talks} talks asked, {picked} targets picked, {refused} refused"
                + (toggles ? $"; the toggles: {latchFlips} latches set or cleared, {crouchLatched} ticks crouch latched, {walkLatched} walk, {pointerLatched} the pointer" : "")
                + (fights || begun + blocks + refusals > 0 ? $"; the fighting: {begun} blows begun, {strikes} strikes, {blocks} blocks raised, {refusals} attacks refused" : "")
                + (slots || slotPresses > 0 ? $"; the quick slots: {slotPresses} presses, {powersUsed} powers used, {powersRefused} refused" : ""));
        return 0;
    }

    /// <summary>
    /// <c>--record-drive … --slots</c>: presses of the quick slots laid over the drive, so a tape carries them. In each
    /// dozen seconds: slot <i>n</i> tapped at 1.5 + n − 1 s (1 to 8, three ticks each), slot 1 held from 9.6 s to 10.1 s
    /// (one use), and slots 5 and 2 together at 10.5 s (slot 2's use).
    /// </summary>
    internal static ActionFrame SlotDrive(int t, ActionFrame frame)
    {
        int k = t % (12 * World.TicksPerSecond);
        static bool At(int k, float s) => k >= (int)(s * World.TicksPerSecond) && k < (int)(s * World.TicksPerSecond) + 3;
        var held = frame.Held;
        for (int n = 1; n <= QuickSlots.Count; n++) if (At(k, 1.5f + n - 1)) held |= QuickSlotButtons.Of(n);
        if (k >= (int)(9.6f * World.TicksPerSecond) && k < (int)(10.1f * World.TicksPerSecond)) held |= Buttons.Slot1;
        if (At(k, 10.5f)) held |= Buttons.Slot5 | Buttons.Slot2;
        return frame with { Held = held };
    }

    /// <summary>
    /// <c>--record-drive … --fights</c>: attacks and blocks laid over the drive, so a tape carries blows, the buffer, the
    /// block and the masquerade's catch. In each dozen seconds: a tap of attack at 0.5 s and another at 0.8 s (buffered),
    /// attack held from 2.5 s to 3.0 s (a strong blow), block held from 5 s to 6.5 s with a tap of attack at 5.5 s (refused:
    /// blocking), taps at 8.0, 8.2 and 8.4 s (the third refused: mid-swing), and the masquerade key at 11.9 s (the catch off,
    /// or on again; in a public place the catch refuses every press while it is on).
    /// </summary>
    internal static ActionFrame FightDrive(int t, ActionFrame frame)
    {
        int k = t % (12 * World.TicksPerSecond);
        static bool Within(int k, float from, float to) => k >= (int)(from * World.TicksPerSecond) && k < (int)(to * World.TicksPerSecond);
        static bool Tap(int k, float s) => Within(k, s, s + 2f / World.TicksPerSecond);
        var held = frame.Held;
        if (Tap(k, 0.5f) || Tap(k, 0.8f) || Within(k, 2.5f, 3.0f) || Tap(k, 5.5f) || Tap(k, 8.0f) || Tap(k, 8.2f) || Tap(k, 8.4f)) held |= Buttons.Attack;
        if (Within(k, 5f, 6.5f)) held |= Buttons.Block;
        if (Tap(k, 11.9f)) held |= Buttons.Masquerade;
        return frame with { Held = held };
    }

    /// <summary>
    /// <c>--record-drive … --toggles</c>: presses of the toggled actions laid over the drive, so a tape carries the
    /// latches and what clears them. In each dozen seconds: crouch toggle at 0.6 s (the jump at 2 s lets it go), walk
    /// toggle at 1.2 s (the sprint at 3 s lets it go), crouch toggle on and off again at 7.0 s and 7.4 s, and the
    /// freed pointer's toggle at 10.1 s and 10.6 s (the view stops turning between).
    /// </summary>
    internal static ActionFrame ToggleDrive(int t, ActionFrame frame)
    {
        int k = t % (12 * World.TicksPerSecond);
        static bool At(int k, float s) => k >= (int)(s * World.TicksPerSecond) && k < (int)(s * World.TicksPerSecond) + 3;
        var held = frame.Held;
        if (At(k, 0.6f) || At(k, 7.0f) || At(k, 7.4f)) held |= Buttons.CrouchToggle;
        if (At(k, 1.2f)) held |= Buttons.WalkToggle;
        if (At(k, 10.1f) || At(k, 10.6f)) held |= Buttons.FreePointerToggle;
        return frame with { Held = held };
    }

    /// <summary>
    /// <c>--record-drive … --pointer</c>: from 11 s to 12 s of each dozen the pointer is freed and swept across the
    /// picture's lower half (16 by 9), clicked four times on the way, so a tape carries the pick, the verb and the click.
    /// </summary>
    internal static ActionFrame PointerDrive(int t, ActionFrame frame)
    {
        float s = (t % (12 * World.TicksPerSecond)) / (float)World.TicksPerSecond;
        if (s < 11f) return frame;
        float across = (s - 11f);
        var place = new System.Numerics.Vector2(0.15f + 0.7f * across, 0.62f - 0.12f * across);
        var held = frame.Held | Buttons.FreePointer;
        int k = t % (12 * World.TicksPerSecond) - 11 * World.TicksPerSecond;
        if (k % 30 is >= 10 and < 13) held |= Buttons.PointerClick;
        return frame with { Held = held, Pointer = place, Aspect = 16f / 9f };
    }

    /// <summary>The scripted drive's actions at tick <paramref name="t"/>: a pattern of a dozen seconds, repeated (unbound from 2.5 to 7.5 s).</summary>
    internal static ActionFrame ScriptedDrive(int t)
    {
        float s = (t % (12 * World.TicksPerSecond)) / (float)World.TicksPerSecond;
        var move = new System.Numerics.Vector2(s is >= 9f and < 10f ? 1f : 0f, s < 9f ? 1f : 0f);
        float turn = s < 1f ? 0.012f : s < 2f ? -0.012f : s is >= 4f and < 4.5f ? 0.02f : 0f;
        float pitch = s is >= 10f and < 10.5f ? 0.004f : s is >= 10.5f and < 11f ? -0.004f : 0f;
        var held = Buttons.None;
        if (t % (2 * World.TicksPerSecond) < 3) held |= Buttons.Jump;
        if (s is >= 5.5f and < 6.4f) held |= Buttons.Jump;   // a hold, so a body that can charge charges (the human jumps on the press)
        if (s is >= 3f and < 5f) held |= Buttons.Sprint;
        if (s is >= 6f and < 7f) held |= Buttons.Walk;
        if (s is (>= 8f and < 8.02f) or (>= 8.1f and < 8.12f)) held |= Buttons.SwitchView;   // the left shoulder, then first person
        // Unbound from 2.5 s to 7.5 s, so the tape carries the masquerade's key and the sprint at 3 s is the body's own.
        if (s is (>= 2.5f and < 2.52f) or (>= 7.5f and < 7.52f)) held |= Buttons.Masquerade;
        return new ActionFrame(move, new System.Numerics.Vector2(turn, pitch), held, new System.Numerics.Vector2(0.5f, 0.5f));
    }

    // ---- checking a recording ----

    static int ReplayCheck(Cli cli, PlatformSettings settings, HostLog log, string path)
    {
        var tape = Tape.Read(path);
        var options = SessionOptions.ForTape(tape.Header, cli, settings, log);
        using var session = Session.Create(options);
        // The modules first: a recording made with another set of modules says so, not a digest.
        var differences = session.Modules.Differences(tape.Header.Modules, out bool sameModules);
        foreach (var d in differences) log.Say($"replay-check: {d}");
        if (!sameModules)
        {
            log.Say($"replay-check: {path} was recorded with other modules than game {session.Modules.Game.Id} loads here; its digests cannot agree, so it is not replayed");
            return 3;
        }
        int checkedCount = 0;
        for (int i = 0; i < tape.Frames.Count; i++)
        {
            session.Step(tape.Frames[i]);
            if (tape.Digests.TryGetValue(session.Tick, out ulong want))
            {
                ulong got = session.Digest();
                checkedCount++;
                if (got != want)
                {
                    log.Say($"replay-check: {path} parts from its recording at tick {session.Tick} ({session.Tick / (double)World.TicksPerSecond:0.00} s): digest {got:x16}, recorded {want:x16}"
                        + (differences.Count > 0 && tape.Header.Modules.Count > 0 ? " (the modules' files differ from the recording's, above)" : ""));
                    return 1;
                }
            }
        }
        log.Say($"replay-check: {path} agrees: {tape.Frames.Count} ticks, {checkedCount} digests");
        return 0;
    }
}

/// <summary>A reason the program cannot run, said plainly to the player.</summary>
public sealed class SessionException(string message) : Exception(message);
