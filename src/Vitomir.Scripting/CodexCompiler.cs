// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Compiles the game's Java scripts, in process, with no JDK on the machine.
//
// What runs is 1.1's own compiled scripts, Codex.nob's classes, with their natives bridged
// (CodexClasses), and over them an overlay of whatever the player's Codex folder holds that is not the
// SDK's own sources (a mod's): changed or new sources compiled against the shipped classes, loose
// classes copied, searched first as 1.1's search path searches the loose folders before the archive.
// The source path — the whole Codex tree compiled, the bridge generated from it — is a developer's
// (--script-sources), kept for comparison. The Eclipse batch compiler (ECJ) is converted to a .NET
// assembly by IKVM at build time, so the engine can call it directly; every output is cached under a
// hash of what went in, so an unchanged install loads without compiling anything.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Vitomir.Scripting;

/// <summary>Which Java the scripts are compiled as.</summary>
public enum JavaLevel
{
    /// <summary>
    /// Java 8 against the runtime's own class library. The shipped scripts all compile and run
    /// here (measured: 467 sources, 44/44 scene scripts, the same engine calls as Legacy), and it
    /// is the only level at which a mod can mix the game's 1998 code with modern code in one
    /// build — which is the reason it is the default.
    /// </summary>
    Modern,
    /// <summary>
    /// Java 1.3 against the game's own <c>classes.zip</c> — the JDK 1.1 library the scripts were
    /// written for. Faster (a much smaller library to scan) and the strictest fidelity, kept for
    /// anything that will not compile modern.
    /// </summary>
    Legacy,
}

/// <param name="OutputDir">The classes the loader searches first, and where a recompile writes: the source path's whole build,
/// or the shipped classes' overlay (empty when the Codex folder holds nothing but the SDK's own sources).</param>
/// <param name="Classes">How many .class files the build has (the base's and the overlay's).</param>
/// <param name="Sources">How many sources went in (the source path), or how many classes (the shipped ones and our two).</param>
/// <param name="Notes">JDK 1.1 compatibility fixes applied, if any (the shipped classes' every start; the source path's when it compiles).</param>
/// <param name="FromClasses">Made from the shipped classes (`Codex.nob`) rather than compiled from sources.</param>
/// <param name="BaseDir">The shipped classes, bridged, searched after <paramref name="OutputDir"/>; null for the source path.</param>
/// <param name="Overlay">The classes the overlay puts over the shipped ones, each with where it came from.</param>
/// <param name="SdkIgnored">How many of the Codex folder's files are the SDK's own sources, unchanged, and so ignored.</param>
public sealed record CodexBuild(string OutputDir, int Classes, int Sources, int Natives,
                                long CompileMs, bool CacheHit, IReadOnlyList<string> Notes, bool FromClasses = false,
                                string? BaseDir = null, IReadOnlyList<OverlayClass>? Overlay = null, int SdkIgnored = 0)
{
    /// <summary>How a note of a file the overlay left out begins (a second of one class's name; the copy-protection hook).</summary>
    public const string LeftOutNote = "the Codex folder's ";

    /// <summary>The folders the loader searches, in order: the overlay (or the source path's build), then the shipped classes.</summary>
    public IReadOnlyList<string> SearchPath => BaseDir is null ? [OutputDir] : [OutputDir, BaseDir];

    /// <summary>
    /// What a start says of the build after its <c>scripts:</c> line: each 1.1 compat note, and for the shipped classes
    /// the overlay — which classes came from the Codex folder, by name and file — and the SDK's own sources ignored.
    /// </summary>
    public IEnumerable<string> Lines()
    {
        foreach (var note in Notes)
            yield return (note.StartsWith(LeftOutNote, StringComparison.Ordinal) ? "  overlay: " : "  1.1 compat: ") + note;
        if (!FromClasses) yield break;
        var over = Overlay ?? [];
        string sdk = $"{SdkIgnored} of the Codex folder's files are the SDK's own sources, unchanged (the setup's manifest knows each): ignored";
        if (over.Count == 0)
            yield return SdkIgnored == 0 ? "  overlay: none (no Codex folder, or nothing in it: the shipped classes run as they are)"
                                         : $"  overlay: none ({sdk}, the shipped classes run as they are)";
        else
        {
            yield return $"  overlay: {over.Count} {(over.Count == 1 ? "class" : "classes")} from the Codex folder over the shipped ones: "
                         + string.Join(", ", over.Select(o => $"{o.Name} ({o.From})"));
            if (SdkIgnored > 0) yield return $"  {sdk}";
        }
    }
}

