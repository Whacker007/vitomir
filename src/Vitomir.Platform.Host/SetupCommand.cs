// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host;

/// <summary>
/// **<c>vitomir-platform --setup</c>**: the setup and import tool's working half in text, each step the
/// <see cref="SetupService"/>'s, which the launcher's Setup page calls the same way. <c>--setup</c> alone runs every step but the
/// import of saves (which wants a choice): find, check (by size; the import hashes every file as it copies it), import the data, list
/// 1.1's own saves, FFmpeg. A step on its own:
/// <c>--setup find</c>, <c>check [--fast]</c>, <c>import</c>, <c>verify</c> and <c>repair</c> (the imported game files), <c>saves</c>, <c>import-saves &lt;SP4[=SP12]&gt;... [--replace]</c>,
/// <c>ffmpeg [--install-ffmpeg]</c>, <c>status</c>, <c>shortcut [desktop|startmenu on|off|update]</c> (<see cref="Shortcuts"/>; alone: how both stand). <c>--from &lt;folder&gt;</c> names the install (else the first GOG or Steam copy
/// found). The exit codes are <see cref="Exit"/>'s. A developer's <c>--developer --manifest &lt;file&gt;</c> checks against another manifest.
/// </summary>
public static partial class SetupCommand
{
    /// <summary>The switch, a players' word: it sets the game up (its window is the launcher's Setup page).</summary>
    public const string Switch = "--setup";

    /// <summary>The steps, as the command line names them.</summary>
    public static readonly string[] Steps = ["all", "find", "check", "import", "verify", "repair", "saves", "import-saves", "ffmpeg", "status",
        "module-add", "module-remove", "module-list", "module-order", "module-enable", "module-disable", "module-check", "module-accept",
        "move-user-folder", "shortcut"];

    /// <summary>The exit codes.</summary>
    public static class Exit
    {
        /// <summary>Done: every step asked for did what it was asked.</summary>
        public const int Done = 0;
        /// <summary>Failed: a file could not be read or written, the run was stopped, or winget failed.</summary>
        public const int Failed = 1;
        /// <summary>The command line is wrong (a step or switch it does not know, a manifest that does not read).</summary>
        public const int Usage = 2;
        /// <summary>No install found, the folder is not one, or its files are not 1.1's as the manifest has them (each named).</summary>
        public const int NotTheData = 3;
        /// <summary>Not enough room for the import.</summary>
        public const int NoRoom = 4;
        /// <summary>A save or character was not imported (one is there already and --replace was not given, or it does not read); or a pack (<c>module-add</c>).</summary>
        public const int NotImported = 5;
        /// <summary><c>--setup ffmpeg</c> alone: no FFmpeg found.</summary>
        public const int NoFFmpeg = 6;
        /// <summary>The player modules on have errors (each named): the game does not start until they are mended or turned off.</summary>
        public const int ModuleErrors = 7;
        /// <summary>The player modules on have warnings not accepted for this combination (each named): <c>--setup module-accept</c>.</summary>
        public const int ModuleWarnings = 8;
    }

    /// <summary>Whether the command line asks for the setup.</summary>
    public static bool Asked(Cli cli) => cli.Flag(Switch);

