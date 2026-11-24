// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Platform.Assets;

namespace Vitomir.Platform.Modules;

/// <summary>The modules were refused (fail closed): every problem the checker found, named.</summary>
public sealed class ModuleCheckException(IReadOnlyList<string> problems)
    : Exception($"the modules were refused: {string.Join("; ", problems)}")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>What the checker found: every problem of the game (none when it may load), the problems of modules it does not load, the modules found, the game's load order and its registry.</summary>
public sealed record ModuleCheck
{
    /// <summary>Every problem of the game and the modules it loads, each once, in the order found: any refuses the game.</summary>
    public required IReadOnlyList<string> Problems { get; init; }

    /// <summary>
    /// Problems of modules the game does not load (and of folders that are no good module): they do not refuse
    /// the game, the loader logs them, and <c>--check-modules</c> fails on them. When the game lists a module
    /// that is not there, these join <see cref="Problems"/> (one of them may be why).
    /// </summary>
    public IReadOnlyList<string> Elsewhere { get; init; } = [];

    /// <summary>Every module found, by folder.</summary>
    public required IReadOnlyList<ModuleManifest> Found { get; init; }

    /// <summary>The game's modules in load order (empty when there are problems).</summary>
    public IReadOnlyList<ModuleManifest> Order { get; init; } = [];

    /// <summary>The game's registry (null when there are problems).</summary>
    public AssetRegistry? Assets { get; init; }

    public bool Ok => Problems.Count == 0;

    /// <summary>Whether the game and every module found are without a problem (what <c>--check-modules</c> asks).</summary>
    public bool AllWell => Problems.Count == 0 && Elsewhere.Count == 0;
}

/// <summary>
/// The checker (ours): run by the loader at every start and
/// on its own (<c>vitomir-platform --check-modules</c>). It proves, failing closed and naming every problem: each
/// manifest is valid; every id is well formed; no module id is claimed by two folders and no asset id by two
/// files; each module's dependencies are present and their versions accepted; each module was built for a
/// compatible platform API (<see cref="PlatformApi"/>); a content module depends on no rules module; there is no
/// dependency cycle; and for the game, that it lists every module it loads, that every override is declared,
/// overrides an id that a module it depends on has, and that two modules overriding one id are ordered by their
/// dependencies (else both are named). The game's order is its list's, moved only where a dependency must load
/// first (stable). A problem of a module the game does not load is named apart (<see cref="ModuleCheck.Elsewhere"/>)
/// and does not refuse the game.
/// </summary>
public static class ModuleChecker
{
    /// <summary>
    /// The modules in <paramref name="modulesFolders"/> (each a folder of module folders, in the order given;
    /// within one, its subfolders in ordinal order): every folder with a manifest, every folder without one and
    /// every module id two folders claim named.
    /// </summary>
    public static IReadOnlyList<ModuleManifest> Find(IEnumerable<string> modulesFolders, List<string> problems)
    {
        var tagged = new List<(string? Module, string Problem)>();
        var found = Find(modulesFolders, tagged);
        problems.AddRange(tagged.Select(t => t.Problem));
        return found;
    }

    static IReadOnlyList<ModuleManifest> Find(IEnumerable<string> modulesFolders, List<(string? Module, string Problem)> problems)
    {
        var found = new List<ModuleManifest>();
        var byId = new Dictionary<string, ModuleManifest>(StringComparer.Ordinal);
        foreach (var root in modulesFolders)
        {
            if (!Directory.Exists(root)) continue;
            var folders = Directory.GetDirectories(root);
            Array.Sort(folders, StringComparer.Ordinal);
            foreach (var folder in folders)
            {
                if (!File.Exists(Path.Combine(folder, ModuleManifest.FileName)))
                {
                    problems.Add((null, $"{Path.GetFullPath(folder)}: a folder among the modules with no {ModuleManifest.FileName}"));
                    continue;
                }
                var said = new List<string>();
                var m = ModuleManifest.Read(folder, said);
                problems.AddRange(said.Select(p => ((string?)null, p)));
                if (m is null) continue;
                if (byId.TryGetValue(m.Id, out var other))
                {
                    problems.Add((m.Id, $"the module id {m.Id} is claimed twice, by {other.Folder} and {m.Folder}"));
                    continue;
                }
                byId[m.Id] = m;
                found.Add(m);
            }
        }
        return found;
    }