/// <summary>One class of the overlay: its name, and the file of the Codex folder it came from (a source compiled, or a loose class).</summary>
public sealed record OverlayClass(string Name, string From);

/// <summary>
/// Gathers, compiles and caches the player's Codex scripts. One of these per install; the engine
/// keeps it for the life of the process.
/// </summary>
public sealed class CodexCompiler
{
    readonly string codexDir, classesZipPath, cacheRoot;
    /// <summary>The shipped classes this runs (`Codex.nob`, or a folder of them), or null for the source path.</summary>
    readonly string? shipped;

    public JavaLevel Level { get; }
    /// <summary>The last successful build, or null before <see cref="Build"/> has run.</summary>
    public CodexBuild? Current { get; private set; }

    /// <summary>The source path over an install's <c>Codex</c> folder (a developer's: <see cref="SourcesSwitch"/>, and the tests that compare).</summary>
    public CodexCompiler(string gameDir, string cacheRoot, JavaLevel level = JavaLevel.Modern)
        : this(Path.Combine(gameDir, "Codex"), Path.Combine(gameDir, "classes.zip"), cacheRoot, level) { }

    /// <summary>
    /// **The scripts a game runs**: the shipped classes of a game folder or an install
    /// (`GameFiles.CompiledScripts`: `Codex.nob`, or the layout's <c>scripts\compiled</c>), bridged, with the overlay of its Codex folder
    /// (`GameFiles.CodexFolder`) — what 1.1 runs. <paramref name="sources"/> (<see cref="SourcesSwitch"/>, a developer's) or
    /// <see cref="SourcesVariable"/> (the suites' seam) takes the source path instead: the whole Codex folder compiled.
    /// </summary>
    public static CodexCompiler For(Vitomir.Engine.Vfs.GameFiles files, string cacheRoot, bool sources = false, JavaLevel level = JavaLevel.Modern) =>
        sources || SourcesBySeam ? new(files.CodexFolder, files.ClassesZip, cacheRoot, level)
                                 : FromClasses(files.CompiledScripts, files.CodexFolder, files.ClassesZip, cacheRoot, level);

    /// <summary>The same for a 1.1 install's folder (its <c>Codex.nob</c> and <c>Codex</c>), as the tests name one.</summary>
    public static CodexCompiler ForInstall(string gameDir, string cacheRoot, bool sources = false, JavaLevel level = JavaLevel.Modern) =>
        sources || SourcesBySeam ? new(gameDir, cacheRoot, level)
                                 : FromClasses(Path.Combine(gameDir, "Codex.nob"), Path.Combine(gameDir, "Codex"), Path.Combine(gameDir, "classes.zip"), cacheRoot, level);

    /// <summary>
    /// **The developer's switch for the source path**: <c>--script-sources</c> compiles the whole Codex folder (the SDK's
    /// sources, the bridge generated from them) instead of running the shipped classes, for comparison. Through
    /// <c>vitomir-platform --game classic</c> it is a developer's (with <c>--developer</c>: the module's manifest does not declare it).
    /// </summary>
    public const string SourcesSwitch = "--script-sources";

    /// <summary>The suites' seam for the source path: <c>VITOMIR_SCRIPT_SOURCES=1</c>, which the programs a test starts inherit.</summary>
    public const string SourcesVariable = "VITOMIR_SCRIPT_SOURCES";

    /// <summary>Whether <see cref="SourcesVariable"/> asks for the source path.</summary>
    public static bool SourcesBySeam =>
        Environment.GetEnvironmentVariable(SourcesVariable) is { Length: > 0 } v && v != "0";

