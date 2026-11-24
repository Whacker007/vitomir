// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using Vitomir.Platform.Assets;
using Vitomir.Platform.PlayerModules;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.Host;

/// <summary>
/// **The player modules' steps of the setup** (<c>vitomir-platform --setup module-…</c>), each the <see cref="PlayerModuleStore"/>'s,
/// which a page of the launcher can call the same way:
/// <c>module-add &lt;folder | zip | .nob&gt; [--id &lt;id&gt;] [--replace]</c> imports a pack and puts it at the end of the game's list, on;
/// <c>module-remove &lt;id&gt;</c>; <c>module-list</c>; <c>module-order &lt;id&gt;...</c> (those first, in that order);
/// <c>module-enable &lt;id&gt; [--off]</c> and <c>module-disable &lt;id&gt;</c>; <c>module-check [--all] [--fresh]</c> (the start's resolution
/// and checks, every problem a line; <c>--all</c> names every file of a problem of many); <c>module-accept</c> (accepts the warnings of
/// the combination on, which a start then plays). <c>--game &lt;id&gt;</c> names the game (the Classic game's by default).
/// </summary>
public static partial class SetupCommand
{
    static int Modules(Cli cli, string step, Action<string> say, CancellationToken cancel)
    {
        string gameId = cli.Arg("--game") is { } g && !g.Contains('.') && !g.Contains('/') && !g.Contains('\\') ? g : PlayerModuleManifest.ClassicGame;
        var store = new PlayerModuleStore(UserData.Folder, gameId);
        var words = Positional(cli.Args, Switch, 1).ToList();
        switch (step)
        {
            case "module-add":
                {
                    if (words.Count != 1) { say("setup: module-add: name one pack: a folder, a zip or a .nob (module-add <path> [--id <id>] [--replace])"); return Exit.Usage; }
                    say($"== module-add: {words[0]} into {store.Folder}, for the game {gameId}");
                    using var install = BaseInstall(cli, say, required: false);
                    var report = store.Add(words[0], new ModuleImportOptions
                    {
                        Id = cli.Arg("--id"), Replace = cli.Flag("--replace"), By = PlatformVersion.Text, Progress = new Tenths("module-add", say),
                    }, install, cancel);
                    foreach (var o in report.LeftOut.Where(o => o.Class is "code" or "program"))
                        say($"  refused ({o.Class}): {o.Path}: {o.Why}");
                    var documents = report.LeftOut.Where(o => o.Class == "document").ToList();
                    foreach (var o in documents.Take(10)) say($"  kept aside (a document, never read by the game): {o.Path}");
                    if (documents.Count > 10) say($"  ... and {Count(documents.Count - 10)} more documents kept aside");
                    foreach (var o in report.LeftOut.Where(o => o.Class == "twice")) say($"  left out: {o.Path}: {o.Why}");
                    foreach (var n in report.Notes) say($"setup: {n}");
                    if (report.Module is not { } m)
                    {
                        say($"setup: not imported: {report.Refusal}");
                        return Exit.NotImported;
                    }
                    int refused = report.LeftOut.Count(o => o.Class is "code" or "program");
                    say($"setup: imported the module {m.Id} {m.Manifest.Version} ({PackKindWords.Words(m.Kinds)}{(m.Rules ? "; a rules module: every player of a network game needs it, and saves name it" : "")}): "
                        + $"{Of(m.Record.Files.Count, "file")} ({SetupWords.Size(m.Record.Files.Sum(f => f.Size))}), {Count(report.Copied)} copied, {Count(report.Kept)} already there; "
                        + $"each read back and its SHA-256 kept" + (refused > 0 ? $"; {Count(refused)} refused (code and program files: player modules carry data only)" : "")
                        + (documents.Count > 0 ? $"; {Count(documents.Count)} kept aside in {Path.Combine(m.Folder, PlayerModule.DocumentsFolderName)}" : ""));
                    int broken = m.Record.Files.Count(PlayerModuleChecks.DoesNotRead);
                    if (broken > 0) say($"setup: {Count(broken)} of its files do not decode: module-check names them, and the game does not start with it on until they are mended");
                    say($"setup: it is on, at the end of the game's list (module-order changes the order; module-enable {m.Id} --off turns it off); module-check checks the modules on together");
                    return Exit.Done;
                }
            case "module-remove":
                {
                    if (words.Count != 1) { say("setup: module-remove: name one module (module-list lists them)"); return Exit.Usage; }
                    if (store.Remove(words[0]) is { } why) { say($"setup: module-remove: {why}"); return Exit.Usage; }
                    say($"setup: removed the module {words[0]} (its imported copy; the pack it came from is not touched)");
                    return Exit.Done;
                }
            case "module-enable":
            case "module-disable":
                {
                    if (words.Count != 1) { say($"setup: {step}: name one module (module-list lists them)"); return Exit.Usage; }
                    bool on = step == "module-enable" && !cli.Flag("--off");
                    if (store.Enable(words[0], on) is { } why) { say($"setup: {step}: {why}"); return Exit.Usage; }
                    say($"setup: the module {words[0]} is {(on ? "on" : "off")}");
                    return Exit.Done;
                }
            case "module-order":
                {
                    if (words.Count == 0) { say("setup: module-order: name the modules in the order you want (those named first; the others after them, as they were)"); return Exit.Usage; }
                    if (store.Order(words) is { } why) { say($"setup: module-order: {why}"); return Exit.Usage; }
                    say($"setup: the game's list is now: {string.Join(", ", store.List(out _).Modules.Select(e => e.Id + (e.Enabled ? "" : " (off)")))}");
                    return Exit.Done;
                }
            case "module-list":
                {
                    List(store, say);
                    return Exit.Done;
                }
            case "module-check":
            case "module-accept":
                {
                    using var install = BaseInstall(cli, say, required: true);
                    if (install is null) return Exit.NotTheData;
                    var start = store.Prepare(install, fresh: cli.Flag("--fresh"), cancel);
                    say($"== {step}: the player modules on for the game {gameId}, in load order: {(start.Order.Count == 0 ? "none" : string.Join(", ", start.Order.Select(m => m.ToString())))}"
                        + (start.Cached ? " (the checks of this combination, kept from the last check)" : ""));
                    SayProblems(start, cli.Flag("--all"), say);
                    if (step == "module-accept")
                    {
                        if (store.Accept(start) is { } why) { say($"setup: not accepted: {why}"); return Exit.ModuleErrors; }
                        say(start.Warnings.Any()
                            ? $"setup: accepted the {(start.Warnings.Count() == 1 ? "warning" : Of(start.Warnings.Count(), "warning"))} of this combination: the game starts with it; any change (a module added, removed, reordered, updated) asks again"
                            : "setup: nothing to accept: the combination has no warnings");
                        return Exit.Done;
                    }
                    return StartCode(start);
                }
        }
        return Exit.Usage;
    }

