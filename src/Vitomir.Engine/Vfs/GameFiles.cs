// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using Vitomir.Formats;
using Vitomir.Platform.PlayerModules;

namespace Vitomir.Engine.Vfs;

/// <summary>
/// Read-only view of the player's install: loose files first, then the .nob archives. Loose files
/// under the game folder override archive entries of the same path, which is how texture packs
/// such as Neural Redemption install (a "materials" and a "3d" folder dropped into the game
/// folder). Extra data folders can be layered on top without touching the install; the last one
/// given wins. Paths use forward slashes and are case-insensitive everywhere, as on Windows.
/// Every <c>.nob</c> in the game folder is mounted, in the folder's own order (<see cref="MountOrder"/>).
/// The player's modules (<see cref="PlayerModuleLayer"/>) are layered between the two: over the game's own
/// files, in their load order (the last winning), each its loose files and then its archives, and under the
/// extra data folders.
/// (1.1's -user folder and its CD root are left out)
/// </summary>
public sealed class GameFiles : IDisposable
{
    /// <summary>The six archives 1.1 ships. They are not a list the engine keeps: it mounts every
    /// <c>.nob</c> in the folder (<see cref="MountOrder"/>), and these six share no path.</summary>
    public static readonly string[] Archives =
        ["Local_Eng.nob", "Resource.nob", "LMaterials.nob", "Levels.nob", "Sounds.nob", "Codex.nob"];

    /// <summary>How many archives one folder mounts at most (the original stops at 64).</summary>
    public const int MaxArchives = 0x40;

    /// <summary>
    /// The archives a folder mounts, in the order a path is looked for in them. 1.1's file system
    /// (from its start) lists <c>*.nob</c> in the folder
    /// and opens each in the order the listing gives, skipping a name that starts with a
    /// dot, until 64 are open; an archive that does not open is not counted. A file is then looked
    /// for in them in that order, the first that has it winning. So a mod's
    /// own <c>.nob</c> dropped beside the six loads too. Windows lists an NTFS folder by name, upper
    /// case compared ordinal, which is the order here: Codex, Levels, LMaterials, Local_Eng,
    /// Resource, Sounds for the shipped six.
    /// </summary>
    public static IEnumerable<string> MountOrder(string root) =>
        Directory.EnumerateFiles(root)
                 .Where(p => Path.GetExtension(p).Equals(".nob", StringComparison.OrdinalIgnoreCase)
                          && !Path.GetFileName(p).StartsWith('.'))
                 .OrderBy(p => Path.GetFileName(p).ToUpperInvariant(), StringComparer.Ordinal);

    readonly List<NobArchive> archives = [];
    /// <summary>Loose-file folders, highest priority first: the data folders (last given first), then the game folder.</summary>
    readonly List<LooseFolder> loose = [];

    /// <summary>
    /// The player's modules (<see cref="PlayerModuleLayer"/>), highest priority first: the last in the load order first, each its loose
    /// files and then its archives. They come under the developer's data folders and over the game folder's own files.
    /// </summary>
    readonly List<(PlayerModuleLayer Layer, LooseFolder Loose, List<NobArchive> Archives)> moduleLayers = [];

    public string Root { get; }

    /// <summary>
    /// Vitomir's own game folder, when <see cref="Root"/> is one (<see cref="GameData"/>): the path 1.1
    /// knows each file by → where it lives. Null for the 1.1 install itself.
    /// </summary>
    readonly Dictionary<string, string>? laidOut;
    readonly GameData.Manifest? manifest;

    /// <summary>Whether this is Vitomir's own game folder rather than the 1.1 install.</summary>
    public bool IsLayout => laidOut is not null;

