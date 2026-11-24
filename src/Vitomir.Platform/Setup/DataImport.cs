// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The import of 1.1's data into the platform's user folder, and the imported data as a start finds it.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vitomir.Platform.Setup;

/// <summary>Why an import was not accepted (<see cref="None"/> when it was).</summary>
public enum ImportRefusal { None, NotAnInstall, Inside, DataNotWhole, NoRoom, Failed }

/// <summary>
/// **The imported data's marker** (<see cref="ImportedData.MarkerName"/>), written last: the manifest it was checked against, the install it
/// came from (for 1.1's own saves, which stay there), its store, when and by what, how many files and bytes, and whether 1.1's options
/// file came with it. A folder without it is one that did not finish, and no start uses it.
/// </summary>
public sealed record ImportedMarker
{
    /// <summary>1: this marker.</summary>
    public int Format { get; init; } = 1;
    public string Manifest { get; init; } = "";
    public int ManifestVersion { get; init; }
    public string Source { get; init; } = "";
    /// <summary><c>GOG</c>, <c>Steam</c> or <c>unknown</c> (<see cref="SetupWords.StoreKey"/>).</summary>
    public string Store { get; init; } = "";
    public DateTime MadeUtc { get; init; }
    public string MadeBy { get; init; } = "";
    public int Files { get; init; }
    public long Bytes { get; init; }
    /// <summary>
    /// The scripts' sources (the manifest's optional part) came with it, checked: true only of an import made earlier. Now
    /// the import takes them no more — the game runs <c>Codex.nob</c>'s own compiled classes, and the SDK's sources are ignored even
    /// where they are (<c>CodexCompiler</c>) — so a new marker says false; an older one's files are still checked at a start.
    /// </summary>
    public bool Scripts { get; init; }
    /// <summary>1.1's own options file (<c>masquerade.ini</c>) was copied with it, unchecked (the player's own settings).</summary>
    public bool Options { get; init; }
}

/// <summary>What an import did, or why it was not accepted (<see cref="Refusal"/>, <see cref="Problems"/> naming each file).</summary>
public sealed record DataImportReport(
    string Source, string Target, ImportRefusal Refusal, IReadOnlyList<string> Problems,
    int Copied, int Kept, long BytesCopied, long BytesNeeded, PartState Scripts, bool OptionsCopied,
    IReadOnlyList<string> Overriding, int SetAside, ImportedMarker? Marker)
{
    public bool Accepted => Refusal == ImportRefusal.None;
}

/// <summary>
/// **The import of the base game's data** (into the user folder, the install never changed). The
/// install is checked by size first (a missing or changed file refused by name before anything is written), the room needed is checked
/// against the drive's free space, and then each file of the manifest's required part is copied to a <c>.partial</c> beside its place with its SHA-256 taken as it is read, moved in once whole,
/// and read back and hashed again: **a file is accepted only when both hashes are the manifest's**. A file already there at its size and
/// hash is kept (a stopped import carries on; a finished one run again checks everything and copies nothing). Anything else in the folder
/// is set aside (<c>Imported\set-aside\&lt;when&gt;\</c>), never deleted, so nothing but the manifest's files is ever read from it. The
/// marker is written last, through a temporary file: until it is there the folder is not used.
/// <para>The copy is the install's own shape (the archives whole, the loose files where 1.1 has them), so the game reads it exactly as it
/// reads the install, less the overriding files left out. <c>GameData.Make</c>'s layout by kind (<c>vitomir-tools</c>'
/// <c>--install-data</c>) is not used: it is the engine's, which the platform does not reference, it takes every loose file and every
/// <c>.nob</c> the install has (what this import must leave out), and the same manifest checks both the install and this copy.</para>
/// </summary>
public static class DataImport
{
    /// <summary>The room kept free beyond what the files need.</summary>
    public const long Margin = 64L << 20;

    /// <summary>1.1's options file, copied when the install has one (each player's own, so not in the manifest; 1.1 reads it from its folder).</summary>
    public const string OptionsFile = "masquerade.ini";

