// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Vitomir.Engine.Diagnostics;
using Vitomir.Engine.Net;
using Vitomir.Engine.Ui;
using Vitomir.Engine.World;
using Vitomir.Platform.Modules;

namespace Vitomir;

/// <summary>
/// **The replay harness** (ours — 1.1 has nothing like it;
/// <c>--record</c>, <c>--replay</c> and <c>--replay-check</c>). Everything the window's loop reads from
/// outside itself comes through here: the launch (<see cref="Launch"/>, <see cref="Settings"/>), each event
/// (<see cref="Poll"/>), the mouse, the modifiers and the keyboard polled (<see cref="Buttons"/>, <see cref="Mods"/>,
/// <see cref="Keyboard"/>), the frame's step (<see cref="Step"/>) and a film's clock (<see cref="Clock"/>), all in the
/// platform's neutral terms (nothing of SDL's here; the live input is the host's,
/// <see cref="Live"/>). With <c>--record &lt;tape&gt;</c> each is read from the host as always and written to the tape
/// (<see cref="ReplayTape"/>; an event as an <c>in</c> line, <see cref="TapeInput"/>) as the loop reads it; with
/// <c>--replay &lt;tape&gt;</c> or <c>--replay-check &lt;tape&gt;</c> each comes from the tape (an old tape's <c>ev</c>
/// lines read as the same neutral events) and the host's own input is thrown away, so the run is the recorded one
/// again, windowed or offscreen. At the end of every
/// stepped frame <see cref="FrameEnd"/> takes the simulation's digest (<see cref="SimDigest"/>): a recording
/// writes it beside the tape (<c>&lt;tape&gt;.digest</c>), a replay beside it too (<c>&lt;tape&gt;.replay.digest</c>),
/// and a check compares it with the recorded one frame by frame and stops at the first frame and field that
/// differ. With neither flag every call is the host's own.
///
/// **The save folder is the tape's too** (<see cref="SaveFolder"/>, <see cref="TapeSaves"/>): a recording keeps
/// the folder it began with beside the tape, and a replay reads and writes a scratch copy of it, so a
/// recording that loads a slot, or saves and loads its own save, replays whatever the player's folder holds
/// by then.
/// </summary>
static class InputTape
{
    enum Mode { Off, Record, Replay }
    static Mode mode;
    static TapeWriter? writer;
    static TapeReader? reader;
    static DigestWriter? digest;
    static DigestChecker? checker;
    // (The headless loop's copy is retired.) A run's own digests with no tape, `--digest <file>` and `--digest-check <file>`,
    // which the module's headless loop wrote and checked until then (vitomir-platform --game classic --headless --frames n).
    static DigestWriter? ownDigest;
    static DigestChecker? ownChecker;
    static MenuTrail? trail;                     // a check's recorded steps through the menus
    static string? tapePath;
    static bool stopped;
    static int exitCode;
    static int frame;
    static ulong clockZero;
    const long ProgressEveryMs = 15_000;
    static long progressAt = Environment.TickCount64;

    // The input as the loop last polled it (the tape's `state`): what a recording and its replay both read between polls.
    static float mouseX, mouseY;
    static uint buttons;
    static LoopMods mods;
    const int KeyCount = 512;
    static readonly bool[] keyboard = new bool[KeyCount];
    static readonly SortedSet<int> keysDown = [];

    /// <summary>The flags whose input a replay has from the tape instead, and so leaves out of the launch: SDL's queue fed by the harness.</summary>
    static readonly string[] Fed = ["--ui-click", "--ui-type", "--menu-steps"];

    /// <summary>
    /// The files a recording wrote as it was played — its sound, its own log file, and those the host program's additions
    /// name (<see cref="RunAddition.Outputs"/>). A replay leaves them out of its launch, so replaying a recording never writes
    /// over the recording's own files: until then a replay of `play02.tape` opened `C:\Games\Vitomir\play02.tsv` again
    /// and started it afresh. Named again after the tape, they are the replay's own.
    /// </summary>
    static readonly string[] Outputs = ["--record-sound", "--log-file"];

    static bool IsOutput(string flag) => Array.IndexOf(Outputs, flag) >= 0 || RunAdditions.Names(flag, a => a.Outputs);

    /// <summary>Whether a tape is being written or read.</summary>
    public static bool On => mode != Mode.Off;
    /// <summary>Whether a tape is being read.</summary>
    public static bool Replaying => mode == Mode.Replay;

    /// <summary>A replay that checks its digest (`--replay-check`): its time is the tape's, so it need not wait for the screen.</summary>
    public static bool Checking => checker is not null;

    /// <summary>
    /// A replay nobody is watching, which need not wait for the screen: a check, or a replay given
    /// <c>--replay-fast</c> (what <c>--replay-folder --rebase</c> runs a tape with, <see cref="ReplayFolder"/>).
    /// Its frames' steps are the tape's either way, so the simulation is the same at any pace.
    /// </summary>
    public static bool Unwatched => checker is not null || (fast && mode == Mode.Replay);

    /// <summary><c>--replay-full</c>: a replay nobody watches shown at its own size all the same (ours).</summary>
    public static bool Full { get; private set; }

    /// <summary><c>--replay-slot N</c>: where on the screen a replay's small window stands (ours, <c>--replay-folder</c> gives each check one).</summary>
    public static int Slot { get; private set; }
    static bool fast;

