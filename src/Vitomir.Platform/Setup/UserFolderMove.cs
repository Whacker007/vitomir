// Copyright (c) 2026 Whacker007. Part of Vitomir, under the Vitomir Licence 1.0 (the repository's LICENSE).
// Free for noncommercial use, never to be sold, never shipped with the original game's files.
// The user folder set up on a first start (the usual place, an existing one, or portable), and moved to another folder: copied,
// every file's SHA-256 checked, then vitomir.ini written and the old folder removed.
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace Vitomir.Platform.Setup;

/// <summary>What a first start's choice or a move answered: done or not, one line in words, and the user folder now.</summary>
public sealed record UserFolderAnswer(bool Done, string Line, string? Folder = null);

/// <summary>
/// **A first start's choices** (no ini, and the usual place missing or empty: <see cref="UserFolderChoice.Chosen"/> false), as the
/// launcher's Play page offers them: **Use the usual place** (it is made, and marked), **Use an existing one…** (a folder that looks like
/// a user folder of Vitomir's, <see cref="UserFolderMarks.LooksLikeOne"/>; the ini written) and **Make portable** (<c>UserData</c> inside the
/// Vitomir folder; the ini written). The last two need the Vitomir folder to be writable (<see cref="IniWhy"/>). Each also mends a bad ini,
/// by writing its <c>user</c> again.
/// </summary>
public static class UserFolderSetup
{
    /// <summary>Null when <c>vitomir.ini</c> can be written at the top of the Vitomir folder, else why not (Program Files; a folder it cannot write in).</summary>
    public static string? IniWhy(UserFolderPlace place, FolderPlaces places)
    {
        string top = place.TopFolder;
        if (places.ProgramFilesOf(top) is { } programs) return $"the Vitomir folder is in Program Files ({programs}), where Windows lets no program write without asking";
        if (!Directory.Exists(top)) return $"the Vitomir folder {top} is not there";
        if (places.WriteTest(top) is { } why) return $"the Vitomir folder {top} cannot be written to ({why})";
        return null;
    }