    /// <summary>The shipped classes of <paramref name="archiveOrFolder"/>, bridged into the cache, with the overlay of <paramref name="codexDir"/> (which need not exist).</summary>
    public static CodexCompiler FromClasses(string archiveOrFolder, string codexDir, string classesZip, string cacheRoot, JavaLevel level = JavaLevel.Modern) =>
        new(codexDir, classesZip, cacheRoot, level, archiveOrFolder);

    CodexCompiler(string codexDir, string classesZip, string cacheRoot, JavaLevel level, string shipped)
    {
        this.codexDir = codexDir;
        classesZipPath = classesZip;
        this.cacheRoot = cacheRoot;
        Level = level;
        this.shipped = shipped;
        if (!File.Exists(shipped) && !Directory.Exists(shipped)) throw new FileNotFoundException($"no shipped classes in {shipped}", shipped);
    }

    public CodexCompiler(string codexDir, string classesZip, string cacheRoot, JavaLevel level)
    {
        shipped = null;
        this.codexDir = codexDir;
        classesZipPath = classesZip;
        this.cacheRoot = cacheRoot;
        Level = level;
        if (!Directory.Exists(codexDir)) throw new DirectoryNotFoundException($"no Codex scripts in {codexDir}");
    }

    /// <summary>
    /// Writes our own Java — the host the engine calls scripts through — out of the assembly and
    /// into the cache, next to the generated bridge.
    /// </summary>
    static string WriteHost(string dir)
    {
        Directory.CreateDirectory(dir);
        var asm = typeof(CodexCompiler).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".java", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var path = Path.Combine(dir, Path.GetFileName(name.Replace('/', Path.DirectorySeparatorChar)));
            var text = reader.ReadToEnd();
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
        }
        return dir;
    }

    string SystemDir => Path.Combine(codexDir, "System");
    string ClassesZip => classesZipPath;

    /// <summary>ECJ's switches for this level. The bridge and the scripts share them.</summary>
    List<string> Switches() => Level == JavaLevel.Modern
        ? ["-source", "1.8", "-target", "1.8", "-encoding", "ISO-8859-1", "-nowarn", "-g:lines,source"]
        : ["-source", "1.3", "-target", "1.3", "-encoding", "ISO-8859-1", "-nowarn", "-g:lines,source",
           "-bootclasspath", Path.GetFullPath(ClassesZip)];

    /// <summary>
    /// Compiles the whole tree, or reports a cache hit. <paramref name="force"/> recompiles even
    /// when the cache is good, which is what a "reload scripts" command would do.
    /// </summary>
    public CodexBuild Build(bool force = false)
    {
        if (Level == JavaLevel.Legacy && !File.Exists(ClassesZip))
            throw new FileNotFoundException($"Legacy needs the game's classes.zip", ClassesZip);
        // **One build at a time in a cache** (ours): the bridge and the host are written out afresh by every
        // process, then hashed and compiled, and a build that finds no marker deletes the folder it is about to fill.
        // Four replays started together by `--replay-folder` did all of that at once, and one of them read a
        // bridge another was writing: its scripts did not compile, and its game ran with none (a recording,
        // play-20261006-093525 diverged at its first game frame, "scripts … replayed (none)"). The lock is
        // the cache's own file; the others wait, then find the marker and load the cache.
        using var held = CacheLock(cacheRoot);
        if (shipped is not null) return Current = BuildFromClasses(force);

        // The bridge is generated from the player's own sources, into a scratch folder, before the
        // hash is taken: its content is part of what we are compiling.
        var bridgeDir = Path.Combine(cacheRoot, "_bridge");
        int natives = CodexBridge.Generate(SystemDir, bridgeDir);
        var hostDir = WriteHost(Path.Combine(cacheRoot, "_host"));

        var sources = Gather(bridgeDir, hostDir);
        var switches = Switches();
        var key = Hash(sources, switches);
        var outDir = Path.GetFullPath(Path.Combine(cacheRoot, key));
        var marker = Path.Combine(outDir, ".complete");

        if (force && Directory.Exists(outDir)) Directory.Delete(outDir, true);
        if (File.Exists(marker))
            return Current = new CodexBuild(outDir, Directory.GetFiles(outDir, "*.class").Length,
                                            sources.Count, natives, 0, true, []);

        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        // Compile. If the only errors are JDK 1.1 leniencies we know how to neutralise, fix them in
        // shadow copies — never in the player's files — and compile again.
        var notes = new List<string>();
        long total = 0; bool ok = false; string errors = "";
        for (int attempt = 1; attempt <= 3 && !ok; attempt++)
        {
            var argv = new List<string>(switches) { "-d", outDir };
            argv.AddRange(sources);
            (ok, errors, long ms) = Run(argv);
            total += ms;
            if (ok) break;
            var fixes = Compat.FixUninitializedLocals(errors, sources, Path.Combine(outDir, "_compat"));
            if (fixes.Count == 0) break;
            notes.AddRange(fixes);
        }
        if (!ok) throw new CodexCompileException(errors);

        File.WriteAllText(marker,
            "# Vitomir script cache. Delete this file (or the folder) to force a full recompile.\n" +
            "# The cache is keyed by the hash of the player's sources; this only records the last build.\n" +
            $"{DateTime.UtcNow:o} {Level} ecj {total} ms\n");
        return Current = new CodexBuild(outDir, Directory.GetFiles(outDir, "*.class").Length,
                                        sources.Count, natives, total, false, notes);
    }

    // ---- the shipped classes ---------------------------------------------------------------------------------------

    /// <summary>The base's cache key's version: a change in what the base holds or how it is made is a new one, so old caches miss.</summary>
    const string BaseFormat = "classes-3";

    /// <summary>
    /// The shipped classes, the `Codex*` ones bridged and every one checked for a local read unassigned (<see cref="CodexClasses.Rewrite"/>),
    /// into a cache folder keyed by every class's bytes, with our two Java files — the bridge's dispatcher and the host — compiled
    /// beside them by ECJ against them (the base); then the overlay of the Codex folder over it (<see cref="BuildOverlay"/>). The
    /// copy-protection hook stays out, as the source path leaves its source out.
    /// </summary>
    CodexBuild BuildFromClasses(bool force)
    {
        var sw = Stopwatch.StartNew();
        var classes = CodexClasses.Read(shipped!);
        classes.Remove(Path.GetFileNameWithoutExtension(CodexBridge.Excluded));
        var srcDir = Path.Combine(cacheRoot, "_classes-src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "EngineBridge.java"), CodexBridge.EngineBridgeSource, Encoding.Latin1);
        var hostDir = WriteHost(Path.Combine(cacheRoot, "_host"));
        var ours = new List<string> { Path.GetFullPath(Path.Combine(srcDir, "EngineBridge.java")) };
        ours.AddRange(Directory.GetFiles(hostDir, "*.java").Select(Path.GetFullPath).Order(StringComparer.Ordinal));
        var switches = Switches();

        string key;
        using (var sha = SHA256.Create())
        {
            foreach (var (name, bytes) in classes)
            {
                var n = Encoding.UTF8.GetBytes(name + "\0");
                sha.TransformBlock(n, 0, n.Length, null, 0);
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
            foreach (var f in ours) { var d = File.ReadAllBytes(f); sha.TransformBlock(d, 0, d.Length, null, 0); }
            var tail = Encoding.UTF8.GetBytes(BaseFormat + "|ecj-4.6.1|" + string.Join("|", switches.Where(a => !a.Contains(Path.DirectorySeparatorChar))));
            sha.TransformFinalBlock(tail, 0, tail.Length);
            key = "c" + Convert.ToHexString(sha.Hash!)[..15].ToLowerInvariant();
        }
        var baseDir = Path.GetFullPath(Path.Combine(cacheRoot, key));
        var marker = Path.Combine(baseDir, ".complete");
        if (force && Directory.Exists(baseDir)) Directory.Delete(baseDir, true);

        int natives;
        var notes = new List<string>();
        bool hit = File.Exists(marker);
        long ms = 0;
        if (hit)
        {
            // The marker keeps what the build said: its natives and its 1.1 compat notes, said again at every start.
            var lines = File.ReadAllLines(marker);
            natives = int.Parse(lines.First(l => l.StartsWith("natives ", StringComparison.Ordinal))[8..], System.Globalization.CultureInfo.InvariantCulture);
            notes.AddRange(lines.Where(l => l.StartsWith("note ", StringComparison.Ordinal)).Select(l => l[5..]));
        }
        else
        {
            if (Directory.Exists(baseDir)) Directory.Delete(baseDir, true);
            Directory.CreateDirectory(baseDir);
            natives = 0;
            foreach (var (name, bytes) in classes)
            {
                File.WriteAllBytes(Path.Combine(baseDir, name + ".class"), CodexClasses.Rewrite(name, bytes, notes, out int n));
                natives += n;
            }
            long bridgeMs = sw.ElapsedMilliseconds;
            var argv = new List<string>(switches) { "-classpath", baseDir, "-d", baseDir };
            argv.AddRange(ours);
            var (ok, errors, ecjMs) = Run(argv);
            if (!ok) throw new CodexCompileException(errors);
            ms = bridgeMs + ecjMs;
            File.WriteAllLines(marker, [
                "# Vitomir script cache: the shipped classes (Codex.nob's), bridged. Delete this file (or the folder) to make it again.",
                $"# {DateTime.UtcNow:o} {Level} bridged in {bridgeMs} ms, ecj {ecjMs} ms",
                $"natives {natives}",
                .. notes.Select(n => "note " + n.Replace('\n', ' ')),
            ]);
        }

        var overlay = BuildOverlay(baseDir, key, force, notes, out bool overlayHit, out long overlayMs);
        int count = Directory.GetFiles(baseDir, "*.class").Length + overlay.Classes.Count;
        return new CodexBuild(overlay.Dir, count, classes.Count + ours.Count, natives, ms + overlayMs, hit && overlayHit, notes, true,
                              baseDir, overlay.Classes, overlay.SdkIgnored);
    }

    // ---- the overlay ------------------------------------------------------------------------------------------

    /// <summary>A file of the Codex folder that is not the SDK's own: its path in the folder (forward slashes) and on the disk.</summary>
    sealed record ModFile(string Rel, string Full)
    {
        public string Class => Path.GetFileNameWithoutExtension(Full);
        public bool IsSource => Full.EndsWith(".java", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>1.1's search path's loose folders, in its order (<c>codex;codex\SP;codex\MP;codex\system</c>).</summary>
    static readonly string[] SearchFolders = ["", "SP", "MP", "System"];

    /// <summary>
    /// **The SDK's own sources**: the setup's manifest's <c>scripts</c> part, each file by its path in the Codex folder (lower
    /// case, forward slashes) and its SHA-256. A source of the Codex folder that is one of them, byte for byte, is the SDK's
    /// (Nihilistic's revision of the tree the shipped classes were compiled from) and is ignored: the shipped class wins.
    /// </summary>
    static readonly Lazy<IReadOnlyDictionary<string, string>> sdk = new(() =>
        Vitomir.Platform.Setup.DataManifest.Builtin.Of(Vitomir.Platform.Setup.DataManifest.Scripts)
            .ToDictionary(f => f.Path[(f.Path.IndexOf('/') + 1)..].ToLowerInvariant(), f => f.Sha256));

    /// <summary>The SDK's own sources, by path (<see cref="sdk"/>); a test's own set may stand in (<see cref="SdkSourcesForTests"/>).</summary>
    static IReadOnlyDictionary<string, string> Sdk => SdkSourcesForTests ?? sdk.Value;

    /// <summary>The tests' seam: another set of the SDK's sources (path in the Codex folder → SHA-256) than the manifest's.</summary>
    public static IReadOnlyDictionary<string, string>? SdkSourcesForTests { get; set; }

    /// <summary>Whether a file of the Codex folder (by its path in it) is one of the SDK's own sources, byte for byte.</summary>
    static bool IsSdkOwn(string rel, string full) =>
        Sdk.TryGetValue(rel.ToLowerInvariant(), out var sha) && Vitomir.Platform.Setup.DataManifest.Sha256Of(full) == sha;

    /// <summary>
    /// What the Codex folder holds that is not the SDK's own: each <c>.java</c> and <c>.class</c> of its four folders, in 1.1's search
    /// order, the first of a name taken (a source and a loose class of one name: the source, as a compile on load would make it);
    /// the copy-protection hook never. <paramref name="sdkIgnored"/> counts the SDK's own sources passed over.
    /// </summary>
    List<ModFile> ModFiles(out int sdkIgnored, out List<string> passedOver)
    {
        sdkIgnored = 0;
        passedOver = [];
        var mods = new List<ModFile>();
        var taken = new Dictionary<string, ModFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var sub in SearchFolders)
        {
            var dir = sub.Length == 0 ? codexDir : Path.Combine(codexDir, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (var full in Directory.GetFiles(dir).Order(StringComparer.OrdinalIgnoreCase))
            {
                bool source = full.EndsWith(".java", StringComparison.OrdinalIgnoreCase);
                if (!source && !full.EndsWith(".class", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = (sub.Length == 0 ? "" : sub + "/") + Path.GetFileName(full);
                if (source && IsSdkOwn(rel, full)) { sdkIgnored++; continue; }
                var file = new ModFile(rel, Path.GetFullPath(full));
                if (file.Class.Equals(Path.GetFileNameWithoutExtension(CodexBridge.Excluded), StringComparison.OrdinalIgnoreCase)) { passedOver.Add($"{rel}: the copy-protection hook, never run"); continue; }
                if (taken.TryGetValue(file.Class, out var had))
                {
                    if (had.IsSource || !file.IsSource) { passedOver.Add($"{rel}: {had.Rel} comes first"); continue; }
                    mods.Remove(had);
                    passedOver.Add($"{had.Rel}: {rel} is its source");
                }
                taken[file.Class] = file;
                mods.Add(file);
            }
        }
        return mods;
    }

    /// <summary>
    /// **The overlay**: the Codex folder's files that are not the SDK's own (<see cref="ModFiles"/>), into a cache folder keyed by
    /// the base and by each file's path and bytes — the loose classes copied (a `Codex*` one bridged; any with a local read unassigned
    /// fixed, as the base's), then the sources compiled by ECJ against the overlay and the base (a `Codex*.java` bridged first, as the
    /// source path bridges it; a JDK 1.1 leniency fixed in a shadow copy, as the source path's). The loader searches it before the
    /// base, as 1.1's search path puts the loose folders before <c>Codex.nob</c>. With nothing to put there it is an empty folder,
    /// which a compile on load writes into. Its marker lists each class with its SHA-256; a folder whose classes are not those (a
    /// class a compile on load wrote there, a file taken away) is made again, so nothing stale is ever searched first.
    /// </summary>
    (string Dir, IReadOnlyList<OverlayClass> Classes, int SdkIgnored) BuildOverlay(string baseDir, string baseKey, bool force, List<string> notes,
                                                                                out bool hit, out long ms)
    {
        var sw = Stopwatch.StartNew();
        var mods = ModFiles(out int sdkIgnored, out var passedOver);
        string key;
        using (var sha = SHA256.Create())
        {
            foreach (var m in mods)
            {
                var n = Encoding.UTF8.GetBytes(m.Rel.ToLowerInvariant() + "\0");
                sha.TransformBlock(n, 0, n.Length, null, 0);
                var d = File.ReadAllBytes(m.Full);
                sha.TransformBlock(d, 0, d.Length, null, 0);
            }
            sha.TransformFinalBlock([], 0, 0);
            key = mods.Count == 0 ? "none" : Convert.ToHexString(sha.Hash!)[..12].ToLowerInvariant();
        }
        var dir = Path.GetFullPath(Path.Combine(cacheRoot, baseKey + "-overlay-" + key));
        var marker = Path.Combine(dir, ".complete");
        foreach (var p in passedOver) notes.Add(CodexBuild.LeftOutNote + p + ": left out of the overlay");
        if (!force && Kept(dir, marker) is { } kept)
        {
            hit = true;
            ms = sw.ElapsedMilliseconds;
            notes.AddRange(File.ReadAllLines(marker).Where(l => l.StartsWith("note ", StringComparison.Ordinal)).Select(l => l[5..]));
            return (dir, kept, sdkIgnored);
        }
        hit = false;
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        var made = new List<OverlayClass>();
        var mine = new List<string>();
        foreach (var m in mods.Where(m => !m.IsSource))
        {
            File.WriteAllBytes(Path.Combine(dir, m.Class + ".class"), CodexClasses.Rewrite(m.Class, File.ReadAllBytes(m.Full), mine, out _));
            made.Add(new(m.Class, m.Rel));
        }
        var sources = new List<string>();
        foreach (var m in mods.Where(m => m.IsSource))
        {
            if (CodexClasses.Bridged(m.Class))
            {
                CodexBridge.BridgeOne(m.Full, Path.Combine(dir, "_bridge"));
                sources.Add(Path.Combine(dir, "_bridge", Path.GetFileName(m.Full)));
            }
            else sources.Add(m.Full);
            made.Add(new(m.Class, m.Rel));
        }
        if (sources.Count > 0)
        {
            var switches = Switches();
            string classpath = dir + java.io.File.pathSeparator + baseDir;
            bool ok = false; string errors = "";
            for (int attempt = 1; attempt <= 3 && !ok; attempt++)
            {
                var argv = new List<string>(switches) { "-classpath", classpath, "-d", dir };
                argv.AddRange(sources);
                (ok, errors, _) = Run(argv);
                if (ok) break;
                var fixes = Compat.FixUninitializedLocals(errors, sources, Path.Combine(dir, "_compat"));
                if (fixes.Count == 0) break;
                mine.AddRange(fixes);
            }
            if (!ok) throw new CodexCompileException("the Codex folder's sources over the shipped classes: " + errors);
        }
        notes.AddRange(mine);
        made.Sort((a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        File.WriteAllLines(marker, [
            "# Vitomir script cache: the Codex folder's overlay over the shipped classes. Delete this file (or the folder) to make it again.",
            $"# {DateTime.UtcNow:o} {Level} {mods.Count} files, {sw.ElapsedMilliseconds} ms",
            .. made.Select(m => $"from {m.Name}\t{m.From}"),
            .. Directory.GetFiles(dir, "*.class").Order(StringComparer.Ordinal).Select(f => $"class {Path.GetFileName(f)}\t{Vitomir.Platform.Setup.DataManifest.Sha256Of(f)}"),
            .. mine.Select(n => "note " + n.Replace('\n', ' ')),
        ]);
        ms = sw.ElapsedMilliseconds;
        return (dir, made, sdkIgnored);
    }

    /// <summary>The overlay's classes when its folder holds exactly what its marker lists (each by its SHA-256), else null.</summary>
    static IReadOnlyList<OverlayClass>? Kept(string dir, string marker)
    {
        if (!File.Exists(marker)) return null;
        var lines = File.ReadAllLines(marker);
        var listed = lines.Where(l => l.StartsWith("class ", StringComparison.Ordinal)).Select(l => l[6..].Split('\t'))
                          .ToDictionary(f => f[0], f => f[1], StringComparer.Ordinal);
        var there = Directory.GetFiles(dir, "*.class");
        if (there.Length != listed.Count) return null;
        foreach (var f in there)
            if (!listed.TryGetValue(Path.GetFileName(f), out var sha) || Vitomir.Platform.Setup.DataManifest.Sha256Of(f) != sha) return null;
        return [.. lines.Where(l => l.StartsWith("from ", StringComparison.Ordinal)).Select(l => l[5..].Split('\t')).Select(f => new OverlayClass(f[0], f[1]))];
    }

    /// <summary>
    /// Recompiles one script against the built tree — what a level load does when that scene's
    /// source has changed since it was compiled (for the shipped classes, a source of the Codex folder that is not the SDK's
    /// own, into the overlay). Measured in tens of milliseconds. <paramref name="into"/> writes it elsewhere (a test's measure).
    /// </summary>
    public long Recompile(string sourceFile, string? into = null)
    {
        var build = Current ?? throw new InvalidOperationException("Build() first");
        using var held = CacheLock(cacheRoot);                 // the cache's classes are shared too (see Build)
        string classpath = string.Join(java.io.File.pathSeparator, build.SearchPath);
        var argv = new List<string>(Switches()) { "-classpath", classpath, "-d", into ?? build.OutputDir, sourceFile };
        var (ok, errors, ms) = Run(argv);
        return ok ? ms : throw new CodexCompileException(errors);
    }

    /// <summary>How long a process waits for another's build of the same cache before giving up (a cold build takes seconds).</summary>
    public static readonly TimeSpan CacheWait = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The cache's lock: <c>.lock</c> in <paramref name="root"/>, opened for this process alone, held until disposed.
    /// Waits for another process (or another compiler in this one) that holds it, up to <see cref="CacheWait"/>.
    /// </summary>
    public static FileStream CacheLock(string root)
    {
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, ".lock");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (clock.Elapsed < CacheWait) { Thread.Sleep(50); }
        }
    }

    /// <summary>Whether a script's source is newer than the class the cache holds for it (for the shipped classes, the overlay's).</summary>
    public bool NeedsRecompile(string sourceFile)
    {
        var build = Current;
        if (build is null) return true;
        var cls = Path.Combine(build.OutputDir, Path.GetFileNameWithoutExtension(sourceFile) + ".class");
        return !File.Exists(cls) || File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(cls);
    }

    /// <summary>
    /// The source file for a script class, or null when the player's tree has none — for the shipped classes, none either when it
    /// is the SDK's own source, unchanged (the shipped class runs), so only a mod's source, or one edited since, is compiled
    /// on load.
    /// </summary>
    public string? SourceOf(string className)
    {
        foreach (var sub in SearchFolders)
        {
            var p = Path.Combine(codexDir, sub, className + ".java");
            if (!File.Exists(p)) continue;
            if (shipped is not null && IsSdkOwn((sub.Length == 0 ? "" : sub + "/") + className + ".java", p)) return null;
            return p;
        }
        return null;
    }

    /// <summary>
    /// Our bridge and host, then the player's Codex tree: the root, SP, MP, and System minus the
    /// <c>Codex*</c> classes the bridge replaces (and the copy-protection hook, which we never run).
    /// </summary>
    List<string> Gather(string bridgeDir, string hostDir)
    {
        var replaced = CodexBridge.ReplacedFiles(SystemDir);
        var sources = new List<string>();
        sources.AddRange(Directory.GetFiles(bridgeDir, "*.java"));
        sources.AddRange(Directory.GetFiles(hostDir, "*.java"));
        foreach (var sub in new[] { "", "SP", "MP" })
        {
            var dir = Path.Combine(codexDir, sub);
            if (Directory.Exists(dir)) sources.AddRange(Directory.GetFiles(dir, "*.java"));
        }
        sources.AddRange(Directory.GetFiles(SystemDir, "*.java")
            .Where(f => !replaced.Contains(Path.GetFileName(f))
                     && !Path.GetFileName(f).Equals(CodexBridge.Excluded, StringComparison.OrdinalIgnoreCase)));
        return [.. sources.Select(Path.GetFullPath).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Every source's name and bytes, plus the compiler settings: change any and the cache misses.</summary>
    static string Hash(List<string> sources, List<string> switches)
    {
        using var sha = SHA256.Create();
        foreach (var f in sources)
        {
            var name = Encoding.UTF8.GetBytes(Path.GetFileName(f).ToLowerInvariant() + "\0");
            sha.TransformBlock(name, 0, name.Length, null, 0);
            var data = File.ReadAllBytes(f);
            sha.TransformBlock(data, 0, data.Length, null, 0);
        }
        var tail = Encoding.UTF8.GetBytes("ecj-4.6.1|" + string.Join("|", switches.Where(a => !a.Contains(Path.DirectorySeparatorChar))));
        sha.TransformFinalBlock(tail, 0, tail.Length);
        return Convert.ToHexString(sha.Hash!)[..16].ToLowerInvariant();
    }

    static (bool Ok, string Errors, long Ms) Run(List<string> argv)
    {
        var stdout = new java.io.StringWriter();
        var stderr = new java.io.StringWriter();
        var sw = Stopwatch.StartNew();
        bool ok = org.eclipse.jdt.core.compiler.batch.BatchCompiler.compile(
            [.. argv], new java.io.PrintWriter(stdout), new java.io.PrintWriter(stderr), null);
        return (ok, stderr.toString(), sw.ElapsedMilliseconds);
    }
}

public sealed class CodexCompileException(string errors)
    : Exception("the game's scripts did not compile:\n" + (errors.Length > 4000 ? errors[..4000] + "\n… (truncated)" : errors));
