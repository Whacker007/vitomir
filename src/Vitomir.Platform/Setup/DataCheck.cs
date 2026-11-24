// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The check of an install against the setup's manifest.
using System.Globalization;
using System.Security.Cryptography;
using Vitomir.Formats;

namespace Vitomir.Platform.Setup;

/// <summary>How far a check goes: every file's size (fast: a few milliseconds), or its size and its SHA-256 (the whole 1.26 GB read).</summary>
public enum CheckDepth { Sizes, Full }

/// <summary>
/// A step's progress, for the command line's percentages and the window's bar: which pass it is (<see cref="Step"/>: <see cref="Sizes"/>,
/// <see cref="Hashes"/>, <see cref="Copy"/>), the file it is at (only a file that pass reads: a file missing from the install is never
/// named), and how far it is.
/// </summary>
public sealed record SetupProgress(string Step, string Item, long Done, long Total)
{
    /// <summary>The check by size (an import's first pass too).</summary>
    public const string Sizes = "sizes";
    /// <summary>The full check: every file's size and SHA-256.</summary>
    public const string Hashes = "sizes and hashes";
    /// <summary>The import's copy: each file copied, read back and hashed (or kept when it is there and right).</summary>
    public const string Copy = "copy and hash";
}

/// <summary>One file of the manifest that is not as it says: missing, of another size, or of other bytes.</summary>
public sealed record FileProblem(string Part, string Path, string Why)
{
    public override string ToString() => $"{Path}: {Why}";
}

/// <summary>How an optional part stands in an install: all there and right, not there at all, or there in part or wrong.</summary>
public enum PartState { Whole, Absent, Broken }