    /// <summary>
    /// Runs it; every line through <paramref name="say"/> (the program's log: the terminal and the log file), as plain ASCII
    /// (<see cref="SetupWords.Plain"/>). <paramref name="importer"/> is a test's save importer (else this program, a process a save).
    /// </summary>
    public static int Run(Cli cli, Action<string> say, string? program = null, ISetupMachine? machine = null, CancellationToken cancel = default, ISaveImporter? importer = null,
        UserFolderPlace? where = null, FolderPlaces? places = null, IShortcutWriter? shortcuts = null)
    {
        var told = say;
        say = line => told(SetupWords.Plain(line));
        string step = cli.Arg(Switch) ?? "all";
        if (!Steps.Contains(step))
        {
            say($"setup: {step} is not a step of the setup ({string.Join(", ", Steps)})");
            return Exit.Usage;
        }
        if (step.StartsWith("module-", StringComparison.Ordinal)) return Modules(cli, step, say, cancel);
        if (step == "shortcut") return Shortcut(cli, say, where ?? UserFolderPlace.ThisProgram(), shortcuts ?? Shortcuts.ThisMachine());
        if (step == "move-user-folder") return MoveUserFolder(cli, say, where ?? UserFolderPlace.ThisProgram(), places ?? FolderPlaces.ThisMachine(), machine ?? ThisMachine.Instance, cancel);
        DataManifest manifest;
        if (cli.Arg("--manifest") is { } other)
        {
            if (!cli.Flag(DeveloperSwitch.Flag)) { say("setup: --manifest is a developer's (with --developer): a player's setup checks against the manifest Vitomir carries"); return Exit.Usage; }
            try { manifest = DataManifest.Read(other); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException) { say($"setup: the manifest {other} does not read: {e.Message}"); return Exit.Usage; }
        }
        else manifest = DataManifest.Builtin;
        program ??= ProgramPath();
        var service = new SetupService(UserData.Folder, manifest, machine ?? ThisMachine.Instance, importer ?? new ProgramSaveImporter(program, UserData.Folder), PlatformVersion.Text);
        say($"setup: {PlatformVersion.Text}'s setup: the user data {service.UserData}");
        say($"setup: the manifest {manifest}: {Count(manifest.Of(DataManifest.Game).Count())} files of the game's ({SetupWords.Size(manifest.Bytes(DataManifest.Game))})"
            + (manifest.Of(DataManifest.Scripts).Any() ? $"; it knows the {Count(manifest.Of(DataManifest.Scripts).Count())} files of the SDK's scripts' sources (not needed, not imported)" : "")
            + (cli.Arg("--manifest") is not null ? " (a developer's, --manifest)" : ""));

        string? named = cli.Arg("--from");
        if (named is not null && !InstallFinder.IsInstall(named))
        {
            say($"setup: {named} is not a VTMR install: it has no Resource.nob");
            return Exit.NotTheData;
        }
        switch (step)
        {
            case "find": return Find(service, named, say, out _) is null ? Exit.NotTheData : Exit.Done;
            case "check":
                {
                    if (Find(service, named, say, out _) is not { } chosen) return Exit.NotTheData;
                    return Check(service, chosen, cli.Flag("--fast") ? CheckDepth.Sizes : CheckDepth.Full, say, cancel);
                }
            case "import":
                {
                    if (Find(service, named, say, out _) is not { } chosen) return Exit.NotTheData;
                    return Import(service, chosen, say, cancel);
                }
            case "verify" or "repair":
                return Verify(service, repair: step == "repair", say, cancel);
            case "saves":
                {
                    if (Find(service, named, say, out _) is not { } chosen) return Exit.NotTheData;
                    Saves(service, chosen.Folder, say);
                    return Exit.Done;
                }
            case "import-saves":
                {
                    var picks = Positional(cli.Args, Switch, 1).Select(OriginalPick.Parse).ToList();
                    if (picks.Count == 0) { say("setup: import-saves: name the saves and characters to import (SP4, or SP4=SP12 for another slot; npf000000, or npf000000=<name> for another name; --setup saves lists them)"); return Exit.Usage; }
                    if (Find(service, named, say, out _) is not { } chosen) return Exit.NotTheData;
                    return ImportSaves(service, chosen.Folder, picks, cli.Flag("--replace"), say, cancel);
                }
            case "ffmpeg": return FFmpeg(cli, say, install: cli.Flag("--install-ffmpeg")) ? Exit.Done : cli.Flag("--install-ffmpeg") ? Exit.Failed : Exit.NoFFmpeg;
            case "status":
                {
                    bool ready = Status(service, cli.Flag(DeveloperSwitch.Flag), say);
                    Find(service, named, say, out _);
                    FFmpeg(cli, say, install: false);
                    return ready ? Exit.Done : Exit.NotTheData;
                }
            default:
                {
                    if (Find(service, named, say, out _) is not { } chosen) return Exit.NotTheData;
                    // The check by size, said (the import then hashes every file as it copies it: the full check, once).
                    if (Check(service, chosen, CheckDepth.Sizes, say, cancel) is var checkedCode and not Exit.Done) return checkedCode;
                    int imported = Import(service, chosen, say, cancel);
                    if (imported == Exit.Done) Saves(service, chosen.Folder, say);
                    FFmpeg(cli, say, install: cli.Flag("--install-ffmpeg"));
                    if (imported == Exit.Done)
                        say("setup: done: start the game with vitomir-platform --game classic (it plays from the imported data); "
                            + "1.1's own saves and characters come over with --setup import-saves <SP4 npf000000 ...>");
                    return imported;
                }
        }
    }