    /// <summary>
    /// The process's arguments with the tape's own taken out. <c>--record</c>: the tape opened and the launch
    /// written — the arguments, and a <c>--seed</c> of the clock's when none was given, so the run is
    /// repeatable (<see cref="Vitomir.Engine.Ai.Chance"/>: a seeded run reads no clock for its treasure either);
    /// the save folder as it stands, kept beside the tape and listed (<see cref="TapeSaves.Keep"/>); the save it
    /// loads, with its SHA-256. <c>--replay</c>/<c>--replay-check</c>: the recorded arguments, less
    /// the harness's fed input (<see cref="Fed"/>), with anything else given on the replay's own line after them;
    /// and the scratch save folder made from the kept one (<see cref="TapeSaves.Seed"/>) when the tape lists it.
    /// </summary>
    public static string[] Launch(string[] args)
    {
        // The tape's own flags, taken off before the launch is put together (a launch of settings alone is the front end).
        if (Array.IndexOf(args, "--replay-fast") >= 0) { fast = true; args = [.. args.Where(a => a != "--replay-fast")]; }
        // And the window's (ours): `--replay-full` and `--replay-slot N`. Left in the launch they read as
        // a start, so a replay of a play begun at the title screen skipped it and left its tape at frame 0 — every one
        // of a play-test's recordings under `--replay-folder`, which gives each check a slot.
        if (Array.IndexOf(args, "--replay-full") >= 0) { Full = true; args = [.. args.Where(a => a != "--replay-full")]; }
        if (Array.IndexOf(args, "--replay-slot") is var slotAt and >= 0)
        {
            if (slotAt + 1 < args.Length && int.TryParse(args[slotAt + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) && slot >= 0) Slot = slot;
            args = [.. args.Where((_, i) => i != slotAt && i != slotAt + 1)];
        }
        // The run's own digest, written (`--digest <file>`) or checked (`--digest-check <file>`) at each stepped frame's end, as a
        // recording's is, with no tape: the frames of a windowless run (`--headless --frames n` under the platform) or of any other.
        if (Array.IndexOf(args, "--digest") is var writeAt and >= 0 && writeAt + 1 < args.Length)
        {
            string file = args[writeAt + 1];
            ownDigest = new DigestWriter(LogFile.CreateText(file), $"{Path.GetFileName(file)}, the frames of a run (--digest), on {RuntimeInformation.RuntimeIdentifier}");
            args = [.. args.Where((_, i) => i != writeAt && i != writeAt + 1)];
        }
        if (Array.IndexOf(args, "--digest-check") is var checkAt and >= 0 && checkAt + 1 < args.Length)
        {
            string file = args[checkAt + 1];
            if (LogFile.Find(file) is not { } found) { Console.Error.WriteLine($"classic: --digest-check: no digest at {file}"); Environment.Exit(2); return []; }
            ownChecker = new DigestChecker(DigestReader.Open(found));
            args = [.. args.Where((_, i) => i != checkAt && i != checkAt + 1)];
        }
        int rec = Array.IndexOf(args, "--record"), rep = Array.IndexOf(args, "--replay"), chk = Array.IndexOf(args, "--replay-check");
        if ((rec >= 0 || rep >= 0 || chk >= 0) && Array.IndexOf(args, "--headless") >= 0)
        {
            Console.Error.WriteLine("--record and --replay work on the window's loop: leave out --headless (SDL_VIDEO_DRIVER=offscreen runs it with no window)");
            Environment.Exit(2);
        }
        if (rec >= 0 && rec + 1 < args.Length)
        {
            tapePath = ReplayTape.NameIn(args[rec + 1], DateTime.Now);        // a folder named: a tape of its own name in it
            if (Path.GetDirectoryName(Path.GetFullPath(tapePath)) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            var rest = args.Where((_, i) => i != rec && i != rec + 1).ToList();
            if (!rest.Contains("--seed")) rest.AddRange(["--seed", Vitomir.Engine.Ai.Chance.FromClock(DateTime.Now).ToString(CultureInfo.InvariantCulture)]);
            mode = Mode.Record;
            writer = new TapeWriter(LogFile.CreateText(tapePath), $"recorded {DateTime.Now:yyyy-MM-dd HH:mm:ss} on {RuntimeInformation.RuntimeIdentifier} by {Vitomir.Engine.Ui.FrontEnd.Version}");
            // --password's value is never written: the tape has ****, and the host's password check is taped instead
            // (NetSide.WrongPassword), so a replay answers a join as the recording did.
            foreach (var a in Vitomir.Platform.Modules.SecretSwitches.Masked(rest)) writer.Write("arg", ReplayTape.Escape(a));
            // The save folder as it stands, kept beside the tape and listed on it (TapeSaves): the count even when it is 0.
            var kept = TapeSaves.Keep(PlayersSaves, tapePath);
            writer.Write("saves", kept.Count.ToString(CultureInfo.InvariantCulture), "bytes=" + kept.Sum(k => k.Bytes).ToString(CultureInfo.InvariantCulture));
            foreach (var k in kept) writer.Write("savefile", ReplayTape.Escape(k.Name), "bytes=" + k.Bytes.ToString(CultureInfo.InvariantCulture), "sha256=" + k.Sha);
            if (Value(rest, "--load") is { } save) writer.Write("save", ReplayTape.Escape(save), "sha256=" + Sha(save));
            digest = new DigestWriter(LogFile.CreateText(LogFile.Beside(tapePath, ".digest")), $"{Path.GetFileName(tapePath)}, recorded on {RuntimeInformation.RuntimeIdentifier}");
            Vitomir.Engine.Net.NetTape.Tape = new NetSide();
            Console.WriteLine($"record: {Path.GetFullPath(tapePath)}");
            return [.. rest];
        }
        int at = rep >= 0 ? rep : chk;
        if (at < 0 || at + 1 >= args.Length) return args;
        // Plain or compressed, and found with `.gz` added or taken off (`LogFile`).
        tapePath = LogFile.Find(args[at + 1]) ?? args[at + 1];
        mode = Mode.Replay;
        Vitomir.Engine.Diagnostics.SettingsFile.Replaying = true;           // the recorded choices are the replay's, not this machine's file
        reader = TapeReader.Open(tapePath);
        FirstSize = reader.FirstSize();
        Vitomir.Engine.Net.NetTape.Tape = new NetSide();
        var launch = new List<string>();
        while (reader.Peek is { Kind: "arg" }) launch.Add(ReplayTape.Unescape(reader.Next()!.Value.Fields[0]));
        // The saves the recording began with: this run's save folder is a scratch copy of them (TapeSaves). A tape
        // with no such line is an older one, and reads the player's folder as it always did.
        if (reader.Peek is { Kind: "saves" })
        {
            reader.Next();
            var recorded = new List<TapeSaves.Kept>();
            while (reader.Peek is { Kind: "savefile" } f)
            {
                reader.Next();
                recorded.Add(new TapeSaves.Kept(ReplayTape.Unescape(f.Fields.FirstOrDefault() ?? ""), f.Long("bytes"), f["sha256"] ?? "-"));
            }
            replaySaves = TapeSaves.Seed(tapePath, recorded, Path.Combine(Scratch(), "Saves"));
        }
        if (reader.Peek is { Kind: "save" } s)
        {
            reader.Next();
            string file = ReplayTape.Unescape(s.Fields[0]);
            if (s["sha256"] is { } was && Sha(file) != was)
                Console.WriteLine($"replay: the save {file} is not the one recorded (its SHA-256 differs); the replay will not be the recording");
        }
        for (int i = 0; i < launch.Count; i++)
            if (Array.IndexOf(Fed, launch[i]) >= 0) { launch.RemoveAt(i); if (i < launch.Count) launch.RemoveAt(i); i--; }
        for (int i = 0; i < launch.Count; i++)
            if (IsOutput(launch[i]))
            {
                if (i + 1 < launch.Count) Console.WriteLine($"replay: the recording's {launch[i]} {launch[i + 1]} is left alone");
                launch.RemoveAt(i); if (i < launch.Count) launch.RemoveAt(i); i--;
            }
        launch.AddRange(args.Where((_, i) => i != at && i != at + 1));
        // A check is a test: it costs the player none of his kept logs (the log is not the simulation).
        if (chk >= 0 && !launch.Contains("--no-log-file")) launch.Add("--no-log-file");
        if (chk >= 0)
        {
            string beside = LogFile.Beside(tapePath, ".digest");
            if (LogFile.Find(beside) is not { } recorded) { Console.Error.WriteLine($"replay-check: no digest beside the tape ({beside})"); DropScratch(); Environment.Exit(2); return []; }
            checker = new DigestChecker(DigestReader.Open(recorded));
            trail = MenuTrail.Read(recorded);
        }
        else digest = new DigestWriter(LogFile.CreateText(LogFile.Beside(tapePath, ".replay.digest")), $"{Path.GetFileName(tapePath)}, replayed on {RuntimeInformation.RuntimeIdentifier}");
        Console.WriteLine($"{(chk >= 0 ? "replay-check" : "replay")}: {Path.GetFullPath(tapePath)}, {reader.FrameCount} frames: {string.Join(' ', launch)}");
        return [.. launch];
    }

    static string? Value(IReadOnlyList<string> args, string name)
    {
        for (int i = 0; i + 1 < args.Count; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    static string Sha(string file)
    {
        // A bare name is looked for in the save folder, as `--load` looks for it (`--load SP3`
        // was hashed as a file in the working folder, so a changed save went unnoticed).
        if (!File.Exists(file) && !Directory.Exists(file) && file.IndexOfAny(['/', '\\']) < 0)
            file = Path.Combine(SaveFolder(),
                file.EndsWith(Vitomir.Engine.Save.SaveArchive.Extension, StringComparison.OrdinalIgnoreCase) ? file : file + Vitomir.Engine.Save.SaveArchive.Extension);
        try
        {
            if (File.Exists(file)) return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));
            if (Directory.Exists(file))
                return Convert.ToHexStringLower(SHA256.HashData(Directory.EnumerateFiles(file, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
                    .SelectMany(File.ReadAllBytes).ToArray()));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return "-";
    }

    /// <summary>
    /// A run's settings, which come from the player's files as well as the command line: the Fixes and the
    /// quality-of-life switches in force, the key bindings (those not 1.1's), and the Options screen's values.
    /// A recording writes them each run; a replay puts the recorded ones in force over whatever the files say now.
    /// </summary>
    /// <summary>
    /// The qualities of life, on by default, that came after recordings were already being made: a recording whose
    /// <c>qol</c> line does not name one was made without it and replays with it off, whatever this run's default.
    /// </summary>
    public static readonly string[] QolNewerThanTapes = ["heldshift", "spawnwalls", "partyorder"];

    public static void Settings(Fixes fixes, Qol qol)
    {
        var options = GameOptions.Current;
        var optionProps = typeof(GameOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.CanWrite && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum)).OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        if (mode == Mode.Record)
        {
            writer!.Write("fixes", [.. fixes.All.Select(f => $"{f.Name}={(f.On ? 1 : 0)}")]);
            writer.Write("qol", [.. qol.All.Select(q => $"{q.Name}={(q.On ? 1 : 0)}")]);
            // The rules in force (ours, the rules files' numbers): their hash, and each key that differs from 1.1's with its value.
            writer.Write("rules", Vitomir.Engine.Rules.RulesFile.TapeFields(Vitomir.Engine.Rules.GameRules.Current));
            var faithful = new KeyBindings().Values().ToDictionary(p => p.Key, p => p.Value);
            writer.Write("keys", [.. KeyBindings.Current.Values().Where(p => faithful.GetValueOrDefault(p.Key) != p.Value)
                                                                  .Select(p => $"{p.Key}={ReplayTape.Escape(p.Value)}")]);
            writer.Write("options", [.. optionProps.Select(p => $"{p.Name}={Convert.ToString(p.GetValue(options), CultureInfo.InvariantCulture)}")]);
            // How the interface's sheet lay on the picture (ours, wide, fit or stretch): a click is placed by it.
            // And whose lettering 1.1's menus wore (ours): the Options screen's rows are not where 1.1 has them.
            // The 16:9 stretch says `sheet=16x9` after it (UiLayout.TapeFields): the earlier builds' `wide` was
            // 1.1's stretch on a picture narrower than 16:9, and theirs is the word alone. And `panes=centred`:
            // The earlier build's panes rode the bottom-seated sheet on such a picture, and its tapes say `sheet=16x9` and no more.
            writer.Write("interface", [.. Vitomir.Engine.Ui.UiLayout.TapeFields(Vitomir.Engine.Ui.UiLayout.Mode),
                         $"lettering={Vitomir.Engine.Ui.MenuLettering.Word(Vitomir.Engine.Ui.MenuLettering.Mode)}"]);
            // Whether a player's machine predicts her own walk (Engine.Net.OwnWalk): it changes where she is between snapshots;
            // and (the second part, `strikes`) the start of her own swing (Engine.Net.OwnStrike); and (`casts`) of her own
            // discipline (Engine.Net.OwnCast); and (`drives`) her own keyboard walk (Engine.Net.OwnWalk.Drive).
            if (!Vitomir.Engine.Net.OwnWalk.Enabled) writer.Write("netpredict", "off");
            else
            {
                var fields = new List<string> { "on" };
                if (Vitomir.Engine.Net.OwnStrike.Enabled) fields.Add("strikes");
                if (Vitomir.Engine.Net.OwnCast.Enabled) fields.Add("casts");
                if (Vitomir.Engine.Net.OwnWalk.DrivesEnabled) fields.Add("drives");
                writer.Write("netpredict", [.. fields]);
            }
            // How far up the game camera turned and whether it was smoothed on steps (ours, CameraChoices): the eye, so where a
            // click lands on the world.
            writer.Write("camera", Vitomir.Engine.World.CameraChoices.TapeFields());
        }
        else if (mode == Mode.Replay)
        {
            if (Take("fixes") is { } f)
                foreach (var field in f.Fields) { var (n, v) = Pair(field); fixes[n] = v == "1"; }
            if (Take("qol") is { } q)
            {
                // A recording made before a quality of life existed was played without it: the ones on by default whose
                // absence from the line means off (QolNewerThanTapes), then what the line says.
                foreach (var n in QolNewerThanTapes) qol[n] = false;
                foreach (var field in q.Fields) { var (n, v) = Pair(field); qol[n] = v == "1"; }
            }
            // The recording's rules, whatever this machine's files say; a tape older than the line was played by 1.1's numbers.
            var taped = reader!.Peek is { Kind: "rules" } ? reader.Next() : null;
            Vitomir.Engine.Rules.GameRules.Current = Vitomir.Engine.Rules.RulesFile.FromTape(taped?.Fields, Console.WriteLine);
            if (Take("keys") is { } k)
            {
                var keys = new KeyBindings();
                keys.Apply(IniFile.Parse($"[{KeyBindings.Section}]\n" + string.Concat(k.Fields.Select(field => { var (n, v) = Pair(field); return $"{n} = {ReplayTape.Unescape(v)}\n"; }))));
                KeyBindings.Current = keys;
            }
            if (Take("options") is { } o)
                foreach (var field in o.Fields)
                {
                    var (n, v) = Pair(field);
                    if (optionProps.FirstOrDefault(p => p.Name == n) is { } p)
                        p.SetValue(options, p.PropertyType.IsEnum ? Enum.Parse(p.PropertyType, v) : Convert.ChangeType(v, p.PropertyType, CultureInfo.InvariantCulture));
                }
            // The layout the recording was made in, whatever this machine's is: wide (the default), fit or
            // stretch. A recording from before the line was written was made in 1.1's stretch, the only layout
            // there was. (A build earlier knows no `wide` and passes over the word, so replays such a tape stretched:
            // the same up to 16:9, its clicks off the sheet's place beyond it.) `wide` without `sheet=16x9` is the earlier build's and
            // The older layout, 1.1's stretch on a picture narrower than 16:9, and is replayed as that (UiLayout.FromTape).
            // `wide sheet=16x9` without `panes=centred` is the earlier build's, its panes on the bottom-seated sheet, likewise.
            var layout = Vitomir.Engine.Ui.UiLayoutMode.Stretch;
            // Likewise the lettering: an older recording was made with 1.1's own pictured words.
            var lettering = Vitomir.Engine.Ui.MenuLetteringMode.Original;
            if (reader!.Peek is { Kind: "interface" } && reader.Next() is { } said)
            {
                string? layoutWord = null, sheetWord = null, panesWord = null;
                foreach (var field in said.Fields)
                {
                    var (n, v) = Pair(field);
                    if (n == Vitomir.Engine.Ui.UiLayout.Key) layoutWord = v;
                    if (n == Vitomir.Engine.Ui.UiLayout.TapeSheetKey) sheetWord = v;
                    if (n == Vitomir.Engine.Ui.UiLayout.TapePanesKey) panesWord = v;
                    if (n == "lettering" && Vitomir.Engine.Ui.MenuLettering.Parse(v) is { } l) lettering = l;
                }
                if (Vitomir.Engine.Ui.UiLayout.FromTape(layoutWord, sheetWord, panesWord) is { } m) layout = m;
            }
            Vitomir.Engine.Ui.UiLayout.Mode = layout;
            Vitomir.Engine.Ui.MenuLettering.Mode = lettering;
            // A older recording has no line and was made with no prediction (Engine.Net.OwnWalk); one of the earlier build's first
            // part says `on` alone, her walk and not her swing (Engine.Net.OwnStrike), which came with `strikes`; and her own
            // discipline's start (Engine.Net.OwnCast) with `casts` — a tape of the earlier build's replays without it, as recorded; and her
            // keyboard walk (Engine.Net.OwnWalk.Drive) with `drives`, so an older tape replays without it.
            var predict = reader.Peek is { Kind: "netpredict" } ? reader.Next() : null;
            Vitomir.Engine.Net.OwnWalk.Enabled = predict?.Fields.FirstOrDefault() == "on";
            Vitomir.Engine.Net.OwnStrike.Enabled = Vitomir.Engine.Net.OwnWalk.Enabled && predict!.Value.Fields.Contains("strikes");
            Vitomir.Engine.Net.OwnCast.Enabled = Vitomir.Engine.Net.OwnWalk.Enabled && predict!.Value.Fields.Contains("casts");
            Vitomir.Engine.Net.OwnWalk.DrivesEnabled = Vitomir.Engine.Net.OwnWalk.Enabled && predict!.Value.Fields.Contains("drives");
            // The camera's choices (CameraChoices); an older recording has no line and was made with the base game's camera.
            var camera = reader.Peek is { Kind: "camera" } ? reader.Next() : null;
            Vitomir.Engine.World.CameraChoices.FromTape(camera?.Fields);
        }
    }

    static (string Name, string Value) Pair(string field)
    {
        int eq = field.IndexOf('=');
        return eq < 0 ? (field, "") : (field[..eq], field[(eq + 1)..]);
    }

    static string? scratchSaves, replaySaves;

    /// <summary>A replay's scratch folder, made when first wanted and deleted at the end (<see cref="Exit"/>).</summary>
    static string Scratch() =>
        scratchSaves ??= Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"vitomir-replay-{Environment.ProcessId}")).FullName;

    static void DropScratch()
    {
        if (scratchSaves is not null) try { Directory.Delete(scratchSaves, recursive: true); } catch (IOException) { }
    }

    /// <summary>The player's save folder: beside their settings and logs, never in the install.</summary>
    static string PlayersSaves => Path.Combine(Diag.UserDataFolder, "Saves");

    /// <summary>
    /// **Where the save folder is** — the one answer every reader and writer of it asks for (the slots the load
    /// and save screens list, a load from them, the quick save and load, the autosave, a network game's saves,
    /// a bare name given to <c>--load</c> or <c>--save</c>, <c>--list-saves</c>). The player's
    /// (<see cref="PlayersSaves"/>), except in a replay of a tape that carries its saves (its <c>saves</c>
    /// line, <see cref="TapeSaves"/>): there it is the replay's scratch copy of the folder the recording began
    /// with, read and written alike, so a save the replay makes is the save it loads. A tape with no such line
    /// reads the player's folder, as it did before there was one.
    /// </summary>
    public static string SaveFolder() => replaySaves ?? PlayersSaves;

    /// <summary>
    /// Where a save the run makes is written: its own path, or in a replay a scratch folder's file of the same
    /// name — the replay saves when its recording did (a save reads the world, and reading it can start things:
    /// the treasure tables' generator is made on the first look, <c>Location.Treasure</c>), but never over the player's.
    /// A save into the replay's own save folder (<see cref="SaveFolder"/>, a tape that carries its saves) is
    /// written where it is asked for, since that folder is scratch already and the run reads it back.
    /// </summary>
    public static string SavePath(string path)
    {
        if (mode != Mode.Replay) return path;
        if (replaySaves is not null && TapeSaves.Under(path, replaySaves)) return path;
        return Path.Combine(Scratch(), Path.GetFileName(path));
    }

    /// <summary>
    /// Whether the window's loop is a player's (it waits behind an inactive window): the recorded answer in a
    /// replay, whose own launch has lost the harness's flags. With it the window's size, which the loop reads
    /// from SDL wherever it wants it (the picks among them): a replay's window is made the recorded size.
    /// </summary>
    public static bool Interactive(bool live)
    {
        var (w, h) = Live.PictureSize();                            // the picture's, in borderless full screen with bars
        if (mode == Mode.Record) writer!.Write("interactive", live ? "1" : "0", $"size={w}x{h}");
        else if (mode == Mode.Replay && Take("interactive") is { } line)
        {
            if (line["size"] is { } size && size.Split('x') is [var sw, var sh] && int.TryParse(sw, out var rw) && int.TryParse(sh, out var rh) && (rw, rh) != (w, h) && Live.HasWindow)
                Live.Resize(rw, rh);
            return line.Fields.FirstOrDefault() == "1";
        }
        return live;
    }

    /// <summary>
    /// The size the recording's window had when its first loop began (<see cref="TapeReader.FirstSize"/>); null
    /// when this is no replay or the tape names none. The replay's window is made this size.
    /// </summary>
    public static (int Width, int Height)? FirstSize { get; private set; }

    /// <summary>
    /// The front end's loop begins: the window's size goes on the tape (<c>window size=WxH</c>), and a replay's
    /// window is put to the recorded one — as <see cref="Interactive"/> does for the game's loop, which until
    /// This was once the only place a size was written. A tape from before has no such line here and is left as it is
    /// (its window was made <see cref="FirstSize"/> already).
    /// </summary>
    public static void Window()
    {
        if (!Live.HasWindow || mode == Mode.Off) return;
        var (w, h) = Live.PictureSize();
        if (mode == Mode.Record) writer!.Write("window", $"size={w}x{h}");
        else if (reader!.Peek is { Kind: "window" } line)
        {
            reader.Next();
            if (line["size"] is { } size && size.Split('x') is [var sw, var sh] && int.TryParse(sw, out var rw) && int.TryParse(sh, out var rh) && (rw, rh) != (w, h))
                Live.Resize(rw, rh);
        }
    }

    /// <summary>
    /// The Crypt a network game's character is looked for in (<c>NetPlay</c>): a recording writes whether one of
    /// that name was kept there or made afresh from its template; a replay of a fresh one looks in its empty
    /// scratch folder, so it is made afresh again whatever the player's Crypt holds now, and a replay of a kept
    /// one looks in the player's (and says so when it has gone).
    /// </summary>
    public static string CryptRead(string folder, string name)
    {
        if (mode == Mode.Off) return folder;
        bool kept = Directory.Exists(folder) && new Vitomir.Engine.Net.Crypt(folder).All().Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (mode == Mode.Record) { writer!.Write("crypt", ReplayTape.Escape(name), kept ? "kept" : "fresh"); return folder; }
        if (Take("crypt") is not { } line || line.Fields.ElementAtOrDefault(1) != "kept") return SavePath(folder);
        if (!kept) Console.WriteLine($"replay: {name} was kept in the Crypt when this was recorded and is not now; the replay will not be the recording");
        return folder;
    }

    /// <summary>The clipboard's text as Ctrl+V pastes it: the system's, written, or the recorded text.</summary>
    public static string? Clipboard(Func<string?> live)
    {
        if (mode == Mode.Off) return live();
        if (mode == Mode.Record)
        {
            var text = live();
            writer!.Write("clip", text is null ? "-" : ReplayTape.Escape(text));
            return text;
        }
        return Take("clip") is { } line && line.Fields.FirstOrDefault() is { } f && f != "-" ? ReplayTape.Unescape(f) : null;
    }

    /// <summary>
    /// The top of a frame of the window's loop. False in a replay once the tape has ended (or the replay has
    /// left it), which ends the run.
    /// </summary>
    public static bool Frame()
    {
        if (stopped) return false;
        if (mode == Mode.Record) { writer!.Frame(); frame = writer.Frames; }
        else if (mode == Mode.Off) { if (ownDigest is not null || ownChecker is not null) frame++; }   // a run's own digest numbers its frames as a tape does
        else if (mode == Mode.Replay)
        {
            if (reader!.Done) { Finish(); return false; }
            if (Take("frame") is not { } line) return false;
            frame = (int)ReplayTape.Number(line.Fields.FirstOrDefault() ?? "0");
            // A long check says where it is, by the wall clock (which the simulation never sees).
            if (checker is not null && Environment.TickCount64 - progressAt >= ProgressEveryMs)
            {
                progressAt = Environment.TickCount64;
                Console.WriteLine($"replay-check: frame {frame} of {reader.FrameCount}");
            }
        }
        return true;
    }

    /// <summary>
    /// The next event for the loop, in the platform's terms: the host's (<see cref="IInputHost.Poll"/>, the pointer in the
    /// picture's pixels), written to the tape as it comes (an <c>in</c> line, <see cref="TapeInput"/>), or the tape's, with
    /// the host's own thrown away (a replay has no real input; closing its window still ends it). An old tape's <c>ev</c>
    /// line is read as the neutral event the host would have made of it; one of an event nothing reads is passed over.
    /// </summary>
    public static bool Poll(out LoopInput e)
    {
        if (mode == Mode.Off) return Live.Poll(out e);
        if (mode == Mode.Record)
        {
            bool got = Live.Poll(out e);
            Sample();
            if (got) writer!.Write(TapeInput.Line, TapeInput.Fields(e));
            else writer!.Write("none");
            return got;
        }
        e = default;
        if (Live.Closed()) { Console.WriteLine("replay: the window was closed"); stopped = true; }
        while (true)
        {
            while (reader!.Peek is { Kind: "state" } s) { reader.Next(); Apply(s); }
            if (stopped) return false;
            var next = reader.Next();
            if (next is { Kind: "none" }) return false;
            if (next is { Kind: TapeInput.Line or TapeInput.OldLine } line)
            {
                if (TapeInput.Read(line) is not { } read) continue;
                e = read;
                // The player resized the window: the replay's goes to the same size, which the loop reads from the host.
                if (e.Kind == LoopInputKind.Resized && Live.HasWindow && Live.PictureSize() != ((int)e.X, (int)e.Y))   // the picture's, in a shrunk replay
                    Live.Resize((int)e.X, (int)e.Y);
                return true;
            }
            Desync("an event", next);
            return false;
        }
    }

    /// <summary>The mouse buttons as the loop last polled them (a bit each, <see cref="LoopButton.Mask"/>), and the pointer's place.</summary>
    public static uint Buttons(out float x, out float y)
    {
        if (mode == Mode.Off) return Live.Buttons(out x, out y);
        (x, y) = (mouseX, mouseY);
        return buttons;
    }

    /// <summary>The modifiers as the loop last polled them.</summary>
    public static LoopMods Mods() => mode == Mode.Off ? Live.Mods : mods;

    /// <summary>Sets the modifiers, the host's and the tape's copy with them.</summary>
    public static void SetMods(LoopMods m)
    {
        Live.Mods = m;
        if (mode != Mode.Off) mods = m;
    }

    /// <summary>The keyboard as the loop last polled it, by position (<see cref="LoopScancode"/>).</summary>
    public static ReadOnlySpan<bool> Keyboard() => mode == Mode.Off ? Live.Keyboard() : keyboard;

    /// <summary>
    /// The frame's step in seconds: the clock's, written, or the tape's. What every system of the frame is
    /// stepped by (<see cref="FrameStep"/>), so a replay's frames are the recording's whatever its
    /// own machine's pace.
    /// </summary>
    public static float Step(float live)
    {
        if (mode == Mode.Record) writer!.Write("step", ReplayTape.Float(live));
        else if (mode == Mode.Replay)
        {
            if (Take("step") is { } line) return line.Fields.Length > 0 && float.TryParse(line.Fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : live;
        }
        return live;
    }

    /// <summary>
    /// Whether the frame drew — and so stepped the world, which steps inside the draw — for a frame SDL
    /// gave no picture to (a minimised window): written when it did not; a replay leaves out a frame its
    /// recording could not draw, and says so when it cannot draw one its recording did.
    /// </summary>
    public static bool Drawn(bool live)
    {
        if (mode == Mode.Record) { if (!live) writer!.Write("drawn", "0"); return live; }
        if (mode == Mode.Replay)
        {
            if (reader!.Peek is { Kind: "drawn" }) { reader.Next(); return false; }
            if (!live) Console.WriteLine($"replay: frame {frame} could not be drawn, as its recording's was; the world did not step");
        }
        return live;
    }

    /// <summary>The network's reads (<see cref="NetTape"/>): the transport's clock, its tokens' seed and every datagram it reads.</summary>
    sealed class NetSide : INetTape
    {
        public bool Replaying => mode == Mode.Replay;

        public long Clock(long live)
        {
            if (mode == Mode.Record) writer!.Write("netclock", live.ToString(CultureInfo.InvariantCulture));
            else if (mode == Mode.Replay && Take("netclock") is { } line) return ReplayTape.Number(line.Fields.FirstOrDefault() ?? "0");
            return live;
        }

        public int Seed(int live)
        {
            if (mode == Mode.Record) writer!.Write("netseed", live.ToString(CultureInfo.InvariantCulture));
            else if (mode == Mode.Replay && Take("netseed") is { } line) return (int)ReplayTape.Number(line.Fields.FirstOrDefault() ?? "1");
            return live;
        }

        public bool Receive(System.Net.Sockets.Socket socket, byte[] buffer, ReceiveRead read, Func<byte[], int, byte[]> taped, out int length, out System.Net.EndPoint from)
        {
            if (mode == Mode.Record)
            {
                bool got = read(buffer, out length, out from);
                // The datagram as the transport opened it (the sealed session's keys are never written), its password proof masked.
                writer!.Write("net", got ? Convert.ToHexStringLower(taped(buffer, length)) : "-", got ? "from=" + ReplayTape.Escape(from.ToString() ?? "") : "");
                return got;
            }
            // A replay: the socket's own datagrams are dropped (the other machine is not there), the tape's taken.
            var junk = new byte[65536];
            for (int i = 0; i < 256; i++)
            {
                try { if (!read(junk, out _, out _)) break; }
                catch (System.Net.Sockets.SocketException) { }
            }
            length = 0;
            from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
            if (Take("net") is not { } line || line.Fields.FirstOrDefault() is not { } hex || hex == "-") return false;
            var bytes = Convert.FromHexString(hex);
            bytes.CopyTo(buffer, 0);
            length = bytes.Length;
            if (line["from"] is { } ep && System.Net.IPEndPoint.TryParse(ReplayTape.Unescape(ep), out var parsed)) from = parsed;
            return true;
        }

        /// <summary>
        /// A join's password check: the tape has the game's password as ****, so the host's answer is written, and
        /// a replay takes it. A older tape has no such line: its real password was on its arg line, so the check is live.
        /// </summary>
        public bool WrongPassword(bool live)
        {
            if (mode == Mode.Record) writer!.Write("netpassword", live ? "wrong" : "right");
            else if (mode == Mode.Replay && reader!.Peek is { Kind: "netpassword" } && Take("netpassword") is { } line)
                return line.Fields.FirstOrDefault() == "wrong";
            return live;
        }

        /// <summary>
        /// A join's build and data check: the host's answer written (<c>netidentity same</c>, or <c>netidentity differs</c> and
        /// the line), and a replay takes it, whatever build replays it on whatever install. A older tape has none: live.
        /// </summary>
        public string? Differs(string? live)
        {
            if (mode == Mode.Record)
            {
                if (live is null) writer!.Write("netidentity", "same");
                else writer!.Write("netidentity", "differs", ReplayTape.Escape(live));
            }
            else if (mode == Mode.Replay && reader!.Peek is { Kind: "netidentity" } && Take("netidentity") is { } line)
                return line.Fields.FirstOrDefault() == "differs" ? ReplayTape.Unescape(line.Fields.ElementAtOrDefault(1) ?? "") : null;
            return live;
        }
    }

    /// <summary>
    /// How many frames of sound the mixer makes this frame: the platform's audio output's amount hook
    /// (<c>AudioOutput.Pump</c>; <c>AudioOut.Pump</c> before), told what the device wanted (0 with no device: a muted
    /// recording's mixer makes nothing, a muted replay's makes the recorded frames and drops them), written, or the
    /// recorded number. The mixer plays its channels to their ends as it
    /// makes them, and the next frame frees an ended one, so without this the device's own pace
    /// would reach the world — a voice's channel still held on one machine and gone on another.
    /// </summary>
    public static int AudioFrames(int live)
    {
        if (mode == Mode.Record) { writer!.Write("audio", live.ToString(CultureInfo.InvariantCulture)); return live; }
        if (mode == Mode.Replay && Take("audio") is { } line) return (int)ReplayTape.Number(line.Fields.FirstOrDefault() ?? "0");
        return live;
    }

    /// <summary>A clock reading in nanoseconds (a film's, <c>SDL_GetTicksNS</c>): the clock's, written, or the tape's.</summary>
    public static ulong Clock(ulong live)
    {
        if (mode == Mode.Record)
        {
            if (clockZero == 0) clockZero = live;
            writer!.Write("clock", (live - clockZero).ToString(CultureInfo.InvariantCulture));
        }
        else if (mode == Mode.Replay && Take("clock") is { } line) return 1_000_000_000UL + (ulong)ReplayTape.Number(line.Fields.FirstOrDefault() ?? "0");
        return mode == Mode.Record ? live - clockZero + 1_000_000_000UL : live;
    }

    /// <summary>
    /// The end of a stepped frame: the simulation's digest written (a recording, a replay) or compared with the
    /// recorded one (a check), which stops the run at the first frame that differs.
    /// </summary>
    public static void FrameEnd(Location? place, long clockMs, Func<string?>? scripts = null)
    {
        if (place is null || stopped || mode == Mode.Off && ownDigest is null && ownChecker is null) return;
        var d = SimDigest.Take(place, frame, clockMs, scripts?.Invoke());
        digest?.Write(d);
        ownDigest?.Write(d);
        if (ownChecker?.Check(d) is { } own)
        {
            Console.WriteLine($"classic: digest DIVERGED at {own}");
            Console.WriteLine($"classic: {ownChecker.Agreed} frames agreed before it");
            exitCode = 1;
            stopped = true;
            return;
        }
        if (checker?.Check(d) is { } first)
        {
            Console.WriteLine($"replay-check: DIVERGED at {first}");
            Console.WriteLine($"replay-check: {checker.Agreed} frames agreed before it");
            exitCode = 1;
            stopped = true;
        }
    }

    /// <summary>
    /// A step through the menus (ours, <c>FrontEnd.Trail</c>, <see cref="MenuTrail"/>): kept in the digest by a
    /// recording or a replay, compared with the recording's by a check, which stops at the first that differs.
    /// </summary>
    public static void Menu(string what)
    {
        if (mode == Mode.Off || stopped) return;
        digest?.WriteMenu(frame, what);
        if (trail?.Check(frame, what) is { } first)
        {
            Console.WriteLine($"replay-check: DIVERGED at {first}");
            Console.WriteLine($"replay-check: {trail.Agreed} steps through the menus agreed before it");
            exitCode = 1;
            stopped = true;
        }
    }

    /// <summary>The process's exit code: a check's (0 agreed, 1 diverged), else the run's. Closes the tape and digests.</summary>
    public static int Exit(int code)
    {
        Finish();
        writer?.Dispose(); writer = null;
        digest?.Dispose(); digest = null;
        checker?.Dispose(); checker = null;
        bool ownChecked = ownChecker is not null;
        if (ownChecker is { First: null } agreed)
            Console.WriteLine($"classic: digest check: {agreed.Agreed} frames agreed, none differed" + (agreed.RecordedLeft ? $" (the digest checked goes on past frame {frame})" : ""));
        ownDigest?.Dispose(); ownDigest = null;
        ownChecker?.Dispose(); ownChecker = null;
        DropScratch();
        return (mode == Mode.Replay || ownChecked) && exitCode != 0 ? exitCode : code;
    }

    /// <summary>What a check says of a tape on which no game ran: the menus only, no frames to compare (and, its steps through them that agreed).</summary>
    internal const string MenusOnlyWords = "the menus only: no game ran on this tape, so it had no frames to compare";
    /// <summary>What every such line starts with (`ReplayFolder` looks for it).</summary>
    internal const string MenusOnlyMark = "the menus only:";

    internal static string MenusOnly(MenuTrail? trail) =>
        trail is null ? MenusOnlyWords : $"the menus only: no game ran on this tape; {trail.Agreed} steps through the menus agreed";

    static string Steps(MenuTrail? trail) => trail is null ? "" : $", and {trail.Agreed} steps through the menus";

    static bool finished;
    static void Finish()
    {
        if (finished || mode != Mode.Replay) return;
        finished = true;
        if (checker is { } c && c.First is null && trail?.First is null)
        {
            if (!reader!.Done) { Console.WriteLine($"replay-check: DIVERGED — the run ended {reader.Position} lines into the tape, before its end"); exitCode = 1; }
            else if (c.RecordedLeft) { Console.WriteLine($"replay-check: DIVERGED — the recording's digest has frames after the replay's last ({frame})"); exitCode = 1; }
            else if (trail is { First: null, Missing: { } missing }) { Console.WriteLine($"replay-check: DIVERGED — the recording took a step through the menus the replay did not: {missing}"); exitCode = 1; }
            // **A tape of the menus only** (ours): no game ran, so the digest had no frames to compare. Now
            // its steps through the menus are (MenuTrail); a digest before then has none to compare either.
            else if (c.Agreed == 0) Console.WriteLine($"replay-check: the same — {MenusOnly(trail)}");
            else Console.WriteLine($"replay-check: the same — {c.Agreed} frames agreed{Steps(trail)}");
        }
        else if (checker is null) Console.WriteLine($"replay: {frame} frames replayed; digest {Path.GetFullPath(LogFile.Beside(tapePath!, ".replay.digest"))}");
    }

    static TapeLine? Take(string kind)
    {
        if (reader!.Peek is { } line && line.Kind == kind) return reader.Next();
        Desync($"a {kind} line", reader.Peek);
        return null;
    }

    static void Desync(string wanted, TapeLine? found)
    {
        if (stopped) return;
        Console.WriteLine($"replay: the run left its tape at frame {frame}: it asked for {wanted} where the tape has {(found is { } f ? $"\"{f}\"" : "its end")} — the replay is no longer the recording");
        stopped = true;
        exitCode = 1;
    }

    // ---- the input as polled -------------------------------------------------------------------

    /// <summary>The live input, the host's (<see cref="IInputHost"/>): set by the host before the first poll.</summary>
    public static IInputHost Live { get; set; } = null!;

    static void Sample()
    {
        uint b = Live.Buttons(out float x, out float y);
        var m = Live.Mods;
        var raw = Live.Keyboard();
        int n = raw.Length;
        var fields = new List<string>();
        if (x != mouseX || y != mouseY || b != buttons)
        {
            (mouseX, mouseY, buttons) = (x, y, b);
            fields.Add($"mouse={ReplayTape.Float(x)},{ReplayTape.Float(y)},{b}");
        }
        if (m != mods) { mods = m; fields.Add($"mods=0x{(ushort)m:x}"); }
        bool changed = false;
        for (int i = 0; i < Math.Min(n, KeyCount); i++)
        {
            bool down = raw[i];
            if (down == keyboard[i]) continue;
            keyboard[i] = down;
            if (down) keysDown.Add(i); else keysDown.Remove(i);
            changed = true;
        }
        if (changed) fields.Add("keys=" + (keysDown.Count == 0 ? "-" : string.Join(',', keysDown)));
        var held = KeyInput.HarnessHeld.Order().ToList();
        if (!held.SequenceEqual(harnessWas)) { harnessWas = held; fields.Add("harness=" + (held.Count == 0 ? "-" : string.Join(',', held))); }
        if (fields.Count > 0) writer!.Write("state", [.. fields]);
    }
    static List<int> harnessWas = [];

    static void Apply(TapeLine s)
    {
        if (s["mouse"] is { } mouse)
        {
            var p = mouse.Split(',');
            mouseX = float.Parse(p[0], CultureInfo.InvariantCulture);
            mouseY = float.Parse(p[1], CultureInfo.InvariantCulture);
            buttons = uint.Parse(p[2], CultureInfo.InvariantCulture);
        }
        if (s["mods"] is { } m) mods = (LoopMods)ReplayTape.Number(m);
        if (s["keys"] is { } keys)
        {
            Array.Clear(keyboard);
            keysDown.Clear();
            if (keys != "-") foreach (var k in keys.Split(',')) { int i = int.Parse(k, CultureInfo.InvariantCulture); keyboard[i] = true; keysDown.Add(i); }
        }
        if (s["harness"] is { } harness)
        {
            KeyInput.HarnessHeld.Clear();
            if (harness != "-") foreach (var k in harness.Split(',')) KeyInput.HarnessHeld.Add(int.Parse(k, CultureInfo.InvariantCulture));
        }
    }
}