    /// <summary>Imports <paramref name="source"/> into <paramref name="target"/> (<see cref="ImportedData.Folder"/> for the player's).</summary>
    public static DataImportReport Run(string source, string target, DataManifest manifest, ISetupMachine machine, string madeBy,
                                       IProgress<SetupProgress>? progress = null, CancellationToken cancel = default)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        DataImportReport Refuse(ImportRefusal why, IReadOnlyList<string> problems, PartState scripts = PartState.Absent, long needed = 0, IReadOnlyList<string>? overriding = null) =>
            new(source, target, why, problems, 0, 0, 0, needed, scripts, false, overriding ?? [], 0, null);

        if (!InstallFinder.IsInstall(source)) return Refuse(ImportRefusal.NotAnInstall, [$"{source} is not a VTMR install: it has no Resource.nob"]);
        if (Inside(target, source) || Inside(source, target))
            return Refuse(ImportRefusal.Inside, [$"the imported data's folder ({target}) and the install ({source}) cannot be inside one another"]);

        // Sizes first: a missing or changed file is refused by name before anything is written.
        var check = DataCheck.Run(source, manifest, CheckDepth.Sizes, progress, cancel);
        if (!check.GameWhole) return Refuse(ImportRefusal.DataNotWhole, [.. check.Problems.Select(p => p.ToString())], check.Scripts, 0, check.Overriding);
        // The required part only: the scripts the game runs are Codex.nob's own classes, in the game part. The optional part — the
        // SDK's sources — stays the manifest's list that knows them (the check says how the install's stand), never imported.
        var plan = manifest.Files.Where(f => manifest.Parts.First(p => p.Name == f.Part).Required).ToList();
        string? options = InstallFinder.FileIn(source, OptionsFile);

        long needed = plan.Sum(f => new FileInfo(Place(target, f.Path)) is { Exists: true } have && have.Length == f.Size ? 0 : f.Size);
        long free = machine.FreeBytes(target);
        if (needed > free - Margin)
            return Refuse(ImportRefusal.NoRoom, [$"not enough room: the import needs {SetupWords.Size(needed)} more and {Path.GetPathRoot(target)} has {SetupWords.Size(free)} free (and {SetupWords.Size(Margin)} is kept free)"],
                          check.Scripts, needed, check.Overriding);