    /// <param name="root">The game folder or the 1.1 install.</param>
    /// <param name="dataFolders">A developer's loose folders over everything, the last given winning (<c>--data</c>).</param>
    /// <param name="modules">The player's modules on, in load order, the last winning (<c>--player-module</c>): over the game's own files,
    /// under the developer's folders. Not in <see cref="Fingerprint"/>: the network game compares the rules modules by name instead
    /// (<see cref="RulesModules"/>).</param>
    /// <param name="recookedFolder">The recooked base game (<see cref="Recooked"/>); by default the one beside the imported data, if there is one.</param>
    public GameFiles(string root, IEnumerable<string>? dataFolders = null, IReadOnlyList<PlayerModuleLayer>? modules = null, string? recookedFolder = null)
    {
        Root = root;
        if (GameData.IsLayout(root))
        {
            manifest = GameData.ReadManifest(root) ?? throw new InvalidDataException($"{GameData.ManifestPath(root)} cannot be read");
            laidOut = new Dictionary<string, string>(manifest.Files.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var e in manifest.Files) laidOut.TryAdd(e.One.Replace('\\', '/'), GameData.Target(root, e.Path));
        }
        else
        {
            foreach (var p in MountOrder(root))
            {
                if (archives.Count == MaxArchives) break;
                try { archives.Add(new NobArchive(p)); }
                catch (InvalidDataException) { }   // not an archive: not mounted, not counted (the mount answers 0)
            }
            if (archives.Count == 0) throw new DirectoryNotFoundException($"no .nob archives in {root}");
        }
        foreach (var d in (dataFolders ?? []).Reverse())
        {
            if (!Directory.Exists(d)) throw new DirectoryNotFoundException($"data folder not found: {d}");
            loose.Add(new LooseFolder(d));
        }
        if (laidOut is null) loose.Add(new LooseFolder(root));
        foreach (var m in (modules ?? []).Reverse())
        {
            if (!Directory.Exists(m.DataFolder)) throw new DirectoryNotFoundException($"player module {m.Id}'s data folder not found: {m.DataFolder}");
            moduleLayers.Add((m, new LooseFolder(m.DataFolder, skipTopArchives: true), [.. m.Archives.Select(a => new NobArchive(a))]));
        }
        Modules = [.. modules ?? []];
        if ((recookedFolder ?? RecookedBeside(root)) is { } rc && Recooked.VersionOf(rc) == Recooked.Version) recooked = new LooseFolder(rc);
    }

    /// <summary>The player's modules on, in load order (the last wins).</summary>
    public IReadOnlyList<PlayerModuleLayer> Modules { get; }

    /// <summary>
    /// The rules modules on (<see cref="PlayerModuleLayer.Rules"/>: a world file among theirs), in load order, each as
    /// <c>id version hash</c>: what a network game's machines must agree on and a save records. Empty with none.
    /// </summary>
    public IReadOnlyList<string> RulesModules => [.. Modules.Where(m => m.Rules).Select(m => m.Stamp)];

    /// <summary>How many loose folders come over the player's modules: the developer's data folders (the game folder's own is last, under them).</summary>
    int DataFolderCount => laidOut is null ? loose.Count - 1 : loose.Count;

    /// <summary>
    /// A player module's own loose file of the path 1.1 knows (the top module that has it), for what is read by path (a film), or null.
    /// </summary>
    public string? ModuleFile(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        foreach (var m in moduleLayers)
            if (m.Loose.TryGet(path, out var full)) return full;
        return null;
    }

    /// <summary>A folder of loose files, looked up case-insensitively even on case-sensitive file systems.</summary>
    sealed class LooseFolder(string dir, bool skipTopArchives = false)
    {
        public string Dir { get; } = dir;
        Dictionary<string, string>? index;

        public bool TryGet(string path, out string full)
        {
            full = Path.Combine(Dir, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full)) return true;
            if (OperatingSystem.IsWindows()) return false;     // already case-insensitive
            return Index().TryGetValue(path, out full!);
        }

        /// <summary>
        /// The files directly in one of this folder's folders (<paramref name="folder"/>, forward slashes, ending in one), as paths
        /// relative to this folder: that folder alone is listed, not the whole tree (on a case-sensitive system, through the index).
        /// </summary>
        public IEnumerable<string> Under(string folder)
        {
            string full = Path.Combine(Dir, folder.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(full))
                return Directory.EnumerateFiles(full).Select(f => folder + Path.GetFileName(f)).ToList();
            if (OperatingSystem.IsWindows() || !Directory.Exists(Dir)) return [];
            return Index().Keys.Where(n => n.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && n.IndexOf('/', folder.Length) < 0).ToList();
        }

