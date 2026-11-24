// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
using System.Globalization;
using System.Security.Cryptography;
using Vitomir.Engine.Diagnostics;

namespace Vitomir;

/// <summary>
/// **The saves a recording started with, kept beside its tape** (ours — the replay harness's, as
/// <see cref="InputTape"/> is). A run reads the
/// player's save folder wherever it lists the slots, loads one from the menus or by the quick load, or hosts
/// a network game's save — and a save the run makes itself is read back from there too. So a recording that
/// loaded a slot replayed only until the player saved over it, and one that saved and loaded its own save
/// replayed against whatever the folder held by then.
///
/// - **A recording** (<see cref="Keep"/>) copies the save folder's files as they stand when the tape is opened
///   — every file under it: the slots of both families (<c>SP&lt;n&gt;.vsave</c>, <c>MP&lt;n&gt;.vsave</c>), their
///   <c>.previous</c> versions, a save named on a command line — into <c>&lt;tape&gt;.saves/</c>
///   (<see cref="Beside"/>), and the tape lists them: <c>saves &lt;count&gt; bytes=…</c>, then a
///   <c>savefile &lt;name&gt; bytes=… sha256=…</c> each. A file copy and a hash of the copy: no save is opened as a
///   save, so nothing of the game is started by it. An empty folder makes no folder, and the line says 0.
/// - **A replay** (<see cref="Seed"/>) of a tape with that line copies the listed files into a scratch folder,
///   which is the run's save folder from then on (<see cref="InputTape.SaveFolder"/>) for reads and writes
///   alike, and goes at the end of the run. A file listed and not beside the tape, or not the one recorded,
///   is said by name, and the replay carries on without it.
/// - A tape with no such line — one recorded before this — is replayed as it always was: reads from the
///   player's folder, writes to the scratch folder.
/// </summary>
static class TapeSaves
{
    /// <summary>A file of the save folder as the tape lists it: its name under the folder (<c>/</c> between folders), its size and its SHA-256.</summary>
    public readonly record struct Kept(string Name, long Bytes, string Sha);

    /// <summary>
    /// The folder a tape's saves are kept in: the tape's name with <c>.saves</c> after it, and never
    /// compressed — <c>sitting07.tape.saves/</c> for <c>sitting07.tape</c> and for <c>sitting07.tape.gz</c>
    /// alike (as the digest's name is the tape's with <c>.digest</c>, <see cref="LogFile.Beside"/>).
    /// </summary>
    public static string Beside(string tape) => LogFile.Stem(tape) + ".saves";

    /// <summary>
    /// A recording's: every file under <paramref name="saves"/> copied to <see cref="Beside"/>, and what was
    /// copied. A folder an earlier recording of the same name left is replaced, as its tape is. A file that
    /// cannot be copied is said and left out of the list.
    /// </summary>
    public static List<Kept> Keep(string saves, string tape)
    {
        string beside = Path.GetFullPath(Beside(tape));
        var kept = new List<Kept>();
        try
        {
            if (Directory.Exists(beside)) Directory.Delete(beside, recursive: true);
            // Listed before anything is written, and nothing of the kept folder itself (a tape recorded into the save folder).
            var files = Directory.Exists(saves)
                ? Directory.EnumerateFiles(saves, "*", SearchOption.AllDirectories).Select(Path.GetFullPath)
                    .Where(f => !Under(f, beside)).Order(StringComparer.Ordinal).ToList()
                : [];
            string root = Path.GetFullPath(saves);
            foreach (var file in files)
            {
                string name = Path.GetRelativePath(root, file).Replace('\\', '/');
                try
                {
                    string to = Path.Combine(beside, name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(file, to, overwrite: true);
                    kept.Add(new Kept(name, new FileInfo(to).Length, Sha(to)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Console.WriteLine($"record: {name} could not be kept beside the tape ({e.Message}); a replay will not have it");
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"record: the save folder could not be kept beside the tape ({e.Message}); a replay will have only what is listed");
        }
        Console.WriteLine(kept.Count == 0
            ? $"record: the save folder {saves} holds nothing: no save is kept beside the tape"
            : $"record: {Count(kept.Count)} of the save folder kept beside the tape, {Bytes(kept.Sum(k => k.Bytes))}: {beside}");
        return kept;
    }

    /// <summary>
    /// A replay's: <paramref name="scratch"/> made afresh and given the files the tape lists, out of
    /// <see cref="Beside"/>. Says what it did, and names every file listed that is missing or is not the one
    /// recorded (a changed one is copied all the same: it is what there is).
    /// </summary>
    public static string Seed(string tape, IReadOnlyList<Kept> recorded, string scratch)
    {
        string beside = Path.GetFullPath(Beside(tape));
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);   // an earlier process of this number, ended badly
        Directory.CreateDirectory(scratch);
        var missing = new List<string>();
        var changed = new List<string>();
        long bytes = 0;
        foreach (var k in recorded)
        {
            string local = k.Name.Replace('/', Path.DirectorySeparatorChar);
            string from = Path.Combine(beside, local);
            // A name is a file under the folder and nothing else: a tape is data, and may be somebody else's.
            if (Path.IsPathRooted(local) || k.Name.Split('/').Contains("..") || !File.Exists(from)) { missing.Add(k.Name); continue; }
            try
            {
                string to = Path.Combine(scratch, local);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(from, to, overwrite: true);
                long length = new FileInfo(to).Length;
                bytes += length;
                if (length != k.Bytes || (k.Sha != "-" && Sha(to) != k.Sha)) changed.Add(k.Name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { missing.Add($"{k.Name} ({e.Message})"); }
        }
        int there = recorded.Count - missing.Count;
        Console.WriteLine(recorded.Count == 0
            ? "replay: the recording began with an empty save folder; this run's is a scratch folder, empty"
            : $"replay: the save folder is a scratch copy of {(missing.Count == 0 ? "the " : there.ToString(CultureInfo.InvariantCulture) + " of the ")}" +
              $"{Count(recorded.Count)} the recording began with, {Bytes(bytes)}: {beside}");
        if (missing.Count > 0)
            Console.WriteLine($"replay: not beside the tape{(Directory.Exists(beside) ? "" : $" ({beside} is missing)")}: {string.Join(", ", missing)} — " +
                              "the replay will not be the recording where it reads one");
        if (changed.Count > 0)
            Console.WriteLine($"replay: not the {(changed.Count == 1 ? "file" : "files")} recorded (the size or the SHA-256 differs): {string.Join(", ", changed)} — " +
                              "the replay will not be the recording where it reads one");
        return scratch;
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or a file under it.</summary>
    public static bool Under(string path, string folder)
    {
        var how = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string full = Path.GetFullPath(path), root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return full.Equals(root, how) || full.StartsWith(root + Path.DirectorySeparatorChar, how);
    }

    static string Sha(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    static string Count(int files) => files == 1 ? "1 file" : files.ToString(CultureInfo.InvariantCulture) + " files";

    static string Bytes(long bytes) => bytes.ToString("#,0", CultureInfo.InvariantCulture) + " bytes";
}
