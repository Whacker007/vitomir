// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Setup;

namespace Vitomir.Platform.PlayerModules;

/// <summary>
/// **What a start of a game with player modules finds**: the modules on, in load order; every problem, the resolution's and the checks';
/// the combination's key (<see cref="Combination"/>); whether its warnings were accepted; whether the checks came from the cache.
/// </summary>
public sealed record ModuleStart(IReadOnlyList<PlayerModule> Order, IReadOnlyList<PlayerModuleProblem> Problems, string Combination, bool Accepted, bool Cached)
{
    public IEnumerable<PlayerModuleProblem> Errors => Problems.Where(p => p.Level == ProblemLevel.Error);
    public IEnumerable<PlayerModuleProblem> Warnings => Problems.Where(p => p.Level == ProblemLevel.Warning);

    /// <summary>Whether the game may start: no error, and no warning or the combination's warnings accepted.</summary>
    public bool CanStart => !Errors.Any() && (!Warnings.Any() || Accepted);

    /// <summary>The modules' stamps, in order (<c>id version hash</c>), as the accepted combination keeps them.</summary>
    public IReadOnlyList<string> Stamps => [.. Order.Select(m => m.Stamp)];
}

/// <summary>
/// **The player's modules of a game**: imported into <c>&lt;user data&gt;\PlayerModules\&lt;id&gt;\</c> (one folder each; the platform's
/// modules' own user folders are <c>&lt;user data&gt;\Modules\&lt;id&gt;\</c>, so a player module's id never meets theirs), listed in the game's
/// settings (<see cref="GameModuleList"/>) in the player's order, each on or off. What the setup's module steps and the game's start call:
/// add (import), remove, list, order, on and off, the start's resolution and checks (<see cref="Prepare"/>), and the acceptance of a
/// combination's warnings (<see cref="Accept"/>).
/// </summary>
public sealed class PlayerModuleStore(string userData, string gameId = PlayerModuleManifest.ClassicGame)
{
    /// <summary>The folder of the player's modules in the user data.</summary>
    public const string FolderName = "PlayerModules";

    public string UserData { get; } = userData;
    public string GameId { get; } = gameId;

    /// <summary>Where the modules are imported to.</summary>
    public string Folder => Path.Combine(UserData, FolderName);

    /// <summary>The game's list.</summary>
    public string ListPath => GameModuleList.PathFor(UserData, GameId);

    string CachePath => Path.Combine(Path.GetDirectoryName(ListPath)!, GameModuleList.CheckCacheName);

    /// <summary>The game's list; a list that does not read is said in <paramref name="problem"/>, and nothing is on.</summary>
    public GameModuleList List(out string? problem) => GameModuleList.Read(ListPath, out problem);

