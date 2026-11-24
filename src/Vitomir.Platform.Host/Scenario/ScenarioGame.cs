// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Numerics;
using System.Text.Json;
using Vitomir.Platform.Assets;
using Vitomir.Platform.Assets.OneOne;
using Vitomir.Platform.Modules;
using Vitomir.Platform.Space;

namespace Vitomir.Platform.Host.Scenario;

/// <summary>A trait a sheet may set: its id, its domain (attribute, ability, purchase), its range and the unit its values are in.</summary>
public sealed record GameTrait(string Id, string Domain, string Category, int Least, int Most, string Unit, string Status, string Source);

/// <summary>A power: its id, its name, its kind, its options and each rank's cost and time, from the module's data.</summary>
public sealed record GamePower(string Id, string Label, string Kind, IReadOnlyList<string> Options, IReadOnlyList<(int Rank, int Cost, double Seconds)> Ranks, string Status);

/// <summary>A sheet template: its id, title, species and the values it buys.</summary>
public sealed record GameSheet(string Id, string Title, string Species, IReadOnlyDictionary<string, int> Purchased, IReadOnlyList<string> Powers, string Status);

/// <summary>A module's JSON data asset: its id, its path under the module's Data/, schema, status, whether it is read, its bytes.</summary>
public sealed record GameDataAsset(AssetId Id, string File, string Schema, string Status, bool Read, byte[] Bytes)
{
    System.Text.Json.Nodes.JsonNode? root;

    /// <summary>The asset parsed (once; read only: copy before changing it).</summary>
    public System.Text.Json.Nodes.JsonNode Root => root ??= DataOverrides.Parse(Bytes);
}

/// <summary>An armour piece: its id, name, slot and the regions it covers.</summary>
public sealed record GameArmor(string Id, string Name, string Slot, IReadOnlyList<string> Covers);

/// <summary>
/// What the loaded game lets a scenario name: the 1.1 characters and weapons, the module's presets, sheet
/// templates, traits, powers and their options, armour pieces and switches, and the ground's level (its collision, for places).
/// Built from the game as it is loaded (<see cref="Load"/>), so <c>--check</c> and <c>--describe</c> cannot drift from it; a
/// test makes one by hand.
/// </summary>
public sealed class ScenarioGame : IDisposable
{
    public IReadOnlyCollection<string> Characters { get; init; } = [];
    public IReadOnlyCollection<string> Weapons { get; init; } = [];
    public IReadOnlyDictionary<string, FoePreset> Presets { get; init; } = new Dictionary<string, FoePreset>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> SheetTemplates { get; init; } = [];
    public IReadOnlyList<GameSheet> Sheets { get; init; } = [];
    public IReadOnlyList<GameTrait> Traits { get; init; } = [];
    public IReadOnlyDictionary<string, GamePower> Powers { get; init; } = new Dictionary<string, GamePower>(StringComparer.Ordinal);
    public IReadOnlyCollection<string> Armor { get; init; } = [];
    public IReadOnlyList<GameArmor> ArmorPieces { get; init; } = [];
    public IReadOnlyDictionary<string, (string Help, string Module)> Switches { get; init; } = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
    /// <summary>The module's data assets (id, schema, status), for <c>--describe data</c>.</summary>
    public IReadOnlyList<(string Id, string Schema, string Status)> Data { get; init; } = [];
    /// <summary>
    /// The modules' JSON data assets, each with its bytes as read (no override), its path under the module's
    /// <c>Data/</c> (<c>combat/weapons.json</c>, the name a scenario's <c>overrides.data</c> gives) and whether the modules read it as they
    /// were configured (or the program at load: the foes, the wind-ups); for <c>overrides.data</c>'s checks and <c>--describe data</c>.
    /// </summary>
    public IReadOnlyList<GameDataAsset> DataAssets { get; init; } = [];

