// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The setup's manifest: the files of 1.1 that Vitomir reads, each by its size and SHA-256.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Vitomir.Platform.Setup;

/// <summary>One file the manifest names: its part, its path as the install spells it (forward slashes), its size and its SHA-256.</summary>
public sealed record ManifestFile(string Part, string Path, long Size, string Sha256);

/// <summary>A part of the manifest: the game's own files (<see cref="DataManifest.Game"/>, required) or the SDK's scripts' sources (optional, never imported).</summary>
public sealed record ManifestPart(string Name, bool Required, string What);

/// <summary>
/// **The setup's manifest** (*"we look for correct copies of the actual files that we would use … I
/// don't want this to be a full DRM check on the base game itself"*): the files of 1.1 Vitomir reads, each by its path, size and
/// SHA-256, and nothing else of the install. Names, sizes and hashes only: it holds none of the game's data
/// (<c>docs/ORIGINAL_CONTENT_POLICY.md</c>, item 6: what Vitomir ships may be keyed to the player's files, never made of them).
/// <para>Two parts. <b>game</b> (required): the six archives, <c>classes.zip</c>, the films (<c>Video</c>), the loose sounds
/// (<c>Sounds</c>) and <c>Chronicles</c> — what <c>GameFiles</c> reads (3,133 files, 1.26 GB in 1.1 as GOG and Steam ship it, byte for
/// byte the same in both: the survey) — <c>Codex.nob</c> among them, whose compiled classes are the scripts the game runs.
/// <b>scripts</b> (optional): the scripts' Java sources (<c>Codex</c>), Nihilistic's SDK's, not the stores' (neither GOG's nor
/// Steam's copy has them). They are not imported: the part is the list that **knows the SDK's own files** — a source of a
/// Codex folder that is one of them byte for byte is ignored, and the shipped class runs (<c>CodexCompiler</c>'s overlay); any other
/// is a mod's.</para>
/// <para>Its own name and version (<see cref="Name"/>, <see cref="Version"/>): a later change of 1.1's data is a new manifest, never an
/// edit of this one. The file is text, one record a line, tab-separated, <c>#</c> a comment: <c>manifest</c>, <c>version</c>,
/// <c>made</c>, <c>part</c> (name, required or optional, what), <c>overrides</c> (the folders whose loose files would override an
/// archive's, as a texture pack installs) and <c>file</c> (part, size, SHA-256, path).</para>
/// </summary>
public sealed class DataManifest
{
    /// <summary>The required part: the game's own files.</summary>
    public const string Game = "game";

    /// <summary>The optional part: the scripts' Java sources (<c>Codex</c>, Nihilistic's SDK), the list that knows them (never imported).</summary>
    public const string Scripts = "scripts";

    /// <summary>The manifest's resource in this assembly, and its file name in the source tree (<c>src/Vitomir.Platform/Setup/Manifests</c>).</summary>
    public const string BuiltinName = "vtmr-1.1-data-1.tsv";

    /// <summary>Its name: what it is the manifest of.</summary>
    public string Name { get; init; } = "";

    /// <summary>Its version: a new one for any change of the files it names.</summary>
    public int Version { get; init; }

    /// <summary>What it was made from, for the reader.</summary>
    public string Made { get; init; } = "";

    public IReadOnlyList<ManifestPart> Parts { get; init; } = [];

    /// <summary>The install's top folders (lower case) whose loose files 1.1 reads over its archives' (a texture pack's <c>materials</c>, <c>3d</c>).</summary>
    public IReadOnlyList<string> OverrideFolders { get; init; } = [];

    public IReadOnlyList<ManifestFile> Files { get; init; } = [];

    /// <summary>The files of one part.</summary>
    public IEnumerable<ManifestFile> Of(string part) => Files.Where(f => f.Part == part);

    /// <summary>The bytes of one part (all of them with null).</summary>
    public long Bytes(string? part = null) => Files.Where(f => part is null || f.Part == part).Sum(f => f.Size);

    /// <summary>Its name and version as one word: <c>vtmr-1.1-data v1</c>.</summary>
    public override string ToString() => $"{Name} v{Version.ToString(CultureInfo.InvariantCulture)}";

    // ---- which of the install's files Vitomir reads ------------------------------------------------------------------------

    /// <summary>The six archives 1.1 ships (as <c>GameFiles.Archives</c> names them).</summary>
    public static readonly string[] Archives = ["Local_Eng.nob", "Resource.nob", "LMaterials.nob", "Levels.nob", "Sounds.nob", "Codex.nob"];

    /// <summary>The install's loose folders the game reads (the game part).</summary>
    public static readonly string[] GameFolders = ["Chronicles", "Video", "Sounds"];

    /// <summary>The scripts' sources' folder (the scripts part).</summary>
    public const string ScriptsFolder = "Codex";