    /// <summary>
    /// **<c>--setup move-user-folder &lt;folder | portable&gt; [--keep-old]</c>**: the user folder moved (<see cref="UserFolderMove"/>): checked
    /// first (refused with why), every file copied and checked by its SHA-256 with its progress at each tenth, then <c>vitomir.ini</c> written
    /// and the old folder removed (kept with <c>--keep-old</c>). Ctrl+C cancels it, the old folder still in use; the same line again carries on.
    /// </summary>
    static int MoveUserFolder(Cli cli, Action<string> say, UserFolderPlace where, FolderPlaces places, ISetupMachine machine, CancellationToken cancel)
    {
        var choice = UserFolderChoice.Resolve(where);
        say($"setup: the user folder {choice.Folder} ({choice.How}); {VitomirIni.FileName}: {where.IniPath}{(choice.Ini.Exists ? "" : " (not there)")}");
        if (Positional(cli.Args, Switch, 1).FirstOrDefault() is not { } target)
        {
            say("setup: move-user-folder: name the folder to move it to, or portable (UserData inside the Vitomir folder)");
            return Exit.Usage;
        }
        if (UserFolderMove.Plan(choice, target, places, machine, out string? refused) is not { } plan)
        {
            say($"setup: move-user-folder: not moved: {refused}");
            return Exit.NotTheData;
        }
        foreach (var w in plan.Warnings) say($"setup: move-user-folder: warning: {w}");
        say($"setup: move-user-folder: {choice.Folder} to {plan.To}: {Count(plan.Files)} files, {SetupWords.Size(plan.Bytes)}{(plan.Resuming ? "; carrying on a move that stopped" : "")}");
        var report = UserFolderMove.Run(plan, cli.Flag("--keep-old"), PlatformVersion.Text, new Tenths("move", say), cancel);
        say($"setup: move-user-folder: {report.Line}");
        return report.Done ? Exit.Done : Exit.Failed;
    }

