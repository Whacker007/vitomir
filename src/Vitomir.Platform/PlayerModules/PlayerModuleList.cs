// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitomir.Platform.PlayerModules;

/// <summary>How much a problem of the player modules weighs: an error refuses the start; a warning needs the player's acceptance of the combination; a note is said.</summary>
public enum ProblemLevel { Error, Warning, Note }

/// <summary>
/// **One problem of the player modules**: its weight, the module it is about, the file (or files: a problem of many files is one problem
/// with its files listed), what is wrong and what to do. <see cref="Line"/> is how the command line and the launcher say it.
/// </summary>
public sealed record PlayerModuleProblem(ProblemLevel Level, string Module, string File, string Text, string ToDo)
{
    /// <summary>Every file the problem is about, when it is about more than one (<see cref="File"/> is then the first).</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    /// <summary>The problem's kind, for a page to sort by (<c>dependency</c>, <c>overlap</c>, <c>made-for</c>, …).</summary>
    public string Kind { get; init; } = "";

    public bool IsError => Level == ProblemLevel.Error;

    /// <summary>The line: <c>warning: module b: materials/x.tga: … ; to do</c>, a problem of many files naming the first three and how many more.</summary>
    public string Line => LineOf(3);

    /// <summary>The line with every file named.</summary>
    public string FullLine => LineOf(int.MaxValue);

    string LineOf(int shown)
    {
        string level = Level switch { ProblemLevel.Error => "error", ProblemLevel.Warning => "warning", _ => "note" };
        string files = Files.Count > 1
            ? string.Join(", ", Files.Take(shown)) + (Files.Count > shown ? $" and {(Files.Count - shown).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} more" : "")
            : File;
        return $"{level}: {(Module.Length > 0 ? "module " + Module : "the modules")}{(files.Length > 0 ? ": " + files : "")}: {Text}{(ToDo.Length > 0 ? "; " + ToDo : "")}";
    }

    /// <summary>What to do as a page says it (the launcher's): the command line's hints in brackets, <c>(module-enable x)</c>, left out.</summary>
    public string PageToDo => System.Text.RegularExpressions.Regex.Replace(ToDo, @"\s*\((?:module-|vitomir-platform )[^)]*\)", "");

    /// <summary>One stable line for the combination's record of what was accepted (every file, no counts in words).</summary>
    public string Key => $"{Level}|{Kind}|{Module}|{string.Join(",", Files.Count > 0 ? Files : [File])}|{Text}";
}

/// <summary>One module in a game's list: its id and whether it is on.</summary>
public sealed record GameModuleEntry
{
    public string Id { get; init; } = "";
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// **A combination of modules whose warnings the player accepted**: the combination's key (<see cref="ModuleStart.Combination"/>: the base
/// game's data and every module on, in order, by its id, version, content hash and manifest hash), when, the modules as they were
/// (<c>id version hash</c>) and the warnings accepted. Any change to the combination is another key, and asks again.
/// </summary>
public sealed record AcceptedCombination
{
    public string Combination { get; init; } = "";
    public DateTime AcceptedUtc { get; init; }
    public List<string> Modules { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// **A game's player modules**, kept in the game's settings (<c>&lt;user data&gt;\Games\&lt;game&gt;\modules.json</c>, beside its window's
/// settings): the modules in the player's order, each on or off, and the combinations whose warnings were accepted. The engine's
/// lists are the platform's own JSON; a file that does not read is named and treated as empty (nothing on), never guessed at.
/// </summary>
public sealed class GameModuleList
{
    /// <summary>The list's file name in the game's settings folder.</summary>
    public const string FileName = "modules.json";

    /// <summary>The checks' last result for the combination on, kept to spare a start the checks when nothing changed.</summary>
    public const string CheckCacheName = "modules-check.json";

    public List<GameModuleEntry> Modules { get; set; } = [];
    public List<AcceptedCombination> Accepted { get; set; } = [];

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The list's file for <paramref name="gameId"/> under <paramref name="userData"/>.</summary>
    public static string PathFor(string userData, string gameId) => Path.Combine(userData, "Games", gameId, FileName);

    /// <summary>The list in <paramref name="path"/>; an empty one when there is none, or (with the reason in <paramref name="problem"/>) when it does not read.</summary>
    public static GameModuleList Read(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return new GameModuleList();
        try
        {
            var list = JsonSerializer.Deserialize<GameModuleList>(File.ReadAllText(path), Json) ?? new GameModuleList();
            list.Modules = [.. list.Modules.Where(m => m.Id.Length > 0).DistinctBy(m => m.Id)];
            return list;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problem = $"{path} cannot be read ({e.Message}): no player module is on until it is fixed or removed";
            return new GameModuleList();
        }
    }

    /// <summary>Writes the list, through a temporary file.</summary>
    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".writing";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The cached checks of a combination, as kept beside the list.</summary>
    public sealed record CheckCache
    {
        public string Combination { get; init; } = "";
        public List<CachedProblem> Problems { get; init; } = [];
    }

    /// <summary>A problem as the cache keeps it.</summary>
    public sealed record CachedProblem
    {
        public string Level { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Module { get; init; } = "";
        public string File { get; init; } = "";
        public string Text { get; init; } = "";
        public string ToDo { get; init; } = "";
        public List<string> Files { get; init; } = [];

        public static CachedProblem Of(PlayerModuleProblem p) => new()
        {
            Level = p.Level.ToString(), Kind = p.Kind, Module = p.Module, File = p.File, Text = p.Text, ToDo = p.ToDo, Files = [.. p.Files],
        };

        public PlayerModuleProblem Problem =>
            new(Enum.TryParse<ProblemLevel>(Level, out var l) ? l : ProblemLevel.Error, Module, File, Text, ToDo) { Kind = Kind, Files = Files };
    }

    /// <summary>The cached checks in <paramref name="path"/>, or null.</summary>
    public static CheckCache? ReadCache(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<CheckCache>(File.ReadAllText(path), Json); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    /// <summary>Keeps the checks of a combination.</summary>
    public static void WriteCache(string path, CheckCache cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".writing";
        File.WriteAllText(temp, JsonSerializer.Serialize(cache, Json));
        File.Move(temp, path, overwrite: true);
    }
}