    /// <summary>
    /// **The part a file of the install belongs to**, by its path relative to the install (either slash), or null for a file Vitomir
    /// does not read (the programs, the store's and the installer's files, the SDK's other folders, the saves, <c>Extras</c>, <c>Miles</c>).
    /// </summary>
    public static string? PartOf(string relative)
    {
        relative = relative.Replace('\\', '/').TrimStart('/');
        int slash = relative.IndexOf('/');
        if (slash < 0)
            return Archives.Contains(relative, StringComparer.OrdinalIgnoreCase) || relative.Equals("classes.zip", StringComparison.OrdinalIgnoreCase) ? Game : null;
        string top = relative[..slash];
        if (GameFolders.Contains(top, StringComparer.OrdinalIgnoreCase)) return Game;
        if (top.Equals(ScriptsFolder, StringComparison.OrdinalIgnoreCase)) return Scripts;
        return null;
    }

    /// <summary>The parts every manifest made here has.</summary>
    public static readonly ManifestPart[] StandardParts =
    [
        new(Game, true, "the six archives, classes.zip, the films (Video), the loose sounds (Sounds) and Chronicles"),
        new(Scripts, false, "the scripts' Java sources (Codex), from Nihilistic's SDK: neither GOG's nor Steam's copy has them"),
    ];

    /// <summary>The top folders of 1.1's six archives (as they hold them, lower case): a loose folder of one of these names overrides them.</summary>
    public static readonly string[] StandardOverrideFolders =
        ["3d", "conversations", "levels", "materials", "misc", "paths", "quests", "scenes", "sounds", "stores", "strings", "ui"];

    // ---- reading and writing ------------------------------------------------------------------------------------------------

    /// <summary>The manifest this program carries (an embedded resource, <see cref="BuiltinName"/>).</summary>
    public static DataManifest Builtin
    {
        get
        {
            using var s = typeof(DataManifest).Assembly.GetManifestResourceStream("Setup/" + BuiltinName)
                ?? throw new InvalidOperationException($"the setup's manifest {BuiltinName} is not in {typeof(DataManifest).Assembly.GetName().Name}");
            using var r = new StreamReader(s, Encoding.UTF8);
            return Parse(r.ReadToEnd(), BuiltinName);
        }
    }

    /// <summary>A manifest from a file (<c>--manifest</c>, a developer's).</summary>
    public static DataManifest Read(string path) => Parse(File.ReadAllText(path, Encoding.UTF8), path);

