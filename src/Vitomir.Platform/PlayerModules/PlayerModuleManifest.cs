// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.Json;
using Vitomir.Platform.Modules;

namespace Vitomir.Platform.PlayerModules;

/// <summary>
/// The versions of another module a player module accepts: at least <see cref="Least"/>, of its major (the platform's rule,
/// <see cref="ModuleVersion.Satisfies"/>), or, when <see cref="Most"/> is given (<c>"1.2.0 - 1.4.9"</c>), from one to the other
/// inclusive.
/// </summary>
public sealed record VersionRange(ModuleVersion Least, ModuleVersion? Most)
{
    /// <summary>Reads <c>"1.2.0"</c> or <c>"1.2.0 - 1.4.9"</c>; null when it is neither.</summary>
    public static VersionRange? Parse(string? text)
    {
        if (text is null) return null;
        var parts = text.Split(" - ", StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && ModuleVersion.TryParse(parts[0], out var least)) return new(least, null);
        if (parts.Length == 2 && ModuleVersion.TryParse(parts[0], out var from) && ModuleVersion.TryParse(parts[1], out var to) && from.CompareTo(to) <= 0)
            return new(from, to);
        return null;
    }

    /// <summary>Whether <paramref name="version"/> is in the range.</summary>
    public bool Accepts(ModuleVersion version) =>
        Most is { } most ? version.CompareTo(Least) >= 0 && version.CompareTo(most) <= 0 : version.Satisfies(Least);

    /// <summary>The range as the problems word it: <c>1.2.0 or a later 1.x</c>, <c>1.2.0 to 1.4.9</c>.</summary>
    public string Words => Most is { } most ? $"{Least} to {most}" : $"{Least} or a later {Least.Major}.x";

    public override string ToString() => Most is { } most ? $"{Least} - {most}" : Least.ToString();
}

/// <summary>A module a player module needs, and the versions of it it accepts.</summary>
public sealed record PlayerDependency(string Id, VersionRange Versions);

/// <summary>
/// **A player module's manifest**, <c>module.json</c> in its folder: who it is, which game it is for, what it needs, what it must
/// load after, what it cannot be played with, which modules' models its textures were made for, and which of its files fit another
/// module's. The platform's manifest shape (<see cref="ModuleManifest"/>) without code: no <c>platform</c>, <c>assembly</c>, <c>type</c>,
/// <c>overrides</c> or <c>switches</c>, since a player module carries data only and replaces whole files of 1.1's by their paths.
/// Read strictly, as the platform's is: a missing or mistyped field, a field given twice and a field it does not know are each named once.
/// <code>
/// {
///   "id": "sharper-prague",
///   "name": "Sharper Prague",
///   "version": "1.0.0",
///   "author": "someone",
///   "description": "Prague's walls at 512 pixels",
///   "game": "classic",
///   "kind": ["look"],
///   "dependencies": { "base-pack": "1.2.0" },
///   "loadAfter": [ "other-pack" ],
///   "incompatible": [ "rival-pack" ],
///   "madeFor": [ "oneone" ],
///   "fits": { "new-models": [ "3d/materials/christof.tga" ], "base-pack": "*" },
///   "actions": { "sbVault": "open-vault" }
/// }
/// </code>
/// </summary>
public sealed record PlayerModuleManifest
{
    /// <summary>The manifest's file name in a module's folder.</summary>
    public const string FileName = "module.json";

    /// <summary>The fields a player module's manifest may have.</summary>
    public static readonly string[] Fields =
        ["id", "name", "version", "author", "description", "game", "kind", "dependencies", "loadAfter", "incompatible", "madeFor", "fits", "actions"];

    /// <summary>The only game player modules are for in release 1.</summary>
    public const string ClassicGame = "classic";

    /// <summary>The ids no player module may take: 1.1's own source and the platform's modules.</summary>
    public static readonly string[] Reserved = [AssetId.OneOne, "classic", "enhanced", "people", "platform", "vitomir"];

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ModuleVersion Version { get; init; }
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>The game it is for (<see cref="ClassicGame"/>).</summary>
    public required string Game { get; init; }

    /// <summary>The kinds the author says it is; the files decide (<see cref="PackKinds"/>), and a difference is a warning.</summary>
    public PackKinds? Kind { get; init; }

    public IReadOnlyList<PlayerDependency> Dependencies { get; init; } = [];

