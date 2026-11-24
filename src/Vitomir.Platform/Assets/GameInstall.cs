// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Taken from src/Vitomir.Engine/Vfs/GameFiles.cs (and the manifest of Vfs/GameData.cs) at b4185ef, reworked for the platform.
using System.Text.Json;
using System.Text.Json.Serialization;
using Vitomir.Formats;

namespace Vitomir.Platform.Assets;

/// <summary>
/// The player's 1.1 files, read only: either the 1.1 install itself — its <c>.nob</c>
/// archives and its loose files — or a Vitomir game folder laid out from one (the
/// files unpacked by kind, with a manifest, <c>vitomir-game.json</c>, naming the path 1.1 knows each by).
/// Paths are 1.1's own (<c>3d/models/christof.nod</c>), with forward slashes, and case-insensitive
/// everywhere, as on Windows.
/// <para>
/// As in 1.1 (and the old engine's <c>GameFiles</c>): every <c>.nob</c> in the install's folder is mounted,
/// in the order Windows lists them (upper-cased names, ordinal), at most 64; a
/// loose file of the install wins over an archive's entry of the same path (a texture pack dropped into
/// the folder); a file is looked for in the archives in mount order, the first that has it winning.
/// Materials and textures named without a folder are looked for along 1.1's search path
/// (<see cref="MaterialSearchPath"/>). 1.1's <c>-user</c> folder and CD root are left out, as the
/// old engine leaves them.
/// </para>
/// <para>
/// Reading is safe from several threads at once (assets load on worker threads): the
/// archives are read under a lock.
/// </para>
/// </summary>
public sealed class GameInstall : IDisposable
{
    /// <summary>How many archives one folder mounts at most (the original stops at 64).</summary>
    public const int MaxArchives = 0x40;

    /// <summary>The manifest that makes a folder a Vitomir game folder.</summary>
    public const string ManifestName = "vitomir-game.json";

    /// <summary>
    /// Where 1.1 looks for a material or a texture named without a folder, in order: the game's start-up
    /// registers these nine folders for resource kind 1, and the open joins each to the
    /// name in turn before trying the name on its own.
    /// </summary>
    public static readonly string[] MaterialSearchPath =
    [
        "ui/materials", "ui/icons", "materials", "3d/materials", "materials/general",
        "materials/prague", "materials/vienna", "materials/london", "materials/new_york",
    ];

    readonly List<NobArchive> archives = [];
    readonly Dictionary<string, string>? looseIndex;     // the install's loose files: 1.1 path → file on disk
    readonly Dictionary<string, string>? laidOut;        // a game folder's: 1.1 path → file on disk
    readonly List<string> archiveNamesInOrder = [];      // archive entries' 1.1 paths, mount order then archive order
    readonly Lock archiveLock = new();
    Dictionary<string, string>? byFileName;

    /// <summary>The folder opened.</summary>
    public string Root { get; }

    /// <summary>Whether this is a Vitomir game folder rather than the 1.1 install itself.</summary>
    public bool IsLayout => laidOut is not null;

    GameInstall(string root)
    {
        Root = root;
        if (IsGameFolder(root))
        {
            var manifest = ReadManifest(root) ?? throw new InvalidDataException($"{Path.Combine(root, ManifestName)} cannot be read");
            laidOut = new Dictionary<string, string>(manifest.Files.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var e in manifest.Files)
            {
                string one = Normal(e.One);
                if (laidOut.TryAdd(one, Path.Combine(root, e.Path.Replace('/', Path.DirectorySeparatorChar))) && e.Archive is not null)
                    archiveNamesInOrder.Add(one);
            }
            return;
        }
        foreach (var p in MountOrder(root))
        {
            if (archives.Count == MaxArchives) break;
            try { archives.Add(new NobArchive(p)); }
            catch (InvalidDataException) { }   // not an archive: not mounted, not counted (the original's mount answers 0)
        }
        if (archives.Count == 0) throw new DirectoryNotFoundException($"no .nob archives in {root}");
        foreach (var a in archives) archiveNamesInOrder.AddRange(a.Names.Select(Normal));
        looseIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            looseIndex.TryAdd(Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'), f);
    }

    /// <summary>Opens the 1.1 install or a Vitomir game folder at <paramref name="root"/>.</summary>
    /// <exception cref="DirectoryNotFoundException">The folder is neither.</exception>
    public static GameInstall Open(string root)
    {
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"no folder {root}");
        return new GameInstall(Path.GetFullPath(root));
    }

    /// <summary>
    /// Finds the player's 1.1 files: <paramref name="explicitDir"/> when given (and then only it, so a folder
    /// named by mistake is refused rather than swapped for another); else <c>VTMR_GAME_DIR</c>; else
    /// <c>C:\Games\VTMR</c>. A folder counts when it is an install (it has <c>Resource.nob</c>) or a Vitomir
    /// game folder (it has the manifest). Null when there is none.
    /// </summary>
    public static string? Locate(string? explicitDir = null)
    {
        string?[] candidates = !string.IsNullOrEmpty(explicitDir) ? [explicitDir]
            : [Environment.GetEnvironmentVariable("VTMR_GAME_DIR"), @"C:\Games\VTMR"];
        foreach (var d in candidates)
            if (!string.IsNullOrEmpty(d) && (IsGameFolder(d) || IsInstall(d))) return d;
        return null;
    }

    /// <summary><see cref="Locate"/> and <see cref="Open"/> together; null when nothing is found.</summary>
    public static GameInstall? TryOpen(string? explicitDir = null) => Locate(explicitDir) is { } d ? Open(d) : null;

