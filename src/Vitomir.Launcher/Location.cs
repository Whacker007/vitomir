// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// Where the launcher runs from ("unzipping into a place like program files is not going to work for this. So
// somewhere like C:\Games, or somewhere else."): its own folder checked at its start, and said plainly with what to do.
using System.Text.RegularExpressions;

namespace Vitomir.Launcher;

/// <summary>What is wrong with the folder the launcher runs from.</summary>
public enum LocationKind
{
    /// <summary>Under <c>Program Files</c> or <c>Program Files (x86)</c> (by the known folders' paths, not their names).</summary>
    ProgramFiles,
    /// <summary>Inside a zip being browsed: Windows' (or a zip program's) temporary copy under the temporary folder, or the zip's own shell path.</summary>
    InsideZip,
    /// <summary>A folder it cannot write to (a test file could not be made and deleted there).</summary>
    NotWritable,
    /// <summary>The folder is not there (a developer's <c>--location</c> naming one that does not exist): not written to.</summary>
    Missing,
}

/// <summary>One thing wrong with the folder, in words.</summary>
public sealed record LocationProblem(LocationKind Kind, string Words);

/// <summary>
/// The machine's own folders the check compares with: the program files' known folders (<c>FOLDERID_ProgramFiles</c>,
/// <c>FOLDERID_ProgramFilesX86</c>, through .NET's <see cref="Environment.SpecialFolder"/>), and the temporary folders (the user's
/// <c>%TEMP%</c>, <c>%TMP%</c>, <c>%LOCALAPPDATA%\Temp</c>, .NET's <see cref="Path.GetTempPath"/>). A test gives its own.
/// </summary>
public sealed record LocationPlaces(IReadOnlyList<string> ProgramFiles, IReadOnlyList<string> Temp)
{
    /// <summary>This machine's.</summary>
    public static LocationPlaces ThisMachine()
    {
        static IEnumerable<string> Some(params string?[] all) => all.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!);
        var programs = Some(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                            Environment.GetEnvironmentVariable("ProgramW6432"));
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var temp = Some(Path.GetTempPath(), Environment.GetEnvironmentVariable("TEMP"), Environment.GetEnvironmentVariable("TMP"),
                        local.Length > 0 ? Path.Combine(local, "Temp") : null);
        return new([.. programs.Distinct(StringComparer.OrdinalIgnoreCase)], [.. temp.Distinct(StringComparer.OrdinalIgnoreCase)]);
    }
}

/// <summary>
/// **The check of the folder the launcher runs from**: at its start, its
/// own folder (<see cref="AppContext.BaseDirectory"/>) is checked for the three places Vitomir cannot work from, each said plainly with
/// what to do (<see cref="Advice"/>):
/// <list type="bullet">
/// <item>**under Program Files** (either one), by the known folders' paths, so a Windows in another language, or one whose program
/// files are on another drive, is caught too: Windows lets no program write there without asking, and Vitomir keeps its user data
/// apart but a package is meant to sit in a folder of the player's own;</item>
/// <item>**inside a zip being browsed**: a program started from a zip that Explorer shows as a folder is run from a temporary copy
/// Windows makes under the temporary folder (<c>%TEMP%\Temp1_&lt;zip&gt;.zip\</c>), or 7-Zip (<c>%TEMP%\7zO&lt;hex&gt;\</c>) or WinRAR
/// (<c>%TEMP%\Rar$EXa&lt;n&gt;.&lt;n&gt;\</c>) do the same, with only some of its files and gone when the zip is closed; and the
/// zip's own shell path (<c>...\Vitomir.zip\VitomirPlatform\bin</c>, a segment that is a <c>.zip</c> file) when it can tell;</item>
/// <item>**a folder it cannot write to**: a test file made and deleted there (<see cref="WriteTest"/>).</item>
/// </list>
/// Nothing is refused: the launcher shows what it found and the player can carry on. Pure but for the write test, which is given.
/// </summary>
public static class Location
{
    /// <summary>What to do, said with every problem.</summary>
    public const string Advice = @"Unzip Vitomir's zip into a folder of your own, such as C:\Games\Vitomir, and start vitomir-launcher.exe from there.";

