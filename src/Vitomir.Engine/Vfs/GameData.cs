// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitomir.Engine.Vfs;

/// <summary>
/// **Vitomir's own game folder** (ours). Vitomir ships as a folder,
/// <c>Vitomir\</c>, with the program in <c>bin\</c>; the player's game files go into <c>game\</c>
/// beside it, **unpacked out of 1.1's archives and laid out by kind** — models, animations, textures,
/// sounds, levels (<see cref="Layout"/>) — the bytes unchanged. After that Vitomir never needs the 1.1
/// install again, except to list and import 1.1's own saves, which it reads from the install the copy
/// came from while that is still there. A manifest (<see cref="ManifestName"/>) names every file: the
/// path 1.1 knows it by, where it now lives, its size and SHA-256, and the archive that held it, which
/// is everything <see cref="GameFiles"/> needs to answer exactly as the install would.
/// </summary>
public static class GameData
{
    // ---- where things are ----------------------------------------------------------------------

    /// <summary>
    /// The Vitomir folder: <c>VITOMIR_HOME</c> when set; else the folder above the program's when the
    /// program is in a folder called <c>bin</c> (the shipped layout); else the program's own (a
    /// build's output folder).
    /// </summary>
    public static string Home
    {
        get
        {
            if (Environment.GetEnvironmentVariable("VITOMIR_HOME") is { Length: > 0 } home) return Path.GetFullPath(home);
            return HomeOf(AppContext.BaseDirectory);
        }
    }

