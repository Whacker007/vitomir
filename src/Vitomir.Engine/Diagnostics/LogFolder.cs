// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Where the logs live between runs.
//
// The engine keeps a basic record of every launch whether or not anybody asked for one, because the
// run you most want a log of is the one that fell over before you thought to turn logging on. Each
// launch starts with empty plain-text files in Logs\; the previous launch's are zipped into
// Logs\Archive\ first, named for when that run started, and the oldest are dropped once there are
// more than the settings keep.
using System.IO.Compression;

namespace Vitomir.Engine.Diagnostics;

/// <summary>The logs folder for one installation, and the archive of earlier runs beneath it.</summary>
public sealed class LogFolder(string root)
{
    public string Root { get; } = root;
    public string Archive => Path.Combine(Root, "Archive");

    public const string Extension = ".log";
    public const string ArchivePrefix = "Vitomir_";

    /// <summary>
    /// Files from the previous run, zipped into the archive and then removed, and the archive
    /// pruned to the newest <paramref name="keep"/>. Returns the zip written, or null when there
    /// was nothing to archive. Never throws: a log folder that cannot be tidied is not a reason to
    /// refuse to start.
    /// </summary>
    public string? Roll(int keep, Action<string>? complain = null)
    {
        try
        {
            Directory.CreateDirectory(Root);
            // A log another game still has open (two windows on one machine) is not the previous
            // run's: it stays where it is, for that game to go on writing.
            var previous = Directory.GetFiles(Root, "*" + Extension).Where(f => !InUse(f)).ToArray();
            if (previous.Length == 0) return null;
            if (keep <= 0)
            {
                foreach (var f in previous) Delete(f);
                return null;
            }

            Directory.CreateDirectory(Archive);
            var zip = Unique(Path.Combine(Archive, ArchivePrefix + Stamp(previous) + ".zip"));
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var file in previous)
                    using (var entry = archive.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal).Open())
                    using (var source = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        source.CopyTo(entry);
            foreach (var file in previous) Delete(file);
            Prune(keep, complain);
            return zip;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            complain?.Invoke($"could not archive the previous run's logs: {e.Message}");
            return null;
        }
    }

    /// <summary>Whether another process is writing the file (it cannot be opened for writing alone).</summary>
    public static bool InUse(string file)
    {
        try
        {
            using var _ = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>Drops the oldest archives once there are more than <paramref name="keep"/>.</summary>
    public void Prune(int keep, Action<string>? complain = null)
    {
        if (!Directory.Exists(Archive)) return;
        var zips = new DirectoryInfo(Archive).GetFiles(ArchivePrefix + "*.zip")
                                             .OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        foreach (var old in zips.Skip(Math.Max(0, keep)))
        {
            try { old.Delete(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { complain?.Invoke($"could not delete {old.Name}: {e.Message}"); }
        }
    }

    /// <summary>The archives present, newest first.</summary>
    public IEnumerable<string> Archives() =>
        Directory.Exists(Archive)
            ? new DirectoryInfo(Archive).GetFiles(ArchivePrefix + "*.zip")
                                        .OrderByDescending(f => f.LastWriteTimeUtc).Select(f => f.FullName)
            : [];

    /// <summary>
    /// When the run whose files these are was launched. The log writes that as its own first line,
    /// which is the honest answer; a file with no such line falls back to when it was created.
    /// </summary>
    static string Stamp(IReadOnlyList<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                foreach (var line in File.ReadLines(file).Take(8))
                {
                    if (!line.StartsWith("# " + FileLogSink.LaunchedAt, StringComparison.Ordinal)) continue;
                    var text = line[("# " + FileLogSink.LaunchedAt).Length..].Trim();
                    if (DateTime.TryParse(text, out var when)) return when.ToString("yyyy-MM-dd_HHmmss");
                }
            }
            catch (IOException) { }
        }
        try { return File.GetCreationTime(files[0]).ToString("yyyy-MM-dd_HHmmss"); }
        catch (IOException) { return DateTime.Now.ToString("yyyy-MM-dd_HHmmss"); }
    }

    static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        for (int n = 2; n < 1000; n++)
        {
            var candidate = Path.Combine(dir, $"{stem}_{n}.zip");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }

    static void Delete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