    /// <summary>Modules it loads after when they are on, without needing them.</summary>
    public IReadOnlyList<string> LoadAfter { get; init; } = [];

    /// <summary>Modules it cannot be played with.</summary>
    public IReadOnlyList<string> Incompatible { get; init; } = [];

    /// <summary>
    /// The bases its textures were made for: <c>oneone</c> (1.1's models) or other modules' ids. Null when not said: then a module that
    /// ships no model using a texture it replaces is taken to have made it for 1.1's models (<see cref="PlayerModuleChecks"/>).
    /// </summary>
    public IReadOnlyList<string>? MadeFor { get; init; }

    /// <summary>Files of its own the author says fit another module's models: by module, the 1.1 paths, or every file (<c>"*"</c>: null).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>?> Fits { get; init; } = new Dictionary<string, IReadOnlyList<string>?>();

    /// <summary>
    /// Its interface elements bound to Vitomir's own actions (<see cref="InterfaceActions"/>): an element's name in its layouts to an
    /// action's name, as written. The checker refuses an action not on the list and says which bindings 1.1's own elements keep.
    /// </summary>
    public IReadOnlyDictionary<string, string> Actions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the author says <paramref name="path"/> fits <paramref name="module"/>'s models.</summary>
    public bool FitsWith(string module, string path) =>
        Fits.TryGetValue(module, out var files) && (files is null || files.Any(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Reads <c>module.json</c> in <paramref name="folder"/>; null (and the problems added) when it is missing or not a good manifest.</summary>
    public static PlayerModuleManifest? Read(string folder, List<string> problems)
    {
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) { problems.Add($"{path}: missing (a player module's folder holds its {FileName})"); return null; }
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { problems.Add($"{path}: could not be read ({e.Message})"); return null; }
        return Parse(text, FileName, problems);
    }

    /// <summary>Reads a manifest from its text; <paramref name="where"/> names it in the problems.</summary>
    public static PlayerModuleManifest? Parse(string json, string where, List<string> problems)
    {
        int before = problems.Count;
        var f = StrictJson.Object(json, where, Fields, problems);
        if (f is null) return null;

        string? id = f.Text("id", required: true);
        if (id is not null && IdProblem(id) is { } idProblem) f.Problem($"'id': {idProblem}");
        string? name = f.Text("name", required: true);
        var version = f.Version("version", required: true);
        string? author = f.Text("author", required: false);
        string? description = f.Text("description", required: false);
        string? game = f.Text("game", required: true);
        if (game is not null && game != ClassicGame) f.Problem($"'game' is '{game}'; player modules are for the game '{ClassicGame}'");

        PackKinds? kind = null;
        if (f.Has("kind"))
        {
            PackKinds k = 0;
            foreach (var item in f.Array("kind"))
            {
                if (item.ValueKind != JsonValueKind.String || PackKindWords.Parse(item.GetString()) is not { } one)
                    f.Problem($"'kind' holds 'look', 'world' and 'interface' (the kinds of the module's files)");
                else k |= one;
            }
            kind = k;
        }

        var dependencies = new List<PlayerDependency>();
        foreach (var (depId, value) in f.Members("dependencies"))
        {
            if (IdProblem(depId) is { } p) { f.Problem($"'dependencies': {p}"); continue; }
            if (depId == id) { f.Problem("'dependencies': it names itself"); continue; }
            if (value.ValueKind != JsonValueKind.String || VersionRange.Parse(value.GetString()) is not { } range)
                f.Problem($"'dependencies': '{depId}' must give the versions it accepts, \"major.minor.patch\" (that or a later one of its major) or \"1.2.0 - 1.4.9\"");
            else dependencies.Add(new PlayerDependency(depId, range));
        }

        var loadAfter = Ids(f, "loadAfter", id);
        var incompatible = Ids(f, "incompatible", id);
        foreach (var both in dependencies.Select(d => d.Id).Intersect(incompatible))
            f.Problem($"'{both}' is both a dependency and incompatible");

        List<string>? madeFor = null;
        if (f.Has("madeFor"))
        {
            madeFor = [];
            foreach (var item in f.Array("madeFor"))
            {
                if (item.ValueKind != JsonValueKind.String) { f.Problem("'madeFor' holds module ids ('oneone' for 1.1's own models)"); continue; }
                var m = item.GetString()!;
                if (m != AssetId.OneOne && IdProblem(m) is { } p) { f.Problem($"'madeFor': {p}"); continue; }
                if (!madeFor.Contains(m)) madeFor.Add(m);
            }
        }

        var fits = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal);
        foreach (var (other, value) in f.Members("fits"))
        {
            if (other != AssetId.OneOne && IdProblem(other) is { } p) { f.Problem($"'fits': {p}"); continue; }
            if (value.ValueKind == JsonValueKind.String && value.GetString() == "*") { fits[other] = null; continue; }
            if (value.ValueKind != JsonValueKind.Array) { f.Problem($"'fits': '{other}' gives the files that fit its models (1.1 paths), or \"*\" for every file"); continue; }
            var files = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } file) { f.Problem($"'fits': '{other}' holds 1.1 paths (strings)"); continue; }
                files.Add(file.Replace('\\', '/').TrimStart('/'));
            }
            fits[other] = files;
        }

        var actions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (element, value) in f.Members("actions"))
        {
            if (element.Length == 0 || element.Length > 31 || element.Any(c => char.IsWhiteSpace(c) || c == '='))
                f.Problem($"'actions': '{element}' is not an interface element's name (up to 31 letters, digits and marks, no space or '=')");
            else if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } action)
                f.Problem($"'actions': '{element}' names one of Vitomir's interface actions (a string, such as \"open-vault\")");
            else if (!actions.TryAdd(element, action)) f.Problem($"'actions': '{element}' is bound twice (names are read without regard to case)");
        }

        if (problems.Count > before || id is null || name is null || version is null || game is null) return null;
        return new PlayerModuleManifest
        {
            Id = id, Name = name, Version = version.Value, Author = author ?? "", Description = description ?? "", Game = game, Kind = kind,
            Dependencies = dependencies, LoadAfter = loadAfter, Incompatible = incompatible, MadeFor = madeFor, Fits = fits, Actions = actions,
        };
    }

    static List<string> Ids(StrictJson f, string field, string? self)
    {
        var ids = new List<string>();
        foreach (var item in f.Array(field))
        {
            if (item.ValueKind != JsonValueKind.String) { f.Problem($"'{field}' holds module ids (strings)"); continue; }
            var m = item.GetString()!;
            if (IdProblem(m) is { } p) { f.Problem($"'{field}': {p}"); continue; }
            if (m == self) { f.Problem($"'{field}': it names itself"); continue; }
            if (!ids.Contains(m)) ids.Add(m);
        }
        return ids;
    }

    /// <summary>What is wrong with a player module's id, or null: the platform's module id rule, and none of the <see cref="Reserved"/> ids.</summary>
    public static string? IdProblem(string id) =>
        AssetId.ModuleProblem(id) ?? (Reserved.Contains(id) ? $"'{id}' is taken (1.1's own files and the platform's modules)" : null);

    /// <summary>The manifest as JSON, in the field order of <see cref="Fields"/> (what an import writes for a bare pack).</summary>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("id", Id);
            w.WriteString("name", Name);
            w.WriteString("version", Version.ToString());
            if (Author.Length > 0) w.WriteString("author", Author);
            if (Description.Length > 0) w.WriteString("description", Description);
            w.WriteString("game", Game);
            if (Kind is { } kind)
            {
                w.WriteStartArray("kind");
                foreach (var k in PackKindWords.List(kind)) w.WriteStringValue(k);
                w.WriteEndArray();
            }
            if (Dependencies.Count > 0)
            {
                w.WriteStartObject("dependencies");
                foreach (var d in Dependencies) w.WriteString(d.Id, d.Versions.ToString());
                w.WriteEndObject();
            }
            void Strings(string field, IReadOnlyList<string> items)
            {
                w.WriteStartArray(field);
                foreach (var s in items) w.WriteStringValue(s);
                w.WriteEndArray();
            }
            if (LoadAfter.Count > 0) Strings("loadAfter", LoadAfter);
            if (Incompatible.Count > 0) Strings("incompatible", Incompatible);
            if (MadeFor is { } made) Strings("madeFor", made);
            if (Fits.Count > 0)
            {
                w.WriteStartObject("fits");
                foreach (var (other, files) in Fits)
                    if (files is null) w.WriteString(other, "*");
                    else Strings(other, files);
                w.WriteEndObject();
            }
            if (Actions.Count > 0)
            {
                w.WriteStartObject("actions");
                foreach (var (element, action) in Actions) w.WriteString(element, action);
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    public override string ToString() => $"{Id} {Version}";
}
