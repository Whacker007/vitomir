// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitomir.Platform.PlayerModules;

/// <summary>
/// One data file of a player module, as its import found it: its 1.1 path, the archive of the module's it is in (null: a loose file in
/// <c>Data\</c>), its size and SHA-256, its kind, and what was read of it — a texture's size and alpha, a model's materials, and what
/// does not decode (<see cref="Problem"/>; an error at every check until the module is mended).
/// </summary>
public sealed record ModuleFileRecord
{
    public string Path { get; init; } = "";
    /// <summary>The module's archive holding it (its file name in <c>Data\</c>), or null for a loose file.</summary>
    public string? In { get; init; }
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
    /// <summary><c>look</c>, <c>world</c> or <c>interface</c> (<see cref="PackKindWords"/>).</summary>
    public string Kind { get; init; } = "";
    /// <summary>A texture's width and height.</summary>
    public int? Width { get; init; }
    public int? Height { get; init; }
    /// <summary>A texture has an alpha channel (32 bits a pixel).</summary>
    public bool? Alpha { get; init; }
    /// <summary>A texture's alpha is used: some pixel is not fully opaque.</summary>
    public bool? AlphaUsed { get; init; }
    /// <summary>A model's materials, by name, in its own order.</summary>
    public List<string>? Materials { get; init; }
    /// <summary>A film of no bytes: the pack takes 1.1's film out (the game skips it).</summary>
    public bool? Empty { get; init; }
    /// <summary>
    /// A film whose bytes are not a Bink film (a JSGME-era pack's <c>#Dummy.bik</c> text, of any size): it takes 1.1's film out as an
    /// empty one does, since the game skips a film it cannot open; a note, never an error.
    /// </summary>
    public bool? NotFilm { get; init; }
    /// <summary>Why the file does not decode, or null when it reads (or is of a kind the import does not decode).</summary>
    public string? Problem { get; init; }
}

/// <summary>A <c>.nob</c> archive of a player module's, kept whole in its <c>Data\</c> and mounted at the module's place in the order.</summary>
public sealed record ModuleArchiveRecord
{
    /// <summary>Its file name in <c>Data\</c>.</summary>
    public string Path { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
}

/// <summary>A file of the pack the import left out: refused (code, program) or kept aside as a document; with its path in the pack and why.</summary>
public sealed record ModuleLeftOut
{
    public string Path { get; init; } = "";
    /// <summary><c>code</c>, <c>program</c>, <c>document</c> or <c>twice</c> (a second file of one 1.1 path).</summary>
    public string Class { get; init; } = "";
    public string Why { get; init; } = "";
}

/// <summary>
/// **A player module's import record**, <c>vitomir-module.json</c> in its folder, written last: the module it holds, where it came from,
/// when and by what, the hash of its content (every data file's path and SHA-256) and of its manifest, its kinds, every data file
/// (<see cref="ModuleFileRecord"/>), its archives, and what was left out of the pack and why. A module's folder without it did not finish
/// importing, and no game uses it.
/// </summary>
public sealed record ModuleRecord
{
    /// <summary>1: this record.</summary>
    public int Format { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
    /// <summary>What it was imported from: a folder, a zip or a .nob, by its full path on this machine.</summary>
    public string Source { get; init; } = "";
    /// <summary><c>folder</c>, <c>zip</c> or <c>nob</c>.</summary>
    public string SourceKind { get; init; } = "";
    /// <summary>The pack's own layout, when it is one the import knows (<c>jsgme</c>: a <c>MODS\&lt;name&gt;\</c> folder), else empty.</summary>
    public string Layout { get; init; } = "";
    public DateTime ImportedUtc { get; init; }
    public string ImportedBy { get; init; } = "";
    /// <summary>SHA-256 over every data file's path and hash (<see cref="PlayerModule.ContentHashOf"/>), hex.</summary>
    public string ContentHash { get; init; } = "";
    /// <summary>SHA-256 of <c>module.json</c> as written, hex (its fits and madeFor decide warnings).</summary>
    public string ManifestHash { get; init; } = "";
    /// <summary>The kinds of its files (<see cref="PackKindWords"/>).</summary>
    public List<string> Kinds { get; init; } = [];
    public List<ModuleFileRecord> Files { get; init; } = [];
    public List<ModuleArchiveRecord> Archives { get; init; } = [];
    public List<ModuleLeftOut> LeftOut { get; init; } = [];
}

/// <summary>
/// **One layer of the game's files from a player module**, as the engine's files are given it (<c>--player-module &lt;folder&gt;</c>):
/// its id, version and content hash, whether it is a rules module, its loose files' folder (<c>Data\</c>) and its archives. Made from
/// an imported module (<see cref="PlayerModule.Layer"/>) or read back from its folder (<see cref="Open"/>).
/// </summary>
public sealed record PlayerModuleLayer(string Id, string Version, string ContentHash, PackKinds Kinds, string DataFolder, IReadOnlyList<string> Archives)
{
    /// <summary>Its interface elements bound to Vitomir's own actions, as its manifest writes them (<see cref="InterfaceActions"/>).</summary>
    public IReadOnlyDictionary<string, string> Actions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether it changes the rules or the world: a world file among its files (<see cref="PackKindWords.IsRules"/>), or a binding that
    /// opens the advancement screen or the vault where 1.1 would not (<see cref="InterfaceActions.ChangeRules"/>).
    /// </summary>
    public bool Rules => PackKindWords.IsRules(Kinds) || InterfaceActions.ChangeRules(Actions);