    /// <summary>
    /// Whether this is an unzipped Vitomir — the program in a <c>bin</c> folder, or <c>VITOMIR_HOME</c>
    /// set — rather than a build's output. Only then does a first start ask for the game (<c>FirstRun</c>).
    /// </summary>
    public static bool Shipped =>
        Environment.GetEnvironmentVariable("VITOMIR_HOME") is { Length: > 0 } || !Path.GetFullPath(HomeOf(AppContext.BaseDirectory))
            .Equals(Path.GetFullPath(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)), StringComparison.OrdinalIgnoreCase);

    /// <summary>The Vitomir folder for a program in <paramref name="programDir"/>: its parent when it is a <c>bin</c>, else itself.</summary>
    public static string HomeOf(string programDir)
    {
        string exe = Path.TrimEndingDirectorySeparator(programDir);
        return Path.GetFileName(exe).Equals("bin", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(exe)! : exe;
    }

    /// <summary>The game's files, laid out: <c>Vitomir\game</c>.</summary>
    public static string DefaultGame => Path.Combine(Home, "game");

    /// <summary>
    /// The compiled scripts' cache: <c>Vitomir\cache\codex</c>; under a host that gave the game a cache folder (the Classic
    /// module under the platform's program, whose program and module folders are not the player's to write), its <c>codex</c>.
    /// </summary>
    public static string CodexCache => Diagnostics.HostFolders.Cache is { } hosted ? Path.Combine(hosted, "codex") : Path.Combine(Home, "cache", "codex");

    /// <summary>The manifest, inside the game folder. A folder without one did not finish being made.</summary>
    public const string ManifestName = "vitomir-game.json";

    public static string ManifestPath(string gameDir) => Path.Combine(gameDir, ManifestName);

    /// <summary>Whether <paramref name="dir"/> is a finished game folder of Vitomir's own.</summary>
    public static bool IsLayout(string dir) => File.Exists(ManifestPath(dir));

    // ---- the layout ----------------------------------------------------------------------------

    /// <summary>
    /// **Where each of 1.1's folders goes**, by the path 1.1 knows a file by (the archives' own paths,
    /// and the install's loose folders): the longest match wins, and what matches nothing keeps its
    /// path under <c>other\</c> — so a mod's new folder is kept too. Below the folder named here, a
    /// file keeps 1.1's own sub-folders and name. The map is the one place that knows both sets of
    /// names; the conversion to Vitomir's own formats will change what is in the folders, not this.
    /// </summary>
    public static readonly (string OneOne, string Ours)[] Layout =
    [
        ("3d/models/", "models/"),
        ("3d/anims/", "animations/"),
        ("3d/motions/", "motions/"),
        ("3d/materials/", "textures/models/"),
        ("ui/materials/", "textures/interface/"),
        ("ui/icons/", "textures/icons/"),
        ("materials/", "textures/"),              // general, prague, vienna, london, new_york, and the loose Materials\
        ("ui/", "interface/"),
        ("levels/", "levels/"),
        ("scenes/", "scenes/"),
        ("chronicles/", "chronicles/"),
        ("conversations/", "conversations/"),
        ("paths/", "camera-paths/"),
        ("quests/", "quests/"),
        ("stores/", "stores/"),
        ("strings/", "text/"),
        ("misc/", "rules/"),
        ("sounds/", "sounds/"),                   // with the loose Sounds\Dialog
        ("video/", "video/"),
        ("codex/", "scripts/source/"),
        ("masquerade.ini", "settings/masquerade.ini"),
        ("classes.zip", "scripts/classes.zip"),
    ];

    /// <summary>Where the file 1.1 knows as <paramref name="oneOne"/> lives in Vitomir's layout.</summary>
    public static string Place(string oneOne)
    {
        oneOne = oneOne.Replace('\\', '/').TrimStart('/');
        foreach (var (from, to) in Layout.OrderByDescending(m => m.OneOne.Length))
            if (oneOne.StartsWith(from, StringComparison.OrdinalIgnoreCase)
                && (from.EndsWith('/') || oneOne.Length == from.Length))
                return to + oneOne[from.Length..];
        if (!oneOne.Contains('/') && oneOne.EndsWith(".class", StringComparison.OrdinalIgnoreCase))
            return "scripts/compiled/" + oneOne;                                // Codex.nob's own classes
        return "other/" + oneOne;
    }

    /// <summary><c>masquerade.ini</c> in a game folder: the layout's <c>settings\</c>, or an install's own.</summary>
    public static string OptionsIni(string gameDir) =>
        Path.Combine(gameDir, IsLayout(gameDir) ? Path.Combine("settings", "masquerade.ini") : "masquerade.ini");

    // ---- what stays out ------------------------------------------------------------------------

    /// <summary>
    /// What a copy leaves out of the install: the programs and libraries (Vitomir runs none of 1.1's
    /// code), the installer's and the store's own files, the manual and the read-mes, 1.1's screenshots,
    /// and four folders — the saves (read from the install itself, <see cref="OriginalInstall"/>), the
    /// editor's SDK, the store's extras and the sound driver. Everything else is taken, whatever it is.
    /// </summary>
    public static readonly string[] LeftOutFolders = ["SaveGames", "NodSDK", "Extras", "Miles"];

    static readonly HashSet<string> LeftOutExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".isu", ".sdb", ".bat", ".lnk", ".url", ".ico", ".inf", ".pdf", ".rtf", ".partial",
    };

    static readonly string[] LeftOutNames =
        ["EULA.txt", "goglog.ini", "dxcfg.ini", "webcache.zip", "texture.dat", "kver.pub"];

    /// <summary>Whether a file of the install, by its path relative to the install, stays out.</summary>
    public static bool LeftOut(string relative)
    {
        relative = relative.Replace('\\', '/');
        int slash = relative.IndexOf('/');
        if (slash > 0 && LeftOutFolders.Contains(relative[..slash], StringComparer.OrdinalIgnoreCase)) return true;
        string name = Path.GetFileName(relative);
        if (slash < 0)
        {
            if (LeftOutNames.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("goggame-", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("SHOT", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith(".nob", StringComparison.OrdinalIgnoreCase)) return true;     // unpacked, not copied
        }
        return LeftOutExtensions.Contains(Path.GetExtension(name));
    }

    // ---- the manifest ---------------------------------------------------------------------------

    public sealed class Manifest
    {
        /// <summary>1: this layout.</summary>
        public int Format { get; set; } = 1;
        public DateTime MadeUtc { get; set; }
        /// <summary>The Vitomir that made it.</summary>
        public string MadeBy { get; set; } = "";
        /// <summary>The install it was made from, for 1.1's saves (<see cref="OriginalInstall"/>).</summary>
        public string Source { get; set; } = "";
        /// <summary>The archives unpacked, in the order 1.1 mounts them (<see cref="GameFiles.MountOrder"/>).</summary>
        public List<string> Archives { get; set; } = [];
        /// <summary>
        /// Every file, in the order a listing gives them: the install's loose files first, then each
        /// archive's in mount order and the archive's own order.
        /// </summary>
        public List<Entry> Files { get; set; } = [];
        /// <summary>What the install had that was left out, for the reader's benefit.</summary>
        public List<string> LeftOut { get; set; } = [];

        [JsonIgnore] public long Bytes => Files.Sum(f => f.Size);
    }

    public sealed class Entry
    {
        /// <summary>The path 1.1 knows it by, as the archive or the install spells it.</summary>
        public string One { get; set; } = "";
        /// <summary>Where it lives in the layout, relative to the game folder, forward slashes.</summary>
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public DateTime WrittenUtc { get; set; }
        public string Sha256 { get; set; } = "";
        /// <summary>
        /// The first archive (in mount order) that holds this path, and where in 1.1's archive order —
        /// even when the bytes came from a loose file of the install, which wins over an archive as it
        /// does in 1.1. A lookup by file name alone sees only these (<see cref="GameFiles.FindByFileName"/>).
        /// </summary>
        [JsonPropertyName("arc")] public string? Archive { get; set; }
        [JsonPropertyName("rank")] public int? ArchiveRank { get; set; }
        /// <summary>The bytes are the install's loose file, not an archive's.</summary>
        public bool Loose { get; set; }
    }

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The manifest of the game folder at <paramref name="gameDir"/>, or null when there is none (or it cannot be read).</summary>
    public static Manifest? ReadManifest(string gameDir)
    {
        string path = ManifestPath(gameDir);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), Json); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    static void WriteManifest(string gameDir, Manifest m)
    {
        string path = ManifestPath(gameDir), temp = path + ".writing";
        File.WriteAllText(temp, JsonSerializer.Serialize(m, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// The 1.1 install a game folder stands for, where 1.1's own saves are: the folder itself when it
    /// is an install, the one the layout was made from when that is still there, or null. The base
    /// game's data the platform's setup imported (`Vitomir.Platform.Setup.ImportedData`: the
    /// install's own shape, with a marker naming the install it came from) stands for that install
    /// while it is there, so the load screen still offers 1.1's own saves.
    /// </summary>
    public static string? OriginalInstall(string gameDir)
    {
        if (!IsLayout(gameDir)) return Vitomir.Platform.Setup.ImportedData.SourceOf(gameDir) ?? gameDir;
        return ReadManifest(gameDir) is { Source.Length: > 0 } m && Directory.Exists(m.Source) ? m.Source : null;
    }

    // ---- making it --------------------------------------------------------------------------------

    /// <summary>What a run did, for the report.</summary>
    public sealed record Report(int Written, int Kept, int SetAside, long BytesWritten, Manifest Manifest, List<string> Problems)
    {
        public bool Ok => Problems.Count == 0;
    }

    /// <summary>One file to be laid out: where 1.1 has it and how to read it.</summary>
    sealed record Wanted(string One, long Size, DateTime WrittenUtc, Func<Stream> Open, bool Loose, string? Archive, int? Rank)
    {
        public string Path { get; set; } = "";
    }

    /// <summary>
    /// **Lays the install at <paramref name="source"/> out into <paramref name="gameDir"/>**, and checks
    /// every file byte for byte against the install before the manifest is written. The install is
    /// only read. Every archive the install mounts (<see cref="GameFiles.MountOrder"/>) is unpacked, and
    /// every loose file of the install copied; where both have a path, the loose file wins and the first
    /// archive in mount order is the one recorded for it — the install's own order of look-ups
    /// (`GameFiles`). Each file goes to a <c>.partial</c> beside its place and is moved in once whole,
    /// with its write time, so a run that stops half way leaves no half file under a real name, and a
    /// run again writes only what is not there at the same size and time. A file the folder has that
    /// the install does not is **set aside** beside it (<c>set-aside\&lt;when&gt;\</c>), never deleted.
    /// The manifest goes last; a folder without one is one that did not finish.
    /// </summary>
    public static Report Make(string source, string gameDir, Action<string>? say = null, CancellationToken cancel = default)
    {
        say ??= _ => { };
        source = Path.GetFullPath(source);
        gameDir = Path.GetFullPath(gameDir);
        if (IsLayout(source))
            return Fail($"{source} is a game folder of Vitomir's own, not a VTMR install");
        if (!File.Exists(Path.Combine(source, "Resource.nob")))
            return Fail($"{source} is not a VTMR install: it has no Resource.nob");
        if (Inside(gameDir, source) || Inside(source, gameDir))
            return Fail($"the game folder ({gameDir}) and the install ({source}) cannot be inside one another");

        var archives = new List<ZipArchive>();
        try
        {
            // What the install has, in the order a listing gives it: loose files, then the archives.
            var wanted = new List<Wanted>();
            var byOne = new Dictionary<string, Wanted>(StringComparer.OrdinalIgnoreCase);
            var leftOut = new List<string>();
            foreach (var full in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
                                          .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string rel = Path.GetRelativePath(source, full).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.EndsWith(".nob", StringComparison.OrdinalIgnoreCase) && !rel.Contains('/')) continue;   // unpacked below
                if (LeftOut(rel)) { leftOut.Add(rel); continue; }
                var info = new FileInfo(full);
                var w = new Wanted(rel, info.Length, info.LastWriteTimeUtc, () => new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20), true, null, null);
                wanted.Add(w);
                byOne[rel] = w;
            }
            var mounted = new List<string>();
            int rank = 0;
            foreach (var nob in GameFiles.MountOrder(source))
            {
                if (mounted.Count == GameFiles.MaxArchives) break;
                ZipArchive zip;
                try { zip = ZipFile.OpenRead(nob); }
                catch (InvalidDataException) { continue; }                     // not an archive: not mounted
                archives.Add(zip);
                string name = Path.GetFileName(nob);
                mounted.Add(name);
                foreach (var e in zip.Entries)
                {
                    if (string.IsNullOrEmpty(e.Name)) continue;
                    string one = e.FullName.Replace('\\', '/').TrimStart('/');
                    int r = rank++;
                    if (LeftOut(one)) { leftOut.Add($"{name}:{one}"); continue; }   // Codex.nob's own javac.exe and the like
                    if (byOne.TryGetValue(one, out var had))
                    {
                        // A loose file or an earlier archive already has it: the first archive to hold it is the one named.
                        if (had.Archive is null) byOne[one] = wanted[wanted.IndexOf(had)] = had with { Archive = name, Rank = r };
                        continue;
                    }
                    var entry = e;
                    var w = new Wanted(one, e.Length, e.LastWriteTime.UtcDateTime, () => entry.Open(), false, name, r);
                    wanted.Add(w);
                    byOne[one] = w;
                }
            }

            // Where each goes; a place two paths would share goes to the second under other\.
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var w in wanted)
            {
                string p = Place(w.One);
                if (!taken.Add(p)) { p = "other/" + w.One; taken.Add(p); }
                w.Path = p;
            }

            long total = wanted.Sum(w => w.Size);
            Directory.CreateDirectory(gameDir);
            long present = wanted.Sum(w => new FileInfo(Target(gameDir, w.Path)) is { Exists: true } f && f.Length == w.Size ? w.Size : 0);
            long free = new DriveInfo(Path.GetPathRoot(gameDir)!).AvailableFreeSpace;
            if (total - present > free - (64L << 20))
                return Fail($"not enough room: the game folder needs {Mb(total - present)} more and {Path.GetPathRoot(gameDir)} has {Mb(free)} free");
            say($"laying out {wanted.Count} files ({Mb(total)}) from {source} ({mounted.Count} archives unpacked) into {gameDir}; {leftOut.Count} left out");

            // An earlier manifest goes first: from here until the new one is written, the folder is unfinished.
            File.Delete(ManifestPath(gameDir));

            int written = 0, kept = 0;
            long bytesWritten = 0, done = 0;
            int lastPercent = -1;
            foreach (var w in wanted)
            {
                cancel.ThrowIfCancellationRequested();
                string target = Target(gameDir, w.Path);
                var have = new FileInfo(target);
                if (have.Exists && have.Length == w.Size && have.LastWriteTimeUtc == w.WrittenUtc) kept++;
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string partial = target + ".partial";
                    using (var from = w.Open())
                    using (var to = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                    {
                        from.CopyTo(to, 1 << 20);
                        to.Flush(flushToDisk: true);
                    }
                    File.SetLastWriteTimeUtc(partial, w.WrittenUtc);
                    File.Move(partial, target, overwrite: true);
                    written++;
                    bytesWritten += w.Size;
                }
                done += w.Size;
                int percent = total == 0 ? 100 : (int)(done * 100 / total);
                if (percent / 10 != lastPercent / 10) { say($"  {percent}%"); lastPercent = percent; }
            }

            // Anything the folder holds that the install does not: set aside, not deleted.
            var keep = new HashSet<string>(wanted.Select(w => w.Path), StringComparer.OrdinalIgnoreCase) { ManifestName };
            int setAsideCount = 0;
            string setAside = Path.Combine(Path.GetDirectoryName(gameDir)!, "set-aside", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            foreach (var full in Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories).ToList())
            {
                string rel = Path.GetRelativePath(gameDir, full).Replace(Path.DirectorySeparatorChar, '/');
                if (keep.Contains(rel)) continue;
                if (rel.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".writing", StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(full);
                    continue;
                }
                string to = Target(setAside, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(full, to);
                say($"  set aside: {rel}");
                setAsideCount++;
            }

            // Every file checked against the install byte for byte (by hash), and the hash kept.
            say("checking every file against the install");
            var problems = new List<string>();
            var entries = new List<Entry>(wanted.Count);
            foreach (var w in wanted)
            {
                cancel.ThrowIfCancellationRequested();
                string mine = Hash(Target(gameDir, w.Path));
                string theirs;
                using (var s = w.Open()) theirs = Hash(s);
                if (mine != theirs) problems.Add($"{w.One} ({w.Path}): differs from the install");
                entries.Add(new Entry
                {
                    One = w.One, Path = w.Path, Size = w.Size, WrittenUtc = w.WrittenUtc, Sha256 = mine,
                    Archive = w.Archive, ArchiveRank = w.Rank, Loose = w.Loose,
                });
            }
            var manifest = new Manifest
            {
                MadeUtc = DateTime.UtcNow, MadeBy = Ui.FrontEnd.Version, Source = source, Archives = mounted,
                Files = entries, LeftOut = leftOut,
            };
            if (problems.Count == 0) WriteManifest(gameDir, manifest);
            return new Report(written, kept, setAsideCount, bytesWritten, manifest, problems);
        }
        finally
        {
            foreach (var z in archives) z.Dispose();
        }

        Report Fail(string why) => new(0, 0, 0, 0, new Manifest { Source = source }, [why]);
    }

    // ---- checking it ------------------------------------------------------------------------------

    /// <summary>
    /// Whether the game folder at <paramref name="gameDir"/> is as its manifest says: every file there
    /// at its size — and, with <paramref name="hashes"/>, its SHA-256. Files the manifest does not name
    /// are counted, not held against it. Null when there is no manifest.
    /// </summary>
    public static CheckResult? Check(string gameDir, bool hashes = false) =>
        ReadManifest(gameDir) is { } m ? Check(gameDir, m, hashes) : null;

    public static CheckResult Check(string gameDir, Manifest m, bool hashes = false)
    {
        var missing = new List<string>();
        var changed = new List<string>();
        foreach (var e in m.Files)
        {
            var f = new FileInfo(Target(gameDir, e.Path));
            if (!f.Exists) missing.Add(e.Path);
            else if (f.Length != e.Size || hashes && Hash(f.FullName) != e.Sha256) changed.Add(e.Path);
        }
        var named = new HashSet<string>(m.Files.Select(e => e.Path), StringComparer.OrdinalIgnoreCase) { ManifestName };
        var extra = hashes && Directory.Exists(gameDir)
            ? Directory.EnumerateFiles(gameDir, "*", SearchOption.AllDirectories)
                       .Select(f => Path.GetRelativePath(gameDir, f).Replace(Path.DirectorySeparatorChar, '/'))
                       .Where(r => !named.Contains(r)).Order(StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        return new CheckResult(m, missing, changed, extra);
    }

    public sealed record CheckResult(Manifest Manifest, List<string> Missing, List<string> Changed, List<string> Extra)
    {
        public bool Whole => Missing.Count == 0 && Changed.Count == 0;
    }

    // ---- helpers ------------------------------------------------------------------------------

    public static string Target(string gameDir, string relative) => Path.Combine(gameDir, relative.Replace('/', Path.DirectorySeparatorChar));

    static string Hash(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Hash(s);
    }

    static string Hash(Stream s) => Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();

    static bool Inside(string inner, string outer)
    {
        string o = Path.TrimEndingDirectorySeparator(outer) + Path.DirectorySeparatorChar;
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return (Path.TrimEndingDirectorySeparator(inner) + Path.DirectorySeparatorChar).StartsWith(o, cmp);
    }

    static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0} MB";
}