    /// <summary>A folder Windows, 7-Zip or WinRAR makes for a program started from inside a zip (under the temporary folder).</summary>
    static readonly Regex ZipTemp = new(@"^(Temp\d+_.+\.zip|7zO[0-9A-Fa-f]+|Rar\$[A-Za-z]{2,4}\d+(\.\d+)?|wz[0-9A-Fa-f]{2,})$", RegexOptions.CultureInvariant);

    /// <summary>
    /// **The check** of <paramref name="folder"/>: what is wrong with it, nothing when all is well. <paramref name="writeTest"/> answers
    /// null when a file could be made and deleted in a folder, else why not (<see cref="WriteTest"/>); <paramref name="isFile"/> whether
    /// a path is a file (the zip's shell path), <see cref="File.Exists"/> by default.
    /// </summary>
    public static IReadOnlyList<LocationProblem> Check(string folder, LocationPlaces places, Func<string, string?> writeTest, Func<string, bool>? isFile = null)
    {
        isFile ??= File.Exists;
        string full = Norm(folder);
        var problems = new List<LocationProblem>();
        if (places.ProgramFiles.Select(Norm).FirstOrDefault(p => p.Length > 0 && Under(full, p)) is { } programs)
            problems.Add(new(LocationKind.ProgramFiles,
                $"Vitomir is in Program Files ({programs}), where it cannot write."));
        string? zip = null;
        foreach (var temp in places.Temp.Select(Norm).Where(t => t.Length > 0))
        {
            if (!Under(full, temp)) continue;
            var rest = Path.GetRelativePath(temp, full).Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (rest.FirstOrDefault(s => ZipTemp.IsMatch(s)) is { } made) { zip = $"a temporary copy of a zip ({Path.Combine(temp, made)})"; break; }
        }
        if (zip is null)
        {
            // The zip's own shell path: a segment of the folder's path that is a .zip file.
            string at = "";
            foreach (var part in full.Split(Path.DirectorySeparatorChar))
            {
                at = at.Length == 0 ? (part.Length == 0 ? Path.DirectorySeparatorChar.ToString() : part + Path.DirectorySeparatorChar) : Path.Combine(at, part);
                if (part.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && isFile(at)) { zip = $"the zip {at}"; break; }
            }
        }
        if (zip is not null)
            problems.Add(new(LocationKind.InsideZip,
                $"Vitomir is running from inside its zip ({zip}), which does not work."));
        if (!Directory.Exists(full))
            problems.Add(new(LocationKind.Missing, $"The folder {full} is not there (nothing was written to it)."));
        else if (zip is null && writeTest(full) is { } why)
            problems.Add(new(LocationKind.NotWritable,
                $"Vitomir cannot write in its folder {full} ({why}). It needs a folder of your own that it can write in."));
        return problems;
    }

    /// <summary>
    /// **The write test**: a file of its own made in <paramref name="folder"/> (<c>.vitomir-write-test-&lt;guid&gt;.tmp</c>, new, never
    /// one that is there), a byte written, and deleted again. Answers null when that worked, else what failed.
    /// </summary>
    public static string? WriteTest(string folder)
    {
        string path = Path.Combine(folder, $".vitomir-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) f.WriteByte(0);
            File.Delete(path);
            return File.Exists(path) ? "a test file could be made there but not deleted" : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
            return e is UnauthorizedAccessException ? "it is not allowed to" : e.Message;
        }
    }

    /// <summary>The check's words: each problem, then <see cref="Advice"/>; nothing when all is well.</summary>
    public static IReadOnlyList<string> Words(IReadOnlyList<LocationProblem> problems) =>
        problems.Count == 0 ? []
        // Inside a zip, its temporary folder not there follows from it: said once (N3).
        : [.. problems.Where(p => !(p.Kind == LocationKind.Missing && problems.Any(q => q.Kind == LocationKind.InsideZip))).Select(p => p.Words), Advice];

    static string Norm(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return ""; }
    }

    static bool Under(string path, string folder) =>
        path.Equals(folder, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