        var problems = new List<string>();
        int copied = 0, kept = 0;
        long bytesCopied = 0, done = 0, total = plan.Sum(f => f.Size);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Directory.CreateDirectory(target);
            // An earlier marker goes first: from here until the new one is written, the folder is not used.
            File.Delete(Path.Combine(target, ImportedData.MarkerName));
            foreach (var f in plan)
            {
                cancel.ThrowIfCancellationRequested();
                string to = Place(target, f.Path);
                var have = new FileInfo(to);
                if (have.Exists && have.Length == f.Size && DataManifest.Sha256Of(to) == f.Sha256) { kept++; placed.Add(f.Path); }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    string partial = to + ".partial";
                    string read = CopyHashing(check.Found[f.Path], partial, f, progress, done, total, cancel);
                    string? wrong = read != f.Sha256 ? $"{f.Path}: other bytes in the install than the manifest's (SHA-256 {read[..12]}..., not {f.Sha256[..12]}...)" : null;
                    if (wrong is null)
                    {
                        File.Move(partial, to, overwrite: true);
                        string back = DataManifest.Sha256Of(to);
                        if (back != f.Sha256) wrong = $"{f.Path}: copied, but read back other bytes (SHA-256 {back[..12]}...): the disk or the copy failed";
                    }
                    else File.Delete(partial);
                    if (wrong is null) { copied++; bytesCopied += f.Size; placed.Add(f.Path); }
                    else problems.Add(wrong);
                }
                done += f.Size;
                progress?.Report(new(SetupProgress.Copy, f.Path, done, total));
            }
            if (problems.Count > 0)
                return new(source, target, ImportRefusal.DataNotWhole, problems, copied, kept, bytesCopied, needed, check.Scripts, false, check.Overriding, 0, null);

            bool optionsCopied = false;
            string optionsTo = Path.Combine(target, OptionsFile);
            if (options is not null && !File.Exists(optionsTo)) { File.Copy(options, optionsTo); optionsCopied = true; }
            if (File.Exists(optionsTo)) placed.Add(OptionsFile);

            // Anything else in the folder — a file put there by hand, an older import's scripts' sources, a stopped copy's .partial — set aside or
            // deleted (only a .partial, which is this import's own), so the game reads nothing but the manifest's files from it.
            int setAside = 0;
            string aside = Path.Combine(Path.GetDirectoryName(target)!, "set-aside", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            foreach (var (rel, full) in DataCheck.Index(target))
            {
                if (placed.Contains(rel) || rel.Equals(ImportedData.MarkerName, StringComparison.OrdinalIgnoreCase)
                    || rel.Equals(ImportedData.ManifestCopyName, StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) { File.Delete(full); continue; }
                string moved = Place(aside, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
                File.Move(full, moved);
                setAside++;
            }
            foreach (var dir in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);

            // The manifest it was checked against, kept beside it: a start checks the files against this copy (a developer's
            // manifest, --manifest, is then known as one: ImportedData.Open).
            File.WriteAllText(Path.Combine(target, ImportedData.ManifestCopyName), manifest.Text());
            var marker = new ImportedMarker
            {
                Manifest = manifest.Name, ManifestVersion = manifest.Version, Source = source, Store = SetupWords.StoreKey(check.Store),
                MadeUtc = DateTime.UtcNow, MadeBy = madeBy,
                Files = placed.Count(p => !p.Equals(OptionsFile, StringComparison.OrdinalIgnoreCase)),
                Bytes = plan.Where(f => placed.Contains(f.Path)).Sum(f => f.Size), Scripts = false, Options = File.Exists(optionsTo),
            };
            ImportedData.WriteMarker(target, marker);
            return new(source, target, ImportRefusal.None, problems, copied, kept, bytesCopied, needed, check.Scripts, optionsCopied, check.Overriding, setAside, marker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"stopped: {e.Message} (nothing in the install was changed; run the import again to continue from where it stopped)");
            return new(source, target, ImportRefusal.Failed, problems, copied, kept, bytesCopied, needed, check.Scripts, false, check.Overriding, 0, null);
        }
    }

    /// <summary>Where the file the install knows as <paramref name="relative"/> goes under <paramref name="root"/>.</summary>
    public static string Place(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Copies <paramref name="from"/> to <paramref name="to"/> (made afresh), answering the SHA-256 of the bytes read.</summary>
    internal static string CopyHashing(string from, string to, ManifestFile f, IProgress<SetupProgress>? progress, long before, long total, CancellationToken cancel)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
        using (var output = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            var buffer = new byte[1 << 20];
            long read = 0, said = 0;
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, n);
                output.Write(buffer, 0, n);
                read += n;
                if (read - said >= 64L << 20) { said = read; progress?.Report(new(SetupProgress.Copy, f.Path, before + read, total)); }
            }
            output.Flush(flushToDisk: true);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    static bool Inside(string inner, string outer)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return (inner + Path.DirectorySeparatorChar).StartsWith(outer + Path.DirectorySeparatorChar, cmp);
    }
}

/// <summary>How the imported data stands at a start (<see cref="ImportedData.Open"/>).</summary>
public sealed record ImportedState(string Folder, ImportedMarker? Marker, IReadOnlyList<string> Problems, IReadOnlyList<string> Notes)
{
    /// <summary>Imported, its marker there, every file of it at its size.</summary>
    public bool Ready => Marker is not null && Problems.Count == 0;

    /// <summary>Nothing imported yet (no marker).</summary>
    public bool None => Marker is null;
}

