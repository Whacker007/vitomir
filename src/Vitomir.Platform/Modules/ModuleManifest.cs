// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Text;
using System.Text.Json;

namespace Vitomir.Platform.Modules;

/// <summary>What a module carries: rules (and the story's data), or content (assets, no rules) that rules modules depend on.</summary>
public enum ModuleKind { Rules, Content }

/// <summary>A module another needs, and the least version of it accepted (and by it the major: <see cref="ModuleVersion.Satisfies"/>).</summary>
public sealed record ModuleDependency(string Id, ModuleVersion Least);

/// <summary>A switch a module understands on the command line (<c>--sandbox</c>), with the line the program's help and refusals show for it.</summary>
public sealed record ModuleSwitch(string Name, string Help);

/// <summary>
/// A module's manifest, <c>module.json</c> in its folder (ours):
/// who it is, what it was built for, what it needs, its code if it has any, and the other modules' ids it
/// overrides. Read strictly (<see cref="Parse"/>): a missing or mistyped field, a field given twice and a field
/// it does not know are each named once, so a typo is never silently a default.
/// <code>
/// {
///   "id": "enhanced",
///   "name": "The enhanced game's rules",
///   "version": "0.1.0",
///   "platform": "1.0.0",
///   "kind": "rules",
///   "dependencies": { "people": "1.2.0" },
///   "assembly": "bin/Vitomir.Modules.Enhanced.dll",
///   "type": "Vitomir.Modules.Enhanced.EnhancedModule",
///   "overrides": [ "people:motions/attack_sword" ],
///   "switches": { "sandbox": "run the research sandbox profile" }
/// }
/// </code>
/// </summary>
public sealed record ModuleManifest
{
    /// <summary>The manifest's file name in a module's folder.</summary>
    public const string FileName = "module.json";

    /// <summary>The fields a manifest may have.</summary>
    public static readonly string[] Fields = ["id", "name", "description", "version", "platform", "kind", "dependencies", "assembly", "type", "overrides", "switches"];

    /// <summary>The module's id: the namespace of its assets (<c>enhanced:…</c>).</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public required ModuleVersion Version { get; init; }

    /// <summary>The platform API version it was built for (<see cref="PlatformApi"/>).</summary>
    public required ModuleVersion Platform { get; init; }

    public required ModuleKind Kind { get; init; }

    /// <summary>The modules it needs, in the manifest's order.</summary>
    public IReadOnlyList<ModuleDependency> Dependencies { get; init; } = [];

    /// <summary>Its assembly's path in its folder (<c>/</c> separated), or null for a module of data only.</summary>
    public string? Assembly { get; init; }

    /// <summary>The full name of its <see cref="IModule"/> type in <see cref="Assembly"/>.</summary>
    public string? Type { get; init; }

    /// <summary>Other modules' asset ids it declares it overrides (design 10.1: "overriding is explicit").</summary>
    public IReadOnlyList<AssetId> Overrides { get; init; } = [];

    /// <summary>The command-line switches it understands, in the manifest's order.</summary>
    public IReadOnlyList<ModuleSwitch> Switches { get; init; } = [];

    /// <summary>The folder it was read from (full path), or empty for one parsed from text.</summary>
    public string Folder { get; init; } = "";

    /// <summary>Whether it has code.</summary>
    public bool HasCode => Assembly is not null;

    /// <summary>Reads <c>module.json</c> in <paramref name="folder"/>; null (and the problems added) when it is not a good manifest.</summary>
    public static ModuleManifest? Read(string folder, List<string> problems)
    {
        folder = Path.GetFullPath(folder);
        string path = Path.Combine(folder, FileName);
        if (!File.Exists(path)) { problems.Add($"{path}: missing (a module's folder holds its {FileName})"); return null; }
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (IOException e) { problems.Add($"{path}: could not be read ({e.Message})"); return null; }
        var m = Parse(text, path, problems);
        if (m is null) return null;
        m = m with { Folder = folder };
        if (m.Assembly is { } a && !File.Exists(Path.Combine(folder, a)))
        {
            problems.Add($"{path}: its assembly {a} is not in {folder}");
            return null;
        }
        return m;
    }