    /// <summary>This program, for the save import's process: <c>vitomir-platform</c> beside this assembly (the program's folder).</summary>
    public static string ProgramPath()
    {
        string beside = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "vitomir-platform.exe" : "vitomir-platform");
        return File.Exists(beside) ? beside : Environment.ProcessPath ?? beside;
    }

    /// <summary>The words after <paramref name="after"/> (and its step) up to the next switch.</summary>
    static IEnumerable<string> Positional(string[] args, string after, int skip)
    {
        int at = Array.IndexOf(args, after);
        for (int i = at + 1 + skip; at >= 0 && i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal); i++) yield return args[i];
    }

    // ---- the steps -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <c>--setup shortcut desktop|startmenu on|off|update</c>: the Setup page's two switches (and its Update) on the command line;
    /// <c>--setup shortcut</c> alone says how both stand. The shortcut opens <c>vitomir-launcher.exe</c> at the top of this Vitomir folder.
    /// </summary>
    static int Shortcut(Cli cli, Action<string> say, UserFolderPlace where, IShortcutWriter? writer)
    {
        if (writer is null) { say("setup: shortcut: shortcuts are Windows' (this machine has none to make)"); return Exit.Failed; }
        var shortcuts = new Shortcuts(writer, where.TopFolder);
        var words = Positional(cli.Args, Switch, 1).ToList();
        if (words.Count == 0)
        {
            foreach (var place in new[] { ShortcutPlace.Desktop, ShortcutPlace.StartMenu }) say(Said(shortcuts.Status(place)));
            return Exit.Done;
        }
        if (words.Count != 2 || Shortcuts.Parse(words[0]) is not { } at || words[1].ToLowerInvariant() is not ("on" or "off" or "update"))
        {
            say("setup: shortcut: --setup shortcut desktop|startmenu on|off|update (alone: how both stand)");
            return Exit.Usage;
        }
        var (status, line) = words[1].ToLowerInvariant() switch
        {
            "update" => shortcuts.Update(at),
            var w => shortcuts.Set(at, w == "on"),
        };
        say($"setup: shortcut: {line}");
        say(Said(status));
        bool wanted = words[1].ToLowerInvariant() switch { "off" => status.State is ShortcutState.Off or ShortcutState.NotOurs, _ => status.State == ShortcutState.On };
        return wanted ? Exit.Done : Exit.Failed;
    }

    static string Said(ShortcutStatus s) =>
        $"setup: shortcut {Shortcuts.Word(s.Place)}: {s.State.ToString().ToLowerInvariant()}{(s.Path is { } p ? $" ({p}{(s.PointsAt is { } t ? $" -> {t}" : "")})" : "")}";

    static InstallCandidate? Find(SetupService service, string? named, Action<string> say, out IReadOnlyList<InstallCandidate> found)
    {
        found = service.Find(named);
        say("== find: the installs of Vampire: The Masquerade - Redemption 1.1 (GOG, Steam, the usual folders"
            + (named is not null ? ", the one named" : "") + ")");
        if (found.Count == 0)
        {
            say("setup: no install found. Name its folder with --from <folder> (the one holding Resource.nob); Vitomir supports 1.1 as GOG and Steam sell it");
            return null;
        }
        for (int i = 0; i < found.Count; i++)
        {
            var c = found[i];
            say($"  {i + 1}. {c.Folder}: {c.StoreName} copy (found by {string.Join("; ", c.FoundBy)})" + (c.Supported ? "" : $" - {SetupWords.Unsupported}"));
        }
        var chosen = SetupService.Choose(found, named);
        if (chosen is not null)
            say($"setup: using {chosen.Folder}" + (named is null && found.Count > 1 ? " (--from <folder> names another)" : ""));
        return chosen;
    }

    static int Check(SetupService service, InstallCandidate chosen, CheckDepth depth, Action<string> say, CancellationToken cancel)
    {
        say($"== check: {chosen.Folder} against {service.Manifest} ({(depth == CheckDepth.Full ? "every file's size and SHA-256" : "every file's size: --fast; an import always hashes")})");
        DataCheckReport report;
        try { report = service.Check(chosen.Folder, depth, new Tenths("check", say), cancel); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { say($"setup: the check stopped: {e.Message}"); return Exit.Failed; }
        SayCheck(report, say);
        return report.GameWhole ? Exit.Done : Exit.NotTheData;
    }

    /// <summary>What a check found, in the setup's words: the game's files, its scripts, the files left out and those ignored.</summary>
    static void SayCheck(DataCheckReport report, Action<string> say)
    {
        int files = report.Manifest.Of(DataManifest.Game).Count();
        if (report.GameWhole) say($"setup: the game's files: all {Count(files)} as the manifest has them ({SetupWords.Size(report.Manifest.Bytes(DataManifest.Game))})");
        else
        {
            say($"setup: the game's files: {Count(report.Problems.Count)} of {Count(files)} are not as the manifest has them, so this copy is refused:");
            foreach (var p in report.Problems) say($"  {p}");
            if (report.Store == Store.Unknown)
                say($"setup: this folder is {SetupWords.Unsupported}");
        }
        // The game runs Codex.nob's own compiled classes (in the game's files), as 1.1 does; the SDK's sources are not needed.
        say("setup: the game's scripts: Codex.nob's own compiled classes, as 1.1 runs them" + report.Scripts switch
        {
            PartState.Whole => $" (this install also has the SDK's {Count(report.Manifest.Of(DataManifest.Scripts).Count())} sources, unchanged: not needed, not imported)",
            PartState.Absent => "",
            _ => $" (this install has the SDK's Codex folder with {Count(report.ScriptProblems.Count)} of its sources changed or missing, a mod's: "
                 + "not imported, as the files that would override the game's are not)",
        });
        // A file left out is said to replace 1.1's own only when it has the path of one of 1.1's files; one that only adds a new
        // file (the SDK's loose Materials files do) is said as an addition (DataCheck.Replacements).
        SayLeftOut(report.Replacing, "that would replace 1.1's own (a texture pack's or a mod's: add one as a player module, vitomir-platform --setup module-add <its folder>)", "replaces 1.1's", say);
        SayLeftOut(report.Adding, "that only add to the game, new files 1.1 does not have (a mod's or the SDK's: they replace nothing of 1.1's; a mod is added as a player module)", "adds", say);
        say($"setup: {Count(report.Ignored)} other files of the folder are not Vitomir's to read, and were not looked at (the programs, the store's and installer's files, the saves, the SDK)");
    }

    /// <summary>The files left out of one kind: how many, by top folder, the first ten named.</summary>
    static void SayLeftOut(IReadOnlyList<string> files, string what, string mark, Action<string> say)
    {
        if (files.Count == 0) return;
        say($"setup: left out, {Count(files.Count)} {(files.Count == 1 ? "file" : "files")} {what}: {LeftOutFolders(files)}");
        foreach (var o in files.Take(10)) say($"  left out ({mark}): {o}");
        if (files.Count > 10) say($"  ... and {Count(files.Count - 10)} more");
    }

    /// <summary>Files left out counted by their top folder (<c>Materials/ (12)</c>; an archive by its name).</summary>
    public static string LeftOutFolders(IEnumerable<string> files) =>
        string.Join(", ", files.GroupBy(o => o.Contains('/') ? o[..o.IndexOf('/')] + "/" : o, StringComparer.OrdinalIgnoreCase).Select(g => $"{g.Key} ({Count(g.Count())})"));

    static int Import(SetupService service, InstallCandidate chosen, Action<string> say, CancellationToken cancel)
    {
        say($"== import: the game's files from {chosen.Folder} into {service.DataFolder} (the install is only read)");
        DataImportReport report;
        try { report = service.ImportData(chosen.Folder, new Tenths("import", say), cancel); }
        catch (OperationCanceledException) { say("setup: the import was stopped: nothing is used until it finishes; run it again to carry on from where it stopped"); return Exit.Failed; }
        if (!report.Accepted)
        {
            say(report.Refusal == ImportRefusal.DataNotWhole
                ? $"setup: not imported: {Count(report.Problems.Count)} of the game's files are not as the manifest has them:"
                : "setup: not imported:");
            foreach (var p in report.Problems) say($"  {p}");
            if (report.Refusal == ImportRefusal.DataNotWhole && !chosen.Supported) say($"setup: this folder is {SetupWords.Unsupported}");
            return report.Refusal switch
            {
                ImportRefusal.NoRoom => Exit.NoRoom,
                ImportRefusal.Failed => Exit.Failed,
                _ => Exit.NotTheData,
            };
        }
        var m = report.Marker!;
        say($"setup: imported {Count(m.Files)} files ({SetupWords.Size(m.Bytes)}): {Count(report.Copied)} copied, {Count(report.Kept)} already there; "
            + "each read back and its SHA-256 the manifest's");
        if (report.OptionsCopied) say("setup: 1.1's own options (masquerade.ini) copied with them, as 1.1 reads them from its folder");
        if (report.Overriding.Count > 0) say($"setup: left out, {Count(report.Overriding.Count)} files not 1.1's own (the check lists them: those that would replace 1.1's and the additions)");
        if (report.SetAside > 0) say($"setup: {Count(report.SetAside)} files that were in {service.DataFolder} and are not the manifest's set aside in {Path.Combine(Path.GetDirectoryName(service.DataFolder)!, "set-aside")}");
        say($"setup: the imported data is ready ({ImportedData.MarkerName} written last); made on this machine at the first game start: the scripts' cache "
            + $"({Path.Combine(service.ClassicFolder, "cache", "codex")}: Codex.nob's classes bridged to the engine, a few seconds, once), the window's settings, the game's own settings and logs");
        return Exit.Done;
    }

    /// <summary>
    /// <c>--setup verify</c>: the imported game files read whole and each checked against its manifest's size and SHA-256, every missing,
    /// changed or extra file named; <c>--setup repair</c>: then those copied again from the copy of the game they were imported from.
    /// </summary>
    static int Verify(SetupService service, bool repair, Action<string> say, CancellationToken cancel)
    {
        say($"== verify: the imported game files in {service.DataFolder}, each against its manifest's size and SHA-256 (every byte read)");
        DataVerifyReport report;
        try { report = service.Verify(new Tenths("verify", say), cancel); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { say($"setup: the verification stopped: {e.Message}"); return Exit.Failed; }
        if (report.Refusal is { } why) { say($"setup: not verified: {why}"); return Exit.NotTheData; }
        foreach (var line in VerifyLines(report)) say("setup: " + line);
        if (report.AllGood) return Exit.Done;
        if (!repair) { say("setup: vitomir-platform --setup repair copies them again from the copy of the game they were imported from"); return Exit.NotTheData; }
        say($"== repair: from {report.Marker!.Source}");
        DataRepairReport done;
        try { done = service.Repair(report, new Tenths("repair", say), cancel); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException) { say($"setup: the repair stopped: {e.Message} (run it again)"); return Exit.Failed; }
        if (done.Refusal is { } refused) { say($"setup: not repaired: {refused}"); return Exit.NotTheData; }
        say($"setup: repaired: {Count(done.Copied.Count)} {(done.Copied.Count == 1 ? "file" : "files")} copied again from {done.Source}, each read back and its SHA-256 the manifest's"
            + (done.SetAside > 0 ? $"; {Count(done.SetAside)} extra {(done.SetAside == 1 ? "file" : "files")} set aside in {Path.Combine(Path.GetDirectoryName(service.DataFolder)!, "set-aside")}" : ""));
        return Exit.Done;
    }

    /// <summary>A verification's result in the setup's words (the launcher's Setup page says the same).</summary>
    public static IReadOnlyList<string> VerifyLines(DataVerifyReport report)
    {
        if (report.Refusal is { } why) return [$"not verified: {why}"];
        if (report.AllGood) return [$"all {Count(report.Files)} imported game files are as the manifest has them ({SetupWords.Size(report.Bytes)}), and nothing extra overrides them"];
        var lines = new List<string>
        {
            $"{Count(report.Problems.Count + report.Extra.Count)} of the imported game files are not right: "
            + string.Join(", ", new[] { (report.Missing.Count(), "missing"), (report.Changed.Count(), "changed"), (report.Extra.Count, "extra, overriding them") }
                .Where(c => c.Item1 > 0).Select(c => $"{Count(c.Item1)} {c.Item2}")),
        };
        lines.AddRange(report.Missing.Select(p => $"  missing: {p.Path}"));
        lines.AddRange(report.Changed.Select(p => $"  changed: {p.Path} ({p.Why})"));
        lines.AddRange(report.Extra.Select(e => $"  extra, overriding: {e}"));
        return lines;
    }

    static void Saves(SetupService service, string install, Action<string> say)
    {
        say($"== saves: 1.1's own saves, network games and multiplayer characters in {install}");
        var items = service.Originals(install);
        if (items.Count == 0) { say($"setup: none (no SaveGames\\SP<n> or MP<n> with a !main.nsg, no Crypt\\*.npf)"); return; }
        foreach (var i in items)
        {
            string when = i.WrittenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            string kind = i.Kind switch { OriginalKind.Save => "save", OriginalKind.NetworkSave => "network game", _ => "character" };
            string family = i.Kind == OriginalKind.NetworkSave ? "MP" : "SP";
            string into = i.ImportsAs is null ? $"cannot be imported: {i.Why}"
                : i.Kind == OriginalKind.Character
                    ? service.KeptCharacter(i.ImportsAs) is not null ? $"-> Crypt\\{i.ImportsAs} (a character of that name is kept: --replace, or {i.Id}=<another name>)" : $"-> Crypt\\{i.ImportsAs}"
                : File.Exists(Path.Combine(service.SavesFolder, i.ImportsAs)) ? $"-> {i.ImportsAs} (that slot is taken: --replace, or {i.Id}={family}<n>)" : $"-> {i.ImportsAs}";
            say($"  {i.Id,-10} {kind,-12} {when}  \"{i.Description}\"  {into}");
        }
        say($"setup: import the ones you want with vitomir-platform --setup import-saves {items.FirstOrDefault(i => i.ImportsAs is not null)?.Id ?? "SP1"} [...] "
            + $"(the saves into {service.SavesFolder}, the characters into {service.CryptFolder}; nothing there is written over without --replace)");
    }

    static int ImportSaves(SetupService service, string install, IReadOnlyList<OriginalPick> picks, bool replace, Action<string> say, CancellationToken cancel)
    {
        say($"== import-saves: {string.Join(", ", picks.Select(p => p.As is null ? p.Id : $"{p.Id} as {p.As}"))} from {install} into {service.ClassicFolder}"
            + (replace ? " (--replace: a save or character there is replaced, kept as .previous)" : ""));
        var state = service.Imported();
        say(state.Ready ? $"setup: the game's files for the import: the imported data ({state.Folder})" : $"setup: the game's files for the import: the install's own ({install}; nothing imported yet)");
        IReadOnlyList<SaveImportResult> results;
        try { results = service.ImportOriginals(install, picks, replace, cancel); }
        catch (OperationCanceledException) { say("setup: stopped"); return Exit.Failed; }
        foreach (var r in results)
        {
            say((r.Imported ? "setup: " : r.Refused ? "setup: refused: " : "setup: failed: ") + r.Why);
            if (!r.Imported) foreach (var line in r.Lines.Where(l => l.StartsWith("import:", StringComparison.Ordinal) || l.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(8)) say("  " + line);
            else foreach (var line in r.Lines.Where(l => l.StartsWith("import:", StringComparison.Ordinal) && !l.StartsWith("import: written to", StringComparison.Ordinal))) say("  " + line);
        }
        if (results.All(r => r.Imported)) return Exit.Done;
        return results.Any(r => !r.Imported && !r.Refused) ? Exit.Failed : Exit.NotImported;
    }

    static bool FFmpeg(Cli cli, Action<string> say, bool install)
    {
        say("== ffmpeg: the player's own FFmpeg, for the films (Vitomir ships none)");
        if (SetupService.FFmpeg(cli.Arg("--ffmpeg")) is { } found)
        {
            say($"setup: ffmpeg: {found.FFmpeg} ({found.Version}): the films will play");
            return true;
        }
        say($"setup: ffmpeg: {FFmpegSearch.Skipped}");
        if (!install)
        {
            say($"setup: to install it: {FFmpegSearch.WingetLine}   (or vitomir-platform --setup ffmpeg --install-ffmpeg runs that line; any FFmpeg on the PATH or named by {FFmpegSearch.Variable} does as well)");
            return false;
        }
        say($"setup: ffmpeg: running {FFmpegSearch.WingetLine}");
        int code = FFmpegSearch.InstallWithWinget(say);
        if (code == 0 && SetupService.FFmpeg(cli.Arg("--ffmpeg")) is { } now) { say($"setup: ffmpeg: installed, {now.FFmpeg} ({now.Version})"); return true; }
        say(code == 0 ? "setup: ffmpeg: winget finished, but this process does not see it yet: open a new terminal and run --setup ffmpeg again" : $"setup: ffmpeg: winget ended with {code}");
        return code == 0;
    }

    static bool Status(SetupService service, bool developer, Action<string> say)
    {
        say("== status: the imported data");
        var state = service.Imported(developer);
        if (state.None) { say($"setup: nothing imported yet ({state.Folder}): run vitomir-platform --setup"); return false; }
        var m = state.Marker!;
        say($"setup: {state.Folder}: {Count(m.Files)} files ({SetupWords.Size(m.Bytes)}) imported {m.MadeUtc.ToLocalTime():yyyy-MM-dd HH:mm} by {m.MadeBy} "
            + $"from {m.Source} ({m.Store}), against {m.Manifest} v{m.ManifestVersion}" + (m.Scripts ? "; the SDK's scripts' sources with it (an older import: not read)" : ""));
        foreach (var n in state.Notes) say($"setup: {n}");
        if (state.Ready) { say("setup: every file there at its size: ready"); return true; }
        say($"setup: not usable: {Count(state.Problems.Count)} problems (run vitomir-platform --setup again to mend it):");
        foreach (var p in state.Problems.Take(20)) say($"  {p}");
        return false;
    }

    static string Count(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// A step's progress said at each tenth, in order (the progress is reported on the step's own thread), **each pass by its name**
    /// (an import's two runs of 10% to 100% were not told apart): <c>  import, sizes: 10% (Levels.nob)</c>, then
    /// <c>  import, copy and hash: 10% (...)</c>, the file named only when the pass reads it.
    /// </summary>
    sealed class Tenths(string step, Action<string> say) : IProgress<SetupProgress>
    {
        int last = -1;
        string pass = "";

        public void Report(SetupProgress value)
        {
            if (value.Total <= 0) return;
            if (value.Step != pass) { pass = value.Step; last = -1; }
            int tenth = (int)(value.Done * 10 / value.Total);
            if (tenth == last || tenth == 0) return;
            last = tenth;
            say($"  {step}, {pass}: {tenth * 10}%" + (value.Item.Length > 0 ? $" ({value.Item})" : ""));
        }
    }

    // ---- the game's start (item 8) --------------------------------------------------------------------------------------

    /// <summary>Where a game played by a module's loop (the Classic game) reads 1.1's files from, and what the start says of it.</summary>
    public sealed record BaseData(string? Folder, string Line, string? Refusal, int Code);

    /// <summary>The words a start with nothing set up says (and shows in a box when the game was started by a double-click).</summary>
    public const string NotSetUp =
        "Vampire: The Masquerade - Redemption's own files are not set up for Vitomir yet. Run the setup once: vitomir-platform --setup " +
        "(it finds your GOG or Steam copy of the game, checks it and imports its files; your install is never changed). " +
        "A setup window comes with a later version.";

    /// <summary>
    /// **The base game's files for a game a module's loop plays**: <c>--oneone &lt;folder&gt;</c> (a developer's override, as a developer's
    /// own commands and the tests name <c>C:\Games\VTMR</c>); else the imported data when it is ready (<see cref="ImportedData.Open"/>);
    /// else <c>VTMR_GAME_DIR</c> (a developer's, as the tests set it); else none, and the start is refused with <see cref="NotSetUp"/>
    /// (or, when the imported data is there but damaged, with what is wrong with it), exit code 3. <c>C:\Games\VTMR</c> is no longer looked
    /// for by itself: a player's game reads only what the setup checked.
    /// </summary>
    public static BaseData Resolve(Cli cli, string userData)
    {
        if (cli.Arg("--oneone") is { } oneone)
            return GameInstall.Locate(oneone) is { } given
                ? new(Path.GetFullPath(given), $"the base game's files: {Path.GetFullPath(given)}, the 1.1 install --oneone names (a developer's override of the imported data)", null, 0)
                : new(null, "", $"--oneone {oneone} is not a 1.1 install (it has no Resource.nob) nor a game folder of Vitomir's own", Exit.Usage);
        var state = ImportedData.Open(userData, DataManifest.Builtin, cli.Flag(DeveloperSwitch.Flag));
        if (state.Ready)
        {
            var m = state.Marker!;
            return new(state.Folder, $"the base game's files: {state.Folder}, imported {m.MadeUtc:yyyy-MM-dd HH:mm} UTC from {m.Source} ({m.Store}; {m.Manifest} v{m.ManifestVersion}"
                + ")"
                + string.Concat(state.Notes.Select(n => "; " + n)), null, 0);
        }
        string damaged = state.None ? "" : $"the imported data in {state.Folder} is not usable ({string.Join("; ", state.Problems.Take(3))}{(state.Problems.Count > 3 ? $"; {state.Problems.Count - 3} more" : "")})";
        if (Environment.GetEnvironmentVariable("VTMR_GAME_DIR") is { Length: > 0 } dev && GameInstall.Locate(dev) is { } devFolder)
            return new(Path.GetFullPath(devFolder), $"the base game's files: {Path.GetFullPath(devFolder)}, VTMR_GAME_DIR's (a developer's: "
                + (state.None ? "nothing is imported" : damaged) + ")", null, 0);
        return new(null, "", state.None ? NotSetUp
            : damaged + ". Run vitomir-platform --setup again to mend it (it copies only what is missing or changed).", Exit.NotTheData);
    }
}
