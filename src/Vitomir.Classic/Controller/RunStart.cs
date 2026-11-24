// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// A run's start: its arguments, settings, install and save (once in vitomir.exe's Program.cs; now the Classic module's library).
using System.Globalization;
using Vitomir.Engine.Ai;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Rpg;
using Vitomir.Engine.Save;
using Vitomir.Engine.Ui;
using Vitomir.Engine.Vfs;
using Vitomir.Engine.World;
using Vitomir.Formats;
using Vitomir.Classic;
using static Vitomir.Classic.ClassicRun;

namespace Vitomir
{
    /// <summary>
    /// A run's start: the arguments (a scenario's spelt out), the switches, the log, the options and keys in force, the tools
    /// and reports that end the run there, the install and its files, the window's size, and the save a load or an import
    /// names. The front end and the game start from what it found.
    /// </summary>
    sealed class RunStart : IDisposable
    {
        readonly IClassicHost host;
        /// <summary>The tools the start hands over to (vitomir-tools'), or none (the Classic module under the platform's program).</summary>
        readonly IRunTools? tools;
        internal string[] args;
        internal global::Vitomir.DriveSnags driveSnags = null!;
        internal global::System.Collections.Generic.List<string> switchSays = null!;
        internal global::Vitomir.Engine.World.Qol qol = null!;
        internal string levelName = null!;
        internal string? headless;
        internal global::System.Collections.Generic.List<float[]> clickQueue = null!;
        internal bool clickDown;
        internal float? wasPulled;
        internal int width;
        internal int height;
        internal global::Vitomir.Engine.Vfs.GameFiles files = null!;
        internal global::Vitomir.Engine.World.ChronicleSet chronicles = null!;
        internal global::Vitomir.Engine.Save.SaveGame? loaded;
        internal global::Vitomir.Engine.Save.LocationSave? loadedPlace;
        internal global::Vitomir.Engine.World.Chronicle? loadedChronicle;
        internal string? freshScene;
        /// <summary>Whether the run is one of multiplayer's windowless runs (<see cref="NetRuns"/>): the start stops where it ran them.</summary>
        internal bool windowless;

        internal RunStart(IClassicHost host, string[] args, IRunTools? tools = null) { this.host = host; this.args = args; this.tools = tools; }

        /// <summary>The install's files, closed with the run.</summary>
        public void Dispose() => files?.Dispose();