/// <summary>
/// **What the check found**: the files of the manifest's required part missing or changed, by name (<see cref="Problems"/>); the
/// optional part's state and its problems; the install's files that would override them (<see cref="Overriding"/>: an extra
/// <c>.nob</c>, loose files in a folder of the archives' names, an extra file among the loose folders Vitomir reads), which are left out
/// and said; how many other files were passed over; and where each named file is (its spelling in this install).
/// </summary>
public sealed record DataCheckReport(
    string Folder, Store Store, DataManifest Manifest, CheckDepth Depth,
    IReadOnlyList<FileProblem> Problems, PartState Scripts, IReadOnlyList<FileProblem> ScriptProblems,
    IReadOnlyList<string> Overriding, int Ignored, IReadOnlyDictionary<string, string> Found)
{
    /// <summary>Every file of the required part there and as the manifest says (by size, or by size and hash).</summary>
    public bool GameWhole => Problems.Count == 0;

    /// <summary>
    /// Those of <see cref="Overriding"/> that would **replace** one of 1.1's own files: a loose file with the same path as one of the
    /// manifest's archives' entries (a texture pack's <c>Materials/...</c> over the one in <c>LMaterials.nob</c>), or an extra <c>.nob</c>
    /// holding such a path (<see cref="DataCheck.Replacements"/>). The others only <see cref="Adding"/>: new files, said as additions.
    /// </summary>
    public IReadOnlyList<string> Replacing { get; init; } = [];

    /// <summary>Those of <see cref="Overriding"/> that only add a file 1.1 does not have (the SDK's loose <c>Materials</c> files do), not <see cref="Replacing"/>.</summary>
    public IReadOnlyList<string> Adding => [.. Overriding.Except(Replacing, StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// **The check** (the files Vitomir reads, not the folder; files that would override them left out and
/// said). Only the manifest's files are looked at, each found case-insensitively, by its size (<see cref="CheckDepth.Sizes"/>) or its
/// size and SHA-256 (<see cref="CheckDepth.Full"/>; an import always hashes, <see cref="DataImport"/>). Everything else in the folder is
/// ignored, whatever it is, but for the files that would change what the game reads (<see cref="Overrides"/>). Nothing is written.
/// </summary>
public static class DataCheck
{
    /// <summary>Checks <paramref name="folder"/> against <paramref name="manifest"/>.</summary>
    public static DataCheckReport Run(string folder, DataManifest manifest, CheckDepth depth, IProgress<SetupProgress>? progress = null, CancellationToken cancel = default)
    {
        folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var index = Index(folder);
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = new Dictionary<string, List<FileProblem>>();
        foreach (var p in manifest.Parts) problems[p.Name] = [];
        // The progress counts and names only the files this pass reads (those the install has): a GOG copy has no Codex sources,
        // and a bar labelled with them named files never read.
        string step = depth == CheckDepth.Full ? SetupProgress.Hashes : SetupProgress.Sizes;
        var present = manifest.Files.Where(f => index.ContainsKey(f.Path)).ToList();
        long total = depth == CheckDepth.Full ? present.Sum(f => f.Size) : present.Count, done = 0;
        foreach (var f in manifest.Files)
        {
            cancel.ThrowIfCancellationRequested();
            if (!index.TryGetValue(f.Path, out var full)) problems[f.Part].Add(new(f.Part, f.Path, "missing"));
            else
            {
                found[f.Path] = full;
                long size = new FileInfo(full).Length;
                if (size != f.Size) problems[f.Part].Add(new(f.Part, f.Path, $"{size.ToString("N0", CultureInfo.InvariantCulture)} bytes, not {f.Size.ToString("N0", CultureInfo.InvariantCulture)}"));
                else if (depth == CheckDepth.Full && Hash(full, f, progress, step, done, total, cancel) is var hash && hash != f.Sha256)
                    problems[f.Part].Add(new(f.Part, f.Path, $"other bytes of the same size (SHA-256 {hash[..12]}..., not {f.Sha256[..12]}...)"));
                done += depth == CheckDepth.Full ? f.Size : 1;
                progress?.Report(new(step, f.Path, done, total));
            }
        }

        var required = manifest.Parts.Where(p => p.Required).SelectMany(p => problems[p.Name]).ToList();
        var scriptProblems = problems.TryGetValue(DataManifest.Scripts, out var s) ? s : [];
        int scriptFiles = manifest.Of(DataManifest.Scripts).Count();
        var scripts = scriptProblems.Count == 0 ? PartState.Whole
            : scriptProblems.Count(p => p.Why == "missing") == scriptFiles ? PartState.Absent : PartState.Broken;
        var overriding = Overrides(index, manifest);
        int ignored = index.Count - found.Count - overriding.Count;
        return new DataCheckReport(folder, InstallFinder.StoreOf(folder), manifest, depth, required, scripts, scriptProblems, overriding, ignored, found)
        {
            Replacing = Replacements(overriding, index, manifest),
        };
    }

    /// <summary>
    /// **The files that would override the manifest's**: an extra <c>.nob</c> at the top (1.1 mounts every one), a loose file in a
    /// top folder named as the archives' folders are (<see cref="DataManifest.OverrideFolders"/>: <c>materials</c>, <c>3d</c>, … as a texture
    /// pack installs them) or in one of the loose folders Vitomir reads that the manifest does not name (a mod's chronicle, sound or script).
    /// The loose <c>Sounds</c> folder is both: its manifest's files are 1.1's own, any other file in it an override.
    /// </summary>
    public static IReadOnlyList<string> Overrides(IReadOnlyDictionary<string, string> index, DataManifest manifest)
    {
        var named = new HashSet<string>(manifest.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var read = new HashSet<string>(manifest.OverrideFolders.Concat(DataManifest.GameFolders).Append(DataManifest.ScriptsFolder), StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var rel in index.Keys)
        {
            if (named.Contains(rel)) continue;
            int slash = rel.IndexOf('/');
            bool over = slash < 0 ? rel.EndsWith(".nob", StringComparison.OrdinalIgnoreCase) : read.Contains(rel[..slash]);
            if (over) list.Add(rel);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// **Which of the files left out would replace one of 1.1's own** (a console once showed every one said as an override): a loose
    /// file whose path is that of one of 1.1's files, the manifest's own or an entry of one of its archives found in the install (their
    /// directories read, not their files: <see cref="NobArchive"/>), or an extra <c>.nob</c> holding one such path. Every other one only
    /// adds a file (the SDK's loose <c>Materials</c> files do). An archive that does not read as one adds nothing to 1.1's paths, and an
    /// extra one that does not read is said as an addition. In <paramref name="overriding"/>'s order.
    /// </summary>
    public static IReadOnlyList<string> Replacements(IReadOnlyList<string> overriding, IReadOnlyDictionary<string, string> index, DataManifest manifest)
    {
        if (overriding.Count == 0) return [];
        var own = new HashSet<string>(manifest.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var f in manifest.Files)
            if (f.Path.IndexOf('/') < 0 && f.Path.EndsWith(".nob", StringComparison.OrdinalIgnoreCase) && index.TryGetValue(f.Path, out var full))
                foreach (var name in EntriesOf(full)) own.Add(name);
        var list = new List<string>();
        foreach (var o in overriding)
        {
            bool archive = o.IndexOf('/') < 0 && o.EndsWith(".nob", StringComparison.OrdinalIgnoreCase);
            if (archive ? index.TryGetValue(o, out var full) && EntriesOf(full).Any(own.Contains) : own.Contains(o)) list.Add(o);
        }
        return list;
    }

    /// <summary>An archive's entries' paths (forward slashes), or none when it does not read as one.</summary>
    static IReadOnlyList<string> EntriesOf(string archive)
    {
        try
        {
            using var nob = new NobArchive(archive);
            return [.. nob.Entries.Select(e => e.Name)];
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException) { return []; }
    }

    /// <summary>Every file under <paramref name="folder"/>: its path relative to it (forward slashes) → its full path, case-insensitively.</summary>
    public static Dictionary<string, string> Index(string folder)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return map;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var f in Directory.EnumerateFiles(folder, "*", options))
            map.TryAdd(Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'), f);
        return map;
    }

    /// <summary>A file's SHA-256, the progress said every 64 MB.</summary>
    static string Hash(string full, ManifestFile f, IProgress<SetupProgress>? progress, string step, long before, long total, CancellationToken cancel)
    {
        using var s = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        long read = 0, said = 0;
        int n;
        while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, n);
            read += n;
            if (read - said >= 64L << 20) { said = read; progress?.Report(new(step, f.Path, before + read, total)); }
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