    /// <summary>The base game's files the checks read: the start's own (<see cref="Resolve"/>); none said, and an error when <paramref name="required"/>.</summary>
    static GameInstall? BaseInstall(Cli cli, Action<string> say, bool required)
    {
        var data = Resolve(cli, UserData.Folder);
        if (data.Folder is not null) return GameInstall.TryOpen(data.Folder);
        say(required ? $"setup: the player modules are checked against the base game's files, which are not set up: {data.Refusal}"
                     : "setup: the base game's files are not set up yet: the pack's paths keep its own spelling (1.1's is taken where it is known)");
        return null;
    }

    static void List(PlayerModuleStore store, Action<string> say)
    {
        var list = store.List(out string? problem);
        var (installed, broken) = store.Installed();
        say($"== module-list: the player modules of the game {store.GameId} ({store.Folder}), in the game's order");
        if (problem is not null) say($"setup: {problem}");
        if (list.Modules.Count == 0 && installed.Count == 0 && broken.Count == 0) { say("setup: none (module-add <folder | zip | .nob> adds a pack)"); return; }
        int n = 0;
        foreach (var e in list.Modules)
        {
            n++;
            if (installed.TryGetValue(e.Id, out var m))
            {
                var whole = m.CheckWhole();
                say($"  {n}. {(e.Enabled ? "on " : "off")} {m.Id} {m.Manifest.Version} \"{m.Manifest.Name}\"{(m.Manifest.Author.Length > 0 ? " by " + m.Manifest.Author : "")}: "
                    + $"{PackKindWords.Words(m.Kinds)}{(m.Rules ? " (rules)" : "")}, {Of(m.Record.Files.Count, "file")} ({SetupWords.Size(m.Record.Files.Sum(f => f.Size))}), "
                    + $"imported {m.Record.ImportedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} from {m.Record.Source}"
                    + (whole.Count > 0 ? $"; NOT WHOLE: {string.Join("; ", whole.Take(3))}" : "")
                    + (m.Record.Files.Count(PlayerModuleChecks.DoesNotRead) is var bad and > 0 ? $"; {Count(bad)} files do not decode" : ""));
            }
            else if (broken.TryGetValue(e.Id, out var why)) say($"  {n}. {(e.Enabled ? "on " : "off")} {e.Id}: {why}");
            else say($"  {n}. {(e.Enabled ? "on " : "off")} {e.Id}: not imported (module-add it, or module-remove {e.Id})");
        }
        foreach (var m in installed.Values.Where(m => !list.Modules.Any(e => e.Id == m.Id)))
            say($"  -  off {m.Id} {m.Manifest.Version}: imported, not in the game's list (module-enable {m.Id})");
        foreach (var (id, why) in broken.Where(b => !list.Modules.Any(e => e.Id == b.Key)))
            say($"  -  {id}: {why}");
        say("setup: module-check checks the modules on together (dependencies, order, the files they share, the textures and the models they were made for)");
    }