/// <summary>
/// **The imported data in the user folder**: <c>&lt;user data&gt;\Imported\vtmr-1.1\</c> (the platform's user data,
/// <c>%LOCALAPPDATA%\Vitomir</c> or <c>VITOMIR_USERDATA</c>), finished when its marker is there.
/// </summary>
public static class ImportedData
{
    /// <summary>The folder of imported things in the user data.</summary>
    public const string ImportedFolderName = "Imported";

    /// <summary>The base game's data's folder in it.</summary>
    public const string GameFolderName = "vtmr-1.1";

    /// <summary>The marker, written last.</summary>
    public const string MarkerName = "vitomir-imported.json";

    /// <summary>The manifest the data was checked against, kept beside the marker.</summary>
    public const string ManifestCopyName = "vitomir-imported-manifest.tsv";

    /// <summary>The base game's imported data under <paramref name="userData"/>.</summary>
    public static string Folder(string userData) => Path.Combine(userData, ImportedFolderName, GameFolderName);

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    /// <summary>The marker of the folder, or null when there is none or it does not read.</summary>
    public static ImportedMarker? ReadMarker(string folder)
    {
        string path = Path.Combine(folder, MarkerName);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<ImportedMarker>(File.ReadAllText(path), Json); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }

    internal static void WriteMarker(string folder, ImportedMarker marker)
    {
        string path = Path.Combine(folder, MarkerName), temp = path + ".writing";
        File.WriteAllText(temp, JsonSerializer.Serialize(marker, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// The install a folder of imported data came from, when it is one and that install is still there (1.1's own saves are read from
    /// it: the load screen's list, <c>GameData.OriginalInstall</c>); null otherwise.
    /// </summary>
    public static string? SourceOf(string folder) =>
        ReadMarker(folder) is { Source.Length: > 0 } m && Directory.Exists(m.Source) ? m.Source : null;

    /// <summary>
    /// **The imported data as a start finds it**: its marker, the manifest it was checked against (its copy beside the marker), and every
    /// file of the parts it took at that manifest's size (a few milliseconds; the import hashed them). Data checked against another manifest
    /// than <paramref name="carried"/> (this program's, <see cref="DataManifest.Builtin"/>) is to be imported again — unless
    /// <paramref name="developer"/>: a developer's manifest (<c>--manifest</c>) is then taken, and <see cref="ImportedState.Notes"/> says so.
    /// </summary>
    public static ImportedState Open(string userData, DataManifest carried, bool developer = false)
    {
        string folder = Folder(userData);
        var marker = ReadMarker(folder);
        if (marker is null) return new(folder, null, [], []);
        var problems = new List<string>();
        var notes = new List<string>();
        DataManifest kept;
        try { kept = DataManifest.Read(Path.Combine(folder, ManifestCopyName)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(folder, marker, [$"the manifest it was checked against ({ManifestCopyName}) cannot be read: {e.Message}"], []);
        }
        if (kept.Name != marker.Manifest || kept.Version != marker.ManifestVersion)
            problems.Add($"its manifest's copy is {kept}, and its marker says {marker.Manifest} v{marker.ManifestVersion}");
        if (kept.Name != carried.Name || kept.Version != carried.Version)
        {
            if (developer) notes.Add($"imported against the manifest {kept}, not this Vitomir's {carried}: taken (--developer)");
            else problems.Add($"imported against the manifest {kept}, and this Vitomir carries {carried}: import it again");
        }
        foreach (var f in kept.Files)
        {
            if (f.Part == DataManifest.Scripts && !marker.Scripts) continue;
            var have = new FileInfo(DataImport.Place(folder, f.Path));
            if (!have.Exists) problems.Add($"{f.Path}: missing");
            else if (have.Length != f.Size) problems.Add($"{f.Path}: {have.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes, not {f.Size.ToString("N0", CultureInfo.InvariantCulture)}");
        }
        return new(folder, marker, problems, notes);
    }
}
