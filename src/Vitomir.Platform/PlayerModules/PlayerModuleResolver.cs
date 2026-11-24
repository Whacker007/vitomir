// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
namespace Vitomir.Platform.PlayerModules;

/// <summary>The modules a game plays with, in their load order, and what the resolution found.</summary>
public sealed record ModuleResolution(IReadOnlyList<PlayerModule> Order, IReadOnlyList<PlayerModuleProblem> Problems);

/// <summary>
/// **The game's list resolved before play**: the modules that are on, in the player's order, moved only where a dependency or a
/// <c>loadAfter</c> must load first (stable: each time the first module in the player's order whose dependencies and load-afters that
/// are on are all placed); and every problem named — a module on that is not imported or not whole (the files put in its copy by hand one
/// problem of kind <c>added</c>, naming them all), of another game, a dependency
/// that is missing, off or of a version outside the range, a cycle, an incompatible pair on together. A module moved from the player's
/// order is said as a note.
/// </summary>
public static class PlayerModuleResolver
{
    /// <param name="list">The game's list (its order and what is on).</param>
    /// <param name="installed">The imported modules, by id.</param>
    /// <param name="broken">Modules whose folder is there but does not open (no record, a manifest that does not read), by id, with why.</param>
    /// <param name="gameId">The game being started.</param>
    public static ModuleResolution Resolve(GameModuleList list, IReadOnlyDictionary<string, PlayerModule> installed,
                                           IReadOnlyDictionary<string, string> broken, string gameId)
    {
        var problems = new List<PlayerModuleProblem>();
        var on = new List<PlayerModule>();
        foreach (var entry in list.Modules.Where(m => m.Enabled))
        {
            if (installed.TryGetValue(entry.Id, out var m))
            {
                if (m.Manifest.Game != gameId)
                {
                    problems.Add(Error(entry.Id, "", "game", $"it is a module of the game {m.Manifest.Game}, not of {gameId}", $"turn it off (module-enable {entry.Id} --off)"));
                    continue;
                }
                // The data files put in its copy by hand are one problem of the module's, naming them all (ours: a page says it once,
                // with Update); every other way it is not as imported, one problem a file.
                var byHand = new List<string>();
                foreach (var p in m.CheckWhole())
                {
                    string file = p[..Math.Max(0, p.IndexOf(':'))], text = p[(p.IndexOf(':') + 1)..].Trim();
                    if (text == PlayerModule.AddedByHand) { byHand.Add(file); continue; }
                    problems.Add(Error(entry.Id, file, "whole", text, "add it again from its source (module-add), or turn it off"));
                }
                if (byHand.Count > 0)
                    problems.Add(Error(entry.Id, byHand[0], "added", PlayerModule.AddedByHand, "add it again from its source (module-add), or turn it off") with { Files = byHand });
                on.Add(m);
            }
            else if (broken.TryGetValue(entry.Id, out var why))
                problems.Add(Error(entry.Id, "", "whole", why, $"add it again from its source (module-add), or remove it (module-remove {entry.Id})"));
            else
                problems.Add(Error(entry.Id, "", "missing", "it is on, and is not imported", $"add it (module-add <its folder or zip>) or remove it from the list (module-remove {entry.Id})"));
        }

        var onIds = on.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var m in on)
            foreach (var d in m.Manifest.Dependencies)
            {
                if (!installed.TryGetValue(d.Id, out var dep))
                    problems.Add(Error(m.Id, "", "dependency", $"it needs the module {d.Id} ({d.Versions.Words}), which is not imported", $"add {d.Id}, or turn {m.Id} off"));
                else if (!onIds.Contains(d.Id))
                    problems.Add(Error(m.Id, "", "dependency", $"it needs the module {d.Id}, which is off", $"turn it on (module-enable {d.Id}), or turn {m.Id} off"));
                else if (!d.Versions.Accepts(dep.Manifest.Version))
                    problems.Add(Error(m.Id, "", "dependency", $"it needs the module {d.Id} at {d.Versions.Words}, and {dep.Manifest.Version} is imported",
                        $"add a version of {d.Id} it accepts, or turn {m.Id} off"));
            }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in on)
            foreach (var other in m.Manifest.Incompatible.Concat(on.Where(o => o.Manifest.Incompatible.Contains(m.Id)).Select(o => o.Id)))
            {
                if (!onIds.Contains(other) || other == m.Id) continue;
                var pair = string.CompareOrdinal(m.Id, other) < 0 ? $"{m.Id}|{other}" : $"{other}|{m.Id}";
                if (!reported.Add(pair)) continue;
                var says = m.Manifest.Incompatible.Contains(other) ? m.Id : other;
                problems.Add(Error(m.Id, "", "incompatible", $"it cannot be played with the module {other} ({says}'s module.json says so), and both are on",
                    $"turn one of them off"));
            }

        // The order: the player's, each module placed once everything it needs or loads after (and is on) is placed.
        IEnumerable<string> Before(PlayerModule m) =>
            m.Manifest.Dependencies.Select(d => d.Id).Concat(m.Manifest.LoadAfter).Where(onIds.Contains).Distinct();
        var placed = new List<PlayerModule>();
        var left = on.ToList();
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(m => Before(m).All(b => placed.Any(p => p.Id == b)));
            if (next is null)
            {
                var cycle = Cycle(left, Before);
                problems.Add(Error(cycle[0], "", "cycle", $"a cycle of dependencies and load-afters: {string.Join(" -> ", cycle)}",
                    "the modules cannot all load after each other: turn one of them off"));
                placed.AddRange(left);
                break;
            }
            placed.Add(next);
            left.Remove(next);
        }
        foreach (var m in placed)
        {
            var later = Before(m).Where(b => on.FindIndex(o => o.Id == b) > on.IndexOf(m)).ToList();
            if (later.Count > 0)
                problems.Add(new PlayerModuleProblem(ProblemLevel.Note, m.Id, "", $"it loads after {string.Join(", ", later)}, which it needs or loads after, not where your order puts it", "") { Kind = "order" });
        }
        return new ModuleResolution(placed, problems);
    }

    /// <summary>
    /// **The list sorted by dependencies** (the launcher's Sort by dependencies): every module of the list, on or off, in the player's
    /// order, each moved after the modules it needs or loads after that are in the list (stable: each time the first module in the
    /// player's order whose dependencies and load-afters in the list are all placed). Modules in a cycle keep their order after the rest.
    /// </summary>
    public static IReadOnlyList<string> Sorted(GameModuleList list, IReadOnlyDictionary<string, PlayerModule> installed)
    {
        var ids = list.Modules.Select(e => e.Id).ToList();
        var inList = ids.ToHashSet(StringComparer.Ordinal);
        IEnumerable<string> Before(string id) => installed.TryGetValue(id, out var m)
            ? m.Manifest.Dependencies.Select(d => d.Id).Concat(m.Manifest.LoadAfter).Where(inList.Contains).Distinct()
            : [];
        var placed = new List<string>();
        var left = ids.ToList();
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(id => Before(id).All(placed.Contains));
            if (next is null) { placed.AddRange(left); break; }
            placed.Add(next);
            left.Remove(next);
        }
        return placed;
    }

    static PlayerModuleProblem Error(string module, string file, string kind, string text, string toDo) =>
        new(ProblemLevel.Error, module, file, text, toDo) { Kind = kind };

    /// <summary>A cycle among modules none of which can be placed: followed from the first until it comes round.</summary>
    static List<string> Cycle(List<PlayerModule> left, Func<PlayerModule, IEnumerable<string>> before)
    {
        var byId = left.ToDictionary(m => m.Id);
        var path = new List<string>();
        string at = left[0].Id;
        while (!path.Contains(at))
        {
            path.Add(at);
            at = before(byId[at]).First(byId.ContainsKey);           // one of them is unplaced, so in the cycle's part
        }
        int from = path.IndexOf(at);
        var cycle = path.Skip(Math.Max(0, from)).ToList();
        cycle.Add(at);
        return cycle;
    }
}
