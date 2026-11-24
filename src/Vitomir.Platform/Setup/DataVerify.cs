// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The imported game files verified against the manifest they were imported with, and repaired from the copy of the game they came from.
using System.Globalization;

namespace Vitomir.Platform.Setup;

/// <summary>
/// **What a verification of the imported game files found** (<see cref="DataVerify.Run"/>): each of the manifest's files that is
/// missing or changed (another size, or other bytes of the same size), and each extra file that would override them (a loose file in a
/// folder of the archives' names, an extra <c>.nob</c>); or why the files could not be verified at all (<see cref="Refusal"/>: nothing
/// imported, a manifest copy that does not read).
/// </summary>
public sealed record DataVerifyReport(string Folder, ImportedMarker? Marker, string? Refusal, IReadOnlyList<FileProblem> Problems, IReadOnlyList<string> Extra,
                                      int Files, long Bytes)
{
    /// <summary>Every file as the manifest has it, and nothing extra over them.</summary>
    public bool AllGood => Refusal is null && Problems.Count == 0 && Extra.Count == 0;

    /// <summary>The files missing.</summary>
    public IEnumerable<FileProblem> Missing => Problems.Where(p => p.Why == "missing");

    /// <summary>The files there but changed.</summary>
    public IEnumerable<FileProblem> Changed => Problems.Where(p => p.Why != "missing");
}

/// <summary>What a repair did (<see cref="DataVerify.Repair"/>), or why it could not (<see cref="Refusal"/>, and the copy to select).</summary>
public sealed record DataRepairReport(string? Refusal, string Source, IReadOnlyList<string> Copied, int SetAside)
{
    public bool Repaired => Refusal is null;
}

/// <summary>
/// **Verify files** (as a store's "verify integrity"): the imported game files (<see cref="ImportedData.Folder"/>) read whole and each
/// checked against the size and SHA-256 of the manifest they were imported with (its copy beside the marker), and the folder looked
/// through for extra files that would override them. **Repair** copies again only the files found missing or changed, from the copy of the
/// game they were imported from (the marker's source), once that copy's own files are checked to be the manifest's (size and SHA-256);
/// each copied to a <c>.partial</c>, moved in and read back as the import does. Extra files are set aside as the import sets them aside,
/// never deleted. The marker stays: each file moves in whole, so the folder is never half a file.
/// </summary>
public static class DataVerify
{
    /// <summary>Verifies the imported game files under <paramref name="userData"/> (every byte read: about the import's 1.26 GB).</summary>
    public static DataVerifyReport Run(string userData, IProgress<SetupProgress>? progress = null, CancellationToken cancel = default)
    {
        string folder = ImportedData.Folder(Path.GetFullPath(userData));
        var marker = ImportedData.ReadMarker(folder);
        if (marker is null) return new(folder, null, "nothing is imported yet: import the game's files first", [], [], 0, 0);
        DataManifest manifest;
        try { manifest = DataManifest.Read(Path.Combine(folder, ImportedData.ManifestCopyName)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(folder, marker, $"the manifest the files were imported with ({ImportedData.ManifestCopyName}) cannot be read ({e.Message}): import the game's files again", [], [], 0, 0);
        }
        var check = DataCheck.Run(folder, manifest, CheckDepth.Full, progress, cancel);
        // The scripts' sources are verified only where an older import took them.
        var problems = marker.Scripts ? [.. check.Problems, .. check.ScriptProblems] : check.Problems;
        var files = manifest.Files.Where(f => manifest.Parts.First(p => p.Name == f.Part).Required || marker.Scripts).ToList();
        return new(folder, marker, null, problems, check.Overriding, files.Count, files.Sum(f => f.Size));
    }

    /// <summary>
    /// **Repairs** what <paramref name="verified"/> found: the copy of the game the files came from checked (it must be there, and each file
    /// to copy again of the manifest's size and SHA-256 in it), then each missing or changed file copied again and read back, each extra
    /// file set aside (<c>Imported\set-aside\&lt;when&gt;\</c>). A copy of the game that is gone or changed refuses it, saying which copy to
    /// select and to import again.
    /// </summary>
    public static DataRepairReport Repair(DataVerifyReport verified, IProgress<SetupProgress>? progress = null, CancellationToken cancel = default)
    {
        if (verified.Refusal is { } why) return new(why, "", [], 0);
        string source = verified.Marker!.Source;
        if (verified.AllGood) return new(null, source, [], 0);
        string again = "select your copy of the game in step 1 and select Import again (it copies only what is missing or changed)";
        if (source.Length == 0 || !InstallFinder.IsInstall(source))
            return new($"the copy of the game the files were imported from ({(source.Length == 0 ? "not recorded" : source)}) is not there any more: {again}", source, [], 0);
        DataManifest manifest = DataManifest.Read(Path.Combine(verified.Folder, ImportedData.ManifestCopyName));
        var byPath = manifest.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var index = DataCheck.Index(source);
        var plan = verified.Problems.Select(p => byPath[p.Path]).ToList();
        long total = plan.Sum(f => f.Size) * 2, done = 0;

        // The copy of the game first: each file to copy again there, of the manifest's size and bytes.
        foreach (var f in plan)
        {
            cancel.ThrowIfCancellationRequested();
            string? bad = !index.TryGetValue(f.Path, out var at) ? "missing"
                : new FileInfo(at).Length != f.Size ? $"{new FileInfo(at).Length.ToString("N0", CultureInfo.InvariantCulture)} bytes, not {f.Size.ToString("N0", CultureInfo.InvariantCulture)}"
                : DataManifest.Sha256Of(at) != f.Sha256 ? "other bytes than the manifest's" : null;
            if (bad is not null)
                return new($"the copy of the game the files were imported from ({source}) is not as the manifest has it either ({f.Path}: {bad}): "
                           + "select a copy of the game whose files are 1.1's (Check files says so) in step 1 and select Import again", source, [], 0);
            done += f.Size;
            progress?.Report(new(SetupProgress.Copy, f.Path, done, total));
        }
        var copied = new List<string>();
        foreach (var f in plan)
        {
            cancel.ThrowIfCancellationRequested();
            string to = DataImport.Place(verified.Folder, f.Path), partial = to + ".partial";
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            string read = DataImport.CopyHashing(index[f.Path], partial, f, progress, done, total, cancel);
            if (read != f.Sha256)
            {
                File.Delete(partial);
                return new($"{f.Path} read other bytes from {source} as it was copied: the disk or the copy failed; {again}", source, copied, 0);
            }
            File.Move(partial, to, overwrite: true);
            if (DataManifest.Sha256Of(to) != f.Sha256)
                return new($"{f.Path} was copied, but read back other bytes: the disk or the copy failed; {again}", source, copied, 0);
            copied.Add(f.Path);
            done += f.Size;
            progress?.Report(new(SetupProgress.Copy, f.Path, done, total));
        }
        // Extra files over them: set aside, never deleted (as the import does).
        int setAside = 0;
        string aside = Path.Combine(Path.GetDirectoryName(verified.Folder)!, "set-aside", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        foreach (var extra in verified.Extra)
        {
            string from = DataImport.Place(verified.Folder, extra);
            if (!File.Exists(from)) continue;
            string moved = DataImport.Place(aside, extra);
            Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
            File.Move(from, moved);
            setAside++;
        }
        return new(null, source, copied, setAside);
    }
}