    /// <summary>Every imported module, by id; and every folder that is not one (an import that did not finish, a manifest that does not read), with why.</summary>
    public (Dictionary<string, PlayerModule> Installed, Dictionary<string, string> Broken) Installed()
    {
        var installed = new Dictionary<string, PlayerModule>(StringComparer.Ordinal);
        var broken = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(Folder)) return (installed, broken);
        foreach (var dir in Directory.EnumerateDirectories(Folder).Order(StringComparer.Ordinal))
        {
            var problems = new List<string>();
            string name = Path.GetFileName(dir);
            if (PlayerModule.Open(dir, problems) is { } m)
            {
                if (m.Id != name) broken[name] = $"its folder is {name} and its module.json says {m.Id}";
                else installed[m.Id] = m;
            }
            else broken[name] = string.Join("; ", problems.Select(p => p.Replace(dir, $"{FolderName}{Path.DirectorySeparatorChar}{name}")));
        }
        return (installed, broken);
    }

    /// <summary>
    /// **Adds a pack**: imports it (<see cref="PlayerModuleImport.Run"/>) and, when it is new to the game's list, puts it at the end, on.
    /// <paramref name="install"/> gives 1.1's own spelling of its paths when the base game's files are at hand.
    /// </summary>
    public ModuleImportReport Add(string source, ModuleImportOptions options, GameInstall? install = null, CancellationToken cancel = default)
    {
        if (install is not null && options.BaseSpelling is null) options = options with { BaseSpelling = PlayerModuleImport.SpellingOf(install) };
        var report = PlayerModuleImport.Run(source, Folder, options, cancel);
        if (report.Module is { } m)
        {
            var list = List(out _);
            if (!list.Modules.Any(e => e.Id == m.Id)) list.Modules.Add(new GameModuleEntry { Id = m.Id, Enabled = true });
            list.Write(ListPath);
        }
        return report;
    }

    /// <summary>Removes a module: its folder (the import's own copy; the pack it came from is never touched) and its line in the list. Answers why not, or null.</summary>
    public string? Remove(string id)
    {
        var list = List(out _);
        string folder = Path.Combine(Folder, id);
        bool listed = list.Modules.RemoveAll(e => e.Id == id) > 0;
        if (!listed && !Directory.Exists(folder)) return $"there is no module {id} (module-list lists them)";
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        list.Write(ListPath);
        return null;
    }

    /// <summary>Turns a module on or off. Answers why not, or null.</summary>
    public string? Enable(string id, bool on)
    {
        var list = List(out _);
        int at = list.Modules.FindIndex(e => e.Id == id);
        if (at < 0)
        {
            if (!Directory.Exists(Path.Combine(Folder, id))) return $"there is no module {id} (module-list lists them)";
            list.Modules.Add(new GameModuleEntry { Id = id, Enabled = on });
        }
        else list.Modules[at] = list.Modules[at] with { Enabled = on };
        list.Write(ListPath);
        return null;
    }

    /// <summary>
    /// **The player's order**: the modules named, in that order, first; the others after them in the order they had. Answers why not, or
    /// null (a name that is no module of the list's).
    /// </summary>
    public string? Order(IReadOnlyList<string> ids)
    {
        var list = List(out _);
        var unknown = ids.Where(id => !list.Modules.Any(e => e.Id == id)).ToList();
        if (unknown.Count > 0) return $"not in the game's list: {string.Join(", ", unknown)} (module-list lists them)";
        if (ids.Distinct().Count() != ids.Count) return "a module is named twice";
        var named = ids.Select(id => list.Modules.First(e => e.Id == id)).ToList();
        list.Modules = [.. named, .. list.Modules.Where(e => !ids.Contains(e.Id))];
        list.Write(ListPath);
        return null;
    }

    /// <summary>
    /// **Sorts the game's list by dependencies** (<see cref="PlayerModuleResolver.Sorted"/>): each module after those it needs or loads
    /// after, the player's order kept otherwise. Answers the modules moved down after what they need (none: the list was in order already).
    /// </summary>
    public IReadOnlyList<string> SortByDependencies()
    {
        var list = List(out _);
        var sorted = PlayerModuleResolver.Sorted(list, Installed().Installed);
        var was = list.Modules.Select(e => e.Id).ToList();
        if (sorted.SequenceEqual(was)) return [];
        var moved = sorted.Where((id, i) => i > was.IndexOf(id)).ToList();
        list.Modules = [.. sorted.Select(id => list.Modules.First(e => e.Id == id))];
        list.Write(ListPath);
        return moved;
    }

    /// <summary>
    /// Moves a module one place up (<paramref name="by"/> -1) or down (+1) in the game's list. Answers why not, or null.
    /// </summary>
    public string? Move(string id, int by)
    {
        var list = List(out _);
        int at = list.Modules.FindIndex(e => e.Id == id);
        if (at < 0) return $"there is no module {id} in the game's list";
        int to = at + by;
        if (to < 0 || to >= list.Modules.Count) return by < 0 ? $"{id} is first already" : $"{id} is last already";
        (list.Modules[at], list.Modules[to]) = (list.Modules[to], list.Modules[at]);
        list.Write(ListPath);
        return null;
    }

    /// <summary>What the base game's files are, for the combination's key: the imported data by its manifest, else the folder.</summary>
    public static string BaseIdentity(GameInstall? install)
    {
        if (install is null) return "none";
        if (ImportedData.ReadMarker(install.Root) is { } m) return $"imported {m.Manifest} v{m.ManifestVersion} {m.Files} files {m.Bytes} bytes";
        return $"folder {install.Root}";
    }

    /// <summary>
    /// **The start's view**: the list resolved (<see cref="PlayerModuleResolver"/>) and the modules on checked together
    /// (<see cref="PlayerModuleChecks"/>) against <paramref name="install"/>, the checks taken from the cache when the combination is the
    /// one last checked (unless <paramref name="fresh"/>); whether its warnings were accepted.
    /// </summary>
    public ModuleStart Prepare(GameInstall? install, bool fresh = false, CancellationToken cancel = default)
    {
        var list = List(out string? listProblem);
        var (installed, broken) = Installed();
        var resolved = PlayerModuleResolver.Resolve(list, installed, broken, GameId);
        var problems = new List<PlayerModuleProblem>();
        if (listProblem is not null) problems.Add(new(ProblemLevel.Error, "", GameModuleList.FileName, listProblem, "remove the file to start with no module on") { Kind = "list" });
        problems.AddRange(resolved.Problems);
        string combination = Combination(install, resolved.Order);
        bool cached = false;
        if (resolved.Order.Count > 0 && !resolved.Problems.Any(p => p.IsError && p.Kind is "whole" or "added"))
        {
            if (!fresh && GameModuleList.ReadCache(CachePath) is { } cache && cache.Combination == combination)
            {
                problems.AddRange(cache.Problems.Select(p => p.Problem));
                cached = true;
            }
            else
            {
                var checks = PlayerModuleChecks.Run(install, resolved.Order, cancel);
                problems.AddRange(checks);
                GameModuleList.WriteCache(CachePath, new GameModuleList.CheckCache { Combination = combination, Problems = [.. checks.Select(GameModuleList.CachedProblem.Of)] });
            }
        }
        bool accepted = list.Accepted.Any(a => a.Combination == combination);
        return new ModuleStart(resolved.Order, problems, combination, accepted, cached);
    }

    /// <summary>
    /// The combination's key: SHA-256, hex, over the game, the base game's files (<see cref="BaseIdentity"/>) and each module on in order by
    /// its id, version, content hash and manifest hash. Anything changed (a module added, removed, reordered, updated, its manifest
    /// edited, another base) is another key.
    /// </summary>
    public string Combination(GameInstall? install, IReadOnlyList<PlayerModule> order)
    {
        var text = new StringBuilder();
        text.Append("game ").Append(GameId).Append('\n').Append("base ").Append(BaseIdentity(install)).Append('\n');
        foreach (var m in order) text.Append($"{m.Id} {m.Manifest.Version} {m.Record.ContentHash} {m.Record.ManifestHash}\n");
        return PlayerModule.Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }

    /// <summary>
    /// **Accepts the warnings of the combination <paramref name="start"/> found** (kept with the modules' versions and hashes, and the
    /// warnings). Answers why not, or null: a combination with errors cannot be accepted.
    /// </summary>
    public string? Accept(ModuleStart start)
    {
        if (start.Errors.Any()) return $"it has {start.Errors.Count()} errors, which no acceptance passes: fix them first";
        var list = List(out _);
        list.Accepted.RemoveAll(a => a.Combination == start.Combination);
        list.Accepted.Add(new AcceptedCombination
        {
            Combination = start.Combination, AcceptedUtc = DateTime.UtcNow, Modules = [.. start.Stamps], Warnings = [.. start.Warnings.Select(w => w.Line)],
        });
        list.Write(ListPath);
        return null;
    }
}