    /// <summary>Reads a manifest from its text; <paramref name="where"/> names it in the problems.</summary>
    public static ModuleManifest? Parse(string json, string where, List<string> problems)
    {
        int before = problems.Count;
        var f = StrictJson.Object(json, where, Fields, problems);
        if (f is null) return null;

        string? id = f.Text("id", required: true);
        if (id is not null && AssetId.ModuleProblem(id) is { } idProblem) problems.Add($"{where}: 'id': {idProblem}");
        if (id == AssetId.OneOne) problems.Add($"{where}: 'id' '{AssetId.OneOne}' is the 1.1 install's source and no module's");
        string? name = f.Text("name", required: true);
        string? description = f.Text("description", required: false);
        var version = f.Version("version", required: true);
        var platform = f.Version("platform", required: true);
        ModuleKind? kind = f.Text("kind", required: true) switch
        {
            null => null,
            "rules" => ModuleKind.Rules,
            "content" => ModuleKind.Content,
            var k => f.Problem<ModuleKind?>($"'kind' is '{k}'; it is 'rules' or 'content'", null),
        };

        var dependencies = new List<ModuleDependency>();
        foreach (var (depId, value) in f.Members("dependencies"))
        {
            if (AssetId.ModuleProblem(depId) is { } p) { f.Problem($"'dependencies': {p}"); continue; }
            if (depId == id) { f.Problem($"'dependencies': it names itself"); continue; }
            if (value.ValueKind != JsonValueKind.String || !ModuleVersion.TryParse(value.GetString(), out var least))
                f.Problem($"'dependencies': '{depId}' must give the least version it accepts as \"major.minor.patch\"");
            else dependencies.Add(new ModuleDependency(depId, least));
        }

        string? assembly = f.Text("assembly", required: false);
        string? type = f.Text("type", required: false);
        if (assembly is not null)
        {
            var a = assembly.Replace('\\', '/');
            if (Path.IsPathRooted(a) || a.Split('/').Any(s => s is ".." or "." or ""))
                f.Problem($"'assembly' {assembly} must be a path inside the module's folder");
            else if (!a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                f.Problem($"'assembly' {assembly} is not a .dll");
            assembly = a;
        }
        if ((assembly is null) != (type is null)) f.Problem("'assembly' and 'type' come together (a module of data only has neither)");

        var overrides = new List<AssetId>();
        foreach (var item in f.Array("overrides"))
        {
            if (item.ValueKind != JsonValueKind.String) { f.Problem("'overrides' holds asset ids (strings)"); continue; }
            var text = item.GetString()!;
            if (!AssetId.TryParse(text, out var oid, out var p)) { f.Problem($"'overrides': {p}"); continue; }
            if (overrides.Contains(oid)) { f.Problem($"'overrides': {oid} is named twice"); continue; }
            overrides.Add(oid);
        }

        var switches = new List<ModuleSwitch>();
        foreach (var (switchName, value) in f.Members("switches"))
        {
            if (!IsSwitchName(switchName)) { f.Problem($"'switches': '{switchName}' is not a switch name (lower-case letters, digits and '-', starting with a letter)"); continue; }
            if (value.ValueKind != JsonValueKind.String) { f.Problem($"'switches': '{switchName}' must give its help line (a string)"); continue; }
            switches.Add(new ModuleSwitch(switchName, value.GetString()!));
        }

        if (problems.Count > before || id is null || name is null || version is null || platform is null || kind is null) return null;
        return new ModuleManifest
        {
            Id = id, Name = name, Description = description ?? "", Version = version.Value, Platform = platform.Value, Kind = kind.Value,
            Dependencies = dependencies, Assembly = assembly, Type = type, Overrides = overrides, Switches = switches,
        };
    }

    static bool IsSwitchName(string s) =>
        s.Length is > 0 and <= 40 && s[0] is >= 'a' and <= 'z' && s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public override string ToString() => $"{Id} {Version}";
}

/// <summary>
/// A game: an ordered list of modules, a small JSON file. The enhanced game is
/// <c>{"id": "enhanced", "name": …, "modules": ["enhanced"]}</c>; later <c>["prague1141", "people", "arms",
/// "campaign", "enhanced"]</c>. The order is the load order where the dependencies allow it
/// (<see cref="ModuleChecker"/>); every module the game loads is listed (fail closed: a dependency the list
/// leaves out is named, never pulled in quietly).
/// </summary>
public sealed record GameList(string Id, string Name, IReadOnlyList<string> Modules)
{
    /// <summary>The fields a game file may have.</summary>
    public static readonly string[] Fields = ["id", "name", "description", "modules"];