    /// <summary>Checks the modules in <paramref name="modulesFolders"/> and the game; see the class.</summary>
    public static ModuleCheck Check(IEnumerable<string> modulesFolders, GameList game, GameInstall? oneOne = null)
    {
        var tagged = new List<(string? Module, string Problem)>();
        var roots = modulesFolders.ToList();
        var found = Find(roots, tagged);
        var check = Check(found, game, oneOne, tagged);
        if (game.Modules.Any(id => !found.Any(m => m.Id == id)))
        {
            // Name where the program looked, once, after the modules it did not find, and every problem elsewhere (one may be why).
            var where = string.Join(", ", roots.Select(Path.GetFullPath));
            return check with { Problems = Unique([.. check.Problems, $"(the modules were looked for in {where})", .. check.Elsewhere]), Elsewhere = [] };
        }
        return check;
    }

    /// <summary>Checks <paramref name="found"/> (every module available) and the game made of some of them.</summary>
    public static ModuleCheck Check(IReadOnlyList<ModuleManifest> found, GameList game, GameInstall? oneOne = null) =>
        Check(found, game, oneOne, []);

    static ModuleCheck Check(IReadOnlyList<ModuleManifest> found, GameList game, GameInstall? oneOne, List<(string? Module, string Problem)> before)
    {
        var problems = new List<string>();
        var elsewhere = new List<string>();
        bool InGame(string? id) => id is not null && game.Modules.Contains(id);
        foreach (var (module, problem) in before) (InGame(module) ? problems : elsewhere).Add(problem);

        var byId = new Dictionary<string, ModuleManifest>(StringComparer.Ordinal);
        foreach (var m in found) byId.TryAdd(m.Id, m);

        // Every module found, on its own: its files, the platform it was built for, what it needs.
        var files = new Dictionary<string, ModuleFiles>(StringComparer.Ordinal);
        foreach (var m in found)
        {
            var mine = InGame(m.Id) ? problems : elsewhere;
            files[m.Id] = ModuleFiles.Index(m, mine);
            if (PlatformApi.Refuses(m.Platform) is { } why) mine.Add($"module {m.Id} is refused: {why}");
            foreach (var d in m.Dependencies)
            {
                if (!byId.TryGetValue(d.Id, out var dep))
                    mine.Add($"module {m.Id} needs module {d.Id} (at least {d.Least}, major {d.Least.Major}), which is not there");
                else if (!dep.Version.Satisfies(d.Least))
                    mine.Add($"module {m.Id} needs module {d.Id} at least {d.Least} within major {d.Least.Major}, and {d.Id} is {dep.Version}");
                else if (m.Kind == ModuleKind.Content && dep.Kind == ModuleKind.Rules)
                    mine.Add($"content module {m.Id} depends on rules module {d.Id}; content never depends on rules");
            }
        }
        foreach (var (members, cycle) in Cycles(found, byId))
            (members.Any(InGame) ? problems : elsewhere).Add($"a dependency cycle: {cycle}");

        // The game: every module it lists, every module they need listed, in order.
        var listed = new List<ModuleManifest>();
        foreach (var id in game.Modules)
        {
            if (byId.TryGetValue(id, out var m)) listed.Add(m);
            else problems.Add($"game {game.Id} lists module {id}, which is not there");
        }
        foreach (var m in listed)
            foreach (var d in m.Dependencies)
                if (!game.Modules.Contains(d.Id))
                    problems.Add($"game {game.Id} loads module {m.Id}, which needs module {d.Id}, and the game does not list it{(byId.TryGetValue(d.Id, out var there) ? $" (it is in {there.Folder})" : "")}");
        var order = Order(listed);

        // The game's overrides: each of an id a module it depends on has; two overriders ordered by their dependencies.
        var closure = Closure(found, byId);
        var overriders = new SortedDictionary<AssetId, List<string>>();
        foreach (var m in order)
            foreach (var id in files[m.Id].Overrides.Keys)
            {
                if (!closure[m.Id].Contains(id.Module))
                    problems.Add($"module {m.Id} overrides {id}, but does not depend on module {id.Module}");
                else if (files.TryGetValue(id.Module, out var owner) && !owner.Own.ContainsKey(id))
                    problems.Add($"module {m.Id} overrides {id}, which module {id.Module} does not have");
                if (!overriders.TryGetValue(id, out var list)) overriders[id] = list = [];
                list.Add(m.Id);
            }
        foreach (var (id, list) in overriders)
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (!closure[list[j]].Contains(list[i]) && !closure[list[i]].Contains(list[j]))
                        problems.Add($"modules {list[i]} and {list[j]} both override {id} and neither depends on the other, so the load order does not say which wins");

        var unique = Unique(problems);
        var others = Unique(elsewhere);
        if (unique.Count > 0) return new ModuleCheck { Problems = unique, Elsewhere = others, Found = found };
        var registry = new AssetRegistry([.. order.Select(m => files[m.Id])], closure, oneOne);
        return new ModuleCheck { Problems = [], Elsewhere = others, Found = found, Order = order, Assets = registry };
    }