    /// <summary>Its name in a network game's identity and a save: <c>id version hash</c>, the rules hash's first sixteen hex digits (<see cref="RulesHash"/>).</summary>
    public string Stamp => $"{Id} {Version} {RulesHash[..Math.Min(16, RulesHash.Length)]}";

    /// <summary>
    /// **What decides its rules, as one hash**: its content hash when it binds no interface element (so a look-only or data-only
    /// module's stamp is its content hash, as before bindings existed); else SHA-256, hex, over the content hash and a canonical form
    /// of its <see cref="Actions"/> (<see cref="CanonicalActions"/>), so two machines with the same files and other bindings differ.
    /// </summary>
    public string RulesHash => RulesHashOf(ContentHash, Actions);

    /// <summary>The rules hash of a content hash and bindings (<see cref="RulesHash"/>).</summary>
    public static string RulesHashOf(string contentHash, IReadOnlyDictionary<string, string> actions) =>
        actions.Count == 0 ? contentHash
            : PlayerModule.Hash(System.Text.Encoding.UTF8.GetBytes(contentHash + "\nactions " + CanonicalActions(actions)));

    /// <summary>
    /// Bindings as one canonical text: a JSON object, the element names and the actions in lower case (both are read without regard to
    /// case), the keys sorted by ordinal, no spaces: <c>{"sblvlup":"open-advancement","sbvault":"open-vault"}</c>.
    /// </summary>
    public static string CanonicalActions(IReadOnlyDictionary<string, string> actions)
    {
        using var stream = new MemoryStream();
        using (var w = new System.Text.Json.Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            foreach (var (element, action) in actions.Select(a => (a.Key.ToLowerInvariant(), a.Value.ToLowerInvariant())).OrderBy(a => a.Item1, StringComparer.Ordinal))
                w.WriteString(element, action);
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The layer of the imported module in <paramref name="folder"/>; throws when it is not one (no record, or one that does not read).</summary>
    public static PlayerModuleLayer Open(string folder)
    {
        var problems = new List<string>();
        return PlayerModule.Open(folder, problems)?.Layer
            ?? throw new InvalidDataException($"{folder} is not an imported player module: {string.Join("; ", problems)}");
    }
}

/// <summary>
/// **An imported player module**: its folder in the user data (<c>&lt;user data&gt;\PlayerModules\&lt;id&gt;\</c>), with its
/// <c>module.json</c>, its <c>Data\</c> (1.1's paths; its archives at the top), its <c>Documents\</c> (what the pack carried for people)
/// and its record (<see cref="ModuleRecord"/>).
/// </summary>
public sealed class PlayerModule
{
    /// <summary>The record's file name, written last by the import.</summary>
    public const string RecordName = "vitomir-module.json";

    /// <summary>The data's folder in a module's folder: 1.1's paths under it.</summary>
    public const string DataFolderName = "Data";

    /// <summary>The documents' folder (readmes, settings, pictures the pack carried): never read by the game.</summary>
    public const string DocumentsFolderName = "Documents";

    /// <summary>
    /// The mark <see cref="VerifyAndMark"/> leaves in a module's folder when its files are not as imported (a line a file): the module is an
    /// error at every check (<see cref="CheckWhole"/>) until it is removed and added again (an import of it takes the mark away).
    /// </summary>
    public const string DamagedName = "vitomir-module-damaged.txt";

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Folder { get; }
    public PlayerModuleManifest Manifest { get; }
    public ModuleRecord Record { get; }

    PlayerModule(string folder, PlayerModuleManifest manifest, ModuleRecord record)
    {
        Folder = folder;
        Manifest = manifest;
        Record = record;
        Kinds = record.Kinds.Select(PackKindWords.Parse).OfType<PackKinds>().Aggregate((PackKinds)0, (a, k) => a | k);
    }

    public string Id => Manifest.Id;

    /// <summary>The kinds of its files.</summary>
    public PackKinds Kinds { get; }

    /// <summary>
    /// Whether it changes the rules or the world (a network game's machines must agree on it; a save names it): a world file, or an
    /// interface binding that opens the advancement screen or the vault where 1.1 would not.
    /// </summary>
    public bool Rules => PackKindWords.IsRules(Kinds) || RulesByActions;

    /// <summary>Whether its interface bindings alone make it a rules module (<see cref="InterfaceActions.ChangeRules"/>).</summary>
    public bool RulesByActions => InterfaceActions.ChangeRules(Manifest.Actions);

    public string DataFolder => Path.Combine(Folder, DataFolderName);

    /// <summary>Its archives' full paths, in the order the import found them.</summary>
    public IReadOnlyList<string> Archives => [.. Record.Archives.Select(a => Path.Combine(DataFolder, a.Path))];

    /// <summary>Its layer of the game's files.</summary>
    public PlayerModuleLayer Layer => new(Id, Manifest.Version.ToString(), Record.ContentHash, Kinds, DataFolder, Archives) { Actions = Manifest.Actions };

    /// <summary>The module, its version and its rules hash's first sixteen hex digits: <c>id version hash</c> (<see cref="PlayerModuleLayer.Stamp"/>).</summary>
    public string Stamp => Layer.Stamp;

    public override string ToString() => $"{Id} {Manifest.Version}";

    /// <summary>The module in <paramref name="folder"/>, or null (and why added) when it has no record, or its manifest or record does not read.</summary>
    public static PlayerModule? Open(string folder, List<string> problems)
    {
        folder = Path.GetFullPath(folder);
        string recordPath = Path.Combine(folder, RecordName);
        if (!File.Exists(recordPath)) { problems.Add($"{folder}: its import did not finish (no {RecordName}): add it again"); return null; }
        var manifest = PlayerModuleManifest.Read(folder, problems);
        if (manifest is null) return null;
        ModuleRecord? record;
        try { record = JsonSerializer.Deserialize<ModuleRecord>(File.ReadAllText(recordPath), Json); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            problems.Add($"{recordPath}: cannot be read ({e.Message}): add the module again");
            return null;
        }
        if (record is null || record.Id != manifest.Id)
        {
            problems.Add($"{recordPath}: is not the record of the module {manifest.Id}: add it again");
            return null;
        }
        return new PlayerModule(folder, manifest, record);
    }

    /// <summary>
    /// **Whether its files are as imported**: every data file and archive there at its size, its manifest the one imported (by its hash),
    /// and no file in <c>Data\</c> the import did not put there (one added by hand is named, and refused by its class when it is code or a
    /// program). Answers the problems, none when it is whole. Sizes only: a file changed in place at its size is found by
    /// <see cref="Verify"/>, which hashes.
    /// </summary>
    public IReadOnlyList<string> CheckWhole()
    {
        var problems = new List<string>();
        foreach (var f in Record.Files.Where(f => f.In is null))
        {
            var full = Path.Combine(DataFolder, f.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full)) problems.Add($"{f.Path}: missing since the import");
            else if (new FileInfo(full).Length != f.Size) problems.Add($"{f.Path}: not the size it was imported at");
        }
        foreach (var a in Record.Archives)
        {
            var full = Path.Combine(DataFolder, a.Path);
            if (!File.Exists(full)) problems.Add($"{a.Path}: missing since the import");
            else if (new FileInfo(full).Length != a.Size) problems.Add($"{a.Path}: not the size it was imported at");
        }
        foreach (var rel in NotImported())
        {
            var c = PackFiles.Classify(rel);
            problems.Add(c.Class is PackFileClass.Code or PackFileClass.Program
                ? $"{rel}: put there after the import, and refused: {c.Why} (player modules carry data only)"
                : $"{rel}: {AddedByHand}");
        }
        string damaged = Path.Combine(Folder, DamagedName);
        if (File.Exists(damaged))
            foreach (var line in File.ReadAllLines(damaged).Where(l => l.Contains(':')).Except(problems))
                problems.Add($"{line} (found by Verify): remove the module and add it again");
        string manifestPath = Path.Combine(Folder, PlayerModuleManifest.FileName);
        if (File.Exists(manifestPath) && Hash(File.ReadAllBytes(manifestPath)) != Record.ManifestHash)
            problems.Add($"{PlayerModuleManifest.FileName}: changed since the import: add the module again to check it");
        return problems;
    }

    /// <summary>What <see cref="CheckWhole"/> says of a data file put in its <c>Data\</c> by hand (one that is not code or a program).</summary>
    public const string AddedByHand = "put there after the import: add the module again from its source to take it in";

    /// <summary>The files in its <c>Data\</c> its import did not put there (by their 1.1 paths, <c>/</c> separated, in the folder's order).</summary>
    public IReadOnlyList<string> NotImported()
    {
        var found = new List<string>();
        if (!Directory.Exists(DataFolder)) return found;
        var expected = new HashSet<string>(Record.Files.Where(f => f.In is null).Select(f => f.Path).Concat(Record.Archives.Select(a => a.Path)), StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(DataFolder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(DataFolder, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!expected.Contains(rel)) found.Add(rel);
        }
        return found;
    }

    /// <summary>
    /// The data files put in its <c>Data\</c> by hand since its import (<see cref="NotImported"/> less code and program files, which are
    /// refused on their own): the game does not play them, and an update from the module's own folder takes them in when they are there.
    /// </summary>
    public IReadOnlyList<string> HandAdded() =>
        [.. NotImported().Where(rel => PackFiles.Classify(rel).Class is not (PackFileClass.Code or PackFileClass.Program))];

    /// <summary>As <see cref="CheckWhole"/>, and every file's SHA-256 against the record's.</summary>
    public IReadOnlyList<string> Verify()
    {
        var problems = CheckWhole().ToList();
        if (problems.Count > 0) return problems;
        foreach (var f in Record.Files.Where(f => f.In is null))
            if (HashFile(Path.Combine(DataFolder, f.Path.Replace('/', Path.DirectorySeparatorChar))) != f.Sha256) problems.Add($"{f.Path}: changed since the import");
        foreach (var a in Record.Archives)
            if (HashFile(Path.Combine(DataFolder, a.Path)) != a.Sha256) problems.Add($"{a.Path}: changed since the import");
        return problems;
    }

    /// <summary>
    /// **Verify** as the launcher's Modules page asks it: <see cref="Verify"/>, and when anything is wrong the mark (<see cref="DamagedName"/>)
    /// written, so the module is an error at every check until it is removed and added again. Answers the problems, none when it is as
    /// imported; the mark of an earlier verification is not taken away by a good one (only an import of the module takes it).
    /// </summary>
    public IReadOnlyList<string> VerifyAndMark()
    {
        var problems = Verify().Where(p => !p.EndsWith("(found by Verify): remove the module and add it again", StringComparison.Ordinal)).ToList();
        if (problems.Count > 0) File.WriteAllLines(Path.Combine(Folder, DamagedName), problems);
        return problems;
    }

    /// <summary>
    /// The content hash: SHA-256, hex, over each loose data file's 1.1 path (lower case) and SHA-256 and each archive's name and SHA-256,
    /// a line each, sorted. Two imports of the same files give the same, wherever they came from.
    /// </summary>
    public static string ContentHashOf(IEnumerable<ModuleFileRecord> files, IEnumerable<ModuleArchiveRecord> archives)
    {
        var lines = files.Where(f => f.In is null).Select(f => $"{f.Path.ToLowerInvariant()}\t{f.Sha256}")
            .Concat(archives.Select(a => $"archive {a.Path.ToLowerInvariant()}\t{a.Sha256}"))
            .Order(StringComparer.Ordinal);
        return Hash(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
    }

    /// <summary>SHA-256 of bytes, hex.</summary>
    public static string Hash(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    /// <summary>SHA-256 of a file, hex.</summary>
    public static string HashFile(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }

    internal static void WriteRecord(string folder, ModuleRecord record)
    {
        string path = Path.Combine(folder, RecordName), temp = path + ".writing";
        File.WriteAllText(temp, JsonSerializer.Serialize(record, Json));
        File.Move(temp, path, overwrite: true);
    }
}
