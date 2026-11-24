// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Engine.Levels.L2;
using Vitomir.Engine.World;
using Vitomir.Formats.L2;

namespace Vitomir.Engine.Levels.Cells;

/// <summary>
/// **Ours: which chronicle places are played as cell levels**.
/// The map is empty unless asked for — <c>vitomir --place OldTownDay=&lt;package.l2&gt;</c>, the
/// <see cref="Variable"/> environment variable, or <c>place.&lt;Name&gt;=&lt;package&gt;</c> under
/// <c>[levels]</c> in the ini — and while it is empty nothing here does anything: every place is its 1.1
/// level, as the chronicle file says.
///
/// A mapped place keeps everything the chronicle says of it but its level: its name, its scene, its exits
/// and where they lead, whether they are shut, its flags and its map marker. Only the level file it names is
/// replaced by the package's, as the chronicle is read (<see cref="Apply"/>, from
/// <see cref="ChronicleSet.Load"/>); from there the game builds the place, arrives in it and leaves it by
/// the same code as any other (the journey, <c>Campaign.Travel</c>, 1.1's main loop and what it calls), and
/// the package is found by its name (<see cref="L2Levels.Register"/>).
/// </summary>
public static class CellPlaces
{
    /// <summary>The environment variable that maps places when no option did: <c>Place=package</c> pairs separated by <c>;</c> (a test can hand it to a child process).</summary>
    public const string Variable = "VITOMIR_PLACES";

    static readonly Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> replaced = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The places mapped, each to its package folder.</summary>
    public static IReadOnlyDictionary<string, string> Map => map;

    /// <summary>The 1.1 level a mapped package's level name stands in for (<c>oldtown</c> → <c>P1_OTDY</c>), once a chronicle has been read with the map; null for any other name.</summary>
    public static string? Replaces(string levelName) => replaced.GetValueOrDefault(levelName);

    /// <summary>Maps <paramref name="place"/> (a chronicle's location name) to a cooked package.</summary>
    public static void Set(string place, string package) => map[place] = Path.GetFullPath(package.TrimEnd('/', '\\'));

    /// <summary>Empties the map.</summary>
    public static void Clear() { map.Clear(); replaced.Clear(); }

    /// <summary>
    /// Reads <c>Place=package</c> (several separated by <c>;</c>) into the map. False, with the reason, for
    /// one that is not of that shape or whose package is not a cooked cell level; what came before it stays.
    /// </summary>
    public static bool Parse(string? spec, out string error)
    {
        error = "";
        foreach (var pair in (spec ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0 || eq == pair.Length - 1) { error = $"'{pair}' is not <Place>=<package.l2>"; return false; }
            string place = pair[..eq].Trim(), package = pair[(eq + 1)..].Trim();
            if (!File.Exists(Path.Combine(package, L2Package.ManifestFile)) || !CellLevel.IsCellPackage(package))
            {
                error = $"{package} is not a cooked cell level (no {L2Package.ManifestFile} with cooked cells)";
                return false;
            }
            Set(place, package);
        }
        return true;
    }

    /// <summary>The ini's section, and what a key in it begins with; the rest of the key is the place's name.</summary>
    public const string IniSection = "levels", IniKeyPrefix = "place.";

    /// <summary>
    /// The ini's <c>[levels] place.&lt;Name&gt; = &lt;package.l2&gt;</c> lines into the map; one that names no
    /// cooked package is said and left out. Answers how many were taken.
    /// </summary>
    public static int FromIni(Diagnostics.IniFile ini, Action<string>? say = null)
    {
        string prefix = IniSection + "." + IniKeyPrefix;
        int taken = 0;
        foreach (var (key, value) in ini.Values)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || key.Length == prefix.Length) continue;
            if (Parse($"{key[prefix.Length..]}={value}", out string wrong)) taken++;
            else say?.Invoke($"place: the ini's {key}: {wrong}");
        }
        return taken;
    }

    /// <summary>
    /// Gives each mapped place of <paramref name="chronicles"/> its package's level in place of its 1.1 one
    /// and files the set's levels again; a place no chronicle has is said so. Nothing at all with an empty
    /// map. Answers how many places were mapped.
    /// </summary>
    public static int Apply(ChronicleSet chronicles, Action<string>? say = null)
    {
        if (map.Count == 0 && Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } fromEnvironment
            && !Parse(fromEnvironment, out string wrong))
            (say ?? Console.WriteLine).Invoke($"place: {Variable}: {wrong}");
        return map.Count == 0 ? 0 : Apply(chronicles, map, say ?? Console.WriteLine);
    }

    /// <summary>The same with a map of the caller's own (place → package folder), the run's map left alone: for a test or a tool.</summary>
    public static int Apply(ChronicleSet chronicles, IReadOnlyDictionary<string, string> places, Action<string>? say = null)
    {
        int mapped = 0;
        foreach (var (place, package) in places)
        {
            string level = L2Package.Open(package).Manifest.Name;
            bool found = false;
            foreach (var chronicle in chronicles.All)
            {
                if (chronicle.At(place) is not { } at) continue;
                found = true;
                if (at.LevelName.Equals(level, StringComparison.OrdinalIgnoreCase)) continue;      // read once already
                replaced[level] = at.LevelName;
                say?.Invoke($"place: {chronicle.Name}'s {at.Name} is played as the cell level {level} ({package}) in place of {at.LevelName}");
                at.Level = level + L2Package.Extension;
                mapped++;
            }
            if (!found) say?.Invoke($"place: no chronicle has a place called {place}; {package} is not used");
            else L2Levels.Register(level, package);
        }
        if (mapped > 0) chronicles.Reindex();
        return mapped;
    }
}