    /// <summary>Every problem as a line, errors first; a problem of many files names three and how many more unless <paramref name="all"/>.</summary>
    static void SayProblems(ModuleStart start, bool all, Action<string> say)
    {
        foreach (var p in start.Problems.OrderBy(p => p.Level)) say("  " + (all ? p.FullLine : p.Line));
        int errors = start.Errors.Count(), warnings = start.Warnings.Count();
        if (errors > 0) say($"setup: {Count(errors)} {(errors == 1 ? "error" : "errors")}: the game does not start with these modules on until {(errors == 1 ? "it is" : "they are")} mended or the modules turned off");
        if (warnings > 0)
            say(start.Accepted ? $"setup: {Count(warnings)} {(warnings == 1 ? "warning" : "warnings")}, accepted for this combination"
                               : $"setup: {Count(warnings)} {(warnings == 1 ? "warning" : "warnings")}, not accepted for this combination: the game starts with it once they are (vitomir-platform --setup module-accept)");
        if (errors == 0 && warnings == 0 && start.Order.Count > 0) say("setup: no problem: the game starts with these modules");
    }

    /// <summary><c>1 file</c>, <c>3,139 files</c>.</summary>
    static string Of(int n, string what) => n == 1 ? $"1 {what}" : $"{Count(n)} {what}s";

    static int StartCode(ModuleStart start) => start.Errors.Any() ? Exit.ModuleErrors : start.CanStart ? Exit.Done : Exit.ModuleWarnings;

    /// <summary>
    /// **The player modules at a game's start** (the game's list resolved and its modules checked: <see cref="PlayerModuleStore.Prepare"/>):
    /// the modules' folders in load order, for <c>--player-module</c>; or the start refused, every problem said, with the exit code
    /// (<see cref="Exit.ModuleErrors"/>, <see cref="Exit.ModuleWarnings"/>) and the words for a box. Nothing is said when the game's list is empty.
    /// </summary>
    public static (IReadOnlyList<string> Folders, int? Refused, string Words) StartModules(string gameId, GameInstall? install, Action<string> say)
    {
        var store = new PlayerModuleStore(UserData.Folder, gameId);
        if (!File.Exists(store.ListPath)) return ([], null, "");
        var list = store.List(out _);
        if (list.Modules.Count == 0) return ([], null, "");
        var start = store.Prepare(install);
        if (start.Order.Count == 0 && !start.Problems.Any(p => p.Level != ProblemLevel.Note))
        {
            say($"game {gameId}: player modules: none on");
            return ([], null, "");
        }
        say($"game {gameId}: player modules on, in load order: {string.Join(", ", start.Order.Select(m => $"{m} ({PackKindWords.Words(m.Kinds)}{(m.Rules ? ", rules" : "")})"))}"
            + (start.Cached ? "; the checks of this combination kept from the last" : "; checked"));
        foreach (var p in start.Problems.OrderBy(p => p.Level)) say($"game {gameId}: {SetupWords.Plain(p.Line)}");
        int errors = start.Errors.Count(), warnings = start.Warnings.Count();
        if (errors > 0)
        {
            string words = $"The player modules on have {Count(errors)} {(errors == 1 ? "error" : "errors")} (each said in the log), so the game does not start. "
                + "Mend them, or turn the modules off: vitomir-platform --setup module-check names them, module-enable <id> --off turns one off.";
            say($"game {gameId}: not started: {words}");
            return ([], Exit.ModuleErrors, words);
        }
        if (warnings > 0 && !start.Accepted)
        {
            string words = $"The player modules on have {Count(warnings)} {(warnings == 1 ? "warning" : "warnings")} (each said in the log), not accepted for this combination. "
                + "vitomir-platform --setup module-check lists them and module-accept accepts them; the game then starts with these modules until anything changes.";
            say($"game {gameId}: not started: {words}");
            return ([], Exit.ModuleWarnings, words);
        }
        if (warnings > 0) say($"game {gameId}: {Of(warnings, "warning")} of the player modules, accepted for this combination");
        return ([.. start.Order.Select(m => m.Folder)], null, "");
    }
}