    /// <summary>
    /// **Writes <c>vitomir.ini</c>'s <c>user</c>** (<paramref name="value"/>; empty for the usual place) through a temporary file: the rest of
    /// an ini there kept, or, when that ini has problems, a new one written and the old kept beside it as <c>vitomir.ini.previous</c>. Null
    /// when written, else why not.
    /// </summary>
    public static string? WriteIni(UserFolderPlace place, string value)
    {
        string path = place.IniPath, temp = path + ".writing";
        try
        {
            string? existing = File.Exists(path) ? File.ReadAllText(path) : null;
            if (existing is not null && VitomirIni.Parse(path, existing).Problems.Count > 0)
            {
                File.Copy(path, path + ".previous", overwrite: true);
                existing = null;
            }
            File.WriteAllText(temp, VitomirIni.WithUser(existing, value));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception again) when (again is IOException or UnauthorizedAccessException) { }
            return $"{path} could not be written ({e.Message})";
        }
    }

    /// <summary>**Use the usual place**: <c>%LOCALAPPDATA%\Vitomir</c> made and marked; an ini there with a problem or another folder has its <c>user</c> emptied.</summary>
    public static UserFolderAnswer UseUsual(UserFolderPlace place, FolderPlaces places, string madeBy)
    {
        // An older Vitomir's folder of the same name is never taken (nor deleted): the player renames or deletes it, or chooses another place.
        if (!UserFolderMarks.EmptyOrMissing(place.Usual) && !UserFolderMarks.HasMark(place.Usual))
            return new(false, UserFolderChoice.OlderVitomirWords + ".");
        var ini = VitomirIni.Read(place.IniPath);
        if (ini.Exists && (ini.Problems.Count > 0 || !string.IsNullOrWhiteSpace(ini.User)) && WriteIni(place, "") is { } why)
            return new(false, $"The usual place could not be chosen: {why}.");
        try { UserFolderMarks.Mark(place.Usual, madeBy); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(false, $"{place.Usual} could not be made ({e.Message})."); }
        return new(true, $"Your user folder is the usual place, {place.Usual}.", place.Usual);
    }

    /// <summary>**Use an existing one…**: <paramref name="folder"/> taken when it looks like a user folder of Vitomir's and can be used; the ini written.</summary>
    public static UserFolderAnswer UseExisting(UserFolderPlace place, string folder, FolderPlaces places)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (!Directory.Exists(full)) return new(false, $"{full} is not there.");
        if (!UserFolderMarks.LooksLikeOne(full))
            return new(false, $"{full} does not look like a user folder of Vitomir's: it holds none of what one holds (Imported, Modules, Games, PlayerModules, Launcher, Logs, settings.json).");
        if (places.ProgramFilesOf(full) is { } programs) return new(false, $"{full} is in Program Files ({programs}): Windows lets no program write there without asking.");
        if (place.Bin is { } bin && UserFolderMarks.Under(full, bin)) return new(false, $"{full} is in Vitomir's own bin folder, which a new version replaces.");
        if (places.WriteTest(full) is { } cannot) return new(false, $"{full} cannot be written to ({cannot}).");
        if (IniWhy(place, places) is { } why) return new(false, $"vitomir.ini cannot be written: {why}.");
        if (WriteIni(place, UserFolderChoice.IniValue(place, full)) is { } failed) return new(false, char.ToUpperInvariant(failed[0]) + failed[1..] + ".");
        return new(true, $"Your user folder is {full} (vitomir.ini names it).", full);
    }

    /// <summary>**Make portable**: <c>UserData</c> inside the Vitomir folder made and marked, the ini written (<c>user = UserData</c>).</summary>
    public static UserFolderAnswer MakePortable(UserFolderPlace place, FolderPlaces places, string madeBy)
    {
        if (IniWhy(place, places) is { } why) return new(false, $"Vitomir cannot be made portable: {why}.");
        string folder = place.Portable;
        if (!UserFolderMarks.EmptyOrMissing(folder) && !UserFolderMarks.LooksLikeOne(folder))
            return new(false, $"{folder} is there already and is not a user folder of Vitomir's: move it away first.");
        try { UserFolderMarks.Mark(folder, madeBy); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new(false, $"{folder} could not be made ({e.Message})."); }
        if (WriteIni(place, UserFolderPlace.PortableName) is { } failed) return new(false, char.ToUpperInvariant(failed[0]) + failed[1..] + ".");
        return new(true, $"Vitomir is portable: your user folder is {folder}, inside the Vitomir folder (vitomir.ini names it).", folder);
    }
}

/// <summary>
/// **The user folder in use**: a game holds <c>vitomir-in-use.lock</c> in it, shared, for its whole run (games may run side by side); a
/// move holds it alone while it copies, so a game cannot start then and a move cannot start while a game runs. Windows' file sharing
/// (and the advisory lock .NET takes elsewhere) does the telling; a process that ends lets go of it whatever way it ends.
/// </summary>
public static class UserFolderLock
{
    /// <summary>The lock's file at the user folder's top (never copied by a move).</summary>
    public const string FileName = "vitomir-in-use.lock";