    /// <summary>The level the ground is (its folder), or null when none was found.</summary>
    public string? LevelPath { get; init; }
    public LevelSpace? Level { get; init; }
    /// <summary>The modules' configuration (each module's id and configuration hash), for the provenance of a check.</summary>
    public IReadOnlyList<(string Module, string ConfigHash, string Profile)> Stamps { get; init; } = [];

    /// <summary>A 1.1 template the install has (beyond <see cref="Characters"/>) or null with no install.</summary>
    public Func<string, bool>? HasTemplate { get; init; }

    /// <summary>A 1.1 weapon template the install has (beyond the module's <see cref="Weapons"/>), or null with no install.</summary>
    public Func<string, bool>? HasWeapon { get; init; }

    GameInstall? install;

    /// <summary>The ids a sheet's <c>values</c> may set: the traits and the module's purchases (blood_pool, health_pool).</summary>
    public IReadOnlyCollection<string> SheetValueIds => [.. Traits.Select(t => t.Id)];

    /// <summary>
    /// Whether this build has the bow: its event <c>ArrowLoosed</c> is a record of the platform. Until then the
    /// bow's fields, steps, events and check are <see cref="ScenarioErrorCode.NotBuilt"/>.
    /// </summary>
    public static bool BowBuilt { get; } = typeof(Combat.ContactMade).Assembly.GetTypes().Any(t => t.Name == "ArrowLoosed" && t.IsValueType);

    /// <summary>The built-in measures' names for these actors (the results work's <see cref="BuiltInMeasures"/>: <c>&lt;actor&gt;.&lt;name&gt;</c>, <c>run.&lt;name&gt;</c>).</summary>
    public static IReadOnlyList<string> BuiltInMeasures(IEnumerable<string> actors) =>
        [.. actors.SelectMany(a => Scenario.BuiltInMeasures.PerActor.Select(m => $"{a}.{m.Name}")), .. Scenario.BuiltInMeasures.PerRun.Select(m => $"{Scenario.BuiltInMeasures.RunScope}.{m.Name}")];

    public void Dispose() => install?.Dispose();