    /// <summary>Whether <paramref name="dir"/> is the 1.1 install (it has <c>Resource.nob</c>, looked for case-insensitively).</summary>
    public static bool IsInstall(string dir) =>
        Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.nob").Any(p => Path.GetFileName(p).Equals("Resource.nob", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether <paramref name="dir"/> is a finished Vitomir game folder (it has its manifest).</summary>
    public static bool IsGameFolder(string dir) => File.Exists(Path.Combine(dir, ManifestName));

    /// <summary>
    /// The archives a folder mounts, in the order a path is looked for in them: 1.1 lists <c>*.nob</c> in
    /// the folder and opens each in the listing's order, skipping a name that starts with a dot;
    /// Windows lists an NTFS folder by upper-cased name, ordinal.
    /// </summary>
    public static IEnumerable<string> MountOrder(string root) =>
        Directory.EnumerateFiles(root)
                 .Where(p => Path.GetExtension(p).Equals(".nob", StringComparison.OrdinalIgnoreCase)
                          && !Path.GetFileName(p).StartsWith('.'))
                 .OrderBy(p => Path.GetFileName(p).ToUpperInvariant(), StringComparer.Ordinal);

    static string Normal(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>Whether the file 1.1 knows as <paramref name="path"/> is there.</summary>
    public bool Exists(string path)
    {
        path = Normal(path);
        if (laidOut is not null) return laidOut.ContainsKey(path);
        if (looseIndex!.ContainsKey(path)) return true;
        lock (archiveLock)
            foreach (var a in archives)
                if (a.Contains(path)) return true;
        return false;
    }

    /// <summary>Reads the file 1.1 knows as <paramref name="path"/>: the install's loose file, else the first archive that has it.</summary>
    public bool TryRead(string path, out byte[] data)
    {
        path = Normal(path);
        if (laidOut is not null)
        {
            if (laidOut.TryGetValue(path, out var placed) && File.Exists(placed)) { data = File.ReadAllBytes(placed); return true; }
            data = [];
            return false;
        }
        if (looseIndex!.TryGetValue(path, out var full)) { data = File.ReadAllBytes(full); return true; }
        lock (archiveLock)
            foreach (var a in archives)
                if (a.TryRead(path, out data)) return true;
        data = [];
        return false;
    }

    /// <summary>Reads a file; throws <see cref="FileNotFoundException"/> when it is not there.</summary>
    public byte[] Read(string path) => TryRead(path, out var d) ? d : throw new FileNotFoundException(path);

    /// <summary>
    /// Reads a material or a texture named without a folder the way 1.1 resolves one: each folder of
    /// <see cref="MaterialSearchPath"/> joined to the name, then the name on its own. Gives the path found.
    /// </summary>
    public bool TryReadMaterialFile(string name, out byte[] data, out string found)
    {
        name = Normal(name);
        foreach (var dir in MaterialSearchPath)
            if (TryRead(found = dir + "/" + name, out data)) return true;
        found = name;
        return TryRead(name, out data);
    }

    /// <summary>The path <see cref="TryReadMaterialFile"/> would read for <paramref name="name"/>, without reading it; null when none.</summary>
    public string? FindMaterialFile(string name)
    {
        name = Normal(name);
        foreach (var dir in MaterialSearchPath)
            if (Exists(dir + "/" + name)) return dir + "/" + name;
        return Exists(name) ? name : null;
    }

    /// <summary>
    /// An archive entry found by its file name alone, in any folder (the old engine's last fallback for a
    /// texture; no shipped model needs it): the first archive in mount order wins. Null when none has it.
    /// </summary>
    public string? FindByFileName(string fileName)
    {
        if (byFileName is null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in archiveNamesInOrder) map.TryAdd(Path.GetFileName(n), n);
            Interlocked.CompareExchange(ref byFileName, map, null);
        }
        return byFileName.TryGetValue(Path.GetFileName(fileName), out var path) ? path : null;
    }

    /// <summary>
    /// The 1.1 paths under <paramref name="folder"/> (for example <c>misc/templates/</c>), each once: the
    /// install's loose files first (in ordinal order), then the archives' entries in mount order and each
    /// archive's own order — or a game folder's manifest in its order.
    /// </summary>
    public IReadOnlyList<string> List(string folder)
    {
        folder = Normal(folder);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        IEnumerable<string> all = laidOut is not null ? laidOut.Keys : looseIndex!.Keys.Concat(archiveNamesInOrder);
        foreach (var n in all)
            if (n.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && seen.Add(n)) list.Add(n);
        return list;
    }

    public void Dispose()
    {
        lock (archiveLock)
            foreach (var a in archives) a.Dispose();
    }

    // ---- a game folder's manifest (the old engine's GameData.Manifest, only what reading needs) ----

    sealed class Manifest
    {
        public List<Entry> Files { get; set; } = [];
    }

    sealed class Entry
    {
        /// <summary>The path 1.1 knows it by.</summary>
        public string One { get; set; } = "";
        /// <summary>Where it lives, relative to the game folder.</summary>
        public string Path { get; set; } = "";
        /// <summary>The first archive (in mount order) that holds the path, or null for an install's own loose file.</summary>
        [JsonPropertyName("arc")] public string? Archive { get; set; }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    static Manifest? ReadManifest(string gameDir)
    {
        try { return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(Path.Combine(gameDir, ManifestName)), Json); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
}