    /// <summary>
    /// **A game's hold**, shared: the open file (dispose it at the game's end), or null when it could not be made for a reason that is not
    /// a move (the start goes on: the folder's own problems are the readiness check's). <paramref name="moving"/> is true when a move holds it.
    /// </summary>
    public static FileStream? HoldShared(string folder, out bool moving)
    {
        moving = false;
        try
        {
            Directory.CreateDirectory(folder);
            return new FileStream(Path.Combine(folder, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException) { moving = true; return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>**A move's hold**, alone: the open file, or null when a game holds it (or it cannot be made: <paramref name="why"/> says).</summary>
    public static FileStream? TakeAlone(string folder, out string? why)
    {
        why = null;
        try { return new FileStream(Path.Combine(folder, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { why = "the game is running (it holds the user folder): close it first"; return null; }
        catch (UnauthorizedAccessException e) { why = $"the user folder cannot be written to ({e.Message})"; return null; }
    }
}

/// <summary>A move asked for: from the user folder in use to <see cref="To"/> (portable: <c>UserData</c> in the Vitomir folder), its files and bytes, what is kept already from a stopped move, its warnings.</summary>
public sealed record UserFolderMovePlan(UserFolderChoice From, string To, bool Portable, int Files, long Bytes, bool Resuming, IReadOnlyList<string> Warnings)
{
    /// <summary>The ini's value for the new folder.</summary>
    public string IniValue => UserFolderChoice.IniValue(From.Place, To);
}

/// <summary>A move's end: done, cancelled or failed (why, and the old folder still in use), the files copied and kept, and whether the old folder went.</summary>
public sealed record UserFolderMoveReport(bool Done, bool Cancelled, string? Failed, int Copied, int Kept, long Bytes, bool OldRemoved, string? OldKept, string? Folder)
{
    /// <summary>What it did, in a line.</summary>
    public string Line =>
        Done ? $"Moved to {Folder}. "
               + (OldRemoved ? "The old folder was removed." : OldKept is null ? "The old folder was kept." : $"The old folder was kept: {OldKept}")
        : Cancelled ? "Move cancelled: your user folder is still the old one. Move to the same folder to continue."
        : $"The move failed: {Failed}. Your user folder is still the old one. Move to the same folder to continue.";
}

/// <summary>
/// **The user folder moved** to a folder of the player's choice or into the Vitomir folder (portable):
/// <see cref="Plan"/> checks it first (refused for Program Files, the Vitomir folder's own <c>bin</c>, the Vitomir folder itself, a
/// folder inside the user folder or one holding it, a folder that is not empty and is not a stopped move's to the same place, a user
/// folder named by <c>VITOMIR_USERDATA</c>, a game running, and the room; warned for a synced folder); <see cref="Run"/> copies every
/// file (to a <c>.partial</c>, its SHA-256 taken as it is read, moved in, read back and hashed again: a copy is accepted only when the
/// two agree), a file already there at its size and hash kept (so a cancelled or failed move carries on), then writes <c>vitomir.ini</c>
/// and only then removes the old folder (or keeps it when asked). Until the ini is written the old folder stays the one in use.
/// </summary>
public static class UserFolderMove
{
    /// <summary>A move's own record in the folder it copies to, while it copies (which folder it moves from, when it started).</summary>
    public const string ProgressName = "vitomir-move.json";

    /// <summary>Room kept free on the target's drive after the copy.</summary>
    public const long Reserve = 64L * 1024 * 1024;

    /// <summary>What <paramref name="target"/> means: <c>portable</c> (any capitals) the Vitomir folder's <c>UserData</c>, else a folder's path.</summary>
    public static string TargetOf(UserFolderPlace place, string target) =>
        target.Equals("portable", StringComparison.OrdinalIgnoreCase) ? place.Portable : Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));

    /// <summary>**The move checked before anything is written**: the plan, or null with why it is refused.</summary>
    public static UserFolderMovePlan? Plan(UserFolderChoice from, string target, FolderPlaces places, ISetupMachine machine, out string? refused)
    {
        refused = null;
        var place = from.Place;
        if (from.Problem is { } bad) { refused = $"your user folder is not set ({bad}): choose it again first"; return null; }
        if (from.Source == UserFolderSource.Variable)
        {
            refused = $"{UserFolderPlace.VariableName} names your user folder (a developer's): it beats vitomir.ini, which a move writes, so the move would not take; unset it first";
            return null;
        }
        string old = Path.TrimEndingDirectorySeparator(Path.GetFullPath(from.Folder));
        if (UserFolderMarks.EmptyOrMissing(old)) { refused = $"{old} holds nothing yet: there is nothing to move"; return null; }
        bool portable = target.Equals("portable", StringComparison.OrdinalIgnoreCase);
        string to;
        try { to = TargetOf(place, target); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { refused = $"{target} is not a folder's path ({e.Message})"; return null; }
        string top = Path.TrimEndingDirectorySeparator(Path.GetFullPath(place.TopFolder));
        if (to.Equals(old, StringComparison.OrdinalIgnoreCase)) { refused = $"{to} is your user folder already"; return null; }
        if (UserFolderMarks.Under(to, old)) { refused = $"{to} is inside your user folder"; return null; }
        if (UserFolderMarks.Under(old, to)) { refused = $"your user folder is inside {to}"; return null; }
        if (places.ProgramFilesOf(to) is { } programs) { refused = $"{to} is in Program Files ({programs}), where Windows lets no program write without asking"; return null; }
        if (place.Bin is { } bin && UserFolderMarks.Under(to, bin)) { refused = $"{to} is in Vitomir's own bin folder, which a new version replaces"; return null; }
        if (to.Equals(top, StringComparison.OrdinalIgnoreCase)) { refused = $"{to} is the Vitomir folder itself: choose a folder inside it (portable) or elsewhere"; return null; }
        if (File.Exists(to)) { refused = $"{to} is a file, not a folder"; return null; }
        string root = Path.GetPathRoot(to) ?? "";
        if (root.Length > 0 && !places.DriveThere(root)) { refused = $"the drive of {to} is not there"; return null; }
        bool resuming = false;
        if (!UserFolderMarks.EmptyOrMissing(to))
        {
            if (ReadProgress(to) is { } was && was.Equals(old, StringComparison.OrdinalIgnoreCase)) resuming = true;
            else if (UserFolderMarks.LooksLikeOne(to)) { refused = $"{to} is another user folder of Vitomir's already: to use it as it is, choose it with Use an existing one…"; return null; }
            else { refused = $"{to} is not empty and is not a user folder of Vitomir's: choose an empty folder or a new one"; return null; }
        }
        if (portable && UserFolderSetup.IniWhy(place, places) is { } iniWhy) { refused = $"Vitomir cannot be made portable: {iniWhy}"; return null; }
        if (!portable && UserFolderSetup.IniWhy(place, places) is { } why) { refused = $"vitomir.ini cannot be written: {why}"; return null; }
        // A game running holds the folder: a move is refused then (and holds it alone while it copies).
        using (var alone = UserFolderLock.TakeAlone(old, out string? held))
            if (alone is null) { refused = held; return null; }
        var files = Files(old);
        long bytes = files.Sum(f => f.Length), there = 0;
        if (resuming)
            foreach (var f in files)
                if (new FileInfo(Path.Combine(to, Path.GetRelativePath(old, f.FullName))) is { Exists: true } have && have.Length == f.Length) there += f.Length;
        long free = machine.FreeBytes(to);
        if (bytes - there + Reserve > free)
        {
            refused = $"not enough room on the drive of {to}: {SetupWords.Size(bytes - there)} to copy, {SetupWords.Size(Math.Max(0, free))} free (64 MB is kept free)";
            return null;
        }
        var warnings = new List<string>();
        if (places.SyncedBy(to) is { } service)
            warnings.Add($"{to} is in {service}, a synced folder: your saves and the imported game files are uploaded, and a file the sync holds can make a save fail");
        return new(from, to, portable, files.Count, bytes, resuming, warnings);
    }

    /// <summary>The files of a user folder to move, in order (the in-use lock left out).</summary>
    static List<FileInfo> Files(string folder) =>
        [.. new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => !(f.Name.Equals(UserFolderLock.FileName, StringComparison.OrdinalIgnoreCase) && f.DirectoryName is { } d && Path.TrimEndingDirectorySeparator(d).Equals(folder, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f.FullName, StringComparer.Ordinal)];

    static string? ReadProgress(string folder)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ProgressName)));
            return doc.RootElement.TryGetProperty("from", out var from) ? from.GetString() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary>
    /// **The move run**: every file copied and checked (above), the progress reported (<see cref="SetupProgress.Copy"/>, the file, bytes
    /// done of all), <paramref name="cancel"/> leaving the old folder in use; then the new folder marked, the ini written, <paramref name="switched"/>
    /// told (the launcher moves its own log there), and the old folder removed unless <paramref name="keepOld"/>. <paramref name="beforeFile"/>
    /// is called before each file is copied (a test fails one there, as a full disk would).
    /// </summary>
    public static UserFolderMoveReport Run(UserFolderMovePlan plan, bool keepOld, string madeBy, IProgress<SetupProgress>? progress = null,
        CancellationToken cancel = default, Action<string>? switched = null, Action<string>? beforeFile = null)
    {
        string old = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.From.Folder)), to = plan.To;
        int copied = 0, kept = 0;
        long done = 0;
        using (var alone = UserFolderLock.TakeAlone(old, out string? held))
        {
            if (alone is null) return new(false, false, held, 0, 0, 0, false, null, null);
            try
            {
                Directory.CreateDirectory(to);
                File.WriteAllText(Path.Combine(to, ProgressName), JsonSerializer.Serialize(new Dictionary<string, string>
                    { ["format"] = "vitomir-move/1", ["from"] = old, ["startedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }));
                var files = Files(old);
                long total = files.Sum(f => f.Length);
                foreach (var dir in new DirectoryInfo(old).EnumerateDirectories("*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(old, dir.FullName)));
                foreach (var f in files)
                {
                    cancel.ThrowIfCancellationRequested();
                    string rel = Path.GetRelativePath(old, f.FullName), dest = Path.Combine(to, rel);
                    progress?.Report(new(SetupProgress.Copy, rel, done, total));
                    beforeFile?.Invoke(rel);
                    string source = Hash(f.FullName, null, cancel);
                    if (File.Exists(dest) && new FileInfo(dest).Length == f.Length && Hash(dest, null, cancel) == source) kept++;
                    else
                    {
                        string partial = dest + ".partial";
                        string read = Hash(f.FullName, partial, cancel);
                        File.Move(partial, dest, overwrite: true);
                        if (Hash(dest, null, cancel) != read) return Failed($"{rel}: the copy reads back other bytes than were written");
                        if (read != source) return Failed($"{rel} changed while it was copied");
                        copied++;
                    }
                    done += f.Length;
                }
                progress?.Report(new(SetupProgress.Copy, "", total, total));
                File.Delete(Path.Combine(to, ProgressName));
                UserFolderMarks.Mark(to, madeBy);
            }
            catch (OperationCanceledException) { return new(false, true, null, copied, kept, done, false, null, null); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Failed(e.Message); }
            if (UserFolderSetup.WriteIni(plan.From.Place, plan.IniValue) is { } why)
                return Failed($"the copy in {to} is whole, but {why}");
        }
        switched?.Invoke(to);
        if (keepOld) return new(true, false, null, copied, kept, done, false, null, to);
        try
        {
            Directory.Delete(old, recursive: true);
            return new(true, false, null, copied, kept, done, true, null, to);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(true, false, null, copied, kept, done, false, $"not all of it could be removed ({e.Message}); delete {old} yourself", to);
        }

        UserFolderMoveReport Failed(string why) => new(false, false, why, copied, kept, done, false, null, null);
    }

    /// <summary>A file's SHA-256 as it is read (lower-case hex), its bytes written to <paramref name="copyTo"/> on the way when given.</summary>
    static string Hash(string path, string? copyTo, CancellationToken cancel)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
        using (var output = copyTo is null ? null : new FileStream(copyTo, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        {
            var buffer = new byte[1 << 16];
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                sha.AppendData(buffer, 0, n);
                output?.Write(buffer, 0, n);
            }
            output?.Flush(flushToDisk: true);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