    /// <summary>
    /// The load order of <paramref name="listed"/> (the game's order): each time, the first module in the game's
    /// order whose dependencies among the listed have all been placed. Stable; a cycle leaves the rest unplaced
    /// (the checker names the cycle).
    /// </summary>
    public static IReadOnlyList<ModuleManifest> Order(IReadOnlyList<ModuleManifest> listed)
    {
        var placed = new List<ModuleManifest>();
        var placedIds = new HashSet<string>(StringComparer.Ordinal);
        var listedIds = new HashSet<string>(listed.Select(m => m.Id), StringComparer.Ordinal);
        var remaining = new List<ModuleManifest>(listed);
        while (remaining.Count > 0)
        {
            int next = remaining.FindIndex(m => m.Dependencies.All(d => !listedIds.Contains(d.Id) || placedIds.Contains(d.Id)));
            if (next < 0) break;
            placed.Add(remaining[next]);
            placedIds.Add(remaining[next].Id);
            remaining.RemoveAt(next);
        }
        return placed;
    }

    /// <summary>Each dependency cycle among <paramref name="found"/>, once, written <c>a → b → a</c> from the earliest module found in it.</summary>
    static List<(List<string> Members, string Text)> Cycles(IReadOnlyList<ModuleManifest> found, Dictionary<string, ModuleManifest> byId)
    {
        var cycles = new List<(List<string>, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 on the path, 2 done
        var path = new List<string>();
        void Visit(string id)
        {
            state[id] = 1;
            path.Add(id);
            foreach (var d in byId[id].Dependencies)
            {
                if (!byId.ContainsKey(d.Id)) continue;
                if (state.GetValueOrDefault(d.Id) == 1)
                {
                    var loop = path.Skip(path.IndexOf(d.Id)).ToList();
                    // Written from its member found first, so the same cycle reads the same however it was reached.
                    int start = loop.Select(x => found.ToList().FindIndex(m => m.Id == x)).Select((index, at) => (index, at)).Min().at;
                    var turned = loop.Skip(start).Concat(loop.Take(start)).ToList();
                    var text = string.Join(" → ", turned.Append(turned[0]));
                    if (seen.Add(text)) cycles.Add((turned, text));
                }
                else if (state.GetValueOrDefault(d.Id) == 0) Visit(d.Id);
            }
            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }
        foreach (var m in found) if (state.GetValueOrDefault(m.Id) == 0) Visit(m.Id);
        return cycles;
    }

    /// <summary>Every module's dependencies and theirs (not itself), for the modules found.</summary>
    static Dictionary<string, HashSet<string>> Closure(IReadOnlyList<ModuleManifest> found, Dictionary<string, ModuleManifest> byId)
    {
        var closure = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var m in found)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>(m.Dependencies.Select(d => d.Id));
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (id == m.Id || !set.Add(id) || !byId.TryGetValue(id, out var dep)) continue;
                foreach (var d in dep.Dependencies) stack.Push(d.Id);
            }
            set.Remove(m.Id);
            closure[m.Id] = set;
        }
        return closure;
    }

    static List<string> Unique(IEnumerable<string> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var p in problems) if (seen.Add(p)) list.Add(p);
        return list;
    }
}
