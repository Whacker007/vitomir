// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.Host;

/// <summary>
/// The program's side of modules: where it looks for them (<c>Modules/</c> beside
/// it, then <c>--modules &lt;dir&gt;</c>) and for games (<c>Games/</c> beside it), the game asked for
/// (<c>--game</c>), the switches given, loading them for a session through the platform's
/// <see cref="ModuleLoader"/>, and <c>--check-modules</c>, the checker on its own.
/// </summary>
internal static class ModulesCommand
{
    /// <summary>
    /// The game asked for: <c>--game &lt;id or file&gt;</c>, or, with none named, the base game where the program's <c>Games/</c>
    /// holds it and not the default (<see cref="ModuleLoader.WhenNoneNamed"/>: release 1's package), else null for the default.
    /// A <c>--game</c> that names a folder is the 1.1 install given as earlier (<see cref="Program.GameFolder"/> takes it), not a game.
    /// </summary>
    internal static string? GameChoice(Cli cli) =>
        cli.Arg("--game") is { } g && !Directory.Exists(g) ? g : ModuleLoader.WhenNoneNamed(ModuleLoader.ProgramGames);

    /// <summary>The folders of module folders: the program's own, then <paramref name="extra"/>.</summary>
    internal static List<string> ModuleFolders(string? extra) =>
        extra is null ? [ModuleLoader.ProgramModules] : [ModuleLoader.ProgramModules, Path.GetFullPath(extra)];

    /// <summary>The folders of game files: the program's own.</summary>
    internal static List<string> GameFolders => [ModuleLoader.ProgramGames];

    /// <summary>Every switch on the command line (<c>--sandbox</c> is <c>sandbox</c>); the loader gives each module those its manifest declares.</summary>
    internal static HashSet<string> Switches(Cli cli) =>
        new(cli.Args.Where(a => a.Length > 2 && a.StartsWith("--", StringComparison.Ordinal)).Select(a => a[2..]), StringComparer.Ordinal);

    /// <summary>The session's game loaded: found, checked, ordered, its modules' code made and configured.</summary>
    internal static LoadedGame Load(SessionOptions options, GameInstall? install)
    {
        var game = ModuleLoader.FindGame(options.GameList, GameFolders);
        return ModuleLoader.Load(new ModuleLoadOptions
        {
            ModuleFolders = ModuleFolders(options.ModulesFolder),
            Game = game,
            OneOne = install,
            Switches = options.Switches,
            Say = options.Say,
            DataOverrides = Vitomir.Platform.Modules.DataOverrides.FromNote(options.DataOverrides),
        });
    }

    /// <summary>
    /// <c>--check-modules</c>: every module found and the game (<c>--game</c>, default the enhanced one, or the base game where
    /// it is the only one: <see cref="GameChoice"/>) checked,
    /// every problem named; exit code 0 when all is well, 2 when anything is refused. Nothing is loaded or run.
    /// </summary>
    internal static int Check(Cli cli, PlatformSettings settings, HostLog log)
    {
        var folders = ModuleFolders(cli.Arg("--modules"));
        GameList game;
        try { game = ModuleLoader.FindGame(GameChoice(cli), GameFolders); }
        catch (ModuleCheckException e)
        {
            foreach (var p in e.Problems) log.Say($"check-modules: {p}");
            return 2;
        }
        using var install = GameInstall.TryOpen(Program.GameFolder(cli, settings));
        var check = ModuleLoader.Check(folders, game, install);
        log.Say($"check-modules: {check.Found.Count} module(s) in {string.Join(", ", folders)}");
        foreach (var m in check.Found)
        {
            log.Say($"  {m.Id} {m.Version}: {m.Name} ({m.Kind.ToString().ToLowerInvariant()}, {(m.HasCode ? m.Assembly : "data only")}, platform API {m.Platform}"
                + (m.Dependencies.Count == 0 ? "" : $", needs {string.Join(", ", m.Dependencies.Select(d => $"{d.Id} {d.Least}+"))}")
                + (m.Overrides.Count == 0 ? "" : $", {m.Overrides.Count} override(s)") + $") in {m.Folder}");
            // The switches its manifest declares (a game played by a module's own loop takes no other without --developer).
            foreach (var sw in m.Switches) log.Say($"    --{sw.Name}: {sw.Help}");
        }
        if (!check.Ok)
        {
            log.Say($"check-modules: game {game.Id} refused (fail closed), {check.Problems.Count} problem(s):");
            foreach (var p in check.Problems) log.Say($"  - {p}");
        }
        if (check.Elsewhere.Count > 0)
        {
            log.Say($"check-modules: {check.Elsewhere.Count} problem(s) in modules game {game.Id} does not load (they do not stop it):");
            foreach (var p in check.Elsewhere) log.Say($"  - {p}");
        }
        if (!check.AllWell) return 2;
        var assets = check.Assets!;
        log.Say($"check-modules: game {game.Id} ({game.File}) loads {string.Join(", ", check.Order.Select(m => $"{m.Id} {m.Version}"))}; "
            + $"{check.Order.Sum(m => assets.List(m.Id, "").Count)} asset id(s); 1.1 install {(install is null ? "not found" : install.Root)}; platform API {PlatformApi.Version}");
        foreach (var o in assets.Overrides) log.Say($"  {o}");
        log.Say("check-modules: all well");
        return 0;
    }
}