    /// <summary>
    /// The game loaded for a check or a description: the modules as the session loads them (a refusal throws as the
    /// session's does: exit code 2), the 1.1 install when there is one, and the level when one is given. Runs no tick and makes
    /// no world.
    /// </summary>
    public static ScenarioGame Load(SessionOptions options, string? level)
    {
        var install = GameInstall.TryOpen(options.Game);
        LoadedGame modules;
        try { modules = ModulesCommand.Load(options, install); }
        catch { install?.Dispose(); throw; }
        LevelSpace? space = null;
        if (level is not null && File.Exists(Path.Combine(level, "level.json")))
        {
            try { space = LevelSpace.Open(level); }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException) { options.Say($"scenario: the level {level} could not be read ({e.Message})"); }
        }
        var game = From(modules, install, options.Say).At(level, space);
        game.install = install;
        return game;
    }

    /// <summary>The names the loaded modules and 1.1 install give (no level).</summary>
    public static ScenarioGame From(LoadedGame modules, GameInstall? install, Action<string> say)
    {
        var readByModules = modules.Assets.ReadSoFar.ToHashSet();   // before anything here reads them
        var dataAssets = new List<GameDataAsset>();
        var presets = FoePresets.From(modules, say);
        var traits = new List<GameTrait>();
        var powers = new Dictionary<string, GamePower>(StringComparer.Ordinal);
        var sheets = new List<GameSheet>();
        var armor = new List<GameArmor>();
        var weapons = new List<string>();
        var data = new List<(string, string, string)>();
        foreach (var m in modules.Modules)
        {
            string id = m.Manifest.Id;
            foreach (var asset in modules.Assets.List(id, ""))
            {
                if (!modules.Assets.TryResolve(asset, out var src) || src.File is null || !src.File.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                JsonDocument doc;
                try { doc = JsonDocument.Parse(modules.Assets.Read(asset), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }); }
                catch (JsonException) { continue; }
                using (doc)
                {
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    string schema = Str(root, "schema") ?? "", status = Str(root, "status") ?? "";
                    data.Add((asset.ToString(), schema, status));
                    string dataFolder = Path.Combine(m.Manifest.Folder, "Data");
                    if (src.File.StartsWith(dataFolder, StringComparison.Ordinal))
                        dataAssets.Add(new GameDataAsset(asset, Path.GetRelativePath(dataFolder, src.File).Replace('\\', '/'), schema, status,
                            readByModules.Contains(asset) || asset.Path is FoePresets.Asset or OneOneWindUps.Asset, modules.Assets.Read(asset)));
                    string path = asset.Path;
                    if (path == "vampire_trait_registry") Registry(root, traits, status);
                    else if (path == "rules/powers") ReadPowers(root, powers, status);
                    else if (path.StartsWith("sheets/", StringComparison.Ordinal)) Sheet(root, sheets, status);
                    else if (path == "combat/armor") ReadArmor(root, armor);
                    else if (path == "combat/weapons" && root.TryGetProperty("weapons", out var ws) && ws.ValueKind == JsonValueKind.Object) weapons.AddRange(ws.EnumerateObject().Select(w => w.Name));
                    else if (path == "combat/ranged" && root.TryGetProperty("weapons", out var rs) && rs.ValueKind == JsonValueKind.Object) weapons.AddRange(rs.EnumerateObject().Select(w => w.Name));
                }
            }
        }
        // The module's purchases a sheet may set beside its traits (the pools' capacities), as its sheets buy them.
        foreach (var p in sheets.SelectMany(s => s.Purchased.Keys).Distinct().Order(StringComparer.Ordinal))
            if (!traits.Any(t => t.Id == p)) traits.Add(new GameTrait(p, "purchase", "resource", 0, 1_000_000, p.StartsWith("blood", StringComparison.Ordinal) ? "bloodPoints" : p.StartsWith("health", StringComparison.Ordinal) ? "healthPoints" : "points", "SANDBOX", "the module's sheets (Data/sheets)"));
        var switches = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var m in modules.Modules)
            foreach (var s in m.Manifest.Switches) switches.TryAdd(s.Name, (s.Help, m.Manifest.Id));
        var characters = new List<string>();
        characters.AddRange(OneOneCatalogue.Characters.Select(c => c.Name));
        characters.AddRange(Townsfolk.Templates);
        characters.AddRange(presets.Values.Select(p => p.Template));
        OneOneTemplates? templates = install is null ? null : new OneOneTemplates(install);
        return new ScenarioGame
        {
            Characters = [.. characters.Distinct(StringComparer.Ordinal)],
            Weapons = [.. weapons.Distinct(StringComparer.Ordinal)],
            Presets = presets,
            SheetTemplates = [.. ProbeTemplates(modules).Concat(sheets.Select(s => s.Id)).Distinct(StringComparer.Ordinal)],
            Sheets = sheets,
            Traits = traits,
            Powers = powers,
            Armor = [.. armor.Select(a => a.Id)],
            ArmorPieces = armor,
            Switches = switches,
            Data = data,
            DataAssets = dataAssets,
            Stamps = [.. modules.Code.Select(c => (c.Stamp.ModuleId, c.Stamp.ConfigHash, c.Stamp.ProfileId))],
            HasTemplate = templates is null ? null : name => templates.Find(name) is { } t && templates.Resolve(t.Name).ContainsKey(OneOneTemplates.Model),
            HasWeapon = templates is null ? null : name => OneOneWeapon.From(templates, name) is not null,
        };
    }

    /// <summary>The same names on a level (its collision, for places).</summary>
    public ScenarioGame At(string? level, LevelSpace? space) => new()
    {
        Characters = Characters, Weapons = Weapons, Presets = Presets, SheetTemplates = SheetTemplates, Sheets = Sheets, Traits = Traits,
        Powers = Powers, Armor = Armor, ArmorPieces = ArmorPieces, Switches = Switches, Data = Data, Stamps = Stamps, DataAssets = DataAssets,
        HasTemplate = HasTemplate, HasWeapon = HasWeapon, LevelPath = space is null ? null : level, Level = space,
    };

    /// <summary>
    /// The sheet templates the modules' sheets offer (<see cref="IPersonSheets.Templates"/>: the townsperson made in code beside the
    /// data's), read from a world with the modules alone registered (no level, no tick); none when the modules keep no sheets.
    /// </summary>
    static IReadOnlyList<string> ProbeTemplates(LoadedGame modules)
    {
        try
        {
            var builder = new WorldBuilder(1);
            modules.Register(builder);
            var world = builder.Build();
            return world.TryService<IPersonSheets>(out var sheets) ? sheets.Templates : [];
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or NullReferenceException or ArgumentException) { return []; }
    }

    static string? Str(JsonElement o, string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static void Registry(JsonElement root, List<GameTrait> traits, string status)
    {
        (int Lo, int Hi) Domain(string name, int lo, int hi)
        {
            if (!root.TryGetProperty(name, out var d) || d.ValueKind != JsonValueKind.Object) return (lo, hi);
            int Get(string f, int x) => d.TryGetProperty(f, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : x;
            return (Get("min", lo), Get("max_purchased", Get("max", hi)));
        }
        var (alo, ahi) = Domain("attribute_domain", 0, 1000);
        var (blo, bhi) = Domain("ability_domain", 0, 10);
        if (root.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
            foreach (var a in attrs.EnumerateArray())
                if (Str(a, "id") is { } id) traits.Add(new GameTrait(id, "attribute", Str(a, "category") ?? "", alo, ahi, "attributePoints", status, "vampire_trait_registry.json attributes"));
        if (root.TryGetProperty("abilities", out var abs) && abs.ValueKind == JsonValueKind.Array)
            foreach (var a in abs.EnumerateArray())
                if (Str(a, "id") is { } id) traits.Add(new GameTrait(id, "ability", Str(a, "category") ?? "", blo, bhi, "abilityDots", Str(a, "status") ?? status, "vampire_trait_registry.json abilities"));
    }

    static void ReadPowers(JsonElement root, Dictionary<string, GamePower> powers, string status)
    {
        if (!root.TryGetProperty("powers", out var ps) || ps.ValueKind != JsonValueKind.Array) return;
        foreach (var p in ps.EnumerateArray())
        {
            if (Str(p, "power") is not { } id) continue;
            var options = p.TryGetProperty("options", out var os) && os.ValueKind == JsonValueKind.Array ? os.EnumerateArray().Select(o => o.GetString() ?? "").ToList() : [];
            var ranks = new List<(int, int, double)>();
            if (p.TryGetProperty("ranks", out var rs) && rs.ValueKind == JsonValueKind.Array)
                foreach (var r in rs.EnumerateArray())
                    ranks.Add((r.TryGetProperty("rank", out var rk) && rk.TryGetInt32(out int rank) ? rank : 0,
                        r.TryGetProperty("cost", out var c) && c.TryGetInt32(out int cost) ? cost : 0,
                        r.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble() : 0));
            powers[id] = new GamePower(id, Str(p, "label") ?? id, Str(p, "kind") ?? "", options, ranks, Str(p, "ranks_status") ?? status);
        }
    }

    static void Sheet(JsonElement root, List<GameSheet> sheets, string status)
    {
        if (Str(root, "id") is not { } id) return;
        var purchased = new Dictionary<string, int>(StringComparer.Ordinal);
        if (root.TryGetProperty("purchased", out var pu) && pu.ValueKind == JsonValueKind.Object)
            foreach (var v in pu.EnumerateObject()) if (v.Value.TryGetInt32(out int n)) purchased[v.Name] = n;
        var powers = new List<string>();
        if (root.TryGetProperty("powers", out var ps) && ps.ValueKind == JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray()) powers.Add(p.ValueKind == JsonValueKind.Object ? Str(p, "power") ?? Str(p, "id") ?? p.GetRawText() : p.GetRawText());
        sheets.Add(new GameSheet(id, Str(root, "title") ?? "", Str(root, "species") ?? "", purchased, powers, status));
    }

    static void ReadArmor(JsonElement root, List<GameArmor> armor)
    {
        if (!root.TryGetProperty("pieces", out var ps) || ps.ValueKind != JsonValueKind.Object) return;
        foreach (var p in ps.EnumerateObject())
            armor.Add(new GameArmor(p.Name, Str(p.Value, "name") ?? p.Name, Str(p.Value, "slot") ?? "",
                p.Value.TryGetProperty("covers", out var cv) && cv.ValueKind == JsonValueKind.Array ? [.. cv.EnumerateArray().Select(c => c.GetString() ?? "")] : []));
    }

    // ---- places on the ground

    /// <summary>
    /// What is wrong with a body standing at <paramref name="feet"/> on the ground, or null when nothing: outside the free yard
    /// (a named ground), no floor under it within a metre and a half, or inside a wall (a standing body's capsule, 0.3 m round
    /// and 1.8 m tall, overlapping the level by more than 2 cm). Without a level nothing is checked.
    /// </summary>
    public string? PlaceProblem(Vector3 feet, bool freeYard)
    {
        if (freeYard)
        {
            var y = ScenarioGround.Yard;
            if (feet.X < y.X0 || feet.X > y.X1 || feet.Y < y.Y0 || feet.Y > y.Y1)
                return ReaderText($"[{feet.X:0.##}, {feet.Y:0.##}] is outside the free yard (x {y.X0} to {y.X1}, y {y.Y0} to {y.Y1})");
        }
        if (Level is null) return null;
        var down = Level.Collision.Ray(feet + new Vector3(0, 0, 1.5f), -Vector3.UnitZ, 3.0f);
        if (down is null) return ReaderText($"[{feet.X:0.##}, {feet.Y:0.##}] has no floor under it (off the level)");
        float floor = down.Value.Point.Z;
        if (floor > feet.Z + 0.6f) return ReaderText($"[{feet.X:0.##}, {feet.Y:0.##}] is inside a wall or under a roof (solid {floor - feet.Z:0.##} m above the feet)");
        var capsule = Capsule.OfBody(new Vector3(feet.X, feet.Y, MathF.Max(feet.Z, floor) + 0.05f), 0.3f, 1.8f);
        if (Level.Collision.Overlaps(capsule, 0.02f)) return ReaderText($"[{feet.X:0.##}, {feet.Y:0.##}] is inside a wall (a standing body there overlaps the level)");
        return null;
    }

    static string ReaderText(FormattableString s) => s.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What is wrong with a body at <paramref name="feet"/> on the range (built in code: flat and open), or null — off its floor only.</summary>
    public static string? RangeProblem(Vector3 feet)
    {
        var r = Space.BuiltInGrounds.RangeArea;
        const float Body = 0.3f;
        if (feet.X < r.X0 + Body || feet.X > r.X1 - Body || feet.Y < r.Y0 + Body || feet.Y > r.Y1 - Body)
            return ReaderText($"[{feet.X:0.##}, {feet.Y:0.##}] is off the range (its floor: x {r.X0} to {r.X1}, y {r.Y0} to {r.Y1}; a body 0.3 m round)");
        return null;
    }

    /// <summary>The level's floor under a place (for a place given as [x, y]), or the given height when there is no level.</summary>
    public float FloorAt(float x, float y, float fallback)
    {
        if (Level?.Collision.Ray(new Vector3(x, y, fallback + 1.5f), -Vector3.UnitZ, 3.0f) is { } hit) return hit.Point.Z;
        return fallback;
    }

    /// <summary>The level's first start (or the named one), or null.</summary>
    public Vector3? Start(string? name)
    {
        if (Level is null) return null;
        foreach (var s in Level.Starts)
            if (name is null || s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || s.Id.Equals(name, StringComparison.OrdinalIgnoreCase)) return s.Position;
        return null;
    }
}
