// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// What a load says of the rules modules a save was made with (ours).
using Microsoft.Extensions.Logging;
using Vitomir.Engine.Diagnostics;

namespace Vitomir.Engine.Save;

/// <summary>
/// **A load, and the rules modules the save was made with** (ours). A save's header names the rules modules that were on when it
/// was written (<see cref="SaveHeader.Mods"/>: <c>id version hash</c>, in load order; look modules are each player's own and are not
/// named); a load compares them with the ones on (<see cref="Vfs.GameFiles.RulesModules"/>) and warns of each difference, naming it.
/// The load goes on: the player chose the modules.
/// </summary>
public static class LoadModules
{
    /// <summary>
    /// Says each difference between <paramref name="header"/>'s modules and <paramref name="on"/> as a line
    /// (<c>load: the save was made with the rules module x 1.0.0, which is not on here: …</c>) to <paramref name="say"/> and the log.
    /// </summary>
    /// <returns>The differences said, none when the two agree.</returns>
    public static IReadOnlyList<string> Report(SaveHeader header, IReadOnlyList<string> on, Action<string>? say = null)
    {
        var found = Differences(header.Mods, on);
        foreach (var line in found)
        {
            say?.Invoke("load: " + line);
            Diag.For("Vitomir").LogWarning("{Line}", "load: " + line);
        }
        return found;
    }

    /// <summary>The differences between the modules a save names (<paramref name="saved"/>) and those on (<paramref name="on"/>), in words.</summary>
    public static IReadOnlyList<string> Differences(IReadOnlyList<string> saved, IReadOnlyList<string> on)
    {
        if (saved.SequenceEqual(on)) return [];
        static (string Id, string Version, string Hash) Split(string stamp) =>
            stamp.Split(' ') is [var id, var version, var hash] ? (id, version, hash) : (stamp, "", "");
        var s = saved.Select(Split).ToList();
        var o = on.Select(Split).ToList();
        const string Risk = "the game may not play as it was saved";
        var lines = new List<string>();
        foreach (var x in s.Where(x => !o.Any(y => y.Id == x.Id)))
            lines.Add($"the save was made with the rules module {x.Id} {x.Version}, which is not on here: {Risk}");
        foreach (var y in o.Where(y => !s.Any(x => x.Id == y.Id)))
            lines.Add($"the rules module {y.Id} {y.Version} is on here, and the save was made without it: {Risk}");
        foreach (var x in s)
            if (o.FirstOrDefault(y => y.Id == x.Id) is { Id.Length: > 0 } y)
            {
                if (x.Version != y.Version) lines.Add($"the save was made with the rules module {x.Id} {x.Version}, and {y.Version} is on here: {Risk}");
                else if (x.Hash != y.Hash) lines.Add($"the save was made with the rules module {x.Id} {x.Version} with other files or interface bindings than the one on here: {Risk}");
            }
        if (lines.Count == 0)
            lines.Add($"the save was made with the rules modules in another order ({string.Join(", ", s.Select(x => x.Id))}; here {string.Join(", ", o.Select(y => y.Id))}): {Risk}");
        return lines;
    }
}