        /// <summary>Every file under this folder, as a forward-slash path relative to it.</summary>
        public IEnumerable<string> RelativePaths() =>
            Directory.Exists(Dir) ? Index().Keys : [];

        /// <summary>Built once, by whichever thread asks first (the read-ahead reads too).</summary>
        Dictionary<string, string> Index() => LazyInitializer.EnsureInitialized(ref index, BuildIndex);

        Dictionary<string, string> BuildIndex()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(Dir, f).Replace(Path.DirectorySeparatorChar, '/');
                // A player module's archives at the top of its Data folder are mounted, not files of the game's.
                if (skipTopArchives && !rel.Contains('/') && rel.EndsWith(".nob", StringComparison.OrdinalIgnoreCase)) continue;
                map.TryAdd(rel, f);
            }
            return map;
        }
    }

    /// <summary>
    /// Finds the game's files: an explicit folder (`--game`); then Vitomir's own game folder
    /// (<see cref="GameData.DefaultGame"/>), when it finished being made (it has its manifest); then
    /// VTMR_GAME_DIR; then C:\Games\VTMR — the last two the 1.1 install itself.
    /// </summary>
    public static string? Locate(string? explicitDir = null)
    {
        foreach (var d in new[] { explicitDir, GameData.DefaultGame, Environment.GetEnvironmentVariable("VTMR_GAME_DIR"), @"C:\Games\VTMR" })
            if (!string.IsNullOrEmpty(d) && (GameData.IsLayout(d) || File.Exists(Path.Combine(d, "Resource.nob")))) return d;
        return null;
    }

    /// <summary>The 1.1 install itself (an explicit folder, else VTMR_GAME_DIR, then C:\Games\VTMR) — what a game folder is made from.</summary>
    public static string? LocateInstall(string? explicitDir = null)
    {
        // A folder named is the only one tried: one named by mistake is refused, not swapped for another.
        var candidates = !string.IsNullOrEmpty(explicitDir) ? [explicitDir]
            : new[] { Environment.GetEnvironmentVariable("VTMR_GAME_DIR"), @"C:\Games\VTMR" };
        foreach (var d in candidates)
            if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "Resource.nob")) && !GameData.IsLayout(d)) return d;
        return null;
    }

    /// <summary>
    /// Every read since the start that reached the files, and the time spent in them (slow frames): a
    /// laid-out game folder reads a loose file from the disk each time, which on Windows can cost a scan
    /// by the antivirus. A read answered from <see cref="Cache"/> is counted in <see cref="Served"/> instead.
    /// </summary>
    public static long Reads, ReadTicks;

    /// <summary>Called with each path read from the files and the milliseconds it took, when set (diagnostics only).</summary>
    public static Action<string, double>? Traced;

    /// <summary>
    /// Reads answered from <see cref="Cache"/> instead of the files (ours): a file read ahead
    /// (<see cref="Prefetcher"/>). <see cref="Reads"/> and <see cref="Traced"/> count only the reads
    /// that reached the files.
    /// </summary>
    public static long Served;

    /// <summary>Files read ahead of the game and held until it reads them (ours, FileCache.cs).</summary>
    public FileCache Cache { get; } = new();

    public bool TryRead(string path, out byte[] data)
    {
        if (Cache.TryTake(FileCache.Normal(path), out data)) { Interlocked.Increment(ref Served); return true; }
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        bool found = TryReadUntimed(path, out data);
        long spent = System.Diagnostics.Stopwatch.GetTimestamp() - began;
        Interlocked.Increment(ref Reads);
        Interlocked.Add(ref ReadTicks, spent);
        Traced?.Invoke(path, spent * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        return found;
    }

    /// <summary>A read for the read-ahead thread: the same look-up, not counted (<see cref="Prefetcher"/>).</summary>
    internal bool TryReadAhead(string path, out byte[] data) => TryReadUntimed(path, out data);

    bool TryReadUntimed(string path, out byte[] data)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        if (MayHaveText && Recooked.Kind(path) != Recooked.Form.None)
        {
            // Made once a run: a place loaded again reads the same bytes without reading the text again.
            var made = composed.GetOrAdd(path.ToLowerInvariant(), _ => TryRecooked(path, out var bytes) ? bytes : null);
            if (made is not null) { data = (byte[])made.Clone(); return true; }
        }
        int over = DataFolderCount;
        for (int i = 0; i < over; i++)
            if (loose[i].TryGet(path, out var full)) { data = File.ReadAllBytes(full); return true; }
        foreach (var m in moduleLayers)
        {
            if (m.Loose.TryGet(path, out var full)) { data = File.ReadAllBytes(full); return true; }
            foreach (var a in m.Archives)
                if (a.TryRead(path, out data)) return true;
        }
        for (int i = over; i < loose.Count; i++)
            if (loose[i].TryGet(path, out var full)) { data = File.ReadAllBytes(full); return true; }
        // The recooked folder's text data files, laid out as tables, over the original's (`Recooked`): they read the same.
        if (recooked is { } rc && TextTable.Knows(path) && rc.TryGet(path, out var laidText)) { data = File.ReadAllBytes(laidText); return true; }
        if (laidOut is not null && laidOut.TryGetValue(path, out var placed)) { data = File.ReadAllBytes(placed); return true; }
        foreach (var a in archives)
            if (a.TryRead(path, out data)) return true;
        data = [];
        return false;
    }

    public byte[] Read(string path) => TryRead(path, out var d) ? d : throw new FileNotFoundException(path);

    /// <summary>
    /// The recooked base game (ours, <see cref="Recooked"/>): the text the import made of the original's templates, scenes and camera
    /// paths, read as the base game's own over its archives and under everything else. Null when there is none.
    /// </summary>
    public string? RecookedFolder => recooked?.Dir;

    /// <summary>The recooked folder that goes with the imported data at <paramref name="root"/> (beside it), or null for any other root.</summary>
    public static string? RecookedBeside(string root)
    {
        string trimmed = Path.TrimEndingDirectorySeparator(root);
        return Path.GetFileName(trimmed).Equals(Vitomir.Platform.Setup.ImportedData.GameFolderName, StringComparison.OrdinalIgnoreCase)
               && Path.GetDirectoryName(trimmed) is { } parent ? Path.Combine(parent, Recooked.FolderName) : null;
    }
    readonly LooseFolder? recooked;

    /// <summary>
    /// Every place a file can come from, the first winning: the developer's folders, the player's modules (each its loose files, then
    /// its archives), the install's own loose files, the recooked base game, the game folder's or the install's archives.
    /// </summary>
    IEnumerable<(string Where, Func<string, byte[]?> Get, Func<string, IEnumerable<string>> Under)> Sources()
    {
        static byte[]? FromLoose(LooseFolder f, string p) => f.TryGet(p, out var full) ? File.ReadAllBytes(full) : null;
        static byte[]? FromNob(NobArchive a, string p) => a.TryRead(p, out var d) ? d : null;
        static IEnumerable<string> In(IEnumerable<string> names, string folder) =>
            names.Where(n => n.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && n.IndexOf('/', folder.Length) < 0);
        int over = DataFolderCount;
        for (int i = 0; i < over; i++) { var f = loose[i]; yield return (f.Dir, p => FromLoose(f, p), folder => f.Under(folder)); }
        foreach (var m in moduleLayers)
        {
            var l = m.Loose;
            yield return (m.Layer.Id, p => FromLoose(l, p), folder => l.Under(folder));
            foreach (var a in m.Archives) yield return (m.Layer.Id, p => FromNob(a, p), folder => In(a.Names, folder));
        }
        // The install's own loose files: a template's text is never looked for there (only a whole file is), which spares a walk of
        // its folders.
        for (int i = over; i < loose.Count; i++) { var f = loose[i]; yield return (f.Dir, p => FromLoose(f, p), _ => []); }
        if (recooked is { } r) yield return (Recooked.FolderName, p => FromLoose(r, p), folder => r.Under(folder));
        if (laidOut is not null)
            yield return (Root, p => laidOut.TryGetValue(p, out var placed) ? File.ReadAllBytes(placed) : null, _ => []);
        foreach (var a in archives) yield return (a.Path, p => FromNob(a, p), _ => []);
    }

    /// <summary>The original's bytes made from text, by path (null: no text, read as ever), once a run (<see cref="TryRecooked"/>).</summary>
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]?> composed = new();

    /// <summary>Whether any place can hold recooked text: the recooked folder, a player's module or a developer's folder.</summary>
    bool MayHaveText => recooked is not null || moduleLayers.Count > 0 || DataFolderCount > 0;

    /// <summary>
    /// **A template file, a scene or a camera path from recooked text** (<see cref="Recooked"/>), made into the original's bytes so every
    /// reader reads it as before. The places are tried in order: the first that has the file whole — the original's binary, or a scene's or
    /// a path's text — gives it. A template file is the exception, merged template by template: each template comes from the first place
    /// that has its text, and the rest from the first place that has the whole file (the original's binary, or the recooked folder), so a
    /// module that ships one template's text changes that one alone. False when no place has the text: then the file is read as it always was.
    /// </summary>
    bool TryRecooked(string path, out byte[] data)
    {
        data = [];
        var form = Recooked.Kind(path);
        if (form == Recooked.Form.Templates)
        {
            string folder = Recooked.TemplateFolder(path), orderFile = Recooked.TemplateOrder(path);
            var found = new Dictionary<string, NotTemplate>(StringComparer.OrdinalIgnoreCase);
            List<string>? order = null;
            bool anyText = false;
            foreach (var (where, get, under) in Sources())
            {
                foreach (var n in under(folder))
                {
                    if (!n.EndsWith(NotText.Extension, StringComparison.OrdinalIgnoreCase)) continue;
                    string name = NotText.NameOf(n[folder.Length..]);
                    if (found.ContainsKey(name) || get(n) is not { } text) continue;
                    found[name] = NotText.Read(RecookText.Parse(text, $"{where}: {n}"), name);
                    anyText = true;
                }
                if (get(path) is { } whole)
                {
                    if (!anyText) return false;                      // the binary is first: read as ever
                    var file = NotFile.Read(whole, path);
                    foreach (var t in file.Templates) found.TryAdd(t.Name, t);
                    order = [.. file.Templates.Select(t => t.Name)];
                    break;
                }
                if (order is null && get(orderFile) is { } listed) order = NotText.ReadOrder(listed);
            }
            if (!anyText) return false;
            var ordered = new List<NotTemplate>(found.Count);
            var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in order ?? []) if (found.TryGetValue(n, out var t) && placed.Add(n)) ordered.Add(t);
            foreach (var t in found.Values.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)) if (placed.Add(t.Name)) ordered.Add(t);
            data = NotFileWrite.Write(ordered);
            return true;
        }
        string textPath = Recooked.TextOf(path);
        foreach (var (where, get, _) in Sources())
        {
            if (get(path) is not null) return false;                 // the original's binary is first: read as ever
            if (get(textPath) is not { } text) continue;
            var parsed = RecookText.Parse(text, $"{where}: {textPath}");
            data = form == Recooked.Form.Scene
                ? NsdWrite.Write(NsdText.Read(parsed, get(Recooked.GeometryOf(path))))
                : NcpText.WriteBinary(NcpText.Read(parsed));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Where the original looks for a material or a texture named without a folder, in order.
    /// The game's start-up registers these nine folders for resource kind 1, and the
    /// open joins each to the name in turn before finally trying the name on its own.
    /// So <c>black.tga</c>, written by materials that live in <c>3d/materials/</c>, is the one in
    /// <c>materials/Prague/</c> — the only <c>black.tga</c> the game ships.
    /// </summary>
    public static readonly string[] MaterialSearchPath =
    [
        "ui/materials", "ui/icons", "materials", "3d/materials", "materials/general",
        "materials/prague", "materials/vienna", "materials/london", "materials/new_york",
    ];

    /// <summary>Reads a material or texture the way the original resolves one: the search path, then the bare name.</summary>
    public bool TryReadMaterialFile(string name, out byte[] data)
    {
        name = name.Replace('\\', '/').TrimStart('/');
        foreach (var dir in MaterialSearchPath)
            if (TryRead(dir + "/" + name, out data)) return true;
        return TryRead(name, out data);
    }

    Dictionary<string, string>? byFileName;

    /// <summary>
    /// Finds an archive entry by file name alone, in any folder (materials are often referred to by
    /// name only; the SDK says so for NAM). The first archive in load order wins.
    /// </summary>
    public string? FindByFileName(string fileName)
    {
        if (byFileName is null)
        {
            byFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in moduleLayers)                                  // the player's modules first, the last in the order first
            {
                foreach (var n in m.Loose.RelativePaths()) byFileName.TryAdd(Path.GetFileName(n), n);
                foreach (var a in m.Archives)
                    foreach (var n in a.Names) byFileName.TryAdd(Path.GetFileName(n), n);
            }
            if (manifest is not null)                                        // the archives' own entries, in their order
                foreach (var e in manifest.Files.Where(e => e.Archive is not null).OrderBy(e => e.ArchiveRank))
                    byFileName.TryAdd(Path.GetFileName(e.One), e.One);
            foreach (var a in archives)
                foreach (var n in a.Names)
                    byFileName.TryAdd(Path.GetFileName(n), n);
        }
        return byFileName.TryGetValue(Path.GetFileName(fileName), out var path) ? path : null;
    }

    /// <summary>
    /// File names under <paramref name="folder"/> (e.g. "misc/templates/"), loose files first and
    /// then the archives. Some folders — Chronicles\ among them — exist only as loose files, so
    /// listing has to cover both.
    /// </summary>
    public IEnumerable<string> List(string folder)
    {
        folder = folder.Replace('\\', '/').TrimStart('/');
        return loose.Take(DataFolderCount).SelectMany(f => f.RelativePaths())
                    .Concat(moduleLayers.SelectMany(m => m.Loose.RelativePaths().Concat(m.Archives.SelectMany(a => a.Names))))
                    .Concat(loose.Skip(DataFolderCount).SelectMany(f => f.RelativePaths()))
                    .Concat(manifest?.Files.Select(e => e.One) ?? [])
                    .Concat(archives.SelectMany(a => a.Names))
                    .Where(n => n.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Where the file 1.1 knows as <paramref name="path"/> is on disk, for what is read by path rather
    /// than by bytes (a film, handed to the player; the scripts' sources): the game folder's place for
    /// it, or the install's own loose file. Null when there is none (a file only in an archive).
    /// The <c>--data</c> folders are not consulted, as they never were for these.
    /// </summary>
    public string? FullPathOf(string path)
    {
        path = path.Replace('\\', '/').TrimStart('/');
        if (laidOut is not null) return laidOut.TryGetValue(path, out var placed) ? placed : null;
        return loose[^1].TryGet(path, out var full) ? full : null;
    }

    /// <summary>
    /// **What data this machine's game reads, in one word** (ours; the join's check, <c>Net.NetIdentity</c>): every
    /// file a read can reach — the archives' (the first in mount order holding a path winning), the install's loose files
    /// (left out what a game folder leaves out, <see cref="GameData.LeftOut"/>) or a game folder's manifest, then the
    /// <c>--data</c> folders over them — each as its path (in lower case) and its length, sorted, SHA-256 over the lot and
    /// its first sixteen hex digits kept; the files at the top of an install or a game folder (the store's read-mes and
    /// files, <c>masquerade.ini</c>) and a game folder's <c>settings\</c> left out. So an install and the game folder made from it
    /// give the same, and so do two copies of the same data, a GOG and a Steam one among them; a file added, gone or of another length gives another. A file changed in place to the same
    /// length is not seen (reading 1.2 GB at every start is not cheap); <c>--verify-data</c> sees it.
    /// </summary>
    public (string Id, int Files, long Bytes) Fingerprint()
    {
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        static string Key(string path) => path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        // Not the data: the files at the install's top (the store's read-mes, masquerade.ini, which each player's own
        // settings change) and a game folder's settings; a GOG and a Steam copy of the same data agree.
        static bool Played(string key) => key.Contains('/') && !key.StartsWith("settings/", StringComparison.Ordinal);
        if (manifest is not null)
            foreach (var e in manifest.Files)
                if (Played(Key(e.One))) sizes.TryAdd(Key(e.One), e.Size);
        foreach (var a in archives)
            foreach (var (name, length) in a.Entries) sizes.TryAdd(Key(name), length);
        for (int i = loose.Count - 1; i >= 0; i--)
        {
            bool root = laidOut is null && i == loose.Count - 1;
            foreach (var rel in loose[i].RelativePaths())
            {
                if (root && GameData.LeftOut(rel) || !Played(Key(rel))) continue;
                if (loose[i].TryGet(rel, out var full)) sizes[Key(rel)] = new FileInfo(full).Length;
            }
        }
        // The recooked base game is the data the game reads over the archives: a change to it is a change of the data
        // (its marker, which says when and where it was made, is not).
        if (recooked is { } rc)
            foreach (var rel in rc.RelativePaths())
                if (!rel.Equals(Recooked.MarkerName, StringComparison.OrdinalIgnoreCase) && rc.TryGet(rel, out var made))
                    sizes["recooked/" + Key(rel)] = new FileInfo(made).Length;
        var text = new System.Text.StringBuilder();
        foreach (var kv in sizes.OrderBy(kv => kv.Key, StringComparer.Ordinal)) text.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
        return (Convert.ToHexStringLower(hash)[..16], sizes.Count, sizes.Values.Sum());
    }

    /// <summary>
    /// Which store's 1.1 this is, as far as its install says (for the join's line): GOG (a <c>goggame-*</c> file),
    /// Steam (<c>steam_api.dll</c> or <c>steam_appid.txt</c>), or empty when neither is there (or the install is gone).
    /// </summary>
    public string Store()
    {
        string? install = laidOut is null ? Root : manifest?.Source;
        if (install is null || !Directory.Exists(install)) return "";
        try
        {
            if (Directory.EnumerateFiles(install, "goggame-*").Any()) return "GOG";
            if (File.Exists(Path.Combine(install, "steam_api.dll")) || File.Exists(Path.Combine(install, "steam_appid.txt"))) return "Steam";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return "";
    }

    /// <summary>
    /// The scripts' loose folder: the game folder's <c>scripts\source</c>, or the install's <c>Codex</c> — the SDK's sources when
    /// it holds them (the developer's source path compiles them all; the game ignores them) and a mod's files over the
    /// shipped classes.
    /// </summary>
    public string CodexFolder => IsLayout ? Path.Combine(Root, "scripts", "source") : Path.Combine(Root, "Codex");

    /// <summary>
    /// **The scripts the game runs**: 1.1's own, compiled, as it ships them — the layout's <c>scripts\compiled</c> (where
    /// <c>GameData.Place</c> puts <c>Codex.nob</c>'s classes), or the install's (and the imported data's) <c>Codex.nob</c> itself.
    /// </summary>
    public string CompiledScripts => IsLayout ? Path.Combine(Root, "scripts", "compiled") : Path.Combine(Root, "Codex.nob");

    /// <summary>The game's JDK 1.1 library (only the Legacy script level uses it).</summary>
    public string ClassesZip => IsLayout ? Path.Combine(Root, "scripts", "classes.zip") : Path.Combine(Root, "classes.zip");

    public void Dispose()
    {
        foreach (var a in archives) a.Dispose();
        foreach (var m in moduleLayers)
            foreach (var a in m.Archives) a.Dispose();
    }
}