    /// <summary>The file it was read from, or empty.</summary>
    public string File { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>Reads a game file; null (and the problems added) when it is not a good one.</summary>
    public static GameList? Read(string path, List<string> problems)
    {
        path = Path.GetFullPath(path);
        if (!System.IO.File.Exists(path)) { problems.Add($"{path}: no such game file"); return null; }
        var g = Parse(System.IO.File.ReadAllText(path, Encoding.UTF8), path, problems);
        return g is null ? null : g with { File = path };
    }

    /// <summary>Reads a game from its text; <paramref name="where"/> names it in the problems.</summary>
    public static GameList? Parse(string json, string where, List<string> problems)
    {
        int before = problems.Count;
        var f = StrictJson.Object(json, where, Fields, problems);
        if (f is null) return null;
        string? id = f.Text("id", required: true);
        if (id is not null && AssetId.ModuleProblem(id) is { } p) problems.Add($"{where}: 'id': {p.Replace("module id", "game id")}");
        string? name = f.Text("name", required: true);
        string? description = f.Text("description", required: false);
        var modules = new List<string>();
        if (!f.Has("modules")) f.Problem("'modules' is missing (the game's modules, in order)");
        foreach (var item in f.Array("modules"))
        {
            if (item.ValueKind != JsonValueKind.String) { f.Problem("'modules' holds module ids (strings)"); continue; }
            var m = item.GetString()!;
            if (AssetId.ModuleProblem(m) is { } mp) { f.Problem($"'modules': {mp}"); continue; }
            if (modules.Contains(m)) { f.Problem($"'modules': {m} is listed twice"); continue; }
            modules.Add(m);
        }
        if (f.Has("modules") && modules.Count == 0 && problems.Count == before) f.Problem("'modules' is empty");
        if (problems.Count > before || id is null || name is null) return null;
        return new GameList(id, name, modules) { Description = description ?? "" };
    }
}

/// <summary>A JSON object read strictly: its fields by name, every problem said once with where it is.</summary>
internal sealed class StrictJson
{
    readonly Dictionary<string, JsonElement> fields;
    readonly string where;
    readonly List<string> problems;

    StrictJson(Dictionary<string, JsonElement> fields, string where, List<string> problems)
    {
        this.fields = fields;
        this.where = where;
        this.problems = problems;
    }

    /// <summary>The object in <paramref name="json"/>, or null when the text is no JSON object; unknown and doubled fields named once each.</summary>
    public static StrictJson? Object(string json, string where, string[] known, List<string> problems)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            root = doc.RootElement.Clone();
        }
        catch (JsonException e) { problems.Add($"{where}: not valid JSON ({e.Message})"); return null; }
        if (root.ValueKind != JsonValueKind.Object) { problems.Add($"{where}: not a JSON object"); return null; }
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var p in root.EnumerateObject())
        {
            if (fields.ContainsKey(p.Name)) { problems.Add($"{where}: '{p.Name}' is given twice"); continue; }
            fields[p.Name] = p.Value;
            if (System.Array.IndexOf(known, p.Name) < 0) unknown.Add(p.Name);
        }
        foreach (var u in unknown)
            problems.Add($"{where}: unknown field '{u}' (the fields are {string.Join(", ", known)})");
        return new StrictJson(fields, where, problems);
    }

    public bool Has(string name) => fields.ContainsKey(name);

    public void Problem(string text) => problems.Add($"{where}: {text}");

    public T Problem<T>(string text, T value)
    {
        Problem(text);
        return value;
    }

    public string? Text(string name, bool required)
    {
        if (!fields.TryGetValue(name, out var v)) { if (required) Problem($"'{name}' is missing"); return null; }
        if (v.ValueKind != JsonValueKind.String) { Problem($"'{name}' must be a string"); return null; }
        var s = v.GetString()!;
        if (s.Length == 0) { Problem($"'{name}' is empty"); return null; }
        return s;
    }

    public ModuleVersion? Version(string name, bool required)
    {
        var s = Text(name, required);
        if (s is null) return null;
        if (!ModuleVersion.TryParse(s, out var v)) { Problem($"'{name}' is '{s}', not a version (major.minor.patch)"); return null; }
        return v;
    }

    public IEnumerable<(string Name, JsonElement Value)> Members(string name)
    {
        if (!fields.TryGetValue(name, out var v)) return [];
        if (v.ValueKind != JsonValueKind.Object) { Problem($"'{name}' must be an object"); return []; }
        var list = new List<(string, JsonElement)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in v.EnumerateObject())
        {
            if (!seen.Add(p.Name)) { Problem($"'{name}': '{p.Name}' is given twice"); continue; }
            list.Add((p.Name, p.Value));
        }
        return list;
    }

    public IEnumerable<JsonElement> Array(string name)
    {
        if (!fields.TryGetValue(name, out var v)) return [];
        if (v.ValueKind != JsonValueKind.Array) { Problem($"'{name}' must be an array"); return []; }
        return [.. v.EnumerateArray()];
    }
}