        /// <summary>The run's arguments read, its settings in force, a tool or a report run (its exit code), the install and a save found;
        /// null when the run goes on to the front end or a game.</summary>
        internal int? Prepare()
        {
            driveSnags = new DriveSnags();
            // `--scenario <name>`: a named test set-up — a level, a place to stand, who is stood where and
            // what is in hand — spelt out as the flags it stands for (`Scenarios`). What the command line
            // says itself comes first, so it wins over the scenario's.
            string[] scenarioSays = [];
            if (Array.IndexOf(args, "--scenario") is var sc and >= 0)
            {
                string? scenarioName = sc + 1 < args.Length ? args[sc + 1] : null;
                if (scenarioName is null || Scenarios.Find(scenarioName) is not { } chosen)
                {
                    Console.WriteLine("scenarios:");
                    foreach (var each in Scenarios.All) Console.WriteLine($"  {each.Name,-12} {each.What}");
                    return scenarioName is null || scenarioName == "list" ? 0 : 2;
                }
                Console.WriteLine($"scenario {chosen.Name}: {chosen.What}");
                Console.WriteLine($"scenario {chosen.Name}: {chosen.Try}");
                Console.WriteLine($"scenario {chosen.Name}: {string.Join(' ', chosen.Args)}");
                scenarioSays = [$"scenario {chosen.Name}: {chosen.What}", $"scenario {chosen.Name}: {chosen.Try}", $"scenario {chosen.Name}: {string.Join(' ', chosen.Args)}"];
                args = [.. args.Where((_, i) => i != sc && i != sc + 1), .. chosen.Args];
            }

            // Diagnostics first, so that everything after this has somewhere to complain. They are off
            // unless asked for: a run with no diagnostic flag logs warnings and errors and nothing else,
            // and writes no file. Asking for any developer view turns developer mode on with it, so nobody
            // has to remember to pass --debug as well.
            string[] Diagnostic =
            [
                "--scripts-all", "--modes", "--find", "--things", "--chronicles", "--storm",
                "--sky-groups", "--sky-only", "--sky-hide", "--sky-everywhere", "--verbose", "--gpu-debug",
                "--strict-scripts", "--player", "--run", "--gait", "--party", "--party-there", "--watch",
                "--actor-type",
                "--journal", "--arm", "--saves", "--hud", "--ui-right", "--talk", "--character",
                "--inventory", "--gold", "--vault", "--no-rollover", "--no-heads", "--give", "--buff",
                "--take-exit", "--entrance", "--hover", "--companion", "--spawn", "--no-cast", "--free-ammo", "--text-window", "--discipline-pane", "--effects", "--feed",
                "--advance", "--shop", "--shop-name", "--shop-click", "--pointer", "--say", "--film", "--cast", "--use", "--buy-use", "--party-drag",
            ];
            var diagArgs = Diagnostic.Any(Flag) && !Flag("--debug") ? [.. args, "--debug"] : args;
            // Which of the original's own faults the player has chosen to turn off. Not a
            // developer view: --fix is a player option and does not imply --debug. Before the
            // diagnostics, so the log's header can say what is in force.
            // What the reading complains of — a name that is no switch, a switch the feature registry leaves off —
            // goes to the console now and into the log once there is one: a windowed run has no console.
            switchSays = new List<string>();
            var fixes = Fixes.Configure(args, complain: SwitchSays);
            // And the other register: things the original never had, which are **on** unless turned off.
            // Kept apart from the fixes on purpose — when a run looks wrong you want to know in one glance
            // what is not 1.1, and the two answer different halves of that.
            qol = Qol.Configure(args, complain: SwitchSays);
            MenuChoices.Session.Over(fixes, qol);                        // what the Fixes page chose this session, over the command line
            // The two registers met: a switch the feature registry refuses beside the rest is left off, and said.
            Vitomir.Engine.Features.GameFeatures.Registry.Settle(fixes, qol, SwitchSays);
            // The rules (ours): 1.1's numbers, then the Classic module's rules.cfg, then each player module's, the later winning
            // (Engine.Rules). Before the log opens, so its features line says whether the rules are 1.1's.
            var rules = Vitomir.Engine.Rules.RulesFile.Configure(PlayerModules().Select(m => (m.Id, m.DataFolder)), SwitchSays);
            if (!rules.Rules.IsDefault) SwitchSays($"rules: {Vitomir.Engine.Rules.RulesFile.Describe(rules.Rules)}");
            // `--rules`: the resolved rules, a key a line with the layer each came from, then stop.
            if (Flag("--rules"))
            {
                foreach (var line in Vitomir.Engine.Rules.RulesFile.Report(rules)) Console.WriteLine(line);
                return 0;
            }
            // --diagnostics reports the settings a plain run would get, so it does not imply --debug — and
            // it opens no log of its own, because asking what the settings are should not cost the player
            // one of their twenty kept runs.
            // Once for the process: the runs after the first are the same player's session, one log.
            // The launch's random seed (`Chance`): the clock's for a player's game, the fixed one for
            // the harness (a still, a sweep, or what the host program's additions name), `--seed`'s when given. Once for the process, so
            // every stream carries on through the loads and new games that follow; the log's header says it.
            if (host.Runs == 0) Console.WriteLine($"random seed: {Chance.Start(Chance.Choose(Arg("--seed"), Arg("--ai-seed"), Chance.IsHarness(Flag) || RunAdditions.IsHarness(Flag), DateTime.Now)).Seed} ({Chance.Game!.Source})");
            if (host.Runs == 0) Diag.Configure(diagArgs, openLogFile: !Flag("--diagnostics"));
            else Diag.InForce("this run");
            // The log window (a viewer of its own; a developer's): --log-window (vitomir-tools' runs; the platform opens its
            // own for its switch) or the game's vitomir.ini, [log] window = on; the console's logwindow later. Windows alone, a game
            // in a window alone; the line in the log either way.
            if (host.Runs == 0 && (Flag("--log-window") || Diag.Settings.Window) && host.LogWindow("classic", true) is { } logWindow)
                Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diag.For("Vitomir"), "{Line}", logWindow);
            // Streamer mode: the platform's setting, which vitomir-platform has read from its own user data before the
            // module started; vitomir-tools keeps it in its own user data folder. --streamer turns it on for this run.
            if (host.Runs == 0 && Vitomir.Platform.Presentation.StreamerMode.Kept is null)
                Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diag.For("Vitomir"), "{Line}",
                    Vitomir.Platform.Presentation.StreamerMode.Start(Diag.UserDataFolder, args, SwitchSays));
            if (host.Runs == 0)
            {
                // The network-conditions simulator (item 1; a developer's): --net-sim latency=80,jitter=20,loss=2,... every
                // transport made from now on (else VITOMIR_NET_SIM's, a test run's); said on the console and in the log.
                if (Vitomir.Engine.Net.NetSim.Configure(Arg("--net-sim")) is { } simulated)
                {
                    Console.WriteLine(simulated);
                    Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diag.For("Vitomir.Net"), "{Line}", simulated);
                }
                // The network trace of the last session (item 6) beside the game's log, where the log bundle finds it; none with no log folder.
                if ((Diag.Folder?.Root ?? (Diag.FilePath is { } logFile ? Path.GetDirectoryName(Path.GetFullPath(logFile)) : null)) is { } logs)
                    Vitomir.Engine.Net.NetTrace.Folder = logs;
            }
            foreach (var line in scenarioSays) Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diag.For("Vitomir"), "{Line}", line);   // into the log too: a windowed run has no console
            foreach (var line in switchSays) Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(Diag.For("Vitomir"), "{Line}", line);
            if (Flag("--diagnostics"))
            {
                Console.WriteLine(Diag.Explain());
                Console.WriteLine($"fixes: {fixes}");
                foreach (var f in fixes.All) Console.WriteLine($"  {(f.On ? "on " : "off")} {f.Name,-14} {f.What}");
                Console.WriteLine($"quality of life: {qol}");
                foreach (var q in qol.All) Console.WriteLine($"  {(q.On ? "on " : "off")} {q.Name,-14} {q.What}");
                Console.WriteLine($"features: {Diag.FeaturesInForce}");
                return 0;
            }
            if (fixes.ToString() != new Fixes().ToString()) Console.WriteLine($"fixes: {fixes}");
            if (qol.ToString() != new Qol().ToString()) Console.WriteLine($"quality of life: {qol}");
            Console.WriteLine($"features: {Diag.FeaturesInForce}");      // the registry's line: the profile, the rules features on, the repairs

            // **Vitomir's own copy of the game's files** (`GameData`): made from the 1.1 install
            // once, and run from after that.
            // The data, export, cook, convert, stream-fly and glTF commands (vitomir-tools': IRunTools.Commands).
            if (tools?.Commands(ref args) is { } commandExit) return commandExit;

            // **The tester's log bundle** (item 6; a players' switch, menu-free, as /bundle on the chat line): the logs, the
            // network trace of the last session and this machine's build and data in one zip in the game's own folder, then stop.
            if (Flag("--log-bundle"))
            {
                using var bundled = GameFiles.Locate(Arg("--game")) is { } bundleDir ? new GameFiles(bundleDir, Args("--data").ToList(), PlayerModules()) : null;
                Console.WriteLine(Vitomir.Engine.Net.LogBundle.MakeHere(bundled is null ? null : Vitomir.Engine.Net.NetIdentity.Of(bundled)));
                return 0;
            }

            var gameDir = GameFiles.Locate(Arg("--game"));
            if (gameDir is null)
            {
                Console.Error.WriteLine("game data not found: run `vitomir-tools --install-data <your VTMR folder>` to make Vitomir's own game folder, " +
                                        "or pass --game <folder holding Resource.nob>");
                return 2;
            }
            if (GameData.Check(gameDir) is { } copyCheck)
            {
                Console.WriteLine($"game data: {gameDir} (Vitomir's game folder, made from {copyCheck.Manifest.Source}, {copyCheck.Manifest.Files.Count} files)");
                if (!copyCheck.Whole)
                {
                    foreach (var f in copyCheck.Missing.Take(5)) Console.Error.WriteLine($"game data: missing {f}");
                    foreach (var f in copyCheck.Changed.Take(5)) Console.Error.WriteLine($"game data: not the size it was made at: {f}");
                    Console.Error.WriteLine($"game data: {copyCheck.Missing.Count} missing, {copyCheck.Changed.Count} changed — run `vitomir-tools --install-data` again to mend it");
                }
            }
            else Console.WriteLine($"game data: {gameDir} (the 1.1 install itself; `vitomir-tools --install-data` makes Vitomir's own game folder)");
            // The Options screen's settings: masquerade.ini's, then vitomir.ini's [options].
            GameOptions.Configure(gameDir);
            if (Flag("--devmode")) GameOptions.Current.Flags |= GameOptions.DevMode;   // 1.1's `-devmode`
            Vitomir.Engine.Save.Jsonc.Strict = Flag("--save-strict-json");             // saves and .vplayer without comments (ours)
            // The key bindings: 1.1's keys, then vitomir.ini's [keys].
            KeyBindings.Configure(complain: Console.WriteLine);
            (GameOptions.Current, KeyBindings.Current) = (MenuChoices.Session.Over(GameOptions.Current), MenuChoices.Session.Over(KeyBindings.Current));   // this session's choices
            // How the interface's sheet is laid on the picture (ours, UiLayout): the 16:9 stretch, fitted, or 1.1's stretch.
            Vitomir.Engine.Ui.UiLayout.Configure(Arg("--ui-layout"), Console.WriteLine);
            // Whose lettering 1.1's menus wear (ours, MenuLettering): ours, or 1.1's own pictures of its words.
            Vitomir.Engine.Ui.MenuLettering.Configure(Arg("--menu-lettering"), Console.WriteLine);
            // How far up the game camera turns, whether it is smoothed on steps and how far out it zooms (ours, CameraChoices;
            // presentation); what is in its way is seen through in a soft window, or by the developer's x-ray.
            Vitomir.Engine.World.CameraChoices.Configure(Arg("--camera-up"), Arg("--camera-steps"), Console.WriteLine,
                                                         outFlag: Arg("--camera-out"), xray: Flag(Vitomir.Engine.World.CameraChoices.XRayFlag),
                                                         plain: Flag(Vitomir.Engine.World.CameraChoices.PlainFlag));
            // Her own walk predicted on a player's machine (Engine.Net.OwnWalk): on unless the developer's --no-predict, for
            // comparison. Before the tape's settings, which put a recording's own in force (older one: off).
            Vitomir.Engine.Net.OwnWalk.Enabled = !Flag("--no-predict");
            Vitomir.Engine.Net.OwnStrike.Enabled = !Flag("--no-predict");       // and the start of her own swing (the second part)
            Vitomir.Engine.Net.OwnCast.Enabled = !Flag("--no-predict");         // and of her own discipline
            Vitomir.Engine.Net.OwnWalk.DrivesEnabled = !Flag("--no-predict");   // and her own keyboard walk
            InputTape.Settings(fixes, qol);                                  // a tape's: written, or the recorded ones in force
            if (!KeyBindings.Current.AreDefaults) Console.WriteLine($"keys: {KeyBindings.Current}");
            levelName = Arg("--level") ?? "P1_HAVN";
            headless = Arg("--headless");
            // The clicks `--menu-click` asks a windowed run to make (see `ScriptedClick`).
            clickQueue = (Arg("--menu-click") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(at => at.Split(',').Select(F).ToArray()).Where(p => p.Length == 2).ToList();
            clickDown = false;
            // The camera's pull-in last frame, for the journal row below (`CameraPull`).
            wasPulled = null;
            // 1600x900 by default: the size the 1.1 screenshots are taken at, so a shot of ours and a
            // shot of the original can be laid over each other and measured rather than eyeballed. That
            // comparison has found bugs more than once, which is why it is the default and not a
            // flag.
            var size = (Arg("--size") ?? "1600x900").Split('x');
            width = int.Parse(size[0]); height = int.Parse(size[1]);
            // A replay's window is the size its recording's was from the start (ours): the menus place a click
            // by the window's size, and a recording from the title screen names no size until the game's loop.
            if (InputTape.Replaying && Arg("--size") is null && InputTape.FirstSize is { } taped) (width, height) = taped;
            // `--replay-size WxH` (a harness knob, ours): a replay drawn at another size than its recording's, to
            // see what the picture's size costs. Only for a recording with no click in it — a click is placed by the
            // window's size, so a recording with one would go another way and leave its tape.
            if (InputTape.Replaying && Arg("--replay-size")?.Split('x') is { Length: 2 } replaySize
                && int.TryParse(replaySize[0], out int replayWide) && int.TryParse(replaySize[1], out int replayHigh))
                (width, height) = (replayWide, replayHigh);

            var dataFolders = Args("--data").ToList();
            var modules = PlayerModules();
            // The recooked base game (ours, `Recooked`): beside the imported data, made the first time a game starts with none of
            // this program's way (an import made before there was one, or a program that recooks otherwise); `--recooked <folder>`
            // names one (a developer's, over an install).
            string? recookedFolder = Arg("--recooked");
            if (recookedFolder is null && GameFiles.RecookedBeside(gameDir) is { } beside && Vitomir.Formats.Recooked.VersionOf(beside) != Vitomir.Formats.Recooked.Version)
            {
                var began = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var made = Vitomir.Formats.Recooked.Make(gameDir, beside, "Vitomir");
                    Console.WriteLine($"recooked: {made.Templates} templates, {made.Scenes} scenes and {made.Paths} camera paths into {beside} ({began.ElapsedMilliseconds} ms)");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or Vitomir.Formats.NodFormatException)
                {
                    Console.WriteLine($"recooked: not made ({e.Message}); the original files are read instead");
                }
            }
            files = new GameFiles(gameDir, dataFolders, modules, recookedFolder);
            if (files.RecookedFolder is { } rf) Console.WriteLine($"recooked: {rf}");
            foreach (var d in dataFolders) Console.WriteLine($"data folder: {d}");
            foreach (var m in modules)
                Console.WriteLine($"player module: {m.Id} {m.Version} ({Vitomir.Platform.PlayerModules.PackKindWords.Words(m.Kinds)}{(m.Rules ? ", a rules module" : "")}): {m.DataFolder}");

            // The interface art tool (ours, UiArtCommands): a pane's art as one
            // picture and an outline to paint to, and a painted picture cut into the files the engine reads.
            // The interface art tool, `--sweep` and `--story-sweep` (vitomir-tools': IRunTools.WithFiles).
            if (tools?.WithFiles(args, files) is { } filesExit) return filesExit;

            // The campaign's map: which places exist and how they join up. Loose files under Chronicles\,
            // in no archive, so an install without them simply has no sequence.
            chronicles = ChronicleSet.Load(files);
            // Multiplayer's windowless runs: a host nobody plays on, a player that plays itself (`NetRuns`); the program's
            // run after this start, a frame at a time, as the front end and the game are (ClassicProgram.Frame).
            if (NetRuns.Wanted(args)) { windowless = true; return null; }

            // **A save of Vitomir's own** (`Engine.Save`). What it says wins over --level,
            // --scene, --entrance and the player template: the chronicle is put back first, because which
            // scene a place is built with is the chronicle's to say, and the place the party is in is then
            // built from its level and scene and given back everything in it.
            if (tools?.ListSaves(args) is { } listExit) return listExit;   // `--list-saves` (vitomir-tools' report)
            loaded = null;
            loadedPlace = null;
            loadedChronicle = null;
            // A save that does not remember the place the party is in — one imported from 1.1 — has it
            // built as it was first made, the way a journey into it would.
            freshScene = null;
            // --import: a game out of 1.1's own save (`SaveImport`) — the party and the
            // story from its `!main.nsg`, and every place as its `.nsw` left it (`NswImport`).
            // Given a folder (`SaveGames\SP4`) or the file itself. With --import-to it is written as a
            // Vitomir save and the run stops; without, the game starts from it as a load does.
            // A 1.1 character file (`Crypt\npf%06d.npf`) is Vitomir's own character once imported (`OriginalCharacter`): written to
            // --import-to as a `.vplayer` (named --import-name's name when there is one), and the run stops.
            if (Arg("--import") is { } npfPath && npfPath.EndsWith(".npf", StringComparison.OrdinalIgnoreCase) && File.Exists(npfPath))
                return ImportCharacter(npfPath);
            if (Arg("--import") is { } importPath && !importPath.StartsWith("--"))
            {
                string main = Directory.Exists(importPath) ? Path.Combine(importPath, "!main.nsg") : importPath;
                NsgSave original;
                try { original = NsgSave.ReadFile(main); }
                catch (Exception e) when (e is IOException or NodFormatException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"import: {main} could not be read: {e.Message}");
                    return 2;
                }
                var notes = new List<string>();
                loaded = SaveImport.FromOriginal(files, chronicles, original, importPath, notes);
                foreach (var note in notes) Console.WriteLine($"import: {note}");
                if (loaded is null) return 2;
                if (Arg("--import-to") is { } importTo && !importTo.StartsWith("--"))
                {
                    string written = ResolveSave(importTo);
                    SaveArchive.Write(loaded, written);
                    Console.WriteLine($"import: written to {written}");
                    return 0;
                }
            }
            if (loaded is null && Arg("--load") is { } loadPath && !loadPath.StartsWith("--"))
            {
                try { loaded = SaveArchive.Read(ResolveSave(loadPath)); }
                catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"load: {loadPath} could not be read: {e.Message}");
                    // From the menus, back to the main menu with the reason on it (ours): until
                    // then the program simply ended. From a command line, the exit code as before.
                    if (Flag("--play") && Arg("--headless") is null)
                    {
                        host.MenuNotice = ($"The save could not be read.\n{e.Message}" +
                                      (File.Exists(SaveArchive.PreviousOf(ResolveSave(loadPath))) ? $"\nIts previous version is kept as {Path.GetFileName(SaveArchive.PreviousOf(ResolveSave(loadPath)))}." : ""));
                        host.NextRun = [.. RunSettings.Settings(args), "--menu"];
                        return 0;
                    }
                    return 2;
                }
            }
            if (loaded is not null)
            {
                // A save hosted as a network game starts at the revive location, and only a network game's (NetSaves.Start).
                if (NetPlay.Hosting(args))
                {
                    if (Vitomir.Engine.Net.NetSaves.Start(loaded, chronicles, out string notHosted) is not { } hostedAt) { Console.Error.WriteLine($"net: {notHosted}"); return 2; }
                    loaded = Vitomir.Engine.Net.NetSaves.AtRevive(loaded, hostedAt);
                }
                if (loaded.Chronicle is { } savedStory && chronicles[savedStory.Name] is { } story)
                {
                    SaveApply.Chronicle(story, savedStory);
                    loadedChronicle = story;
                }
                if (loaded.World.TryGetValue(loaded.Header.Location, out loadedPlace)) levelName = loadedPlace.Level;
                else if (loadedChronicle?.At(loadedChronicle.Current) is { LevelName.Length: > 0 } arriving)
                {
                    levelName = arriving.LevelName;
                    freshScene = arriving.SceneName.Length > 0 ? arriving.SceneName : null;
                    Console.WriteLine($"load: the save does not remember {loaded.Header.Location}; it is built as it was first made");
                }
                else
                {
                    Console.Error.WriteLine($"load: the save has no record of {loaded.Header.Location}, the place it was taken in");
                    return 2;
                }
                Console.WriteLine($"load: {loaded.Header.Description} — {loaded.Header.Chronicle}, {loaded.World.Count} places remembered, " +
                                  $"{loaded.Party.Members.Count} in the party");
                // The switches the save was played with against the ones in force: each difference said.
                Vitomir.Engine.Features.LoadSwitches.Report(loaded.Header, fixes, qol, Console.WriteLine);
                // The rules modules the save was made with against the ones on: each difference said (the load goes on).
                Vitomir.Engine.Save.LoadModules.Report(loaded.Header, files.RulesModules, Console.WriteLine);
            }
            // `--chronicles`, `--saves`, `--gait` (vitomir-tools' reports: IRunTools.Reports).
            if (tools?.Reports(args, chronicles, gameDir, files) is { } reportsExit) return reportsExit;
            return null;
        }

        /// <summary>
        /// **1.1's character imported**: the file read whole (<see cref="Npf"/>: the header 1.1's list checks, then the player to its last
        /// byte), made into Vitomir's character (<see cref="Vitomir.Engine.Net.OriginalCharacter"/>) and written as the <c>.vplayer</c> text a
        /// <see cref="Vitomir.Engine.Net.Crypt"/> keeps, at --import-to. The setup's import runs it (<c>ProgramSaveImporter</c>), one process a
        /// character as a save; a name clash is the setup's to refuse.
        /// </summary>
        int ImportCharacter(string npfPath)
        {
            if (Arg("--import-to") is not { } to || to.StartsWith("--"))
            {
                Console.Error.WriteLine("import: a character's import writes a .vplayer: name the file with --import-to <file> (the setup does: vitomir-platform --setup import-saves npf000000)");
                return 2;
            }
            NpfCharacter original;
            try { original = Npf.ReadFile(npfPath); }
            catch (Exception e) when (e is IOException or NodFormatException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"import: {npfPath} could not be read: {e.Message}");
                return 2;
            }
            var notes = new List<string>();
            var character = Vitomir.Engine.Net.OriginalCharacter.From(original, notes, files, Arg("--import-name") is { } named && !named.StartsWith("--") ? named : null);
            foreach (var note in notes) Console.WriteLine($"import: {note}");
            if (character.Name.Length == 0) { Console.Error.WriteLine($"import: {npfPath} names nobody"); return 2; }
            string written = Path.GetFullPath(to);
            Directory.CreateDirectory(Path.GetDirectoryName(written)!);
            File.WriteAllText(written, new Vitomir.Engine.Net.Crypt(Path.GetDirectoryName(written)!).Text(character));
            Console.WriteLine($"import: written to {written}");
            return 0;
        }

        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }

        bool Flag(string name) => Array.IndexOf(args, name) >= 0;

        IEnumerable<string> Args(string name) => args.Select((a, i) => (a, i)).Where(t => t.a == name && t.i + 1 < args.Length).Select(t => args[t.i + 1]);

        /// <summary>The player's modules on, in load order: each <c>--player-module &lt;folder&gt;</c> the platform's start gives, read from its record.</summary>
        List<Vitomir.Platform.PlayerModules.PlayerModuleLayer> PlayerModules() =>
            [.. Args("--player-module").Select(Vitomir.Platform.PlayerModules.PlayerModuleLayer.Open)];

        float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        void SwitchSays(string m) { Console.Error.WriteLine(m); switchSays.Add(m); }
    }
}