    /// <summary>Reads the text; a line it does not understand is an error naming the line.</summary>
    /// <exception cref="InvalidDataException">The text is not a manifest.</exception>
    public static DataManifest Parse(string text, string source)
    {
        string name = "", made = "";
        int version = 0;
        var parts = new List<ManifestPart>();
        var overrides = new List<string>();
        var files = new List<ManifestFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            string line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var f = line.Split('\t');
            InvalidDataException Bad(string why) => new($"{source}:{number}: {why}: {line}");
            switch (f[0])
            {
                case "manifest" when f.Length == 2: name = f[1]; break;
                case "version" when f.Length == 2:
                    if (!int.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out version) || version < 1) throw Bad("not a version");
                    break;
                case "made" when f.Length == 2: made = f[1]; break;
                case "part" when f.Length == 4 && f[2] is "required" or "optional": parts.Add(new(f[1], f[2] == "required", f[3])); break;
                case "overrides" when f.Length >= 2: overrides.AddRange(f.Skip(1).Select(o => o.ToLowerInvariant())); break;
                case "file" when f.Length == 5:
                    if (!parts.Exists(p => p.Name == f[1])) throw Bad($"a file of a part not declared ({f[1]})");
                    if (!long.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out long size)) throw Bad("not a size");
                    if (f[3].Length != 64 || !f[3].All(Uri.IsHexDigit)) throw Bad("not a SHA-256");
                    string path = f[4].Replace('\\', '/');
                    if (path.Length == 0 || path.StartsWith('/') || path.Split('/').Any(s => s is "" or "." or "..")) throw Bad("not a path inside the install");
                    if (!seen.Add(path)) throw Bad("a path named twice");
                    files.Add(new(f[1], path, size, f[3].ToLowerInvariant()));
                    break;
                default: throw Bad("not a manifest's line");
            }
        }
        if (name.Length == 0 || version == 0) throw new InvalidDataException($"{source}: not a manifest (no name or no version)");
        if (!parts.Exists(p => p.Required)) throw new InvalidDataException($"{source}: a manifest with no required part");
        return new DataManifest { Name = name, Version = version, Made = made, Parts = parts, OverrideFolders = overrides, Files = files };
    }

    /// <summary>The manifest as its file's text (the order kept: parts as declared, files by part then path, ordinal).</summary>
    public string Text()
    {
        var b = new StringBuilder();
        b.Append("# Vitomir's setup manifest: the files of VTMR 1.1 that Vitomir reads, each by its size and SHA-256.\n");
        b.Append("# Names, sizes and hashes only: no game content (docs/ORIGINAL_CONTENT_POLICY.md, item 6). A change of 1.1's data is a\n");
        b.Append("# new manifest with a new version, never an edit of this one. Made by vitomir-tools --make-setup-manifest.\n");
        b.Append("manifest\t").Append(Name).Append('\n');
        b.Append("version\t").Append(Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
        if (Made.Length > 0) b.Append("made\t").Append(Made.Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
        foreach (var p in Parts) b.Append("part\t").Append(p.Name).Append('\t').Append(p.Required ? "required" : "optional").Append('\t').Append(p.What).Append('\n');
        if (OverrideFolders.Count > 0) b.Append("overrides\t").Append(string.Join('\t', OverrideFolders)).Append('\n');
        foreach (var p in Parts)
            foreach (var f in Of(p.Name).OrderBy(f => f.Path, StringComparer.Ordinal))
                b.Append("file\t").Append(f.Part).Append('\t').Append(f.Size.ToString(CultureInfo.InvariantCulture)).Append('\t')
                 .Append(f.Sha256).Append('\t').Append(f.Path).Append('\n');
        return b.ToString();
    }

    // ---- making one ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// **A manifest of a known-good install** (<c>vitomir-tools --make-setup-manifest</c>): every file of <paramref name="install"/> that
    /// Vitomir reads (<see cref="PartOf"/>), hashed. <paramref name="say"/> hears each tenth of the bytes.
    /// </summary>
    public static DataManifest Make(string install, string name, int version, string made, Action<string>? say = null, CancellationToken cancel = default)
    {
        var picked = Directory.EnumerateFiles(install, "*", SearchOption.AllDirectories)
            .Select(f => (Full: f, Rel: System.IO.Path.GetRelativePath(install, f).Replace(System.IO.Path.DirectorySeparatorChar, '/')))
            .Where(f => PartOf(f.Rel) is not null)
            .OrderBy(f => Array.FindIndex(StandardParts, p => p.Name == PartOf(f.Rel))).ThenBy(f => f.Rel, StringComparer.Ordinal).ToList();   // as Text writes them
        long total = picked.Sum(f => new FileInfo(f.Full).Length), done = 0;
        int tenth = -1;
        var files = new List<ManifestFile>(picked.Count);
        foreach (var (full, rel) in picked)
        {
            cancel.ThrowIfCancellationRequested();
            long size = new FileInfo(full).Length;
            files.Add(new(PartOf(rel)!, rel, size, Sha256Of(full)));
            done += size;
            if (total > 0 && done * 10 / total != tenth) { tenth = (int)(done * 10 / total); say?.Invoke($"  {tenth * 10}%"); }
        }
        return new DataManifest { Name = name, Version = version, Made = made, Parts = StandardParts, OverrideFolders = StandardOverrideFolders, Files = files };
    }

    /// <summary>
    /// **A manifest from a survey's lists** (a survey of real installs): <c>sha256sum</c>'s lines (<c>&lt;hash&gt;  ./path</c>)
    /// and the sizes' (<c>&lt;size&gt;&lt;tab&gt;&lt;date&gt;&lt;tab&gt;./path</c>), each file Vitomir reads taken (<see cref="PartOf"/>); a file
    /// in one list and not the other is an error naming it.
    /// </summary>
    public static DataManifest FromLists(string shaList, string sizeList, string name, int version, string made)
    {
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(sizeList))
        {
            if (line.Length == 0) continue;
            var f = line.Split('\t');
            if (f.Length < 3 || !long.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out long size))
                throw new InvalidDataException($"{sizeList}: not a size's line: {line}");
            sizes[Clean(f[^1])] = size;
        }
        var files = new List<ManifestFile>();
        foreach (var line in File.ReadLines(shaList))
        {
            if (line.Length == 0) continue;
            int gap = line.IndexOf("  ", StringComparison.Ordinal);
            if (gap != 64) throw new InvalidDataException($"{shaList}: not a hash's line: {line}");
            string rel = Clean(line[(gap + 2)..]);
            if (PartOf(rel) is not { } part) continue;
            if (!sizes.TryGetValue(rel, out long size)) throw new InvalidDataException($"{sizeList}: no size for {rel}");
            files.Add(new(part, rel, size, line[..64].ToLowerInvariant()));
        }
        return new DataManifest { Name = name, Version = version, Made = made, Parts = StandardParts, OverrideFolders = StandardOverrideFolders, Files = files };

        static string Clean(string p) => p.Replace('\\', '/').TrimStart('.').TrimStart('/');
    }

    // ---- hashing -------------------------------------------------------------------------------------------------------------

    /// <summary>A file's SHA-256 in lower-case hex.</summary>
    public static string Sha256Of(string path)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexStringLower(SHA256.HashData(s));
    }
}
